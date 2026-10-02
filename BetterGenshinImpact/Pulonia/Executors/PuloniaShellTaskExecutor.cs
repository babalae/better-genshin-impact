using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 以独立子进程运行 Shell 节点，并在取消或超时时结束本次拥有的进程树。
/// </summary>
public sealed class PuloniaShellTaskExecutor : IPuloniaTaskExecutor
{
    /// <summary>
    /// 单路标准输出保留的最大字符数，超过后继续排空但不再占用内存。
    /// </summary>
    private const int MaxCapturedCharacters = 64 * 1024;

    /// <inheritdoc />
    public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } = [new()
    {
        TaskType = "shell",
        DefaultParameters = new JObject
        {
            ["file_name"] = "cmd.exe",
            ["arguments"] = new JArray("/d", "/c", "echo Pulonia Shell sample")
        },
        ParameterSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["file_name"] = new JObject { ["type"] = "string" },
                ["arguments"] = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "string" }
                },
                ["working_directory"] = new JObject { ["type"] = new JArray("string", "null") }
            },
            ["required"] = new JArray("file_name", "arguments"),
            ["additionalProperties"] = false
        },
        PublicParameters = ["file_name", "arguments", "working_directory"]
    }];

    /// <inheritdoc />
    public async Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task,
        PuloniaTaskExecutionContext context, CancellationToken ct)
    {
        var parameters = task.Parameters;
        var fileName = parameters.Value<string>("file_name");
        if (string.IsNullOrWhiteSpace(fileName))
            return PuloniaTaskOutcome.Failure("Shell file_name 不能为空。");

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ResolveWorkingDirectory(parameters.Value<string?>("working_directory"))
        };
        foreach (var argument in parameters["arguments"]?.Values<string>() ?? [])
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
                return PuloniaTaskOutcome.Failure($"Shell 进程 {fileName} 未能启动。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return PuloniaTaskOutcome.Failure($"Shell 进程 {fileName} 启动失败：{ex.Message}");
        }

        // 两路输出必须从进程启动后立即并行排空，避免子进程因管道缓冲区写满而死锁。
        var standardOutputTask = DrainAsync(process.StandardOutput);
        var standardErrorTask = DrainAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 取消不等于进程退出；必须结束本次创建的整个进程树并确认退出后再把控制权交还协调器。
            var killError = TryKillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            if (killError is not null)
                throw new InvalidOperationException("结束 Shell 进程树失败，但已等待进程自行退出：" + killError.Message, killError);
            throw;
        }

        var output = await standardOutputTask.ConfigureAwait(false);
        var error = await standardErrorTask.ConfigureAwait(false);
        var data = new JObject
        {
            ["exit_code"] = process.ExitCode,
            ["standard_output"] = output.Text,
            ["standard_error"] = error.Text,
            ["standard_output_truncated"] = output.Truncated,
            ["standard_error_truncated"] = error.Truncated
        };
        return process.ExitCode == 0
            ? PuloniaTaskOutcome.Success($"Shell 进程正常退出，退出码 {process.ExitCode}。", data)
            : PuloniaTaskOutcome.Failure($"Shell 进程异常退出，退出码 {process.ExitCode}。", data);
    }

    /// <summary>
    /// 解析工作目录；未填写时使用应用程序基础目录。
    /// </summary>
    private static string ResolveWorkingDirectory(string? configuredDirectory)
        => string.IsNullOrWhiteSpace(configuredDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(configuredDirectory, AppContext.BaseDirectory);

    /// <summary>
    /// 尽力结束进程树；进程已自然退出时无需重复处理。
    /// </summary>
    private static Exception? TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            return null;
        }
        catch (InvalidOperationException)
        {
            // 进程在检查后已退出，后续 WaitForExitAsync 仍会确认最终状态。
            return null;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // 不能在结束失败时提前放行下一请求；调用方会继续等待该进程自行退出。
            return process.HasExited ? null : ex;
        }
    }

    /// <summary>
    /// 持续排空一条输出流，只截取有限文本并标记是否截断。
    /// </summary>
    private static async Task<CapturedText> DrainAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) is var read && read > 0)
        {
            var remaining = MaxCapturedCharacters - text.Length;
            if (remaining > 0)
                text.Append(buffer, 0, Math.Min(remaining, read));
            if (read > remaining)
                truncated = true;
        }
        return new CapturedText(text.ToString(), truncated);
    }

    /// <summary>
    /// 一路子进程输出的有限捕获结果。
    /// </summary>
    private readonly record struct CapturedText(string Text, bool Truncated);
}
