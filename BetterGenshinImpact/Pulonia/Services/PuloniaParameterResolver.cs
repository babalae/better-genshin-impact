using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 运行准备与任务计划界面预览共用的有效参数解析，纯函数、不读存储。
/// 层级顺序：能力默认值 → 共享设置（目录默认值，沿设置链由外到内叠加，每层先方案后节点自身，按绑定写入参数路径）
/// → 任务参数（顶层整体替换）→ 本次调用覆盖；显式 null、false、0 与集合都保留。
/// 设置链由调用方组装：普通情况为计划根到叶子；计划引用时为外部祖先 → 被引用计划根 → 引用节点 → 被引用计划内部节点。
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
    /// 解析一条设置链上全部共享设置的有效值与来源；同一项后写者生效，每层先方案后节点自身。
    /// </summary>
    /// <param name="chain">由外到内的设置链。</param>
    public static PuloniaSettingsResolution ResolveSettings(IReadOnlyList<PuloniaSettingLevel> chain)
    {
        var values = new Dictionary<string, JToken>(StringComparer.Ordinal);
        var sources = new Dictionary<string, PuloniaSettingSource>(StringComparer.Ordinal);
        var missing = new List<PuloniaSettingLevel>();

        // 1. 目录默认值
        foreach (var definition in PuloniaSettingCatalog.Definitions)
        {
            values[definition.Key] = definition.DefaultValue.DeepClone();
            sources[definition.Key] = new PuloniaSettingSource(PuloniaSettingSourceKind.Default);
        }

        // 2. 由外到内逐层叠加：方案先于节点自身；目录中不存在的键由保存时的校验拒绝，这里忽略
        foreach (var level in chain)
        {
            if (level.Node.ProfileId is not null)
            {
                if (level.Profile is null)
                    missing.Add(level);
                else
                    Apply(level.Profile.Settings, new PuloniaSettingSource(PuloniaSettingSourceKind.Profile, level));
            }
            Apply(level.Node.Settings, new PuloniaSettingSource(PuloniaSettingSourceKind.Node, level));
        }
        return new PuloniaSettingsResolution(values, sources, missing);

        void Apply(JObject settings, PuloniaSettingSource source)
        {
            foreach (var field in settings.Properties())
            {
                if (!values.ContainsKey(field.Name))
                    continue;
                values[field.Name] = field.Value.DeepClone();
                sources[field.Name] = source;
            }
        }
    }

    /// <summary>
    /// 按固定层级解析具体任务的有效参数与来源。
    /// </summary>
    /// <param name="task">要解析的具体任务。</param>
    /// <param name="definition">节点使用的能力说明；为空时跳过默认值和共享设置。</param>
    /// <param name="chain">由外到内的设置链，最后一层通常是任务自身。</param>
    /// <param name="callOverride">本次调用对该节点地址的覆盖；没有时为 null。</param>
    public static PuloniaParameterResolution Resolve(PuloniaTask task, PuloniaTaskDefinition? definition,
        IReadOnlyList<PuloniaSettingLevel> chain, JObject? callOverride = null)
    {
        var parameters = new JObject();
        var sources = new Dictionary<string, PuloniaParameterLayer>(StringComparer.Ordinal);
        var bindings = definition?.SettingBindings ?? new Dictionary<string, string>();
        var boundRoots = new HashSet<string>(bindings.Values.Select(path => path.Split('.')[0]), StringComparer.Ordinal);

        // 1. 能力默认值
        if (definition is not null)
            ApplyTopLevel(parameters, sources, definition.DefaultParameters, PuloniaParameterLayer.Default, null);

        // 2. 共享设置：只写路径末端，同一对象参数的不同子项可以来自不同层
        var settings = ResolveSettings(chain);
        foreach (var (key, path) in bindings)
        {
            SetPath(parameters, path, settings.Values[key]);
            sources[path.Split('.')[0]] = PuloniaParameterLayer.Settings;
        }

        // 3. 任务参数：被共享设置覆盖的顶层参数不接受直接写入
        var conflicts = ApplyTopLevel(parameters, sources, task.Parameters, PuloniaParameterLayer.Node, boundRoots);

        // 4. 本次调用覆盖最后生效，同样不能直接改写共享设置
        var callConflicts = callOverride is null
            ? []
            : ApplyTopLevel(parameters, sources, callOverride, PuloniaParameterLayer.Call, boundRoots);

        var unused = task.TaskType == "group"
            ? []
            : task.Settings.Properties().Select(property => property.Name).Where(name => !bindings.ContainsKey(name)).ToList();
        return new PuloniaParameterResolution(parameters, sources, settings, bindings.Keys.ToList(), conflicts, callConflicts, unused);
    }

    /// <summary>
    /// 按点分路径写入值，中间对象缺失时创建；写入副本。
    /// </summary>
    internal static void SetPath(JObject root, string path, JToken value)
    {
        var segments = path.Split('.');
        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JObject next)
            {
                next = new JObject();
                current[segments[i]] = next;
            }
            current = next;
        }
        current[segments[^1]] = value.DeepClone();
    }

    /// <summary>
    /// 顶层字段整体替换并记录来源；返回因属于共享设置而被拒绝的字段。
    /// </summary>
    private static List<string> ApplyTopLevel(JObject result, Dictionary<string, PuloniaParameterLayer> sources, JObject values,
        PuloniaParameterLayer layer, HashSet<string>? rejected)
    {
        var conflicts = new List<string>();
        foreach (var field in values.Properties())
        {
            if (rejected?.Contains(field.Name) == true)
            {
                conflicts.Add(field.Name);
                continue;
            }
            result[field.Name] = field.Value.DeepClone();
            sources[field.Name] = layer;
        }
        return conflicts;
    }
}
