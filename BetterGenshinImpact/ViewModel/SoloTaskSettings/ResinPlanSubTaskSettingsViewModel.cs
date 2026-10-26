using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Resin;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>
/// 体力计划子任务设置弹窗的草稿：按子任务参数 Schema 生成设置卡片，隐藏由体力计划决定的字段。
/// 战斗策略与任务优先读取的隐藏字段联动，保持与该任务专属设置页相同的写入方式。
/// </summary>
public sealed partial class ResinPlanSubTaskSettingsViewModel : ObservableObject
{
    /// <summary>打开弹窗时的完整参数；隐藏字段原样保留。</summary>
    private readonly JObject _original;

    /// <summary>子任务完整参数 Schema，确认时用于校验。</summary>
    private readonly JObject _schema;

    /// <summary>子任务设置说明。</summary>
    private readonly ResinPlanSubTaskSpec _spec;

    /// <summary>窗口标题。</summary>
    public string Title { get; }

    /// <summary>弹窗顶部说明。</summary>
    public string Description { get; }

    /// <summary>可编辑的设置卡片。</summary>
    public IReadOnlyList<PuloniaOneDragonParameterFieldViewModel> Fields { get; }

    /// <summary>确认失败的原因；没有问题时为 null。</summary>
    [ObservableProperty] private string? _validationMessage;

    /// <summary>建立子任务设置草稿。</summary>
    /// <param name="spec">子任务设置说明。</param>
    /// <param name="schema">子任务参数的完整 Schema。</param>
    /// <param name="parameters">子任务当前参数。</param>
    /// <param name="resinDialogs">可选的树脂设置弹窗，供嵌套的树脂字段使用。</param>
    public ResinPlanSubTaskSettingsViewModel(ResinPlanSubTaskSpec spec, JObject schema, JObject parameters,
        IResinSettingsDialogs? resinDialogs = null)
    {
        _spec = spec;
        _schema = schema;
        _original = (JObject)parameters.DeepClone();
        Title = $"体力计划 · {spec.DisplayName}设置";
        Description = spec.ResinCapability is null
            ? "树脂使用、秘境选择与奖励识别由体力计划决定，这里不显示；未修改的设置保持原值。"
            : "讨伐次数按下方树脂使用设置（指定次数或刷至耗尽），并受体力用量限制；体力计划只使用原粹树脂。未修改的设置保持原值。";

        // 1. 只展示用户可以决定的字段
        var visible = (JObject)schema.DeepClone();
        if (visible["properties"] is JObject properties)
        {
            foreach (var hidden in spec.HiddenFields)
            {
                properties.Remove(hidden);
            }
        }

        // 2. 树脂使用只展示体力计划会采用的部分（例如首领去掉须臾、脆弱补充）；未修改时保留原值
        var values = (JObject)_original.DeepClone();
        if (spec.ReadResinUsage(values) is { } resin)
        {
            values["resin_usage"] = JObject.Parse(SoloTaskJson.Serialize(resin));
        }

        // 3. 任务优先读取联动字段时，策略卡片显示实际生效的策略
        if (spec is { StrategyField: { } strategyField, StrategyMirrorField: { } mirrorField }
            && values[mirrorField] is JValue { Value: string mirror } && !string.IsNullOrWhiteSpace(mirror))
        {
            values[strategyField] = CombatStrategyResolver.Parse(mirror).Value;
        }

        var definition = new PuloniaTaskDefinition
        {
            TaskType = spec.FieldTaskType ?? PuloniaSoloTaskExecutor.ToTaskType(spec.TaskId),
            DisplayName = spec.DisplayName,
            ParameterSchema = visible
        };
        Fields = PuloniaTaskSettingsEditorViewModel.CreateFields(definition, values, resinDialogs);
    }

    /// <summary>
    /// 按输入生成新的完整参数并校验；修改战斗策略时同时更新联动字段。
    /// </summary>
    /// <returns>新的完整参数；输入无效时返回 null，并在 <see cref="ValidationMessage"/> 中说明原因。</returns>
    public JObject? TryBuild()
    {
        try
        {
            var result = (JObject)_original.DeepClone();
            foreach (var field in Fields.Where(field => field.HasChanges()))
            {
                if (field.BuildValue() is not { } value)
                {
                    continue;
                }

                result[field.Name] = value;
                if (field.Name == _spec.StrategyField && _spec.StrategyMirrorField is { } mirror)
                {
                    // 与专属设置页一致：首领同时写入策略路径，幽境清空脚本包路径后按策略名解析
                    result[mirror] = _spec.ClearMirror ? string.Empty : value.DeepClone();
                }
            }

            PuloniaTaskValidator.ValidateParameters(result, _schema, Title);
            ValidationMessage = null;
            return result;
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or Newtonsoft.Json.JsonException
                                      or PuloniaTaskValidationException)
        {
            ValidationMessage = e.Message;
            return null;
        }
    }
}
