using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 运行准备与任务计划界面预览共用的有效参数解析，纯函数、不读存储。
/// 层级顺序：能力默认值 → 预设（账号可替换选择）→ 祖先分组公共参数（由外到内，同组通用先于资源专用）
/// → 节点参数 → 计划引用公共参数 → 本次调用覆盖；顶层字段整体替换，显式 null、false、0 与集合都保留。
/// 界面预览拿不到的输入（账号、引用公共参数、调用覆盖）不传即跳过该层。
/// </summary>
public static class PuloniaParameterResolver
{
    /// <summary>
    /// 按资源专用说明优先、类型通用说明其次查找能力；未注册时返回 null。
    /// </summary>
    /// <param name="definitions">可用的能力说明。</param>
    /// <param name="task">要解析的具体任务。</param>
    public static PuloniaTaskDefinition? FindDefinition(IReadOnlyCollection<PuloniaTaskDefinition> definitions, PuloniaTask task)
        => definitions.FirstOrDefault(d => d.TaskType == task.TaskType && d.ResourceId == task.ResourceKey)
           ?? definitions.FirstOrDefault(d => d.TaskType == task.TaskType && d.ResourceId is null);

    /// <summary>
    /// 账号绑定替换预设选择：所选账号在计划中启用并为该节点配置了替换时使用替换预设，否则沿用节点自身预设。
    /// </summary>
    /// <param name="task">要解析的具体任务。</param>
    /// <param name="accounts">节点所属计划的账号绑定。</param>
    /// <param name="accountId">本次运行选择的账号；为空表示不应用账号替换。</param>
    public static string? GetEffectivePresetId(PuloniaTask task, IEnumerable<PuloniaTaskAccountBinding> accounts, string? accountId)
    {
        if (accountId is null)
            return task.PresetId;
        var binding = accounts.FirstOrDefault(account => account.AccountId == accountId && account.Enabled);
        return binding?.PresetSelections.TryGetValue(task.Id, out var replacement) == true ? replacement : task.PresetId;
    }

    /// <summary>
    /// 严格匹配参数作用域（预设或公共参数项），防止不同能力或脚本的同名参数相互污染：
    /// 类型相同、参数版本与能力说明一致，且未绑定资源或资源与节点/能力说明一致。
    /// 尚未取得能力说明（界面预览）时按默认参数版本 1 匹配。
    /// </summary>
    /// <param name="taskType">作用域的任务类型。</param>
    /// <param name="resourceId">作用域绑定的资源；为空表示同类型通用。</param>
    /// <param name="schemaVersion">作用域的参数版本。</param>
    /// <param name="task">要解析的具体任务。</param>
    /// <param name="definition">节点使用的能力说明；未注册时为 null。</param>
    public static bool MatchesScope(string taskType, string? resourceId, int schemaVersion, PuloniaTask task,
        PuloniaTaskDefinition? definition)
        => taskType == task.TaskType && schemaVersion == (definition?.SchemaVersion ?? 1)
           && (resourceId is null || resourceId == task.ResourceKey || resourceId == definition?.ResourceId);

    /// <summary>
    /// 按固定层级解析具体任务的有效参数与逐字段来源。
    /// </summary>
    /// <param name="task">要解析的具体任务。</param>
    /// <param name="definition">节点使用的能力说明；为空时跳过默认值层并按参数版本 1 匹配。</param>
    /// <param name="presetId">已应用账号替换的预设 ID，见 <see cref="GetEffectivePresetId"/>。</param>
    /// <param name="preset">按 <paramref name="presetId"/> 找到的预设；找不到时传 null，结果记为缺失。</param>
    /// <param name="groupOverrides">当前计划内由外到内每个祖先分组的公共参数项；引用边界内从被引用计划的根开始。</param>
    /// <param name="referenceOverrides">最近一层计划引用节点的公共参数项；不在引用内时为空。</param>
    /// <param name="callOverride">本次调用对该节点地址的覆盖；没有时为 null。</param>
    public static PuloniaParameterResolution Resolve(PuloniaTask task, PuloniaTaskDefinition? definition,
        string? presetId, PuloniaTaskPreset? preset,
        IEnumerable<IReadOnlyList<PuloniaTaskParameterOverride>> groupOverrides,
        IReadOnlyList<PuloniaTaskParameterOverride>? referenceOverrides = null, JObject? callOverride = null)
    {
        var parameters = new JObject();
        var sources = new Dictionary<string, PuloniaParameterSource>(StringComparer.Ordinal);

        // 1. 能力默认值
        if (definition is not null)
            Apply(parameters, sources, definition.DefaultParameters, new PuloniaParameterSource(PuloniaParameterLayer.Default));

        // 2. 预设：缺失或作用域不符时跳过该层，由调用方决定报错（运行）还是提示（预览）
        var presetStatus = presetId is null ? PuloniaPresetStatus.None
            : preset is null ? PuloniaPresetStatus.Missing
            : MatchesScope(preset.TaskType, preset.ResourceId, preset.SchemaVersion, task, definition)
                ? PuloniaPresetStatus.Applied
                : PuloniaPresetStatus.Incompatible;
        if (presetStatus == PuloniaPresetStatus.Applied)
            Apply(parameters, sources, preset!.Values, new PuloniaParameterSource(PuloniaParameterLayer.Preset));

        // 3. 祖先分组公共参数：更近的分组覆盖更远的分组，同组内资源专用覆盖晚于通用覆盖
        foreach (var group in groupOverrides)
        {
            foreach (var item in OrderByScope(group))
            {
                if (MatchesScope(item.TaskType, item.ResourceId, item.SchemaVersion, task, definition))
                    Apply(parameters, sources, item.Values, new PuloniaParameterSource(PuloniaParameterLayer.Group, item));
            }
        }

        // 4. 节点自身参数
        Apply(parameters, sources, task.Parameters, new PuloniaParameterSource(PuloniaParameterLayer.Node));

        // 5. 计划引用公共参数：只有能力声明为公共的字段能跨入引用边界，含未声明字段的项整体不生效
        var hasUndeclaredPublicOverride = false;
        foreach (var item in OrderByScope(referenceOverrides ?? []))
        {
            if (!MatchesScope(item.TaskType, item.ResourceId, item.SchemaVersion, task, definition))
                continue;
            if (item.Values.Properties().Any(property => definition?.PublicParameters.Contains(property.Name, StringComparer.Ordinal) != true))
            {
                hasUndeclaredPublicOverride = true;
                continue;
            }
            Apply(parameters, sources, item.Values, new PuloniaParameterSource(PuloniaParameterLayer.ReferencePublic, item));
        }

        // 6. 本次调用覆盖最后生效
        if (callOverride is not null)
            Apply(parameters, sources, callOverride, new PuloniaParameterSource(PuloniaParameterLayer.Call));

        return new PuloniaParameterResolution(parameters, sources, presetId, preset, presetStatus, hasUndeclaredPublicOverride);
    }

    /// <summary>
    /// 同一分组内通用覆盖排在资源专用覆盖之前，同类保持编辑顺序（稳定排序）。
    /// </summary>
    private static IEnumerable<PuloniaTaskParameterOverride> OrderByScope(IEnumerable<PuloniaTaskParameterOverride> items)
        => items.OrderBy(item => item.ResourceId is null ? 0 : 1);

    /// <summary>
    /// 顶层字段整体替换并记录来源；已有字段保持原位置，新字段追加在末尾。
    /// </summary>
    private static void Apply(JObject result, Dictionary<string, PuloniaParameterSource> sources, JObject values,
        PuloniaParameterSource source)
    {
        foreach (var field in values.Properties())
        {
            result[field.Name] = field.Value.DeepClone();
            sources[field.Name] = source;
        }
    }
}
