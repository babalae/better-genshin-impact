using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Shell;

/// <summary>
/// 执行 Shell 命令的任务
/// </summary>
/// <param name="runtime">本次运行使用的运行环境，命令执行完毕后激活其游戏窗口</param>
public class ShellTask(GameRuntime runtime) : ISoloTask<ShellTaskParam, NoData>
{
    /// <summary>
    /// 原因码：Shell 任务被禁用。
    /// </summary>
    public const string DisabledReason = "shell_disabled";

    /// <summary>
    /// 原因码：Shell 命令在超时时间内没有执行结束。
    /// </summary>
    public const string TimeoutReason = "shell_timeout";

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime = runtime;

    /// <summary>
    /// 执行 Shell 命令。
    /// </summary>
    /// <param name="param">Shell 任务参数</param>
    /// <param name="ct">取消令牌，取消时抛出 OperationCanceledException</param>
    /// <returns>禁用时返回 Skipped；命令为空或超时返回 Failed；等待到命令结束返回 Success；不等待时返回 Unverified。</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(ShellTaskParam param, CancellationToken ct)
    {
        if (param.Disable)
        {
            TaskControl.Logger.LogWarning("无法执行Shell: Shell任务被禁用");
            return SoloTaskResult.Skipped(DisabledReason, "Shell任务被禁用");
        }

        if (string.IsNullOrEmpty(param.Command))
        {
            TaskControl.Logger.LogWarning("无法执行Shell: Shell为空");
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, "Shell为空");
        }

        // 已取消时直接向上抛出，不再启动命令
        ct.ThrowIfCancellationRequested();

        TaskControl.Logger.LogInformation("执行shell:{Shell},超时时间为 {Wait} 秒", param.Command, param.TimeoutSeconds);

        // timeout 大于 0 时等待命令结束，超时取消或任务被取消；小于等于 0 时不等待、不获取输出，仅启动 shell 即返回
        var waitForExit = param.TimeoutSeconds > 0;
        using var timeoutSignal = waitForExit
            ? new CancellationTokenSource(TimeSpan.FromSeconds(param.TimeoutSeconds))
            : null;
        using var linkedSignal = timeoutSignal != null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutSignal.Token)
            : null;
        var mixedToken = linkedSignal?.Token ?? ct;

        ShellExecutionRecord result;
        try
        {
            result = await StartAndInject(param, waitForExit, mixedToken);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutSignal is { IsCancellationRequested: true })
        {
            // 只有超时引起的取消转为失败结果；任务本身被取消时继续向上抛出
            TaskControl.Logger.LogError("shell {Shell} 执行超时", param.Command);
            return SoloTaskResult.Failed(TimeoutReason, $"shell {param.Command} 执行超时");
        }
        catch (OperationCanceledException)
        {
            TaskControl.Logger.LogError("shell {Shell} 被取消", param.Command);
            throw;
        }

        if (result.End)
        {
            if (param.Output && result.HasOutput)
            {
                TaskControl.Logger.LogInformation("shell {End} 运行结束,输出:{Output}", result.Shell, result.Output);
                return SoloTaskResult.Success($"shell {param.Command} 运行结束");
            }

            TaskControl.Logger.LogInformation("shell {End} 运行结束", param.Command);
        }

        _runtime.Window.Activate();

        // 确认进程已退出才算成功；不等待或未确认退出时只能返回未确认
        if (result.End)
        {
            return SoloTaskResult.Success($"shell {param.Command} 运行结束");
        }

        return SoloTaskResult.Unverified(waitForExit
            ? $"shell {param.Command} 未确认运行结束"
            : $"shell {param.Command} 已启动，未等待执行结束");
    }

    /// <summary>
    /// 启动cmd并注入要执行的命令
    /// </summary>
    /// <param name="param">Task参数</param>
    /// <param name="waitForExit">是否等待到执行结束</param>
    /// <param name="ct">CancellationToken</param>
    private static async Task<ShellExecutionRecord> StartAndInject(ShellTaskParam param, bool waitForExit,
        CancellationToken ct)
    {
        using var cmd = new Process();
        cmd.StartInfo = BuildStartInfo(param);
        cmd.Start();
        await cmd.StandardInput.WriteLineAsync(param.Command.AsMemory(), ct);
        await cmd.StandardInput.FlushAsync(ct);
        cmd.StandardInput.Close();
        if (!waitForExit)
        {
            return new ShellExecutionRecord(false, "", "");
        }

        var outputShell = "";
        var outputText = "";
        await cmd.WaitForExitAsync(ct);
        if (param.Output)
        {
            outputShell = await cmd.StandardOutput.ReadLineAsync(ct) ?? "";
            outputText = await cmd.StandardOutput.ReadToEndAsync(ct);
        }

        if (cmd.HasExited)
        {
            return new ShellExecutionRecord(true, outputShell, outputText);
        }

        cmd.Kill();
        return new ShellExecutionRecord(false, outputShell, outputText);
    }

    private static ProcessStartInfo BuildStartInfo(ShellTaskParam param)
    {
        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/k @echo off",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = param.NoWindow,
            UseShellExecute = false
        };
    }

    /// <summary>
    /// Shell执行记录
    /// </summary>
    /// <param name="End">是否是结束后的记录</param>
    /// <param name="Shell">执行输出的Shell</param>
    /// <param name="Output">Shell输出的内容</param>
    private record ShellExecutionRecord(bool End, string Shell, string Output)
    {
        public bool HasOutput => !string.IsNullOrEmpty(Output) || !string.IsNullOrEmpty(Shell);
    }
}
