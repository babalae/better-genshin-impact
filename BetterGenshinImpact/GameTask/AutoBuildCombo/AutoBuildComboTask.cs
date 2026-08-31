using BetterGenshinImpact.GameTask.AutoFight.Model;
using CsTrees.Blackboard;
using CsTrees.MEAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoBuildCombo;

/// <summary>
/// 自动连招任务
/// 识别队伍 → 调用 LLM 通过 Function Calling 逐节点构建连招行为树 → 打印树给用户
/// 建树发生在战斗开始之前，树暂存内存中，后续集成进 AutoFight 运行
/// </summary>
public class AutoBuildComboTask : ISoloTask
{
    public string Name => "自动连招";

    private CancellationToken _ct;

    public async Task Start(CancellationToken ct)
    {
        _ct = ct;
        AutoBuildComboBuilder? builder = null;
        try
        {
            Logger.LogInformation("{Name}任务启动", Name);

            var combatScenes = GetCombatScenesWithRetry();
            combatScenes.BeforeTask(ct);
            var avatarNames = combatScenes.GetAvatars().Select(a => a.Name).ToList();
            Logger.LogInformation("识别队伍：{Avatars}", string.Join("、", avatarNames));

            var config = TaskContext.Instance().Config.AutoBuildComboConfig;
            var chatClient = CreateChatClient(config);

            var blackboard = new Blackboard();
            builder = new AutoBuildComboBuilder().WithBlackboard(blackboard);

            var tools = new AutoBuildComboTools(builder);
            var aiFunctions = tools.Tools
                // 禁止 LLM 调用 RunTree
                .Where(d => d.Method.Name != nameof(AutoBuildComboTools.RunTree))
                .Select(d => AIFunctionFactory.Create(d))
                .ToArray();

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, BuildInstructions(avatarNames)),
                new(ChatRole.User, $"请为当前队伍构建战斗策略行为树"),
            };
            var options = new ChatOptions { Tools = aiFunctions };

            Logger.LogInformation("开始调用 LLM 构建行为树（模型：{Model}）", config.ModelName);
            var response = await chatClient.GetResponseAsync(messages, options, ct);
            Logger.LogInformation("LLM 返回：{Text}", response.Text);

            // LLM 已通过 BuildTree 工具完成构建；此处再次 Build 获取根节点用于打印
            var root = builder.Build();

            // 树构建完成后直接把队伍信息写入黑板
            blackboard.GrantExclusiveWrite<CombatScenes>(null!, "CombatScenes").Set(combatScenes);

            var ascii = CsTrees.Display.Display.AsciiTree(root);
            Logger.LogInformation("生成的行为树：\n{Tree}", ascii);

            // 暂存树根，供任务设置页的测试按钮启动/暂停 Tick 循环
            AutoBuildComboRuntime.Root = root;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "{Name}任务异常", Name);
            TryLogTreePreview(builder);
            throw;
        }
        finally
        {
            Logger.LogInformation("{Name}任务结束", Name);
        }
    }

    /// <summary>
    /// 异常退出时尝试打印当前已构建的行为树预览，便于定位 LLM 建树进度
    /// </summary>
    private void TryLogTreePreview(AutoBuildComboBuilder? builder)
    {
        if (builder is null)
            return;
        try
        {
            // Preview 不消耗 builder，未关闭作用域以占位节点呈现并自动回滚
            var root = builder.Preview();
            Logger.LogInformation("异常时的行为树预览：\n{Tree}", CsTrees.Display.Display.AsciiTree(root));
        }
        catch (Exception ex)
        {
            Logger.LogWarning("行为树预览失败：{Message}", ex.Message);
        }
    }

    /// <summary>
    /// 根据 LLM 配置创建带工具调用循环的 IChatClient
    /// </summary>
    private IChatClient CreateChatClient(AutoBuildComboConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.PlanningLlmEndpoint) ||
            string.IsNullOrWhiteSpace(config.ModelName) ||
            string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new Exception("请先在任务设置页的“自动连招”卡片中配置 LLM 服务地址、模型名和密钥");
        }

        Uri endpoint;
        try
        {
            endpoint = new Uri(config.PlanningLlmEndpoint.Trim());
        }
        catch (UriFormatException e)
        {
            throw new Exception($"LLM 服务地址无效：{config.PlanningLlmEndpoint}", e);
        }

        // 在 HTTP 传输层前注入原生 JSON 请求/响应日志，用于查验最终发送给 API 及 API 返回的原始内容
        var openAiOptions = new OpenAIClientOptions
        {
            Endpoint = endpoint,
            NetworkTimeout = TimeSpan.FromMinutes(10),
        };

        var openAiClient = new OpenAIClient(new ApiKeyCredential(config.ApiKey), openAiOptions);
        IChatClient client = openAiClient.GetChatClient(config.ModelName).AsIChatClient();

        // 紧贴 provider 装饰：每次请求前移除历史中旧的树预览（只保留最后一个），降低多轮 token 消耗
        client = new CompactResultChatClient(client);

        // 紧贴工具循环装饰：记录每轮对话内容，便于观察 FunctionInvokingChatClient 的中间多轮过程
        client = new ConversationLoggingChatClient(client);

        // 外层装饰：自动执行 LLM 的工具调用并把结果回传，循环直至 LLM 输出最终回复
        return new FunctionInvokingChatClient(client)
        {
            MaximumIterationsPerRequest = 128,
        };
    }

    /// <summary>
    /// 构建给 LLM 的系统指令（静态内容，配合 provider 端前缀缓存）
    /// </summary>
    private static string BuildInstructions(List<string> avatarNames)
    {
        // 只展开与当前队伍标签相关的交叉描述，无匹配内容时整段省略
        var tagPairSection = AvatarProfiles.BuildTagPairSection(avatarNames);
        return $$"""
            你将通过工具调用构建一棵战斗策略行为树，外部将不断循环运行它来进行战斗。
            你的做法是先仔细分析并输出简要的设计思路和行为树草图，然后通过工具调用进行构建，最终调用 BuildTree 完成构建。

            ## 当前队伍
            {{AvatarProfiles.BuildTeamSection(avatarNames)}}

            ## 元素反应
            {{tagPairSection}}

            ## 建树规范
            - 树一开始就是可用的，直接使用并完成它
            - avatarName 必须使用"当前队伍"中列出的角色名
            - 每层打开的作用域（组合节点、黑板）退出前必须使用一次End来关闭，所有作用域关闭后才可调用 BuildTree 来构建树
            - 减少没有意义的组合节点嵌套
            - 工具调用返回的结果中包含 tree 字段，它就是当前行为树的完整预览。由于系统会裁剪历史记录，你只会看到最后一次调用的 tree——它就是当前树的状态
            - tree 是 ASCII 树形文本，缩进表示层级
            - 你必须使用tool_calls而不是reasoning_content来调用工具

            ## 战术要求
            - 策略的核心是：高价值动作优先执行，未就绪时用下位替代补位，保证整个队伍始终有事可做，因此使用有记忆的Selector作为外层逻辑，然后按优先级顺序直接添加以下类型的子节点
                - 单独使用元素战技或元素爆发，直接使用 UseXXXIfReady 作为叶子节点。战技在使用后会进入冷却、爆发在使用后会进入充能，由于行为树会持续Tick，下一次就会执行下位替代，从而自然地产生元素反应或随机Combo
                - 普攻/重击等基础动作永远可用，可直接作为叶子节点直接添加，或添加一个有记忆的Sequence，其中排列2个以上的动作节点
                - 如有特别需要，可以设计复杂的连招序列：可添加一个有记忆的Sequence，先连续使用多个 IsXXXReady 检查，所有检查添加完后，再按顺序使用 UseXXX 或多段普攻或多段重击。在可用性满足的情况下，这样就总是能打出稳定顺序的Combo
            - 子节点一旦满足检查条件，就保证该节点内动作序列全部跑完，因此全程使用有记忆的组合节点
            - 输出角色可站场，在队伍中分析出一个最适合输出的，并且分析是只打普攻、只打重击、还是有特殊打法，一般仅选用一种即可
            - 辅助角色不打普攻或重击，尤其后台角色。但如果队伍里全是辅助角色，可以根据角色特点安排一个打输出，避免全部角色技能未就绪时发呆
            """;
    }

    /// <summary>
    /// 识别队伍角色，复用 AutoFight 的 YOLO 侧面头像识别，失败重试 5 次
    /// </summary>
    private CombatScenes GetCombatScenesWithRetry()
    {
        const int maxRetries = 5;
        const int retryDelayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
            if (combatScenes.CheckTeamInitialized())
            {
                return combatScenes;
            }

            if (attempt < maxRetries)
            {
                Sleep(retryDelayMs, _ct);
            }
        }

        throw new Exception("识别队伍角色失败（已重试 5 次）");
    }
}
