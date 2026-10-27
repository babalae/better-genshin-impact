using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>只负责状态投递与用户命令，OCR/捕获/播放均在服务层。两个子页面共用同一实例，切换页面不中断会话。</summary>
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCommandError))]
    private string _commandError = "";
    /// <summary>本轮运行时冻结设置，停止后可编辑。</summary>
    public bool IsConfigEditable => !Status.IsRunning && !Status.IsBusy;
    /// <summary>是否存在需要显示的命令错误。</summary>
    public bool HasCommandError => !string.IsNullOrWhiteSpace(CommandError);

    /// <summary>按现有页面约定构造注入。</summary>
    public QuestVoicePageViewModel(AllConfig config, QuestVoiceService service, QuestVoiceSessionFactory factory)
    {
        AllConfig = config; Config = config.QuestVoiceConfig;
        _service = service; _factory = factory;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _status = service.Status;
        service.Changed += OnServiceChanged;
        Config.PropertyChanged += OnTtsConfigChanged;
        ObserveEngineConfigs();
        RefreshVoicesForSelection();
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
        OnPropertyChanged(nameof(RequiresBackendDrain));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ConfirmBackendRestartedCommand.NotifyCanExecuteChanged();
        TestSpeechCommand.NotifyCanExecuteChanged();
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
        try
        {
            if (!await FlushSettingsAsync()) { CommandError = "路由草稿尚未保存，请先修正配置或处理冲突。"; return; }
            await _service.StartAsync();
        }
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

    /// <summary>打开 BetterGI 当前主日志文件；若尚无日志文件则打开日志目录。</summary>
    [RelayCommand]
    private void OpenLogFile()
    {
        try
        {
            var logFolder = Global.Absolute("log");
            Directory.CreateDirectory(logFolder);
            var latestLog = new DirectoryInfo(logFolder)
                .GetFiles("better-genshin-impact*.log")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            var target = latestLog?.FullName ?? logFolder;
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception exception) { CommandError = exception.Message; }
    }

    /// <summary>复用 BetterGI 既有日志目录入口打开 log 文件夹。</summary>
    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            var logFolder = Global.Absolute("log");
            Directory.CreateDirectory(logFolder);
            Process.Start("explorer.exe", logFolder);
        }
        catch (Exception exception) { CommandError = exception.Message; }
    }

    /// <summary>选择角色路由 JSON 配置文件。</summary>
    [RelayCommand]
    private void BrowseRoutingCatalog()
    {
        if (PickFile("选择角色路由 JSON 配置文件", "JSON 配置文件 (*.json)|*.json|所有文件 (*.*)|*.*", Config.RoutingCatalogPath, useRelativeIfUnderStartup: true) is { } selected)
        {
            Config.RoutingCatalogPath = selected;
        }
    }

    /// <summary>选择 GPT 权重（.ckpt）文件。</summary>
    [RelayCommand]
    private void BrowseGptWeights()
    {
        if (PickFile("选择 GPT 权重文件 (.ckpt)", "GPT 权重文件 (*.ckpt)|*.ckpt|所有文件 (*.*)|*.*", Config.GptWeightsPath, useRelativeIfUnderStartup: false) is { } selected)
        {
            Config.GptWeightsPath = selected;
        }
    }

    /// <summary>选择 SoVITS 权重（.pth）文件。</summary>
    [RelayCommand]
    private void BrowseSovitsWeights()
    {
        if (PickFile("选择 SoVITS 权重文件 (.pth)", "SoVITS 权重文件 (*.pth)|*.pth|所有文件 (*.*)|*.*", Config.SovitsWeightsPath, useRelativeIfUnderStartup: false) is { } selected)
        {
            Config.SovitsWeightsPath = selected;
        }
    }

    /// <summary>选择参考音频文件。</summary>
    [RelayCommand]
    private void BrowseReferenceAudio()
    {
        if (PickFile("选择参考音频文件", "音频文件 (*.wav;*.mp3;*.flac;*.ogg)|*.wav;*.mp3;*.flac;*.ogg|所有文件 (*.*)|*.*", Config.ReferenceAudioPath, useRelativeIfUnderStartup: false) is { } selected)
        {
            Config.ReferenceAudioPath = selected;
        }
    }

    /// <summary>选择 PP-OCRv6 Rec ONNX 模型文件。</summary>
    [RelayCommand]
    private void BrowseOcrModel()
    {
        if (PickFile("选择 PP-OCRv6 Rec ONNX 模型文件", "ONNX 模型文件 (*.onnx)|*.onnx|所有文件 (*.*)|*.*", Config.OcrModelPath, useRelativeIfUnderStartup: true) is { } selected)
        {
            Config.OcrModelPath = selected;
        }
    }

    /// <summary>打开文件选择对话框；支持程序目录内路径规范化。</summary>
    private static string? PickFile(string title, string filter, string? currentPath, bool useRelativeIfUnderStartup)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = false
        };
        try
        {
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                var full = Path.GetFullPath(currentPath, Global.StartUpPath);
                var directory = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }
            }
        }
        catch
        {
            // 忽略无效初始路径，使用系统默认目录打开对话框。
        }

        if (dialog.ShowDialog() != true) return null;
        if (useRelativeIfUnderStartup)
        {
            try
            {
                var startup = Path.GetFullPath(Global.StartUpPath);
                var chosen = Path.GetFullPath(dialog.FileName);
                if (chosen.StartsWith(startup, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetRelativePath(startup, chosen).Replace('\\', '/');
                }
            }
            catch
            {
                // 转换相对路径失败时直接保留完整路径。
            }
        }
        return dialog.FileName;
    }
}
