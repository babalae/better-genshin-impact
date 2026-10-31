using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Plugins.Hosting;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.ViewModel.Pages.Plugins;

/// <summary>
/// 插件页“已安装插件”中的一项：显示插件信息与状态，启用开关直接启用或停用插件。
/// </summary>
public sealed partial class InstalledPluginItemViewModel : ObservableObject
{
    /// <summary>
    /// 插件宿主。
    /// </summary>
    private readonly PluginManager _manager;

    /// <summary>
    /// 按可插拔能力标识取显示名称。
    /// </summary>
    private readonly Func<string, string> _pluggableName;

    /// <summary>
    /// 正在按插件状态刷新开关，期间的开关变化不触发启用或停用。
    /// </summary>
    private bool _syncing;

    /// <summary>
    /// 启用开关。
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>
    /// 是否正在启用或停用。
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// 插件设置弹窗。
    /// </summary>
    private readonly IPluginSettingsDialogs _settingsDialogs;

    /// <summary>
    /// 创建插件项。
    /// </summary>
    /// <param name="entry">插件条目。</param>
    /// <param name="manager">插件宿主。</param>
    /// <param name="pluggableName">按可插拔能力标识取显示名称。</param>
    /// <param name="settingsDialogs">插件设置弹窗。</param>
    public InstalledPluginItemViewModel(PluginEntry entry, PluginManager manager, Func<string, string> pluggableName,
        IPluginSettingsDialogs settingsDialogs)
    {
        Entry = entry;
        _manager = manager;
        _pluggableName = pluggableName;
        _settingsDialogs = settingsDialogs;
        Entry.PropertyChanged += OnEntryPropertyChanged;
        SyncFromEntry();
    }

    /// <summary>
    /// 插件条目。
    /// </summary>
    public PluginEntry Entry { get; }

    /// <summary>
    /// 插件名称。
    /// </summary>
    public string Name => Entry.Manifest.Name;

    /// <summary>
    /// 插件标识。
    /// </summary>
    public string Id => Entry.Manifest.Id;

    /// <summary>
    /// 版本与作者。
    /// </summary>
    public string VersionText => Entry.Manifest.Authors.Count == 0
        ? Entry.Manifest.Version
        : $"{Entry.Manifest.Version} · {string.Join("、", Entry.Manifest.Authors)}";

    /// <summary>
    /// 插件说明。
    /// </summary>
    public string Description => Entry.Manifest.Description;

    /// <summary>
    /// 状态文字。
    /// </summary>
    public string StatusText
    {
        get
        {
            var text = Entry.State switch
            {
                PluginState.Enabled => "已启用",
                PluginState.Disabling => "正在停用，等待进行中的调用结束",
                PluginState.Incompatible => $"不兼容：{Entry.Error}",
                PluginState.LoadFailed => $"加载失败：{Entry.Error}",
                PluginState.Duplicate => $"标识重复：{Entry.Error}",
                PluginState.Uninstalled => "已卸载，文件将在下次启动时删除",
                _ => "未启用",
            };
            return Entry.PendingVersion is { } pending ? $"{text} · 重启后更新到 {pending}" : text;
        }
    }

    /// <summary>
    /// 状态是否表示有问题，界面用醒目颜色显示。
    /// </summary>
    public bool IsProblem => Entry.State is PluginState.Incompatible or PluginState.LoadFailed or PluginState.Duplicate;

    /// <summary>
    /// 开关是否可用。
    /// </summary>
    public bool CanToggle => !IsBusy && Entry.State is PluginState.Disabled or PluginState.Enabled or PluginState.LoadFailed;

    /// <summary>
    /// 是否可以卸载。
    /// </summary>
    public bool CanUninstall => !IsBusy && Entry.State is not (PluginState.Disabling or PluginState.Uninstalled);

    /// <summary>
    /// 插件提供的功能，按可插拔能力列出实现名称。
    /// </summary>
    public string ImplementationsText => Entry.Implementations.Count == 0
        ? "启用后显示"
        : string.Join("；", Entry.Implementations
            .GroupBy(i => i.PluggableId)
            .Select(g => $"{_pluggableName(g.Key)}（{string.Join("、", g.Select(i => i.DisplayName))}）"));

    /// <summary>
    /// 是否有可打开的主页。
    /// </summary>
    public bool HasHomepage => TryGetHomepage(out _);

    /// <summary>
    /// 是否显示“设置”按钮：插件声明了设置界面且未被卸载。插件程序集加载（第一次启用）之后才能知道有没有设置界面。
    /// </summary>
    public bool HasSettings => Entry.Runtime?.SettingsViewModelType is not null && Entry.State != PluginState.Uninstalled;

    /// <summary>
    /// 停止跟踪插件条目的变化。插件项被移除时调用。
    /// </summary>
    public void Detach() => Entry.PropertyChanged -= OnEntryPropertyChanged;

    /// <summary>
    /// 打开插件目录。
    /// </summary>
    [RelayCommand]
    private void OpenDirectory()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Entry.Directory}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e)
        {
            _ = ThemedMessageBox.ErrorAsync($"无法打开目录：{e.Message}", "插件");
        }
    }

    /// <summary>
    /// 在弹窗中打开插件设置。每次打开用插件服务容器新建设置 VM（构造函数可以注入插件上下文），读到的是已保存的设置；
    /// 窗口关闭后释放实现了 IDisposable 的 VM。
    /// </summary>
    [RelayCommand]
    private void OpenSettings()
    {
        if (!HasSettings || Entry.Runtime is not { SettingsViewModelType: { } vmType } runtime)
        {
            return;
        }

        object settingsViewModel;
        try
        {
            settingsViewModel = ActivatorUtilities.CreateInstance(runtime.Services, vmType);
        }
        catch (Exception e)
        {
            _ = ThemedMessageBox.ErrorAsync($"无法打开插件“{Name}”的设置：{e.GetBaseException().Message}", "插件设置");
            return;
        }

        try
        {
            _settingsDialogs.Show(Name, settingsViewModel);
        }
        finally
        {
            (settingsViewModel as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// 在浏览器中打开插件主页。
    /// </summary>
    [RelayCommand]
    private void OpenHomepage()
    {
        if (!TryGetHomepage(out var uri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e)
        {
            _ = ThemedMessageBox.ErrorAsync($"无法打开主页：{e.Message}", "插件");
        }
    }

    /// <summary>
    /// 开关被用户切换时启用或停用插件。
    /// </summary>
    partial void OnIsEnabledChanged(bool value)
    {
        if (!_syncing)
        {
            _ = ApplyEnabledAsync(value);
        }
    }

    /// <summary>
    /// 是否正在启用或停用变化时，刷新依赖它的按钮状态。
    /// </summary>
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(CanUninstall));
    }

    /// <summary>
    /// 启用或停用插件；失败时提示原因，开关恢复为插件的实际状态。
    /// </summary>
    private async Task ApplyEnabledAsync(bool enable)
    {
        IsBusy = true;
        try
        {
            if (enable)
            {
                if (!_manager.Enable(Entry))
                {
                    await ThemedMessageBox.ErrorAsync($"启用插件“{Name}”失败：{Entry.Error}", "启用插件");
                }
            }
            else
            {
                await _manager.DisableAsync(Entry);
            }
        }
        finally
        {
            IsBusy = false;
            SyncFromEntry();
        }
    }

    /// <summary>
    /// 插件条目变化时刷新显示。
    /// </summary>
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e) => SyncFromEntry();

    /// <summary>
    /// 按插件状态刷新开关和全部显示属性。
    /// </summary>
    private void SyncFromEntry()
    {
        _syncing = true;
        try
        {
            IsEnabled = Entry.State is PluginState.Enabled or PluginState.Disabling;
        }
        finally
        {
            _syncing = false;
        }

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsProblem));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(ImplementationsText));
        OnPropertyChanged(nameof(HasHomepage));
        OnPropertyChanged(nameof(HasSettings));
    }

    /// <summary>
    /// 只接受 http、https 主页。
    /// </summary>
    private bool TryGetHomepage(out Uri uri)
    {
        if (Uri.TryCreate(Entry.Manifest.Homepage, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }
}
