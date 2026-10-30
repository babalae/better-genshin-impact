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
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>实时触发页“自动配音”卡片：开关、运行状态、最近事件和设置入口；不拥有配音生命周期。</summary>
public partial class QuestVoiceCardViewModel : ObservableObject
{
    /// <summary>应用级配音服务，按开关和截图器状态自动启停。</summary>
    private readonly QuestVoiceService _service;
    /// <summary>设置窗口的单例模型，关闭窗口后保留草稿。</summary>
    private readonly QuestVoiceSettingsViewModel _settings;
    /// <summary>WPF 调度器只异步投递，避免与核心同步通知互锁。</summary>
    private readonly Dispatcher _dispatcher;
    /// <summary>合并高频采样状态，UI 最多一个待处理通知。</summary>
    private int _updateQueued;
    /// <summary>开关与其他设置，由 AllConfig 自动保存。</summary>
    public QuestVoiceConfig Config { get; }
    /// <summary>不可变状态快照，绑定全部使用 OneWay。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmBackendRestartedCommand))]
    private QuestVoiceStatus _status;
    /// <summary>命令失败的具体诊断。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCommandError))]
    private string _commandError = "";
    /// <summary>是否存在需要显示的命令错误。</summary>
    public bool HasCommandError => !string.IsNullOrWhiteSpace(CommandError);

    /// <summary>构造注入服务与设置模型。</summary>
    public QuestVoiceCardViewModel(AllConfig config, QuestVoiceService service, QuestVoiceSettingsViewModel settings)
    {
        Config = config.QuestVoiceConfig;
        _service = service;
        _settings = settings;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
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

    /// <summary>打开设置窗口：窗口打开期间暂停配音，关闭后先保存角色目录草稿，再按新设置恢复。</summary>
    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        CommandError = "";
        using var pause = _service.BeginSettingsEdit();
        try
        {
            await _settings.Management.EnsureLoadedAsync();
            var window = new QuestVoiceSettingsWindow(_settings) { Owner = Application.Current.MainWindow };
            window.ShowDialog();
            if (!await _settings.FlushSettingsAsync())
                CommandError = "角色目录有未保存的修改：请在设置窗口修正输入或处理文件冲突。";
        }
        catch (Exception exception) { CommandError = exception.Message; }
    }

    /// <summary>执行静态配置校验，结果显示在卡片中。</summary>
    [RelayCommand]
    private async Task ValidateAsync(CancellationToken token)
    {
        CommandError = "";
        try { await _service.ValidateAsync(token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { CommandError = exception.Message; }
    }

    /// <summary>用户手动重启 GPT-SoVITS 后明确确认，之后按开关自动恢复。</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmBackendRestarted))]
    private void ConfirmBackendRestarted()
    {
        try { _service.ConfirmBackendRestarted(); }
        catch (Exception exception) { CommandError = exception.Message; }
    }

    /// <summary>只有后端状态未知且没有在运行时才需要确认。</summary>
    private bool CanConfirmBackendRestarted() => Status.BackendUnknown && !Status.IsRunning && !Status.IsBusy;

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
}
