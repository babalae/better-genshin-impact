using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 任务设置编辑器：优先复用独立任务声明的专属设置界面，没有专属界面时才按参数 Schema 生成通用设置卡片。
/// 只维护草稿，不直接修改任务计划；宿主（新增或编辑弹窗）负责提交 <see cref="CollectChanges"/> 的结果。
/// </summary>
public sealed class PuloniaTaskSettingsEditorViewModel : ObservableObject
{
    /// <summary>打开编辑器时的有效参数快照，用于合并校验和来源变化检测。</summary>
    private readonly JObject _original;

    /// <summary>当前任务的能力定义，提供参数 Schema。</summary>
    private readonly PuloniaTaskDefinition _definition;

    /// <summary>独立任务的专属设置视图模型；为空时使用 <see cref="Fields"/>。</summary>
    public SoloTaskSettingsViewModel? DedicatedSettings { get; }

    /// <summary>按 Schema 自动生成的通用设置字段；使用专属界面时为空。</summary>
    public IReadOnlyList<PuloniaOneDragonParameterFieldViewModel> Fields { get; }

    /// <summary>是否使用独立任务的专属设置界面。</summary>
    public bool HasDedicatedSettings => DedicatedSettings is not null;

    /// <summary>是否使用自动生成的通用设置卡片。</summary>
    public bool HasFields => DedicatedSettings is null && Fields.Count > 0;

    /// <summary>任务既没有专属界面也没有可编辑参数。</summary>
    public bool IsEmpty => DedicatedSettings is null && Fields.Count == 0;

    /// <summary>
    /// 建立编辑器；请通过 <see cref="Create"/> 选择专属界面或通用卡片。
    /// </summary>
    /// <param name="definition">能力定义。</param>
    /// <param name="original">有效参数快照。</param>
    /// <param name="dedicatedSettings">专属设置视图模型。</param>
    /// <param name="fields">通用设置字段。</param>
    private PuloniaTaskSettingsEditorViewModel(PuloniaTaskDefinition definition, JObject original,
        SoloTaskSettingsViewModel? dedicatedSettings, IReadOnlyList<PuloniaOneDragonParameterFieldViewModel> fields)
    {
        _definition = definition;
        _original = (JObject)original.DeepClone();
        DedicatedSettings = dedicatedSettings;
        Fields = fields;
    }

    /// <summary>
    /// 为任务建立设置编辑器：独立任务声明了专属设置界面时直接复用，否则按参数 Schema 自动渲染。
    /// </summary>
    /// <param name="registry">独立任务注册表；为空时只使用通用卡片。</param>
    /// <param name="definition">能力定义。</param>
    /// <param name="effectiveParameters">当前有效参数（默认值、预设、分组覆盖与节点参数合并后的结果）。</param>
    /// <returns>设置编辑器。</returns>
    public static PuloniaTaskSettingsEditorViewModel Create(SoloTaskRegistry? registry,
        PuloniaTaskDefinition definition, JObject effectiveParameters)
    {
        // 1. 优先使用独立任务自己的设置界面，与独立任务页、一条龙看到的配置保持一致
        var dedicated = registry is null
            ? null
            : SoloTaskSettingsFactory.Create(registry, PuloniaSoloTaskExecutor.ToTaskId(definition.TaskType), effectiveParameters);
        if (dedicated is not null)
            return new PuloniaTaskSettingsEditorViewModel(definition, effectiveParameters, dedicated, []);

        // 2. 没有专属界面时按 Schema 生成通用设置卡片
        return new PuloniaTaskSettingsEditorViewModel(definition, effectiveParameters, null,
            CreateFields(definition, effectiveParameters));
    }

    /// <summary>
    /// 按能力 Schema 生成通用设置卡片：枚举、秘境和战斗策略显示为下拉框，每周安排展开为七天卡片。
    /// </summary>
    /// <param name="definition">能力定义；为空时没有字段。</param>
    /// <param name="values">当前有效参数。</param>
    /// <returns>设置字段。</returns>
    internal static IReadOnlyList<PuloniaOneDragonParameterFieldViewModel> CreateFields(
        PuloniaTaskDefinition? definition, JObject values)
    {
        if (definition?.ParameterSchema["properties"] is not JObject properties)
            return [];
        var required = new HashSet<string>(
            (definition.ParameterSchema["required"] as JArray ?? new JArray()).Values<string>().OfType<string>(),
            StringComparer.Ordinal);
        return properties.Properties()
            .Select(property =>
            {
                var schema = property.Name == PuloniaSoloTaskExecutor.WeekdayOverridesParameter
                    ? PuloniaOneDragonSettingChoices.WeekdaySchema(properties, values, definition.TaskType)
                    : PuloniaOneDragonSettingChoices.Decorate(property.Name, property.Value as JObject ?? new JObject(),
                        values[property.Name], definition.TaskType);
                return new PuloniaOneDragonParameterFieldViewModel(property.Name, schema, values[property.Name],
                    required.Contains(property.Name));
            })
            .ToArray();
    }

    /// <summary>
    /// 收集相对打开时快照真实发生变化的参数。
    /// </summary>
    /// <returns>变化字段，键为参数名。</returns>
    /// <exception cref="FormatException">通用卡片中存在无法解析的输入。</exception>
    public JObject CollectChanges()
    {
        var changes = new JObject();
        if (DedicatedSettings is not null)
        {
            DedicatedSettings.CollectChanges(changes);
            return changes;
        }
        foreach (var field in Fields)
        {
            if (field.HasChanges() && field.BuildValue() is { } value)
                changes[field.Name] = value;
        }
        return changes;
    }

    /// <summary>
    /// 收集变化并与快照合并后按 Schema 校验，供新增任务在写入计划前确认参数合法。
    /// </summary>
    /// <returns>通过校验的变化字段。</returns>
    /// <exception cref="FormatException">输入无法解析。</exception>
    /// <exception cref="PuloniaTaskValidationException">合并后的参数不符合 Schema。</exception>
    public JObject CollectValidatedChanges()
    {
        var changes = CollectChanges();
        var effective = (JObject)_original.DeepClone();
        foreach (var property in changes.Properties())
            effective[property.Name] = property.Value.DeepClone();
        PuloniaTaskValidator.ValidateParameters(effective, _definition.ParameterSchema, _definition.DisplayName);
        return changes;
    }

    /// <summary>
    /// 判断草稿是否有未提交的修改；输入暂时无法解析也视为有修改，避免丢弃用户输入。
    /// </summary>
    /// <returns>存在修改时返回 true。</returns>
    public bool HasPendingChanges()
    {
        try
        {
            return CollectChanges().Count > 0;
        }
        catch (Exception ex) when (ex is FormatException or Newtonsoft.Json.JsonException)
        {
            return true;
        }
    }

    /// <summary>
    /// 检查要提交的字段在打开编辑器后没有被其他入口（例如高级设置）改写，避免覆盖更新的值。
    /// </summary>
    /// <param name="current">当前有效参数。</param>
    /// <param name="changes">准备提交的变化字段。</param>
    /// <returns>所有变化字段的来源仍与快照一致时返回 true。</returns>
    public bool IsSourceUnchanged(JObject current, JObject changes)
        => changes.Properties().All(property => JToken.DeepEquals(current[property.Name], _original[property.Name]));
}
