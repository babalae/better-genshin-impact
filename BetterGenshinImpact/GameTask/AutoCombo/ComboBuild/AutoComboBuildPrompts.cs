using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

/// <summary>
/// 自动连坛建树提示词构建：用户自定义只针对正文（默认正文见 DefaultXxxBody），留空回退默认
/// “当前队伍”“元素反应”“用户自定义要求”等小节由程序固定拼接在正文之后，用户无需理会任何拼接语法
/// </summary>
internal static class AutoComboBuildPrompts
{
    /// <summary>
    /// 主建树给 LLM 的系统指令默认正文（静态内容，配合 provider 端前缀缓存）
    /// </summary>
    internal const string DefaultMainBody = """
        你将通过工具调用构建一棵技能策略行为树。
        你的做法是先分析并输出设计思路、构建过程的伪代码和行为树草图，然后通过工具调用进行构建，最终调用 BuildTree 完成构建。

        ## 建树规范
        - 每层打开的作用域必须填入正确的子节点、退出前使用一次End来关闭，所有作用域关闭后才可调用 BuildTree 来构建树
        - 减少没有意义的组合节点嵌套
        - 工具调用返回的结果中包含 tree 字段，它就是当前行为树的完整预览，其缩进表示层级。由于系统会裁剪历史记录，你只会看到最后一次调用的 tree
        - `--> ...` 表示当前正在构建的位置，其缩进表示层级
        - 每次响应允许多次工具调用，有把握时应尽快构建

        ## 术语说明
        - 元素战技就是E技能，元素爆发就是Q技能，两者统称技能
        - 通常，元素战技在使用后会进入冷却，元素爆发在使用后充能会归零
        - 通常，元素战技造成伤害时会产生能量，为全队累积充能，因此爆发的使用间隔一般比战技长
        - 应分辨各个技能是否属于站场技能，站场技能是效果只在角色前台登场时成立、切人后无法起效的一类技能，一般是强化角色自身，或为了持续生效角色必须留在场上
          站场期间如果因技能效果有额外的技能可用，应优先于基础动作尝试使用

        ## 战术要求
        优先使用连招，单一角色的技能其次，所有技能都应有机会被使用。因此使用 Selector 作为外层逻辑，然后按优先级顺序添加以下类型的子节点
            1. 连招序列，使用 Sequence 作为子节点，内部再按以下规则设计子节点序列
                1.1. 连招是为了用元素反应或技能效果，去加成单次爆发或在某种短暂状态下才能打出的关键伤害，因此序列中负责铺垫的行为在前，被加成的行为在后
                1.2. 一个连招序列至少要有一种技能效果，和至多一种元素反应主题
                1.3. 连招中的元素反应应考虑元素消耗量，持续性效果的技能更适合为持续性的关键伤害做铺垫
                1.4. 连招序列中如果有多个技能，先连续添加多个 IsXXXReady ，所有检查完成后，再按顺序使用技能：非站场技能使用 UseXXX；站场技能使用 UseXXXIfReadyThenDoActionsByXXX
                1.5. 不要单纯为了触发元素反应而设计连招，因为实际上外层的运转已经在随机触发元素反应了
            2. 单独使用E技能或Q技能
                2.1. 由于冷却或充能的存在，下一次 Tick 就会被拦截，从而执行其他兄弟节点
                2.2. 对每个角色来说，Q技能已经存在于某个连招序列的情况下可以不单独使用，而E技能必须有单独使用的场合以保证队伍充能
                2.3. 非站场技能直接使用 UseXXXIfReady 作为叶子节点；站场技能使用 UseXXXIfReadyThenDoActionsByXXX
        基础攻击兜底由外部单独构建，所有技能暂不可用时整体返回 Failure 即可
        """;

    /// <summary>
    /// 兜底建树给 LLM 的系统指令默认正文：任务范围刻意收窄——只选一个角色、只用基础动作、只产出一条循环输出序列
    /// </summary>
    internal const string DefaultFallbackBody = """
        你将通过工具调用构建一棵兜底攻击行为树：它会在所有技能都不可用的间隙执行，为队伍提供基础输出

        ## 建树规范
        - 行为树的根（Sequence 作用域）已由程序预建打开，你只需在其中依次添加1~5个基础动作子节点
        - 添加完所有动作后使用一次End来关闭作用域，然后调用 BuildTree 来完成构建
        - 技能策略行为树由外部单独构建，并且已考虑到技能效果中可能包含的基础动作需求

        ## 动作设计
        - 从当前队伍中选择站场输出最合适的角色承担兜底攻击
        - 依据角色特性，一般一个普攻子节点即可，如有特殊则按顺序编排若干个基础动作
        - 序列是会被循环 Tick 的，因此序列中必须避免没有意义的重复
        """;

    /// <summary>
    /// 构建主建树的系统指令：正文留空时使用内置默认正文，否则使用自定义正文
    /// 固定追加“当前队伍”“元素反应”小节；只展开与当前队伍标签相关的交叉描述，无匹配内容时整段省略
    /// </summary>
    public static string BuildMain(List<string> avatarNames, AutoComboBuildConfig config, ILogger logger)
    {
        var body = string.IsNullOrWhiteSpace(config.MainPrompt) ? DefaultMainBody : config.MainPrompt;
        var overrides = config.AvatarDescriptionOverrides.ToList();
        var sections = new List<string>
        {
            $"## 当前队伍\n{BuildTeamSection(avatarNames, logger, overrides)}",
        };

        // 只展开与当前队伍标签相关的交叉描述，无匹配内容时整段省略
        var tagPairSection = BuildTagPairSection(avatarNames);
        if (!string.IsNullOrEmpty(tagPairSection))
        {
            sections.Add($"## 元素反应\n{tagPairSection}");
        }

        if (!string.IsNullOrWhiteSpace(config.ExtraPrompt))
        {
            sections.Add($"## 用户自定义要求\n{config.ExtraPrompt.Trim()}");
        }

        return body + "\n\n" + string.Join("\n\n", sections);
    }

    /// <summary>
    /// 构建兜底建树的系统指令：正文留空时使用内置默认正文，否则使用自定义正文
    /// 固定追加“当前队伍”小节
    /// </summary>
    public static string BuildFallback(List<string> avatarNames, AutoComboBuildConfig config, ILogger logger)
    {
        var body = string.IsNullOrWhiteSpace(config.FallbackPrompt) ? DefaultFallbackBody : config.FallbackPrompt;
        return $"{body}\n\n## 当前队伍\n{BuildTeamSection(avatarNames, logger, config.AvatarDescriptionOverrides.ToList())}";
    }

    /// <summary>
    /// 把队伍展开成提示词的"当前队伍"段落
    /// 有档案的角色展开为"名字：X元素角色、月兆角色等（顿号连接），描述"；缺失档案的角色只列名字并记日志
    /// </summary>
    private static string BuildTeamSection(List<string> avatarNames, ILogger logger, List<AvatarProfile>? descriptionOverrides = null)
    {
        var lines = new List<string>();
        foreach (var name in avatarNames)
        {
            var profile = AvatarProfiles.TryGet(name, descriptionOverrides);
            if (profile != null)
            {
                // 元素标签还原为"X元素角色"，其余标签（月兆/星之楔）加"角色"后缀，统一用顿号连接
                var element = profile.Tags.FirstOrDefault(t => t.EndsWith("元素"));
                var others = profile.Tags.Where(t => !t.EndsWith("元素")).Select(t => $"{t}角色").ToList();
                if (element != null)
                {
                    others.Insert(0, $"{element}角色");
                }
                var head = string.Join("、", others);
                lines.Add($"- {name}：{head}，{AvatarProfiles.GetAdaptedDescription(profile)}");
            }
            else
            {
                logger.LogWarning("角色 {Name} 无内置战术描述，提示词中将只列出名字", name);
                lines.Add($"- {name}");
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// 把队伍内标签的两两交叉描述展开成提示词段落
    /// 稀疏：只展开已录入的标签对，未录入的组合不输出
    /// </summary>
    private static string BuildTagPairSection(List<string> avatarNames)
    {
        var tags = avatarNames
            .Select(name => AvatarProfiles.TryGet(name)?.Tags)
            .Where(t => t != null)
            .SelectMany(t => t!)
            .Distinct()
            .ToList();

        var lines = new List<string>();
        for (var i = 0; i < tags.Count; i++)
        {
            for (var j = i + 1; j < tags.Count; j++)
            {
                var desc = AvatarProfiles.TryGetTagPair(tags[i], tags[j]);
                if (desc != null)
                {
                    lines.Add($"- {tags[i].Replace("元素", "")} × {tags[j].Replace("元素", "")}：{desc}");
                }
            }
        }

        return string.Join("\n", lines);
    }
}
