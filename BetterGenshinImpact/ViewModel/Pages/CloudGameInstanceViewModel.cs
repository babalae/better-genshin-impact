using BetterGenshinImpact.Core.CloudGame;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Profile;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask.Session;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Wpf.Ui.Violeta.Controls;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 首页单张云原神实例卡片的 ViewModel。
/// 负责实例窗口生命周期、显示模式、任务启停、Profile 持久化及当前会话日志展示。
/// </summary>
public partial class CloudGameInstanceViewModel : ObservableObject
{
    /// <summary>
    /// 不执行脚本组、只保持实时触发器运行的任务选项。
    /// </summary>
    public const string TriggerOnlyTask = "仅运行实时触发器";

    // 当前卡片对应的持久化 Profile。
    private readonly BetterGiProfile _profile;

    // 负责 Profile 和云模块配置读写。
    private readonly ProfileService _profileService;

    // 负责创建、查找和销毁当前 Profile 对应会话。
    private readonly GameSessionManager _sessionManager;

    // 执行用户选择的现有 BetterGI 脚本组。
    private readonly IScriptService _scriptService;

    // 本期所有云实例共享的全局 BetterGI 配置。
    private readonly AllConfig _config;

    // 记录当前实例生命周期和任务异常。
    private readonly ILogger<CloudGameInstanceViewModel> _logger = App.GetLogger<CloudGameInstanceViewModel>();

    // 当前已启动会话；浏览器未打开时为空。
    private GameSession? _session;

    // 卡片可编辑实例名称，由源生成器公开为 Name。
    [ObservableProperty]
    private string _name;

    // 当前连接或任务状态文本，由源生成器公开为 Status。
    [ObservableProperty]
    private string _status = "未启动";

    // WebView2 会话是否已经创建，由源生成器公开为 IsBrowserStarted。
    [ObservableProperty]
    private bool _isBrowserStarted;

    // 当前卡片是否正在运行脚本或实时触发器，由源生成器公开为 IsTaskRunning。
    [ObservableProperty]
    private bool _isTaskRunning;

    // 用户选中的脚本组名称，由源生成器公开为 SelectedTask。
    [ObservableProperty]
    private string _selectedTask = TriggerOnlyTask;

    /// <summary>
    /// 创建一张云原神实例卡片并恢复最近选择的任务。
    /// </summary>
    /// <param name="profile">卡片对应的 Profile。</param>
    /// <param name="profileService">Profile 持久化服务。</param>
    /// <param name="sessionManager">游戏会话管理器。</param>
    /// <param name="scriptService">现有脚本执行服务。</param>
    /// <param name="config">全局 BetterGI 配置。</param>
    public CloudGameInstanceViewModel(
        BetterGiProfile profile,
        ProfileService profileService,
        GameSessionManager sessionManager,
        IScriptService scriptService,
        AllConfig config)
    {
        _profile = profile;
        _profileService = profileService;
        _sessionManager = sessionManager;
        _scriptService = scriptService;
        _config = config;
        _name = profile.Name;

        // 固定选项始终位于列表首项，再追加磁盘中的脚本组。
        TaskOptions.Add(TriggerOnlyTask);
        ReloadTaskOptions();

        // 仅当原任务仍存在时才恢复选择，避免绑定到已删除脚本组。
        var module = _profileService.ReadModuleConfig(profile.Id);
        if (!string.IsNullOrWhiteSpace(module.LastTask) && TaskOptions.Contains(module.LastTask))
        {
            SelectedTask = module.LastTask;
        }
    }

    /// <summary>
    /// 获取当前可选择的实时触发器模式和脚本组名称。
    /// </summary>
    public ObservableCollection<string> TaskOptions { get; } = [];

    /// <summary>
    /// 获取当前实例最近一百条带 SessionId 的日志。
    /// </summary>
    public ObservableCollection<string> LogEntries { get; } = [];

    /// <summary>
    /// 实例名称完成绑定更新时同步保存 Profile。
    /// </summary>
    partial void OnNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // 去除首尾空白，索引文件与实例 profile.json 同步更新。
        _profile.Name = value.Trim();
        _profileService.SaveProfile(_profile);
    }

    /// <summary>
    /// 用户切换任务时保存模块级 LastTask。
    /// </summary>
    partial void OnSelectedTaskChanged(string value)
    {
        // 只更新当前 Profile 的云模块配置。
        var module = _profileService.ReadModuleConfig(_profile.Id);
        module.LastTask = value;
        _profileService.WriteModuleConfig(_profile.Id, module);
    }

    /// <summary>
    /// 创建或显示当前 Profile 对应的独立 WebView2 窗口。
    /// 再次点击时会将已有实例切回交互显示。
    /// </summary>
    [RelayCommand]
    private async Task OpenBrowserAsync()
    {
        if (_session != null)
        {
            // 已启动实例不重复创建，只切回交互模式。
            await _session.Target.ShowInteractiveAsync();
            return;
        }

        try
        {
            Status = "正在创建 WebView2";
            _session = await _sessionManager.CreateCloudSessionAsync(_profile);

            // 订阅会话状态和日志，并先复制订阅前已经产生的启动日志。
            _session.StateChanged += OnSessionStateChanged;
            _session.LogAdded += OnSessionLogAdded;
            foreach (var entry in _session.LogEntries)
            {
                LogEntries.Add(entry);
            }
            IsBrowserStarted = true;
            UpdateSessionState();
        }
        catch (Exception e)
        {
            Status = $"启动失败：{e.GetBaseException().Message}";
            _logger.LogError(e, "启动云原神实例失败，ProfileId={ProfileId}", _profile.Id);
            await ThemedMessageBox.ErrorAsync(Status);
        }
    }

    /// <summary>
    /// 关闭当前 Profile 的 WebView2 会话并清空卡片运行状态。
    /// </summary>
    [RelayCommand]
    private async Task CloseBrowserAsync()
    {
        if (_session == null)
        {
            return;
        }

        try
        {
            // 先取消订阅，防止释放过程继续更新即将重置的卡片。
            _session.StateChanged -= OnSessionStateChanged;
            _session.LogAdded -= OnSessionLogAdded;
            await _sessionManager.StopSessionAsync(_profile.Id);
        }
        finally
        {
            // 即使释放过程抛出异常，卡片也不能继续持有失效会话引用。
            _session = null;
            IsBrowserStarted = false;
            IsTaskRunning = false;
            Status = "未启动";
            LogEntries.Clear();
        }
    }

    /// <summary>
    /// 确保会话存在后切换为可交互显示。
    /// </summary>
    [RelayCommand]
    private async Task ShowInteractiveAsync()
    {
        await EnsureSessionAsync();
        if (_session != null)
        {
            await _session.Target.ShowInteractiveAsync();
            SaveDisplayMode(CloudGameDisplayMode.Interactive);
        }
    }

    /// <summary>
    /// 确保会话存在后切换为只读显示。
    /// </summary>
    [RelayCommand]
    private async Task ShowReadOnlyAsync()
    {
        await EnsureSessionAsync();
        if (_session != null)
        {
            await _session.Target.ShowReadOnlyAsync();
            SaveDisplayMode(CloudGameDisplayMode.ReadOnly);
        }
    }

    /// <summary>
    /// 确保会话存在后仅隐藏父 HWND。
    /// </summary>
    [RelayCommand]
    private async Task HideBrowserAsync()
    {
        await EnsureSessionAsync();
        if (_session != null)
        {
            await _session.Target.HideAsync();
            SaveDisplayMode(CloudGameDisplayMode.Hidden);
        }
    }

    /// <summary>
    /// 在当前会话作用域中启动实时触发器，并按选择执行脚本组。
    /// </summary>
    [RelayCommand]
    private async Task StartTaskAsync()
    {
        await EnsureSessionAsync();
        if (_session == null)
        {
            return;
        }
        if (_session.State is not GameSessionState.Ready and not GameSessionState.Running)
        {
            // 输入私有 API 未就绪时禁止启动，且不回退到系统键鼠。
            await ThemedMessageBox.WarningAsync("云原神尚未进入游戏或 RTC 输入通道未就绪");
            return;
        }

        try
        {
            // 实时调度器始终先启动，脚本组识图从同一会话截图源读取最新帧。
            _session.StartDispatcher(_config.TriggerInterval);
            _session.UpdateState(GameSessionState.Running, $"正在运行：{SelectedTask}");
            IsTaskRunning = true;

            if (SelectedTask == TriggerOnlyTask)
            {
                // 仅实时触发器模式由用户显式点击停止，不进入脚本服务。
                return;
            }

            // 脚本读取完成后显式进入会话作用域，所有兼容代理将路由到当前实例。
            var group = ReadScriptGroup(SelectedTask);

            // 作用域覆盖整个异步脚本执行过程，并在结束后恢复调用方上下文。
            using var scope = GameSessionContext.Enter(_session);
            await _scriptService.RunMulti(group.Projects, group.Name);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "云原神任务执行失败，SessionId={SessionId}", _session.Id);
            await ThemedMessageBox.ErrorAsync($"云原神任务执行失败：{e.GetBaseException().Message}");
        }
        finally
        {
            if (SelectedTask != TriggerOnlyTask && _session != null)
            {
                // 脚本组自然结束后保留 WebView2 和连接，只恢复 Ready。
                IsTaskRunning = false;
                _session.UpdateState(GameSessionState.Ready, "云游戏连接已就绪");
            }
        }
    }

    /// <summary>
    /// 取消当前会话任务、停止调度器并尽力释放所有云端输入状态。
    /// </summary>
    [RelayCommand]
    private async Task StopTaskAsync()
    {
        if (_session == null)
        {
            return;
        }

        // ManualCancel、Dispatcher 和输入释放都必须解析到同一会话。
        // 停止流程中的静态 API 必须解析到当前卡片会话。
        using var scope = GameSessionContext.Enter(_session);
        _session.CancellationContext.ManualCancel();
        _session.StopDispatcher();
        try
        {
            await _session.Input.ReleaseAllAsync();
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "停止云原神任务时释放输入失败，SessionId={SessionId}", _session.Id);
        }
        IsTaskRunning = false;
        _session.UpdateState(GameSessionState.Ready, "任务已停止");
    }

    /// <summary>
    /// 确保卡片已经创建 WebView2 会话。
    /// </summary>
    private async Task EnsureSessionAsync()
    {
        if (_session == null)
        {
            await OpenBrowserAsync();
        }
    }

    /// <summary>
    /// 将后台健康检查产生的状态变化切换到 UI Dispatcher。
    /// </summary>
    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        UIDispatcherHelper.Invoke(UpdateSessionState);
    }

    /// <summary>
    /// 将后台会话日志追加到当前卡片的 UI 集合。
    /// </summary>
    private void OnSessionLogAdded(object? sender, string entry)
    {
        UIDispatcherHelper.Invoke(() =>
        {
            LogEntries.Add(entry);
            while (LogEntries.Count > 100)
            {
                // ViewModel 与会话使用相同上限，避免 UI 集合无限增长。
                LogEntries.RemoveAt(0);
            }
        });
    }

    /// <summary>
    /// 从当前会话刷新卡片状态文本。
    /// </summary>
    private void UpdateSessionState()
    {
        if (_session != null)
        {
            Status = _session.StatusMessage;
        }
    }

    /// <summary>
    /// 从 User/ScriptGroup 加载可执行脚本组名称。
    /// </summary>
    private void ReloadTaskOptions()
    {
        // 与现有脚本组页面使用同一目录，不复制或创建云专用任务配置。
        var path = Global.Absolute(@"User\ScriptGroup");
        if (!Directory.Exists(path))
        {
            return;
        }

        // 仅展示 JSON 文件名，并按名称稳定排序。
        foreach (var name in Directory.EnumerateFiles(path, "*.json")
                     .Select(Path.GetFileNameWithoutExtension)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .OrderBy(x => x))
        {
            TaskOptions.Add(name!);
        }
    }

    /// <summary>
    /// 按名称从现有脚本组目录读取并反序列化任务。
    /// </summary>
    private static ScriptGroup ReadScriptGroup(string name)
    {
        // SelectedTask 保存的是不带扩展名的文件名。
        var path = Path.Combine(Global.Absolute(@"User\ScriptGroup"), $"{name}.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"找不到配置组：{name}", path);
        }
        return ScriptGroup.FromJson(File.ReadAllText(path));
    }

    /// <summary>
    /// 将最近选择的显示模式写入当前 Profile 的云模块配置。
    /// </summary>
    private void SaveDisplayMode(CloudGameDisplayMode mode)
    {
        // 显示模式属于云模块数据，不写入通用 profile.json。
        var module = _profileService.ReadModuleConfig(_profile.Id);
        module.DisplayMode = mode.ToString();
        _profileService.WriteModuleConfig(_profile.Id, module);
    }
}
