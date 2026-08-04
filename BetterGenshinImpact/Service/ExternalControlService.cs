using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 外部进程控制入口（Go / 脚本等）。
/// 约定：写入 {BetterGI}\User\control\command.json
/// {
///   "cmd": "skip_current_task"
/// }
/// BGI 轮询消费后删除文件。
/// </summary>
public static class ExternalControlService
{
    private static readonly ILogger Logger = App.GetLogger<LoggerTag>();
    private static readonly object Gate = new();
    private static CancellationTokenSource? _loopCts;
    private static Task? _loopTask;

    public static string ControlDir => Global.Absolute(@"User\control");
    public static string CommandFilePath => Path.Combine(ControlDir, "command.json");

    public static void Start()
    {
        lock (Gate)
        {
            if (_loopTask is { IsCompleted: false })
            {
                return;
            }

            Directory.CreateDirectory(ControlDir);
            _loopCts = new CancellationTokenSource();
            var token = _loopCts.Token;
            _loopTask = Task.Run(() => PollLoopAsync(token), token);
            Logger.LogInformation("外部控制服务已启动，监听: {Path}", CommandFilePath);
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            try
            {
                _loopCts?.Cancel();
            }
            catch
            {
                // ignore
            }

            _loopCts = null;
            _loopTask = null;
        }
    }

    private static async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                TryConsumeCommand();
            }
            catch (Exception e)
            {
                Logger.LogDebug(e, "处理外部控制命令失败");
            }

            try
            {
                await Task.Delay(300, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static void TryConsumeCommand()
    {
        if (!File.Exists(CommandFilePath))
        {
            return;
        }

        string json;
        try
        {
            json = File.ReadAllText(CommandFilePath);
        }
        catch (IOException)
        {
            // 可能正在被外部写入，下一拍再读
            return;
        }

        try
        {
            File.Delete(CommandFilePath);
        }
        catch
        {
            // 删除失败也不重复执行：先解析再尽力删
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        ExternalControlCommand? cmd;
        try
        {
            cmd = JsonSerializer.Deserialize<ExternalControlCommand>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "外部控制命令 JSON 无效: {Json}", json);
            return;
        }

        if (cmd == null || string.IsNullOrWhiteSpace(cmd.Cmd))
        {
            return;
        }

        switch (cmd.Cmd.Trim().ToLowerInvariant())
        {
            case "skip_current_task":
            case "skip_task":
            case "next_task":
            case "skip_current_group":
            case "skip_group":
            case "next_group":
                Logger.LogInformation("收到外部命令: 跳过当前一条龙任务，继续下一个任务");
                if (!RunnerContext.Instance.RequestSkipCurrentOneDragonTask())
                {
                    Logger.LogWarning("跳过一条龙任务命令未执行：当前没有正在执行的一条龙任务");
                }
                break;
            case "stop":
            case "manual_stop":
                Logger.LogInformation("收到外部命令: 停止全部任务");
                CancellationContext.Instance.ManualCancel();
                break;
            default:
                Logger.LogWarning("未知外部控制命令: {Cmd}", cmd.Cmd);
                break;
        }
    }

    private sealed class ExternalControlCommand
    {
        public string? Cmd { get; set; }
    }

    private sealed class LoggerTag
    {
    }
}
