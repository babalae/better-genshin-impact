using System;
using System.ComponentModel;
using BetterGenshinImpact.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.View;

/// <summary>
///     单套 ncnn 调优参数的设置页草稿：持有一份 <see cref="NcnnTuningSettings" /> 副本与一份已保存基线，
///     提供脏检查与「恢复默认」。三套（检测 / 识别 / YOLO）各自独立实例，互不影响。
/// </summary>
public partial class NcnnTuningViewModel : ObservableObject
{
    private NcnnTuningSettings _baseline;

    public NcnnTuningViewModel(string title, string description, NcnnTuningSettings baseline)
    {
        Title = title;
        Description = description;
        _baseline = baseline.NormalizedCopy();
        Settings = baseline.NormalizedCopy();
        Settings.PropertyChanged += SettingsChanged;
    }

    public string Title { get; }

    public string Description { get; }

    /// <summary>可绑定的草稿参数（XAML 直接绑定 <c>DetTuning.Settings.UseFp16Packed</c>）。</summary>
    public NcnnTuningSettings Settings { get; }

    public bool HasChanges => !Settings.Equals(_baseline);

    public string ChangeSummary => HasChanges ? "已修改" : "与已保存一致";

    /// <summary>INT8 只有对应量化模型才真正生效，UI 用该提示避免误导。</summary>
    public string Int8Hint => "需 int8 量化模型；未量化模型不会因为开启而变快。";

    public string PackingHint => "packing layout 仅在 ARM 等支持打包的平台上带来收益，x86 显卡通常无变化。";

    public string PixelInputHint => "像素直输省掉一次 CHW 浮点写入，但要求模型输入为 0–255（自带归一化），默认关闭。";

    /// <summary>恢复本套参数为 ncnn 默认值（不影响其他两套）。</summary>
    [RelayCommand]
    private void ResetToDefault() => Settings.CopyFrom(NcnnTuningSettings.CreateDefault(_baseline.Threads));

    /// <summary>把当前草稿标记为已保存基线（应用成功后调用）。</summary>
    public void AcceptBaseline()
    {
        _baseline = Settings.NormalizedCopy();
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ChangeSummary));
    }

    /// <summary>用外部值重置草稿与基线（配置重载后调用）。</summary>
    public void Reload(NcnnTuningSettings baseline)
    {
        _baseline = baseline.NormalizedCopy();
        Settings.CopyFrom(baseline);
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ChangeSummary));
    }

    public NcnnTuningSettings Snapshot() => Settings.NormalizedCopy();

    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ChangeSummary));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>任意字段变化（主 ViewModel 据此刷新底部摘要与按钮状态）。</summary>
    public event EventHandler? Changed;
}
