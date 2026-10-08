using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Service.Worker;

/// <summary>
/// Worker 的任务执行端口：复用现有 TaskRunner / CancellationContext / RunnerContext，
/// 不重新实现任务系统，也不使用 Process.Kill / Environment.Exit / Thread.Abort。
/// </summary>
public sealed class WorkerTaskExecutor
{
    public const string ScriptGroupTaskType = WorkerTaskTypes.ScriptGroup;
    public const string StartGameTaskType = WorkerTaskTypes.StartGame;

    /// <summary>启动/停止截图器时占用的当前任务标记</summary>
    private const string CaptureTaskType = "capture";

    private static readonly string ScriptGroupDirectory = Global.Absolute(@"User\ScriptGroup");

    private readonly InstanceContext _context;
    private readonly ILogger<WorkerTaskExecutor> _logger;
    private readonly object _sync = new();

    private WorkerState _state = WorkerState.Idle;
    private string? _currentTask;
    private string? _errorMessage;

    public WorkerTaskExecutor(
        InstanceBootstrap bootstrap,
        ILogger<WorkerTaskExecutor> logger)
    {
        _context = bootstrap.Context;
        _logger = logger;
    }

    public WorkerStatusResponse Snapshot()
    {
        lock (_sync)
        {
            return new WorkerStatusResponse
            {
                WorkerSid = _context.WindowsUserSid,
                SessionId = _context.WindowsSessionId,
                ProcessId = _context.ProcessId,
                InstanceId = _context.InstanceId,
                State = _state,
                CurrentTask = _currentTask,
                ErrorMessage = _errorMessage,
                CaptureRunning = IsCaptureRunning()
            };
        }
    }

    /// <summary>
    /// Worker 自己的截图器是否在运行。截图器属于运行环境服务，未构造时视为未运行。
    /// </summary>
    private static bool IsCaptureRunning()
    {
        try
        {
            return App.GetService<GameRuntimeService>()?.IsRunning ?? false;
        }
        catch (Exception)
        {
            // 状态查询不能因为服务未就绪而失败
            return false;
        }
    }

    /// <summary>
    /// 启动 Worker 自己的截图器（含遮罩叠加层）。已在运行时直接成功。
    /// </summary>
    public async Task<(bool Success, string? Error)> TryStartCaptureAsync()
    {
        lock (_sync)
        {
            if (_state is WorkerState.Starting or WorkerState.RunningTask or WorkerState.Stopping)
            {
                return (false, $"Worker 正在执行任务（{_state}），拒绝启动截图器。");
            }

            _state = WorkerState.Starting;
            _currentTask = CaptureTaskType;
            _errorMessage = null;
        }

        string? failure = null;
        var started = false;
        try
        {
            var runtimeService = App.GetService<GameRuntimeService>()
                                 ?? throw new InvalidOperationException("运行环境服务未注册。");
            started = await runtimeService.StartAsync().ConfigureAwait(false);
            if (!started && runtimeService.IsRunning)
            {
                // 启动请求被拒绝但已经在运行：视为成功
                started = true;
            }
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            _logger.LogError(exception, "Worker 启动截图器失败");
        }

        CompleteExecution(failure);
        return (started, failure);
    }

    /// <summary>
    /// 停止 Worker 自己的截图器。正在运行的任务会随运行环境解绑一起取消。
    /// </summary>
    /// <summary>
    /// 退出 Worker 侧的游戏：先让游戏窗口获得焦点，再映射一次 Alt+F4。
    /// 游戏对键鼠的响应有延迟，按下和抬起之间留出间隔。
    /// </summary>
    public (bool Success, string? Error) TryExitGame()
    {
        try
        {
            if (TaskContext.Instance().Runtime is null)
            {
                return (false, "Worker 侧尚未启动截图器，无法退出游戏。");
            }

            // Alt+F4 只会送给前台窗口，先激活游戏窗口
            SystemControl.ActivateWindow();
            Thread.Sleep(300);

            InputHub.Foreground.Keyboard
                .KeyDown(User32.VK.VK_LMENU)
                .Sleep(50)
                .KeyDown(User32.VK.VK_F4)
                .Sleep(50)
                .KeyUp(User32.VK.VK_F4)
                .KeyUp(User32.VK.VK_LMENU);

            _logger.LogInformation("已向 Worker 侧游戏窗口映射 Alt+F4");
            return (true, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Worker 退出游戏失败");
            return (false, exception.GetBaseException().Message);
        }
    }

    public async Task<(bool Success, string? Error)> TryStopCaptureAsync()
    {
        string? failure = null;
        try
        {
            var runtimeService = App.GetService<GameRuntimeService>()
                                 ?? throw new InvalidOperationException("运行环境服务未注册。");
            await runtimeService.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            _logger.LogError(exception, "Worker 停止截图器失败");
        }

        return (failure is null, failure);
    }

    /// <summary>捕获当前游戏画面并编码为 JPEG，调用方负责传输 Base64。</summary>
    public byte[]? CaptureScreenshot()
    {
        using var frame = TaskContext.Instance().Runtime?.Capture.Capture();
        if (frame?.Frame is not { } mat) return null;
        using var resized = new Mat();
        var targetWidth = Math.Min(mat.Width, 900);
        Cv2.Resize(mat, resized, new OpenCvSharp.Size(targetWidth, Math.Max(1, targetWidth * mat.Height / mat.Width)));
        Cv2.ImEncode(".jpg", resized, out var bytes, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 78)]);
        return bytes;
    }

    /// <summary>
    /// 启动任务。已有任务在执行时直接拒绝，不会打断现有任务。
    /// 参数校验在拿到锁后、置状态前完成，校验失败不会留下残留状态。
    /// </summary>
    public bool TryStart(WorkerTaskStartRequest request, out string? error)
    {
        var type = string.IsNullOrWhiteSpace(request.Type)
            ? WorkerTaskTypes.ScriptGroup
            : request.Type.Trim();

        lock (_sync)
        {
            if (_state is WorkerState.Starting or WorkerState.RunningTask or WorkerState.Stopping)
            {
                error = $"Worker 正在执行任务（{_state}），拒绝重复启动。";
                return false;
            }

            // 除「启动截图器」外，所有任务都要求截图器已在运行：
            // 否则 StartGameTask 会在"等待进入游戏主界面"里一直等下去（本机截图器永远不会就绪）
            if (!string.Equals(type, WorkerTaskTypes.StartGame, StringComparison.OrdinalIgnoreCase)
                && !TaskContext.Instance().IsInitialized)
            {
                error = "Worker 截图器尚未启动，请先下发 startGame 任务。";
                return false;
            }

            if (!TryCreateRunner(type, request, out var runner, out var label, out error))
            {
                return false;
            }

            _state = WorkerState.RunningTask;
            _currentTask = label;
            _errorMessage = null;
            _ = Task.Run(runner!);
            error = null;
            return true;
        }
    }

    /// <summary>
    /// 把 Controller 传来的任务标识解析成 Worker 侧的执行入口：
    /// 文件类（配置组/脚本/地图追踪）按同安装目录直接读取，内置功能只按标识启动。
    /// </summary>
    private bool TryCreateRunner(
        string type,
        WorkerTaskStartRequest request,
        out Func<Task>? runner,
        out string label,
        out string? error)
    {
        runner = null;
        error = null;
        label = string.Empty;

        if (string.Equals(type, WorkerTaskTypes.StartGame, StringComparison.OrdinalIgnoreCase))
        {
            label = WorkerTaskTypes.StartGame;
            runner = RunStartGameAsync;
            return true;
        }

        if (string.Equals(type, WorkerTaskTypes.ScriptGroup, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryResolveScriptGroupPath(request.Name, out var groupName, out var groupPath, out error))
            {
                return false;
            }

            ScriptGroup group;
            try
            {
                group = ScriptGroup.FromJson(File.ReadAllText(groupPath));
            }
            catch (Exception exception)
            {
                error = $"读取配置组「{groupName}」失败：{exception.GetBaseException().Message}";
                return false;
            }

            label = $"{WorkerTaskTypes.ScriptGroup}:{groupName}";
            runner = () => RunScriptGroupAsync(group);
            return true;
        }

        if (string.Equals(type, WorkerTaskTypes.ScriptGroups, StringComparison.OrdinalIgnoreCase))
        {
            var names = request.Names?
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .ToArray();
            if (names is null || names.Length == 0)
            {
                error = "连续执行缺少配置组名称列表。";
                return false;
            }

            var loop = request.Loop;
            label = $"{WorkerTaskTypes.ScriptGroups}:{string.Join(",", names)}";
            runner = () => RunScriptGroupsAsync(names, loop);
            return true;
        }

        if (string.Equals(type, WorkerTaskTypes.TaskProgress, StringComparison.OrdinalIgnoreCase))
        {
            var progressName = request.ProgressName?.Trim();
            if (string.IsNullOrWhiteSpace(progressName))
            {
                error = "继续执行缺少任务进度名称。";
                return false;
            }

            label = $"{WorkerTaskTypes.TaskProgress}:{progressName}";
            runner = () => RunTaskProgressAsync(progressName);
            return true;
        }

        if (string.Equals(type, WorkerTaskTypes.OneDragon, StringComparison.OrdinalIgnoreCase))
        {
            var configName = request.Name?.Trim();
            label = string.IsNullOrEmpty(configName)
                ? WorkerTaskTypes.OneDragon
                : $"{WorkerTaskTypes.OneDragon}:{configName}";
            runner = () => RunOneDragonAsync(configName);
            return true;
        }

        if (string.Equals(type, WorkerTaskTypes.Solo, StringComparison.OrdinalIgnoreCase))
        {
            var key = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(key) || !IsSafeToken(key))
            {
                error = $"非法的独立任务标识：{request.Name}";
                return false;
            }

            label = $"{WorkerTaskTypes.Solo}:{key}";
            runner = () => RunSoloTaskAsync(key);
            return true;
        }

        if (string.Equals(type, WorkerTaskTypes.ScriptFolder, StringComparison.OrdinalIgnoreCase))
        {
            var folderName = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(folderName) || !IsSafeToken(folderName))
            {
                error = $"非法的脚本文件夹名称：{request.Name}";
                return false;
            }

            label = $"{WorkerTaskTypes.ScriptFolder}:{folderName}";
            runner = () => RunScriptFolderAsync(folderName);
            return true;
        }

        if (string.Equals(type, WorkerTaskTypes.PathingFile, StringComparison.OrdinalIgnoreCase))
        {
            var fileName = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(fileName) || !IsSafeToken(fileName))
            {
                error = $"非法的地图追踪文件名：{request.Name}";
                return false;
            }

            if (!TryResolveRelativeDirectory(request.Directory, out var directory, out error))
            {
                return false;
            }

            label = $"{WorkerTaskTypes.PathingFile}:{fileName}";
            runner = () => RunPathingFileAsync(fileName, directory);
            return true;
        }

        error = $"不支持的任务类型：{type}。支持 {WorkerTaskTypes.ScriptGroup}、{WorkerTaskTypes.ScriptGroups}、"
                + $"{WorkerTaskTypes.TaskProgress}、{WorkerTaskTypes.OneDragon}、{WorkerTaskTypes.Solo}、"
                + $"{WorkerTaskTypes.ScriptFolder}、{WorkerTaskTypes.PathingFile}、{WorkerTaskTypes.StartGame}。";
        return false;
    }

    /// <summary>
    /// 来自 Controller 的名称一律不允许携带路径分隔符或上跳
    /// </summary>
    private static bool IsSafeToken(string value)
    {
        return value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
               && !value.Contains("..", StringComparison.Ordinal)
               && !value.Contains(Path.DirectorySeparatorChar)
               && !value.Contains(Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// 把 Controller 传来的相对目录解析到安装目录下，禁止越出安装目录
    /// </summary>
    private static bool TryResolveRelativeDirectory(string? relative, out string fullPath, out string? error)
    {
        error = null;
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relative))
        {
            error = "缺少地图追踪所在目录。";
            return false;
        }

        var basePath = Path.GetFullPath(AppContext.BaseDirectory);
        var candidate = Path.GetFullPath(Path.Combine(basePath, relative));
        if (!candidate.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            error = $"地图追踪目录越出安装目录：{relative}";
            return false;
        }

        if (!Directory.Exists(candidate))
        {
            error = $"地图追踪目录不存在：{relative}";
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <summary>
    /// 通过现有 CancellationContext 正常取消当前任务。
    /// </summary>
    public bool TryStop(out string? error)
    {
        lock (_sync)
        {
            if (_state is not (WorkerState.RunningTask or WorkerState.Starting))
            {
                error = "当前没有正在执行的任务。";
                return false;
            }

            _state = WorkerState.Stopping;
        }

        // 暂停中的任务不会走到取消检查点，先解除暂停再取消
        RunnerContext.Instance.IsSuspend = false;
        CancellationContext.Instance.ManualCancel();
        _logger.LogInformation("已请求取消 Worker 当前任务");
        error = null;
        return true;
    }

    /// <summary>
    /// 使用现有 RunnerContext.IsSuspend 的暂停机制（任务在下一次 TaskControl 检查点暂停）。
    /// </summary>
    public bool TryPause(out string? error)
    {
        lock (_sync)
        {
            if (_state != WorkerState.RunningTask)
            {
                error = "当前没有正在执行的任务，无法暂停。";
                return false;
            }
        }

        RunnerContext.Instance.IsSuspend = true;
        error = null;
        return true;
    }

    public bool TryResume(out string? error)
    {
        lock (_sync)
        {
            if (_state != WorkerState.RunningTask)
            {
                error = "当前没有正在执行的任务，无法继续。";
                return false;
            }
        }

        RunnerContext.Instance.IsSuspend = false;
        error = null;
        return true;
    }

    private async Task RunScriptGroupAsync(ScriptGroup group)
    {
        string? failure = null;
        try
        {
            // 延迟解析，避免 Worker 启动时就构造任务/截图器相关服务
            var scriptService = App.GetService<IScriptService>()
                               ?? throw new InvalidOperationException("脚本服务未注册。");
            // RunMulti 内部通过 TaskRunner 获取 TaskSemaphore 并接管取消令牌
            await scriptService.RunMulti(group.Projects, group.Name).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            _logger.LogError(exception, "Worker 执行配置组 {Name} 失败", group.Name);
        }

        CompleteExecution(failure);
    }

    private async Task RunStartGameAsync()
    {
        string? failure = null;
        try
        {
            // 不能经过 TaskRunner：它的 Init/End 要求截图器已经在运行，
            // 而本任务的职责正是启动截图器（StartGameTask 内部会启动运行环境并显示遮罩叠加层）
            CancellationContext.Instance.Set();
            await ScriptService.StartGameTask().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            _logger.LogError(exception, "Worker 启动截图器失败");
        }
        finally
        {
            CancellationContext.Instance.Clear();
        }

        CompleteExecution(failure);
    }

    /// <summary>
    /// 复用调度器的连续执行流程：Worker 读取自己 User\ScriptGroup 下的同名配置组并按序执行
    /// </summary>
    private async Task RunScriptGroupsAsync(string[] names, bool loop)
    {
        string? failure = null;
        try
        {
            var scriptControl = App.GetService<ScriptControlViewModel>()
                                ?? throw new InvalidOperationException("调度器未注册。");
            await scriptControl.OnStartMultiScriptGroupWithNamesAsync(names, loop).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            _logger.LogError(exception, "Worker 连续执行配置组失败");
        }

        CompleteExecution(failure);
    }

    /// <summary>
    /// 复用调度器的继续执行流程：Worker 读取自己的任务进度文件，从断点继续
    /// </summary>
    private async Task RunTaskProgressAsync(string progressName)
    {
        string? failure = null;
        try
        {
            var scriptControl = App.GetService<ScriptControlViewModel>()
                                ?? throw new InvalidOperationException("调度器未注册。");
            // 继续执行依赖已加载的配置组列表
            scriptControl.OnNavigatedTo();
            await scriptControl.OnContinueTaskProgressAsync(progressName).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            _logger.LogError(exception, "Worker 继续执行任务进度失败");
        }

        CompleteExecution(failure);
    }

    /// <summary>
    /// 一条龙：Worker 读取自己 User\OneDragonFlow 下的同名配置单并执行
    /// </summary>
    private async Task RunOneDragonAsync(string? configName)
    {
        string? failure = null;
        try
        {
            var oneDragon = App.GetService<OneDragonFlowViewModel>() ?? new OneDragonFlowViewModel();
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                oneDragon.OnNavigatedTo();

                if (!string.IsNullOrEmpty(configName))
                {
                    var config = oneDragon.ConfigList.FirstOrDefault(c =>
                        string.Equals(c.Name, configName, StringComparison.Ordinal));
                    if (config is null)
                        throw new FileNotFoundException($"一条龙配置不存在：{configName}");

                    oneDragon.SelectedConfig = config;
                    oneDragon.LoadDisplayTaskListFromConfig();
                }
            });

            await oneDragon.OnOneKeyExecute().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            _logger.LogError(exception, "Worker 执行一条龙失败");
        }

        CompleteExecution(failure);
    }

    /// <summary>
    /// 内置独立任务：只按标识启动，参数由 Worker 用自己的配置构造
    /// </summary>
    private async Task RunSoloTaskAsync(string key)
    {
        string? failure = null;
        try
        {
            var taskSettings = App.GetService<TaskSettingsPageViewModel>()
                               ?? throw new InvalidOperationException("任务设置页未注册。");
            if (!await taskSettings.StartSoloTaskByKeyAsync(key).ConfigureAwait(false))
            {
                failure = $"不支持的独立任务：{key}";
            }
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            _logger.LogError(exception, "Worker 执行独立任务 {Key} 失败", key);
        }

        CompleteExecution(failure);
    }

    /// <summary>
    /// JS 脚本文件夹：同安装目录，直接按文件夹名调用
    /// </summary>
    private async Task RunScriptFolderAsync(string folderName)
    {
        string? failure = null;
        try
        {
            var project = new ScriptGroupProject(new ScriptProject(folderName));
            await RunProjectsAsync([project]).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            _logger.LogError(exception, "Worker 执行脚本 {Folder} 失败", folderName);
        }

        CompleteExecution(failure);
    }

    /// <summary>
    /// 地图追踪文件：同安装目录，直接按文件名 + 相对目录调用
    /// </summary>
    private async Task RunPathingFileAsync(string fileName, string directory)
    {
        string? failure = null;
        try
        {
            var project = ScriptGroupProject.BuildPathingProject(fileName, directory);
            await RunProjectsAsync([project]).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            _logger.LogError(exception, "Worker 执行地图追踪 {File} 失败", fileName);
        }

        CompleteExecution(failure);
    }

    private static async Task RunProjectsAsync(IEnumerable<ScriptGroupProject> projects)
    {
        var scriptService = App.GetService<IScriptService>()
                            ?? throw new InvalidOperationException("脚本服务未注册。");
        await scriptService.RunMulti(projects).ConfigureAwait(false);
    }

    private void CompleteExecution(string? failure)
    {
        lock (_sync)
        {
            _currentTask = null;
            if (failure is null)
            {
                _state = WorkerState.Idle;
                _errorMessage = null;
            }
            else
            {
                _state = WorkerState.Error;
                _errorMessage = failure;
            }
        }
    }

    private static bool TryResolveScriptGroupPath(
        string? name,
        out string groupName,
        out string groupPath,
        out string? error)
    {
        groupName = name?.Trim() ?? string.Empty;
        groupPath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(groupName))
        {
            error = $"{ScriptGroupTaskType} 任务缺少配置组名称。";
            return false;
        }

        // 配置组名称来自 Controller，禁止路径穿越
        if (groupName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || groupName.Contains("..", StringComparison.Ordinal))
        {
            error = $"非法的配置组名称：{groupName}";
            return false;
        }

        groupPath = Path.Combine(ScriptGroupDirectory, $"{groupName}.json");
        if (!File.Exists(groupPath))
        {
            error = $"配置组不存在：{groupName}";
            return false;
        }

        return true;
    }
}
