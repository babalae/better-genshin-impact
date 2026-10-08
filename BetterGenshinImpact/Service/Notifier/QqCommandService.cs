using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>QQ 开放平台机器人命令接收器。</summary>
internal sealed class QqCommandService : IHostedService, IDisposable
{
    private readonly WorkerController _workerController;
    private readonly WorkerTaskExecutor _workerTaskExecutor;
    private readonly GameRuntimeService _runtimeService;
    private readonly ExternalControlService _externalControlService;
    private readonly ILogger<QqCommandService> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _listenerLock = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private Task? _listenerTask;
    private CancellationTokenSource? _listenerCts;
    private NotificationConfig? _config;

    public QqCommandService(
        WorkerController workerController,
        WorkerTaskExecutor workerTaskExecutor,
        GameRuntimeService runtimeService,
        ExternalControlService externalControlService,
        ILogger<QqCommandService> logger)
    {
        _workerController = workerController;
        _workerTaskExecutor = workerTaskExecutor;
        _runtimeService = runtimeService;
        _externalControlService = externalControlService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _config = TaskContext.Instance().Config.NotificationConfig;
        _config.PropertyChanged += OnConfigChanged;
        ReconcileListener();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _lifetime.Cancel();
        Task? listenerTask;
        lock (_listenerLock)
        {
            _listenerCts?.Cancel();
            listenerTask = _listenerTask;
        }
        if (listenerTask is not null)
            try { await listenerTask.WaitAsync(cancellationToken); } catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        if (_config is not null) _config.PropertyChanged -= OnConfigChanged;
        lock (_listenerLock)
        {
            _listenerCts?.Cancel();
            _listenerCts?.Dispose();
            _listenerCts = null;
        }
        _lifetime.Dispose();
        _httpClient.Dispose();
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotificationConfig.QqCommandEnabled)
            or nameof(NotificationConfig.QqAppId)
            or nameof(NotificationConfig.QqClientSecret))
            ReconcileListener();
    }

    private void ReconcileListener()
    {
        if (_config is null || _lifetime.IsCancellationRequested) return;
        lock (_listenerLock)
        {
            _listenerCts?.Cancel();
            _listenerCts?.Dispose();
            _listenerCts = null;
            _listenerTask = null;
            if (!_config.QqCommandEnabled
                || string.IsNullOrWhiteSpace(_config.QqAppId)
                || string.IsNullOrWhiteSpace(_config.QqClientSecret)) return;
            _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _listenerTask = ListenLoopAsync(_config.QqAppId, _config.QqClientSecret, _listenerCts.Token);
        }
    }

    private async Task ListenLoopAsync(string appId, string secret, CancellationToken cancellationToken)
    {
        var backoffSeconds = 2;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await QqWebSocketHelper.ListenForCommandsAsync(appId, secret, HandleMessageAsync, cancellationToken);
                backoffSeconds = 2;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (System.Exception exception)
            {
                _logger.LogWarning(exception, "QQ 命令监听连接断开，将在 {Delay} 秒后重连", backoffSeconds);
                try { await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cancellationToken); }
                catch (OperationCanceledException) { break; }
                backoffSeconds = Math.Min(backoffSeconds * 2, 60);
            }
        }
    }

    private async Task HandleMessageAsync(string? userOpenId, string? groupOpenId, string? content, string? messageId)
    {
        if (content is null || !TryParseExternalCommand(content.Trim(), out var command, out var argument))
            return;

        var config = TaskContext.Instance().Config.NotificationConfig;
        if (!IsAuthorized(config, userOpenId))
        {
            _logger.LogWarning("忽略未授权 QQ 命令，用户 OpenID {OpenId}", userOpenId);
            return;
        }

        var reply = await ExecuteExternalCommandAsync(command, argument, _workerController, _workerTaskExecutor, _runtimeService);
        try
        {
            if (_externalControlService.IsReverseConnected && userOpenId is not null)
            {
                await _externalControlService.SendCommandReplyAsync(userOpenId, groupOpenId ?? string.Empty, reply);
                return;
            }
            var notifier = new QqNotifier(_httpClient, config.QqAppId, config.QqClientSecret,
                config.QqOpenId, config.QqGroupOpenId, "text");
            await notifier.SendCommandReplyAsync(userOpenId, groupOpenId, messageId, reply);
        }
        catch (System.Exception exception)
        {
            _logger.LogWarning(exception, "回复 QQ 远程命令结果失败");
        }
    }

    private static bool IsAuthorized(NotificationConfig config, string? userOpenId)
    {
        if (string.IsNullOrWhiteSpace(userOpenId)) return false;
        var allowList = config.QqCommandUserOpenIds
            .Split([',', ';', '，', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowList.Length == 0 && !string.IsNullOrWhiteSpace(config.QqOpenId))
            allowList = [config.QqOpenId.Trim()];
        return allowList.Contains(userOpenId, StringComparer.Ordinal);
    }

    internal static bool TryParseExternalCommand(string content, out string command, out string? argument)
    {
        // 群消息的正文可能带有 QQ 网关插入的机器人提及标记。
        content = Regex.Replace(content, "<@!?[^>]+>", string.Empty).Trim();
        command = string.Empty;
        argument = null;
        if (content.Equals("#一条龙", StringComparison.Ordinal))
        {
            command = WorkerTaskTypes.OneDragon;
            return true;
        }
        command = content switch
        {
            "#启动截图器" => "captureStart",
            "#关闭截图器" => "captureStop",
            "#暂停任务" => "taskPause",
            "#继续任务" => "taskResume",
            "#停止任务" => "taskStop",
            "#退出游戏" => "gameExit",
            _ => string.Empty
        };
        if (command.Length > 0) return true;
        const string startTaskPrefix = "#启动任务";
        if (content.StartsWith(startTaskPrefix, StringComparison.Ordinal))
        {
            var name = content[startTaskPrefix.Length..].Trim();
            command = WorkerTaskTypes.ScriptGroup;
            argument = name.Length == 0 ? null : name;
            return true;
        }
        const string groupPrefix = "#执行调度器";
        if (content.StartsWith(groupPrefix, StringComparison.Ordinal))
        {
            var name = content[groupPrefix.Length..].Trim();
            command = WorkerTaskTypes.ScriptGroup;
            argument = name.Length == 0 ? null : name;
            return true;
        }
        return false;
    }

    internal static async Task<string> ExecuteExternalCommandAsync(string command, string? argument,
        WorkerController workerController, WorkerTaskExecutor workerTaskExecutor, GameRuntimeService runtimeService)
    {
        if (command is "captureStart" or "captureStop" or "taskPause" or "taskResume" or "taskStop" or "gameExit")
            return await ExecuteControlCommandAsync(command, workerController, workerTaskExecutor);

        if (command == WorkerTaskTypes.ScriptGroup && string.IsNullOrWhiteSpace(argument))
            return "用法：#执行调度器 配置组名称";
        if (command == WorkerTaskTypes.ScriptGroup && !ScriptGroupExists(argument!))
            return $"配置组不存在：{argument}";

        var request = new WorkerTaskStartRequest { Type = command, Name = argument };
        try
        {
            if (workerController.IsConnected)
            {
                var status = await workerController.GetStatusAsync();
                if (status.State is WorkerState.RunningTask or WorkerState.Starting or WorkerState.Stopping
                    || !status.CaptureRunning)
                    return status.CaptureRunning ? "Worker 当前正在执行任务，命令未启动。" : "Worker 截图器未启动，命令未执行。";
                var started = await workerController.StartTaskAsync(request);
                return $"已启动：{started.Task}";
            }

            if (!runtimeService.IsRunning)
                return "游戏运行环境未启动，命令未执行。";
            if (TaskControl.TaskSemaphore.CurrentCount == 0 || ScriptGroupProgressTracker.Instance.IsRunning)
                return "当前已有任务正在执行，命令未启动。";
            if (!workerTaskExecutor.TryStart(request, out var error))
                return error ?? "任务启动失败。";
            return command == WorkerTaskTypes.ScriptGroup
                ? $"已启动配置组：{argument}"
                : "已启动一条龙。";
        }
        catch (System.Exception exception)
        {
            return $"启动失败：{exception.GetBaseException().Message}";
        }
    }

    private static async Task<string> ExecuteControlCommandAsync(string command, WorkerController workerController,
        WorkerTaskExecutor workerTaskExecutor)
    {
        try
        {
            if (workerController.IsConnected)
            {
                switch (command)
                {
                    case "captureStart":
                    {
                        var status = await workerController.StartCaptureAsync();
                        return status.CaptureRunning
                            ? "Worker 截图器已启动。"
                            : status.ErrorMessage ?? "Worker 截图器启动命令已发送，但截图器仍未运行。";
                    }
                    case "captureStop":
                    {
                        var status = await workerController.StopCaptureAsync();
                        return !status.CaptureRunning
                            ? "Worker 截图器已关闭。"
                            : status.ErrorMessage ?? "Worker 截图器关闭命令已发送，但截图器仍在运行。";
                    }
                    case "taskPause":
                        await workerController.PauseTaskAsync();
                        return "已请求 Worker 暂停当前任务。";
                    case "taskResume":
                        await workerController.ResumeTaskAsync();
                        return "已请求 Worker 继续当前任务。";
                    case "taskStop":
                        await workerController.StopTaskAsync();
                        return "已请求 Worker 停止当前任务。";
                    case "gameExit":
                        await workerController.ExitGameAsync();
                        return "已向 Worker 游戏发送退出指令。";
                }
            }

            switch (command)
            {
                case "captureStart":
                {
                    var (success, error) = await workerTaskExecutor.TryStartCaptureAsync();
                    return success ? "截图器已启动。" : error ?? "启动截图器失败。";
                }
                case "captureStop":
                {
                    var (success, error) = await workerTaskExecutor.TryStopCaptureAsync();
                    return success ? "截图器已关闭。" : error ?? "关闭截图器失败。";
                }
                case "gameExit":
                {
                    var (success, error) = await Task.Run(workerTaskExecutor.TryExitGame);
                    return success ? "已向游戏发送退出指令。" : error ?? "退出游戏失败。";
                }
            }

            var executorState = workerTaskExecutor.Snapshot().State;
            var taskRunning = TaskControl.TaskSemaphore.CurrentCount == 0
                              || ScriptGroupProgressTracker.Instance.IsRunning
                              || executorState == WorkerState.RunningTask;
            if (!taskRunning) return "当前没有正在执行的任务。";

            switch (command)
            {
                case "taskPause":
                    if (executorState == WorkerState.RunningTask)
                    {
                        return workerTaskExecutor.TryPause(out var pauseError)
                            ? "已请求暂停当前任务。"
                            : pauseError ?? "暂停任务失败。";
                    }
                    RunnerContext.Instance.IsSuspend = true;
                    return "已请求暂停当前任务。";
                case "taskResume":
                    if (executorState == WorkerState.RunningTask)
                    {
                        return workerTaskExecutor.TryResume(out var resumeError)
                            ? "已请求继续当前任务。"
                            : resumeError ?? "继续任务失败。";
                    }
                    RunnerContext.Instance.IsSuspend = false;
                    return "已请求继续当前任务。";
                case "taskStop":
                    RunnerContext.Instance.IsSuspend = false;
                    if (executorState == WorkerState.RunningTask)
                    {
                        return workerTaskExecutor.TryStop(out var stopError)
                            ? "已请求停止当前任务。"
                            : stopError ?? "停止任务失败。";
                    }
                    CancellationContext.Instance.ManualCancel();
                    return "已请求停止当前任务。";
                default:
                    return "不支持的命令。";
            }
        }
        catch (System.Exception exception)
        {
            return $"命令执行失败：{exception.GetBaseException().Message}";
        }
    }

    private static bool ScriptGroupExists(string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..", StringComparison.Ordinal))
            return false;
        var directory = Global.Absolute(@"User\ScriptGroup");
        return File.Exists(Path.Combine(directory, $"{name}.json"));
    }
}
