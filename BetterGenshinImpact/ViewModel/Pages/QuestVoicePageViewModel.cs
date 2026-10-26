using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>只负责状态投递与用户命令，OCR/捕获/播放均在服务层。</summary>
public partial class QuestVoicePageViewModel : ViewModel
{
    /// <summary>应用级单例，页面导航不拥有配音生命周期。</summary>
    private readonly QuestVoiceService _service;
    /// <summary>资源与路径配置服务。</summary>
    private readonly QuestVoiceSessionFactory _factory;
    /// <summary>WPF 调度器只异步投递，避免与核心同步通知互锁。</summary>
    private readonly Dispatcher _dispatcher;
    /// <summary>合并高频采样状态，UI 最多一个待处理通知。</summary>
    private int _updateQueued;
    /// <summary>统一持久化配置，不另建保存按钮。</summary>
    public QuestVoiceConfig Config { get; }
    /// <summary>读取宿主实际选中的全局捕获方式。</summary>
    public AllConfig AllConfig { get; }
    /// <summary>不可变状态快照，绑定全部使用 OneWay。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigEditable))]
    private QuestVoiceStatus _status;
    /// <summary>命令失败的具体诊断。</summary>
    [ObservableProperty] private string _commandError = "";
    /// <summary>本轮运行时冻结设置，停止后可编辑。</summary>
    public bool IsConfigEditable => !Status.IsRunning && !Status.IsBusy;

    /// <summary>按现有页面约定构造注入。</summary>
    public QuestVoicePageViewModel(AllConfig config, QuestVoiceService service, QuestVoiceSessionFactory factory)
    {
        AllConfig = config; Config = config.QuestVoiceConfig;
        _service = service; _factory = factory;
        _dispatcher = Application.Current.Dispatcher;
        _status = service.Status;
        service.Changed += OnServiceChanged;
    }

    /// <summary>读取最新快照而不是排队时的旧快照，避免多线程事件倒序覆盖。</summary>
    private void OnServiceChanged(QuestVoiceStatus value)
    {
        if (_dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _updateQueued, 1) != 0) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            Interlocked.Exchange(ref _updateQueued, 0);
            Status = _service.Status;
        }));
    }
    /// <summary>真实状态驱动所有生命周期按钮。</summary>
    partial void OnStatusChanged(QuestVoiceStatus value)
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ConfirmBackendRestartedCommand.NotifyCanExecuteChanged();
    }
    /// <summary>防止并发启动；远端未知时先恢复后端。</summary>
    private bool CanStart() => IsConfigEditable && !Status.BackendUnknown;
    /// <summary>启动阶段也可以请求停止。</summary>
    private bool CanStop() => Status.IsRunning || Status.IsBusy;
    /// <summary>只能在完全停止后确认外部恢复动作。</summary>
    private bool CanConfirmBackendRestarted() => IsConfigEditable && Status.BackendUnknown;

    /// <summary>启动真实会话；取消令牌可由停止命令取消。</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        CommandError = "";
        try { await _service.StartAsync(); }
        catch (OperationCanceledException) { CommandError = "启动已取消。"; }
        catch (Exception exception) { CommandError = exception.Message; }
    }
    /// <summary>等待核心排空，期间保持 UI 可响应。</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        CommandError = "";
        try { await _service.StopAsync(); }
        catch (Exception exception) { CommandError = exception.Message; }
    }
    /// <summary>执行真实配置校验。</summary>
    [RelayCommand]
    private async Task ValidateAsync(CancellationToken token)
    {
        CommandError = "";
        try { await _service.ValidateAsync(token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { CommandError = exception.Message; }
    }
    /// <summary>检测 HTTP 可达性，不发送合成或后端控制指令。</summary>
    [RelayCommand]
    private async Task ProbeBackendAsync(CancellationToken token)
    {
        try { await _service.ProbeBackendAsync(token); }
        catch (OperationCanceledException) { }
    }
    /// <summary>用户须先手动重启后端，再执行此明确确认。</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmBackendRestarted))]
    private void ConfirmBackendRestarted() => _service.ConfirmBackendRestarted();
    /// <summary>打开真实外置配置目录，缺失示例按需创建。</summary>
    [RelayCommand]
    private async Task OpenConfigurationAsync()
    {
        try
        {
            await Task.Run(() => _factory.EnsureResourcesAsync(default));
            Process.Start(new ProcessStartInfo(_factory.ConfigurationDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) { CommandError = exception.Message; }
    }
    /// <summary>在关联编辑器中打开实际选择的路由 JSON。</summary>
    [RelayCommand]
    private async Task OpenRoutingAsync()
    {
        try
        {
            await Task.Run(() => _factory.EnsureResourcesAsync(default));
            var path = Path.GetFullPath(Config.RoutingCatalogPath, Global.StartUpPath);
            if (!File.Exists(path)) throw new FileNotFoundException("所选路由文件不存在，请在配置目录中建立或选择现有 JSON。", path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) { CommandError = exception.Message; }
    }
}
