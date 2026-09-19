using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class OneDragonFlowViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestSereniteaPotCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopSereniteaPotTestCommand))]
    private bool _isPotTestRunning;

    [ObservableProperty] private bool _potTestIncludeRewards;
    [ObservableProperty] private bool _potTestClaimMailFirst;
    [ObservableProperty] private string _potTestStatus = "请先进入大世界，并在启动页开启截图器。点击下方按钮后才开始测试。";
    private CancellationTokenSource? _potTestCancellation;
    private string? _lastPotTestDirectory;
    public string PotTestInstance => $"测试实例：Windows 会话 {Process.GetCurrentProcess().SessionId} / PID {Environment.ProcessId}";
    private bool CanTestSereniteaPot() => !IsPotTestRunning;
    private bool CanStopSereniteaPotTest() => IsPotTestRunning;

    [RelayCommand(CanExecute = nameof(CanTestSereniteaPot))]
    private async Task TestSereniteaPot(string entryType)
    {
        if (entryType is not ("尘歌壶道具" or "地图传送")) return;
        if (!TaskContext.Instance().IsInitialized || App.GetService<HomePageViewModel>()?.TaskDispatcherEnabled != true
            || !User32.IsWindow(TaskContext.Instance().GameHandle))
        {
            PotTestStatus = "测试未启动：请先进入游戏，并在启动页开启截图器。";
            return;
        }
        uint gamePid;
        int gameSessionId;
        try
        {
            _ = User32.GetWindowThreadProcessId(TaskContext.Instance().GameHandle, out gamePid);
            using var game = Process.GetProcessById((int)gamePid);
            gameSessionId = game.SessionId;
            if (gameSessionId != Process.GetCurrentProcess().SessionId)
            {
                PotTestStatus = "测试未启动：游戏与当前 BGI 不在同一个 Windows 会话。";
                return;
            }
        }
        catch (Exception e)
        {
            PotTestStatus = $"测试未启动：无法确认游戏进程，{e.Message}";
            return;
        }

        IsPotTestRunning = true;
        using var cancellation = new CancellationTokenSource();
        _potTestCancellation = cancellation;
        var includeRewards = PotTestIncludeRewards;
        var claimMailFirst = PotTestClaimMailFirst;
        SereniteaPotTestSession? session = null;
        var result = "not-started";
        var detail = "已有任务运行或截图器未就绪，请查看主日志。";
        try
        {
            var assembly = typeof(OneDragonFlowViewModel).Assembly;
            var binaryPath = string.IsNullOrEmpty(assembly.Location) ? Environment.ProcessPath! : assembly.Location;
            using var binary = File.OpenRead(binaryPath);
            session = new SereniteaPotTestSession(Global.Absolute("log/sereniteapot-tests"), new
            {
                EntryType = entryType, IncludeRewards = includeRewards, ClaimMailFirst = claimMailFirst,
                BgiProcessId = Environment.ProcessId, WindowsSessionId = gameSessionId, GameProcessId = gamePid,
                Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                BinarySha256 = Convert.ToHexString(SHA256.HashData(binary)),
                ProgramDirectory = AppContext.BaseDirectory, TaskContext.Instance().Config.CaptureMode
            });
            _lastPotTestDirectory = session.DirectoryPath;
            PotTestStatus = $"测试中：{session.RunId}";
            await new TaskRunner().RunThreadAsync(async () =>
            {
                using var logScope = SereniteaPotTestLogSink.Capture(session);
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellation.Token, CancellationContext.Instance.Cts.Token, timeout.Token);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    _logger.LogInformation("尘歌壶手动测试开始：{RunId}，入口={EntryType}，先领邮件={Mail}，包含奖励={Rewards}",
                        session.RunId, entryType, claimMailFirst, includeRewards);
                    SereniteaPotUi.SaveCapture("test-start");
                    if (claimMailFirst) await new ClaimMailRewardsTask().DoOnce(linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    var success = await new GoToSereniteaPotTask().TestAsync(entryType, includeRewards, linked.Token);
                    result = success ? (includeRewards ? "reward-flow-finished" : "entry-confirmed") : "failed";
                    detail = success
                        ? (includeRewards ? "阿圆与奖励流程已结束，请核对截图中的领取结果。" : "已确认进入壶内，角色停留在壶内。")
                        : $"测试失败，阶段：{session.FailureStage ?? "查看 test.log"}。";
                    SereniteaPotUi.SaveCapture("test-end");
                }
                catch (Exception e) when (e is OperationCanceledException or NormalEndException)
                {
                    result = timeout.IsCancellationRequested && !cancellation.IsCancellationRequested ? "timed-out" : "cancelled";
                    detail = result == "timed-out" ? "测试超过 5 分钟，已停止。" : "测试已停止。";
                    _logger.LogWarning("尘歌壶手动测试中断：{Reason}", e.Message);
                }
                catch (Exception e)
                {
                    result = "error";
                    detail = e.Message;
                    _logger.LogError(e, "尘歌壶手动测试异常");
                    SereniteaPotUi.SaveFailure("test-exception");
                }
                finally
                {
                    _logger.LogInformation("尘歌壶手动测试结束：{RunId}，结果={Result}，说明={Detail}", session.RunId, result, detail);
                }
            });
            session.Complete(result, detail);
            PotTestStatus = $"{detail} 记录：{session.RunId}";
        }
        catch (Exception e)
        {
            _logger.LogError(e, "准备或保存尘歌壶测试记录失败");
            PotTestStatus = $"测试记录异常：{e.Message}。请同时检查主日志。";
        }
        finally
        {
            session?.Dispose();
            _potTestCancellation = null;
            IsPotTestRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopSereniteaPotTest))]
    private void StopSereniteaPotTest()
    {
        _potTestCancellation?.Cancel();
        PotTestStatus = "正在停止本次测试并保存记录…";
    }

    [RelayCommand]
    private void OpenSereniteaPotTestLogs()
    {
        try
        {
            var directory = _lastPotTestDirectory ?? Global.Absolute("log/sereniteapot-tests");
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            PotTestStatus = $"无法打开测试记录：{e.Message}";
        }
    }
}
