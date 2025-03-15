using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Shell;

public class ShellTask(ShellTaskParam param) : ISoloTask
{
    public string Name => "Shell";

    public Task Start(CancellationToken ct = default)
    {
        return Execute(ct);
    }

    private async Task Execute(CancellationToken ct)
    {
        if (param.Disable)
        {
            TaskControl.Logger.LogWarning("无法执行Shell: Shell任务被禁用");
            return;
        }

        if (string.IsNullOrEmpty(param.Command))
        {
            TaskControl.Logger.LogWarning("无法执行Shell: Shell为空");
            return;
        }

        if (ct.IsCancellationRequested)
        {
            TaskControl.Logger.LogError("shell {Shell} 被取消", param.Command);
        }

        TaskControl.Logger.LogInformation("执行shell:{Shell},超时时间为 {Wait} 秒", param.Command, param.TimeoutSeconds);

        var mixedToken = ct;
        var waitForExit = true;
        CancellationTokenSource? timeoutSignal = null;
        if (param.TimeoutSeconds > 0)
        {
            timeoutSignal = new CancellationTokenSource(TimeSpan.FromSeconds(param.TimeoutSeconds));
            // 超时取消或任务被取消
            mixedToken = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutSignal.Token).Token;
        }
        else
        {
            // timeout小于0不等待,不获取输出,仅仅启动shell即返回
            waitForExit = false;
        }

        ShellExecutionRecord result;
        try
        {
            result = await StartAndInject(param, waitForExit, mixedToken);
        }
        catch (OperationCanceledException)
        {
            if (timeoutSignal is { IsCancellationRequested: true })
            {
                TaskControl.Logger.LogError("shell {Shell} 执行超时", param.Command);
            }

            TaskControl.Logger.LogError("shell {Shell} 被取消", param.Command);
            return;
        }

        if (result.End)
        {
            if (param.Output && result.HasOutput)
            {
                TaskControl.Logger.LogInformation("shell {End} 运行结束,输出:{Output}", result.Shell, result.Output);
                return;
            }

            TaskControl.Logger.LogInformation("shell {End} 运行结束", param.Command);
        }

        SystemControl.ActivateWindow();
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
