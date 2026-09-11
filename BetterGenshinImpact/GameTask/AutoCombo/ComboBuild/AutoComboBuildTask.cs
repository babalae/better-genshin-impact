using BetterGenshinImpact.GameTask.AutoCombo.ComboRun;
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

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

/// <summary>
/// 自动连招任务
/// 识别队伍 → 调用 LLM 通过 Function Calling 逐节点构建连招行为树 → 打印树给用户
/// 建树发生在战斗开始之前，树暂存内存中，后续集成进 AutoFight 运行
/// </summary>
public class AutoComboBuildTask : ISoloTask
{
    public string Name => "自动连招";

    /// <summary>FunctionInvokingChatClient 单次 GetResponseAsync 允许的最大工具调用循环轮数</summary>
    private const int MaxToolCallIterations = 128;

    public async Task Start(CancellationToken ct)
    {
        try
        {
            Logger.LogInformation("{Name}任务启动", Name);

            var combatScenes = CombatScenes.GetCombatScenesWithRetry();
            var avatarNames = combatScenes.GetAvatars().Select(a => a.Name).ToList();
            Logger.LogInformation("识别队伍：{Avatars}", string.Join("、", avatarNames));

            var config = TaskContext.Instance().Config.AutoComboBuildConfig;

            // 只暂存建树会话；CombatScenes 的绑定与生命周期由消费方（测试按钮/后续 AutoFight）负责
            AutoComboRuntime.Session = await BuildComboTreeAsync(avatarNames, config, Logger, ct);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "{Name}任务异常", Name);
            throw;
        }
        finally
        {
            Logger.LogInformation("{Name}任务结束", Name);
        }
    }

    /// <summary>
    /// 从已知队伍角色名开始，调用 LLM 通过 Function Calling 逐节点构建连招行为树（不含角色识别，可脱离游戏运行）
    /// 日志由调用方注入：主任务传 TaskControl.Logger，单测可传自定义实现，避免触及主程序静态初始化
    /// 返回建树会话（含构建器、黑板与队伍名）；异常退出时尝试打印当前已构建的行为树预览，便于定位 LLM 建树进度
    /// </summary>
    public static async Task<ComboTreeSession> BuildComboTreeAsync(List<string> avatarNames, AutoComboBuildConfig config, ILogger logger, CancellationToken ct)
    {
        AutoComboBuildBuilder? builder = null;
        try
        {
            var chatClient = CreateChatClient(config, logger);

            var blackboard = new Blackboard();
            builder = new AutoComboBuildBuilder().WithBlackboard(blackboard);

            var tools = new AutoComboBuildTools(builder);
            var aiFunctions = tools.Tools
                // 禁止 LLM 调用 RunTree
                .Where(d => d.Method.Name != nameof(AutoComboBuildTools.RunTree))
                .Select(d => AIFunctionFactory.Create(d))
                .ToArray();

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, BuildInstructions(avatarNames, config.ExtraPrompt, logger)),
                new(ChatRole.User, $"请为当前队伍构建战斗策略行为树"),
            };
            var options = new ChatOptions { Tools = aiFunctions };

            logger.LogInformation("开始调用 LLM 构建行为树（模型：{Model}）", config.ModelName);
            var response = await chatClient.GetResponseAsync(messages, options, ct);
            logger.LogInformation("LLM 返回：{Text}", response.Text);

            // FunctionInvokingChatClient 正常完成时每个 FunctionCallContent 都有配对 FunctionResultContent（CallId 相同）；
            // 达到 MaximumIterationsPerRequest 上限时最后一轮调用不执行，是唯一出现无配对调用的退出路径，以此显式报错
            var executedCallIds = response.Messages.SelectMany(m => m.Contents)
                .OfType<FunctionResultContent>().Select(r => r.CallId).ToHashSet();
            if (response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>()
                .Any(c => !executedCallIds.Contains(c.CallId)))
            {
                throw new Exception($"LLM 工具调用循环达到上限（{MaxToolCallIterations} 轮）仍未完成建树，最后响应中还有未执行的工具调用");
            }

            // LLM 已通过 BuildTree 工具完成构建；此处再次 Build 获取根节点用于打印
            var root = builder.Build();

            var ascii = CsTrees.Display.Display.AsciiTree(root);
            logger.LogInformation("生成的行为树：\n{Tree}", ascii);

            return new ComboTreeSession
            {
                Builder = builder,
                Blackboard = blackboard,
                TeamNames = avatarNames,
                BuiltAt = DateTimeOffset.Now,
            };
        }
        catch (Exception e)
        {
            logger.LogError(e, "建树异常");
            if (builder is not null)
            {
                try
                {
                    // Preview 不消耗 builder，未关闭作用域以占位节点呈现并自动回滚
                    var preview = builder.Preview();
                    logger.LogInformation("异常时的行为树预览：\n{Tree}", CsTrees.Display.Display.AsciiTree(preview));
                }
                catch (Exception ex)
                {
                    logger.LogWarning("行为树预览失败：{Message}", ex.Message);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// 根据 LLM 配置创建带工具调用循环的 IChatClient
    /// </summary>
    private static IChatClient CreateChatClient(AutoComboBuildConfig config, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(config.PlanningLlmEndpoint) ||
            string.IsNullOrWhiteSpace(config.ModelName))
        {
            throw new Exception("请先在任务设置页的“自动连招”卡片中配置 LLM 服务地址和模型名");
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

        // 密钥通过 Authorization 头随每个请求发送，非 HTTPS 传输时会在网络中明文暴露；仅豁免本机回环地址（本地中转/本地模型）
        var isLoopback = endpoint.Host is "localhost" or "127.0.0.1" or "::1" || endpoint.Host.StartsWith("[::1]");
        if (endpoint.Scheme != Uri.UriSchemeHttps && !isLoopback)
        {
            throw new Exception($"LLM 服务地址必须使用 HTTPS（否则密钥将明文传输），本机回环地址除外：{config.PlanningLlmEndpoint}");
        }

        // 本机回环地址（本地模型/本地中转通常不校验密钥）允许密钥为空，其余地址必须配置
        if (string.IsNullOrWhiteSpace(config.ApiKey) && !isLoopback)
        {
            throw new Exception("请先在任务设置页的“自动连招”卡片中配置 LLM 密钥（本机回环地址除外）");
        }

        // 在 HTTP 传输层前注入原生 JSON 请求/响应日志，用于查验最终发送给 API 及 API 返回的原始内容
        var openAiOptions = new OpenAIClientOptions
        {
            Endpoint = endpoint,
            NetworkTimeout = TimeSpan.FromMinutes(10),
        };

        // 密钥为空时传占位符：OpenAI 客户端拒绝空密钥，而本地服务不校验该头的值
        var apiKey = string.IsNullOrWhiteSpace(config.ApiKey) ? "missing-api-key" : config.ApiKey;
        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), openAiOptions);
        IChatClient client = openAiClient.GetChatClient(config.ModelName).AsIChatClient();

        // CsTrees.MEAI 自带 tree 字段裁剪装饰：每次请求前移除历史中旧的树预览（只保留最后一个），降低多轮 token 消耗
        client = new CompactResultChatClient(client);

        // 对话记录装饰：逐轮记录发给 LLM 与 LLM 发出的内容（回退解析前的原始响应，含藏在 reasoning/文本里的 XML 原文），便于观察 FunctionInvokingChatClient 的中间多轮过程
        client = new ConversationLoggingChatClient(client, logger);

        // XML 工具调用回退解析装饰：解析模型塞进 reasoning/文本里的 XML tool_calls 并注入单独的 tool_calls 字段；
        // 放在记录层外层，回退解析告警会先于"LLM 发出"日志打印
        client = new XmlToolCallFallbackParseChatClient(client, logger);

        // 外层装饰：自动执行 LLM 的工具调用并把结果回传，循环直至 LLM 输出最终回复
        client = new FunctionInvokingChatClient(client)
        {
            MaximumIterationsPerRequest = MaxToolCallIterations,
        };

        // 截断检查装饰：LLM 因上下文耗尽或达到 max_tokens 被截断时（finish_reason=length）显式报错
        return new LengthCutoffCheckChatClient(client, logger);
    }

    /// <summary>
    /// 构建给 LLM 的系统指令（静态内容，配合 provider 端前缀缓存）
    /// </summary>
    private static string BuildInstructions(List<string> avatarNames, string extraPrompt, ILogger logger)
    {
        // 只展开与当前队伍标签相关的交叉描述，无匹配内容时整段省略
        var tagPairSection = AvatarProfiles.BuildTagPairSection(avatarNames);
        var extraPromptSection = string.IsNullOrWhiteSpace(extraPrompt) ? "" : $"\n\n## 用户自定义要求\n{extraPrompt.Trim()}";
        return $$"""
            你将通过工具调用构建一棵战斗策略行为树，外部将不断循环运行它来进行战斗。
            你的做法是先仔细分析并输出设计思路和行为树草图，然后通过合理的工具调用进行构建，最终调用 BuildTree 完成构建。

            ## 建树规范
            - 树一开始就是可用的，不必调用 Reset ，直接使用并完成它，最后一步必须调用 BuildTree 来构建
            - avatarName 必须使用“当前队伍”中列出的角色名
            - 每层打开的作用域必须填入正确的子节点、退出前使用一次End来关闭，所有作用域关闭后才可调用 BuildTree 来构建树
            - 减少没有意义的组合节点嵌套
            - 工具调用返回的结果中包含 tree 字段，它就是当前行为树的完整预览，其缩进表示层级。由于系统会裁剪历史记录，你只会看到最后一次调用的 tree——它就是当前树的状态
            - `--> ...` 表示当前正在构建的位置，其缩进表示层级
            - 每次响应允许多次工具调用，有把握时应尽快构建

            ## 术语说明
            - 元素战技就是E技能，元素爆发就是Q技能，两者统称技能
            - 通常，元素战技在使用后会进入冷却，元素爆发在使用后充能会归零
            - 通常，元素战技造成伤害时会产生能量，为全队累积充能，因此爆发的使用间隔一般比战技长

            ## 战术要求
            优先使用连招，单一角色的动作其次，所有技能都应有机会被使用。因此使用 Selector 作为外层逻辑，然后按优先级顺序添加以下类型的子节点
                1. 连招序列，使用 Sequence 作为子节点，内部再按以下规则设计子节点序列
                    1.1. 连招是为了用元素反应或技能效果，去加成单次爆发或在某种短暂状态下才能打出的关键伤害
                    1.2. 一个连招序列至少要有一种技能效果，和至多一种元素反应主题
                    1.3. 连招中的元素反应应考虑元素消耗量，持续性效果的技能更适合为持续性的关键伤害做铺垫
                    1.4. 连招序列中如果有多个技能，先连续添加多个 IsXXXReady ，所有检查完成后，再按顺序使用 UseXXX 
                    1.5. 连招序列中仅当技能强化了角色的基础动作，或为了技能持续生效角色必须留在场上输出时，可以用对应的 BasicActionsByXXX 
                    1.6. 不要单纯为了触发元素反应而设计连招，因为实际上外层的运转已经在随机触发元素反应了
                2. 单独使用元素战技或元素爆发，直接使用 UseXXXIfReady 作为叶子节点
                    2.1. 由于冷却或充能的存在，下一次 Tick 就会被拦截，从而执行其他兄弟节点
                    2.2. 连招序列含有爆发的情况下爆发可以不单独使用，而战技应有单独使用的场合以保证队伍充能
                3. 单独的普攻或重击节点可用于外层Selector的兜底，选择队伍中最适合的来在所有技能暂不可用的间隙进行输出
                4. BasicActionsByXXX 会延误兄弟节点，导致就绪的战技/爆发无法及时打出，不作为Selector子节点使用

            ## 当前队伍
            {{AvatarProfiles.BuildTeamSection(avatarNames, logger)}}

            ## 元素反应
            {{tagPairSection}}{{extraPromptSection}}
            """;
    }
}
