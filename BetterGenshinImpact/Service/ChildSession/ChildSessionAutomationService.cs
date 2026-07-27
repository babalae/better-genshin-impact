using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>
/// 通过内置 Child Session 执行无人值守一条龙。
/// 结果文件用于让计划任务跨“新根实例/复用现有根实例”两种启动形态等待真实终态。
/// </summary>
public sealed class ChildSessionAutomationService(
    ChildSessionService childSessionService,
    InstanceService instanceService,
    ILogger<ChildSessionAutomationService> logger)
{
    private static readonly TimeSpan ChildRegistrationTimeout = TimeSpan.FromSeconds(75);
    private readonly SemaphoreSlim _runSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _knownRuns =
        new(StringComparer.Ordinal);

    public async Task StartAsync(
        CommandLineOptions options,
        bool hideRootWhenDone)
    {
        var runId = options.AutomationRunId;
        var configName = options.OneDragonConfigName;
        var resultPath = options.AutomationResultPath;
        if (string.IsNullOrWhiteSpace(runId)
            || string.IsNullOrWhiteSpace(configName)
            || string.IsNullOrWhiteSpace(resultPath))
        {
            logger.LogError("Child Session 自动化缺少 run-id、配置名或结果路径。");
            return;
        }
        if (!_knownRuns.TryAdd(runId, 0))
        {
            logger.LogInformation("忽略重复的 Child Session 自动化请求：{RunId}", runId);
            return;
        }

        await _runSemaphore.WaitAsync();
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(options.AutomationTimeoutSeconds));
        try
        {
            await WriteStateAsync(resultPath, runId, configName, "starting", null);
            await childSessionService.StartAsync();
            var childSessionId = childSessionService.ChildSessionId
                                 ?? throw new InvalidOperationException(
                                     "Child Session 登录完成后未取得 Windows Session ID。");

            try
            {
                await instanceService.WaitForChildSessionAsync(
                    checked((int)childSessionId),
                    TimeSpan.FromSeconds(15),
                    timeout.Token);
            }
            catch (TimeoutException)
            {
                // 已存在的 Child Session 不会触发首次登录自动启动，显式补一次 BetterGI 启动。
                await childSessionService.LaunchBetterGiAsync();
                await instanceService.WaitForChildSessionAsync(
                    checked((int)childSessionId),
                    ChildRegistrationTimeout,
                    timeout.Token);
            }

            await WriteStateAsync(resultPath, runId, configName, "accepted", null);
            await instanceService.StartOneDragonInChildAsync(
                checked((int)childSessionId),
                runId,
                configName,
                resultPath,
                timeout.Token);
            await WaitForTerminalResultAsync(resultPath, runId, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            await WriteStateAsync(
                resultPath,
                runId,
                configName,
                "timed_out",
                $"超过 {options.AutomationTimeoutSeconds} 秒仍未完成。");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Child Session 自动化失败：{RunId}", runId);
            await WriteStateAsync(
                resultPath,
                runId,
                configName,
                "failed",
                exception.GetBaseException().Message);
        }
        finally
        {
            try
            {
                childSessionService.HideWindow();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "隐藏 Child Session 窗口失败：{RunId}", runId);
                await WriteStateAsync(
                    resultPath,
                    runId,
                    configName,
                    "cleanup_failed",
                    exception.GetBaseException().Message);
            }

            _runSemaphore.Release();
            _knownRuns.TryRemove(runId, out _);
            if (hideRootWhenDone)
            {
                _ = Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => Application.Current.MainWindow?.Hide()));
            }
        }
    }

    private static async Task WaitForTerminalResultAsync(
        string resultPath,
        string runId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(resultPath))
            {
                try
                {
                    var result = JObject.Parse(await File.ReadAllTextAsync(
                        resultPath,
                        cancellationToken));
                    var resultRunId = result.Value<string>("runId");
                    var status = result.Value<string>("status");
                    if (string.Equals(resultRunId, runId, StringComparison.Ordinal)
                        && status is "succeeded" or "failed" or "cancelled"
                            or "timed_out" or "cleanup_failed")
                    {
                        return;
                    }
                }
                catch (JsonException)
                {
                    // 原子替换前后的短暂读取失败继续重试。
                }
            }

            await Task.Delay(500, cancellationToken);
        }
    }

    private static async Task WriteStateAsync(
        string resultPath,
        string runId,
        string configName,
        string status,
        string? message)
    {
        var fullPath = Path.GetFullPath(resultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
                                  ?? throw new InvalidOperationException("结果路径缺少目录。"));
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        var json = JsonConvert.SerializeObject(
            new
            {
                runId,
                configName,
                status,
                message,
                updatedAt = DateTimeOffset.UtcNow
            },
            Formatting.Indented);
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temporaryPath, fullPath, overwrite: true);
                    return;
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException
                          && attempt < 20)
                {
                    // The PowerShell wrapper polls this file and can briefly hold
                    // a handle without delete sharing while an atomic update lands.
                    await Task.Delay(Math.Min(attempt * 25, 250));
                }
            }
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // The state update exception is more useful than cleanup noise.
            }
        }
    }
}
