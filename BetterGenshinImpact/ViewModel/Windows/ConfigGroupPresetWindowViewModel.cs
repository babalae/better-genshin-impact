using BetterGenshinImpact.Core.Script.Group.Preset;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Message;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 调度器内置预制菜浏览窗口的数据和操作。
/// </summary>
public partial class ConfigGroupPresetWindowViewModel : ObservableObject
{
    private readonly ConfigGroupPresetService _presetService;

    public ObservableCollection<ConfigGroupPresetItem> Presets { get; } = [];

    [ObservableProperty]
    private ConfigGroupPresetItem? _selectedPreset;

    public ConfigGroupPresetWindowViewModel(ConfigGroupPresetService presetService)
    {
        _presetService = presetService;
        foreach (var preset in _presetService.Scan())
        {
            Presets.Add(preset);
        }
    }

    [RelayCommand]
    private async Task ApplyPreset()
    {
        if (SelectedPreset == null)
        {
            return;
        }

        var preset = SelectedPreset;
        var result = _presetService.Apply(preset);
        switch (result.Status)
        {
            case ConfigGroupPresetApplyStatus.Success:
                Toast.Success($"预制菜 {preset.Name} 已应用到调度器");
                WeakReferenceMessenger.Default.Send(new RefreshDataMessage("Refresh"));
                break;
            case ConfigGroupPresetApplyStatus.MissingDependencies:
                await ThemedMessageBox.ShowAsync(
                    $"无法应用预制菜“{preset.Name}”。\n\n依赖不满足：\n- {string.Join("\n- ", result.MissingDependencies)}\n\n请前往订阅上述依赖后再重试。",
                    "预制菜依赖不完整");
                break;
            case ConfigGroupPresetApplyStatus.Conflict:
                await ThemedMessageBox.ShowAsync(
                    $"配置组“{preset.Name}”已存在，请先在调度器中重命名后再应用。",
                    "配置组已存在");
                break;
            default:
                await ThemedMessageBox.ShowAsync(
                    result.ErrorMessage ?? "预制菜应用失败。", "预制菜应用失败");
                break;
        }
    }
}
