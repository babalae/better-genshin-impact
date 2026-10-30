using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 自动配音设置窗口的宿主模型，“引擎与声线”“角色”“识别”“高级”四页共用同一实例。
/// 应用级单例：关闭窗口后保留未保存草稿，退出守卫也通过它排空草稿；不拥有配音生命周期。
/// </summary>
public partial class QuestVoiceSettingsViewModel : ViewModel
{
    /// <summary>应用级单例，设置窗口不拥有配音生命周期。</summary>
    private readonly QuestVoiceService _service;
    /// <summary>资源与路径配置服务。</summary>
    private readonly QuestVoiceSessionFactory _factory;
    /// <summary>WPF 调度器只异步投递，避免与核心同步通知互锁。</summary>
    private readonly Dispatcher _dispatcher;
    /// <summary>合并高频采样状态，UI 最多一个待处理通知。</summary>
    private int _updateQueued;
    /// <summary>统一持久化配置，不另建保存按钮。</summary>
    public QuestVoiceConfig Config { get; }
    /// <summary>不可变状态快照，绑定全部使用 OneWay。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigEditable))]
    private QuestVoiceStatus _status;
    /// <summary>命令失败的具体诊断。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCommandError))]
    private string _commandError = "";
    /// <summary>试听或停止排空期间暂时冻结编辑；设置窗口打开时配音已暂停。</summary>
    public bool IsConfigEditable => !Status.IsRunning && !Status.IsBusy;
    /// <summary>是否存在需要显示的命令错误。</summary>
    public bool HasCommandError => !string.IsNullOrWhiteSpace(CommandError);
    /// <summary>GPT-SoVITS 的连接与声线库设置。</summary>
    public GptSovitsSettingsViewModel Gpt { get; }
    /// <summary>7 个引擎：GPT-SoVITS 在前，其余引擎随后；★ 表示默认引擎。</summary>
    public IReadOnlyList<QuestVoiceEngineEntry> Engines { get; }
    /// <summary>正在编辑的引擎；与默认引擎在同一页可见。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetDefaultEngineCommand))]
    private QuestVoiceEngineEntry? _selectedEngine;

    /// <summary>按现有约定构造注入；子模型由本模型创建并共享配置与服务。</summary>
    public QuestVoiceSettingsViewModel(AllConfig config, QuestVoiceService service, QuestVoiceSessionFactory factory,
        RoutingCatalogStore store)
    {
        Config = config.QuestVoiceConfig;
        _service = service; _factory = factory; _store = store;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _status = service.Status;
        service.Changed += OnServiceChanged;
        Gpt = new GptSovitsSettingsViewModel(this, factory, service);
        EngineSettingsViewModel[] others =
        [
            new HuoshanSettingsViewModel(this, factory, service),
            new WindowsSapiSettingsViewModel(this, factory, service),
            new VoicevoxSettingsViewModel(this, factory, service),
            new VitsSettingsViewModel(this, factory, service),
            new OpenAiSpeechSettingsViewModel(this, factory, service),
            new GoogleTtsSettingsViewModel(this, factory, service)
        ];
        Engines = new[] { new QuestVoiceEngineEntry(TtsProviderRegistry.GetInfo("gpt-sovits"), Gpt) }
            .Concat(others.Select(x => new QuestVoiceEngineEntry(x.Provider, x))).ToArray();
        Config.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuestVoiceConfig.ProviderId)) RefreshDefaultEngine();
        };
        RefreshDefaultEngine();
        SelectedEngine = Engines.FirstOrDefault(x => x.IsDefault) ?? Engines[0];
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

    /// <summary>切换编辑对象只使旧请求失效；进入 GPT-SoVITS 时选中第一条声线。</summary>
    partial void OnSelectedEngineChanged(QuestVoiceEngineEntry? oldValue, QuestVoiceEngineEntry? newValue)
    {
        (oldValue?.Settings as EngineSettingsViewModel)?.SetActive(false);
        (newValue?.Settings as EngineSettingsViewModel)?.SetActive(true);
        if (newValue?.Settings is GptSovitsSettingsViewModel) _ = Gpt.SelectFirstProfileAsync();
    }

    /// <summary>把正在编辑的引擎设为正式配音使用的默认引擎。</summary>
    [RelayCommand(CanExecute = nameof(CanSetDefaultEngine))]
    private void SetDefaultEngine()
    {
        if (SelectedEngine is { } entry && IsConfigEditable) Config.ProviderId = entry.Provider.Id;
    }

    /// <summary>当前引擎已是默认时不需要再设置。</summary>
    private bool CanSetDefaultEngine() => SelectedEngine is { IsDefault: false };

    /// <summary>默认引擎变化时同步列表中的 ★ 标记。</summary>
    private void RefreshDefaultEngine()
    {
        foreach (var entry in Engines) entry.IsDefault = entry.Provider.Id == Config.ProviderId;
        SetDefaultEngineCommand.NotifyCanExecuteChanged();
    }

    /// <summary>选择角色目录 JSON 文件。</summary>
    [RelayCommand]
    private void BrowseRoutingCatalog()
    {
        if (PickFile("选择角色目录 JSON 文件", "JSON 配置文件 (*.json)|*.json|所有文件 (*.*)|*.*", Config.RoutingCatalogPath, useRelativeIfUnderStartup: true) is { } selected)
        {
            Config.RoutingCatalogPath = selected;
        }
    }

    /// <summary>打开文件选择对话框；支持程序目录内路径规范化。</summary>
    internal static string? PickFile(string title, string filter, string? currentPath, bool useRelativeIfUnderStartup)
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
