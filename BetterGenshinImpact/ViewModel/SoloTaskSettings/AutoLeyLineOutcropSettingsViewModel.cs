using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.ViewModel.Resin;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>自动地脉花任务的专属设置草稿；对应 auto_ley_line_outcrop 的常用参数，沿用旧一条龙卡片布局。</summary>
public partial class AutoLeyLineOutcropSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <inheritdoc />
    public override string[] EditedParameterNames =>
    [
        "one_dragon_mode", "resin_usage"
    ];

    /// <summary>载入时的跳过准备流程快照。</summary>
    private readonly bool _originalOneDragonMode;

    /// <summary>是否跳过部分准备流程（例如传送回七天神像）。</summary>
    [ObservableProperty] private bool _oneDragonMode;

    /// <summary>从当前有效参数读取地脉花配置。</summary>
    /// <param name="original">当前有效参数快照。</param>
    public AutoLeyLineOutcropSettingsViewModel(JObject original) : this(original, null) { }

    /// <summary>初始化后的树脂快照，用于按结构收集变化。</summary>
    private readonly ResinUsage _originalResin;
    /// <summary>树脂使用设置入口。</summary>
    public ResinUsageEntryViewModel Resin { get; }

    /// <summary>注入可选的树脂弹窗服务，不依赖应用静态容器。</summary>
    [ActivatorUtilitiesConstructor]
    public AutoLeyLineOutcropSettingsViewModel(JObject original, IResinSettingsDialogs? dialogs = null) : base(original)
    {
        _originalResin = ResinUsageViewModelSupport.Read(Original["resin_usage"], AutoLeyLineOutcropTask.ResinCapability);
        Resin = new("自动地脉花", AutoLeyLineOutcropTask.ResinCapability, _originalResin, dialogs);
        TrackEdits([Resin]);
        _originalOneDragonMode = ReadBool(Original, "one_dragon_mode", true);
        _oneDragonMode = _originalOneDragonMode;
    }

    /// <summary>收集地脉花配置的真实变化。</summary>
    /// <param name="changes">接收变化的对象。</param>
    public override void CollectChanges(JObject changes)
    {
        if (Resin.Value != _originalResin) changes["resin_usage"] = JObject.Parse(SoloTaskJson.Serialize(Resin.Value));
        WriteBool(changes, "one_dragon_mode", _originalOneDragonMode, OneDragonMode);
    }
}
