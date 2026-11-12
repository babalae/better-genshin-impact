using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild.FishRelief;

/// <summary>
/// 发鱼站请求信封构建
/// 走发鱼站时 system 消息只上报任务与各规则键，提示词正文由网关按清单重建
/// 任务 id、规则名与键的字面量形式都是与网关清单的数据契约，改动需同步网关 manifests/local
/// </summary>
internal static class FishReliefEnvelope
{
    /// <summary>
    /// 主建树在网关清单中的任务 id
    /// </summary>
    public const string TaskMain = "main";

    /// <summary>
    /// 兜底建树在网关清单中的任务 id
    /// </summary>
    public const string TaskFallback = "fallback";

    /// <summary>
    /// 规则名，对应网关清单 rules[].name，同时是信封 data[].name 与模板变量名：
    /// 队伍规则必报，元素反应规则可选；每项上报的键由该规则 cases[].keys 做字面量匹配
    /// </summary>
    private const string RuleParty = "party";
    private const string RuleElementalReaction = "elementalReaction";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 构建渲染信封：任务 + 各规则上报键。队伍键是角色名，反应键形如“火 × 水”
    /// 网关按清单词表严格匹配键，未命中的键会使整单 400；
    /// 因此队伍中存在无内置档案的角色时直接报错，不把无法渲染的名字发给网关
    /// </summary>
    public static string Build(string task, List<string> avatarNames)
    {
        var unsupported = avatarNames.Where(name => AvatarProfiles.TryGet(name) == null).ToList();
        if (unsupported.Count > 0)
        {
            throw new Exception($"发鱼站暂不支持当前队伍中的角色：{string.Join("、", unsupported)}；请改用自定义 LLM 服务");
        }

        var data = new List<object>(2)
        {
            new
            {
                name = RuleParty,
                keys = avatarNames,
            },
            new
            {
                name = RuleElementalReaction,
                keys = AvatarProfiles.GetTeamTagPairs(avatarNames).Select(pair => pair.Key).ToList(),
            },
        };

        return JsonSerializer.Serialize(new
        {
            task,
            data,
        }, JsonOptions);
    }
}
