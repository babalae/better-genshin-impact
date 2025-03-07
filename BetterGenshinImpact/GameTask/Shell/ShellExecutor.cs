using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.Shell;

[Serializable]
public class ShellExecutor
{
    private string command = string.Empty;
    private int maxWaitSeconds = 60;
    private bool noWindow = true;
    private bool output = true;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };


    public async Task Execute(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(command))
        {
            TaskControl.Logger.LogWarning("无法执行Shell: Shell为空");
            return;
        }

        var cmd = new Process();
        cmd.StartInfo.FileName = "cmd.exe";
        cmd.StartInfo.Arguments = "/k @echo off";
        cmd.StartInfo.RedirectStandardInput = true;
        cmd.StartInfo.RedirectStandardOutput = true;
        cmd.StartInfo.CreateNoWindow = noWindow;
        cmd.StartInfo.UseShellExecute = false;
        if (ct.IsCancellationRequested)
        {
            TaskControl.Logger.LogError("shell {Shell} 被取消", command);
        }

        TaskControl.Logger.LogInformation("执行shell:{Shell},超时时间为 {Wait} 秒", command, maxWaitSeconds);
        var timeoutSignal = new CancellationTokenSource(TimeSpan.FromSeconds(maxWaitSeconds));
        var mixedToken = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutSignal.Token).Token;

        cmd.Start();
        var outputShell = "";
        var outputText = "";
        var cmdCanceled = false;
        try
        {
            await cmd.StandardInput.WriteLineAsync(command.AsMemory(), mixedToken);
            await cmd.StandardInput.FlushAsync(mixedToken);
            cmd.StandardInput.Close();
            await cmd.WaitForExitAsync(mixedToken);
            if (output)
            {
                outputShell = await cmd.StandardOutput.ReadLineAsync(mixedToken) ?? "";
                outputText = await cmd.StandardOutput.ReadToEndAsync(mixedToken);
            }
        }
        catch (OperationCanceledException)
        {
            cmdCanceled = true;
        }

        if (!cmd.HasExited || cmdCanceled)
        {
            cmd.Kill();
            if (ct.IsCancellationRequested)
            {
                TaskControl.Logger.LogError("shell {Shell} 被取消", command);
            }
            else if (timeoutSignal.IsCancellationRequested)
            {
                TaskControl.Logger.LogWarning("shell {Shell} 超时", command);
            }
            else
            {
                TaskControl.Logger.LogWarning("shell {Shell} 出现异常输出，可能未能成功执行。", command);
            }
        }

        if (output)
        {
            TaskControl.Logger.LogInformation("shell {End} 运行结束,输出:{Output}", outputShell, outputText);
        }
        else
        {
            TaskControl.Logger.LogInformation("shell {End} 运行结束", command);
        }

        SystemControl.ActivateWindow();
    }

    public static ShellExecutor BuildFromShellName(string name)
    {
        var obj = new ShellExecutor
        {
            command = name
        };
        return obj;
    }

    public static ShellExecutor BuildFromJson(string json)
    {
        // 留给以后玩
        var task = JsonSerializer.Deserialize<ShellExecutor>(json, JsonOptions) ??
                   throw new Exception("Failed to deserialize ShellExecutorTask");
        return task;
    }
}