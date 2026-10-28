using System.Linq;
using BetterGenshinImpact.GameTask.Common.Resin;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>
/// 体力计划设置页中一个子任务的卡片草稿：参与开关、子任务参数与摘要。
/// 对应体力计划参数中的 <c>{ "enabled": ..., "param": { ... } }</c> 对象，提交时整体写回。
/// </summary>
public sealed partial class ResinPlanSubTaskEntryViewModel : ObservableObject
{
    /// <summary>载入时的整个子任务对象；缺失时为 null。</summary>
    private readonly JObject? _original;

    /// <summary>载入时的参与开关。</summary>
    private readonly bool _originalEnabled;

    /// <summary>载入时的子任务参数。</summary>
    private readonly JObject _originalParam;

    /// <summary>可选的子任务设置弹窗；隔离环境下只显示摘要。</summary>
    private readonly IResinPlanSettingsDialogs? _dialogs;

    /// <summary>子任务设置说明。</summary>
    public ResinPlanSubTaskSpec Spec { get; }

    /// <summary>卡片标题。</summary>
    public string DisplayName => Spec.DisplayName;

    /// <summary>卡片说明。</summary>
    public string Description => Spec.Description;

    /// <summary>是否在体力计划中执行此任务。</summary>
    [ObservableProperty] private bool _enabled;

    /// <summary>子任务参数草稿；弹窗确认后整体替换。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private JObject _param;

    /// <summary>卡片摘要：列出已填写的关键设置。</summary>
    public string Summary
    {
        get
        {
            var parts = Spec.SummaryFields
                .Select(item => (item.Label, Value: Param[item.Field] is JValue { Value: not null } value ? value.ToString() : string.Empty))
                .Where(item => item.Value.Length > 0)
                .Select(item => $"{item.Label}：{item.Value}")
                .ToList();
            // 树脂使用由子任务自己决定时（首领），摘要同时显示次数，卡片上即可看到讨伐几次
            if (Spec.ReadResinUsage(Param) is { } resin && Spec.ResinCapability is { } capability)
            {
                parts.Add("树脂：" + ResinUsageText.Summary(resin, capability));
            }

            return parts.Count == 0 ? "使用默认设置" : string.Join(" · ", parts);
        }
    }

    /// <summary>是否可以打开子任务设置弹窗。</summary>
    public bool CanEdit => _dialogs is not null;

    /// <summary>从体力计划参数中读取子任务对象。</summary>
    /// <param name="spec">子任务设置说明。</param>
    /// <param name="original">体力计划的当前有效参数。</param>
    /// <param name="defaultEnabled">参数缺失时的参与开关。</param>
    /// <param name="dialogs">可选的子任务设置弹窗。</param>
    public ResinPlanSubTaskEntryViewModel(ResinPlanSubTaskSpec spec, JObject original, bool defaultEnabled,
        IResinPlanSettingsDialogs? dialogs)
    {
        Spec = spec;
        _dialogs = dialogs;
        _original = original[spec.Key] as JObject;
        _originalEnabled = _original?["enabled"] is { Type: JTokenType.Boolean } enabled ? enabled.Value<bool>() : defaultEnabled;
        _originalParam = _original?["param"] as JObject ?? new JObject();
        _enabled = _originalEnabled;
        _param = (JObject)_originalParam.DeepClone();
    }

    /// <summary>打开子任务设置弹窗；确定后只更新本页草稿，提交仍由宿主负责。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (_dialogs?.EditSubTask(Spec, Param) is { } edited)
        {
            Param = edited;
        }
    }

    /// <summary>开关或参数相对载入时变化时，整体写回子任务对象，保留对象中的其他字段。</summary>
    /// <param name="changes">接收变化的对象。</param>
    public void CollectChanges(JObject changes)
    {
        if (Enabled == _originalEnabled && JToken.DeepEquals(Param, _originalParam))
        {
            return;
        }

        var value = _original is null ? new JObject() : (JObject)_original.DeepClone();
        value["enabled"] = Enabled;
        value["param"] = Param.DeepClone();
        changes[Spec.Key] = value;
    }
}
