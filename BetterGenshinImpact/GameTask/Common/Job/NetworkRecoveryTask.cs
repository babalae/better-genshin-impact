using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>只在一次实际断网后的恢复窗口中处理确认按钮和登录页面。</summary>
public sealed class NetworkRecoveryTask
{
    private static readonly ILogger RecoveryLogger = App.GetLogger<NetworkRecoveryTask>();
    private static readonly string[] DisconnectTexts =
    [
        "连接已断开", "连接超时", "与服务器断开连接", "网络错误", "无法登录服务器", "重新连接"
    ];
    private static readonly string[] ConfirmTexts = ["确认", "确定", "重新连接", "点击进入", "知道了"];
    private const int DialogWaitRounds = 30;
    private const int DialogWaitIntervalMs = 1000;
    private const int PostDialogWaitRounds = 5;
    private const int PostDialogWaitIntervalMs = 2000;

    public static NetworkRecoveryController CreateController(CancellationToken ct) => new(
        () =>
        {
            var config = TaskContext.Instance().Config.OtherConfig;
            return config.NetworkHealthMonitoringEnabled ? config.NetworkProbeTarget : null;
        },
        token => new NetworkRecoveryTask().Start(token), ct,
        onError: e => RecoveryLogger.LogWarning(e, "网络恢复发生异常，等待下次重试"),
        onInfo: message => RecoveryLogger.LogInformation("{Message}", message),
        onWarning: message => RecoveryLogger.LogWarning("{Message}", message));

    public async Task<bool> Start(CancellationToken ct)
    {
        // 恢复栈只豁免网络暂停，手动暂停仍然有效。
        TrySuspend(ct);
        RecoveryLogger.LogInformation("正在激活游戏窗口并检查断线界面");
        SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
        RecoveryLogger.LogInformation("网络先于游戏弹窗恢复，最多等待 30 秒监测断线确认按钮");

        var playableSeen = false;
        for (var round = 0; round < DialogWaitRounds; round++)
        {
            ct.ThrowIfCancellationRequested();
            using var screen = CaptureToRectArea();

            // 必须先处理遮挡在主界面上的断网确认框，否则背景中的主界面特征仍会命中。
            var dialogResult = TryDismissDisconnectDialog(screen);
            if (dialogResult == DialogResult.Clicked)
            {
                return await WaitAfterDialogAsync(ct);
            }

            if (dialogResult == DialogResult.None)
            {
                var loginResult = await TryHandleLoginUiAsync(screen, ct);
                if (loginResult.HasValue) return loginResult.Value;
                playableSeen |= IsPlayableUi(screen);
            }

            // DetectedButNotClicked 时同样保持暂停并继续采样，避免偶发 OCR/模板漏检。
            await Delay(DialogWaitIntervalMs, ct);
        }

        if (playableSeen)
        {
            RecoveryLogger.LogInformation("等待 30 秒未出现断线弹窗，游戏界面持续可用，解除暂停");
            return true;
        }

        RecoveryLogger.LogWarning("等待断线确认按钮超时，且未识别到可用游戏界面，继续保持暂停");
        return false;
    }

    private static bool IsPlayableUi(ImageRegion image) =>
        Bv.IsInMainUi(image) || Bv.IsInAnyClosableUi(image) || Bv.IsInDomain(image);

    private static async Task<bool> WaitAfterDialogAsync(CancellationToken ct)
    {
        var playableSeen = false;
        // 点掉断线窗口后，登录界面可能延迟数秒出现；不能马上被背景主界面误判成功。
        for (var round = 0; round < PostDialogWaitRounds; round++)
        {
            await Delay(PostDialogWaitIntervalMs, ct);
            using var screen = CaptureToRectArea();
            var dialogResult = TryDismissDisconnectDialog(screen);
            if (dialogResult != DialogResult.None) continue;

            var loginResult = await TryHandleLoginUiAsync(screen, ct);
            if (loginResult.HasValue) return loginResult.Value;
            playableSeen |= IsPlayableUi(screen);
        }

        if (playableSeen)
        {
            RecoveryLogger.LogInformation("断线弹窗已关闭，等待登录界面后确认游戏仍可操作");
            return true;
        }

        RecoveryLogger.LogWarning("断线弹窗已关闭，但暂未识别到主界面或登录界面");
        return false;
    }

    private static async Task<bool?> TryHandleLoginUiAsync(ImageRegion screen, CancellationToken ct)
    {
        using var enter = screen.Find(RecognitionAssets.Get("GameLoading", "EnterGame", screen));
        using var choose = screen.Find(RecognitionAssets.Get("GameLoading", "ChooseEnterGame", screen));
        if (choose.IsExist())
        {
            RecoveryLogger.LogInformation("检测到重新进入按钮，确认重新进入游戏");
            choose.Click();
            await Delay(1000, ct);
            using var afterChoose = CaptureToRectArea();
            return IsPlayableUi(afterChoose) ? true : null;
        }

        if (!enter.IsExist()) return null;

        RecoveryLogger.LogInformation("检测到登录界面，复用现有登录流程重新进入游戏");
        return await new ExitAndReloginJob().EnterGameAsync(ct);
    }

    private static DialogResult TryDismissDisconnectDialog(ImageRegion screen)
    {
        var textRegions = screen.FindMulti(RecognitionObject.Ocr(
            screen.Width * 0.25, screen.Height * 0.25,
            screen.Width * 0.5, screen.Height * 0.5));
        var evidence = textRegions.FirstOrDefault(region => MatchesAny(region.Text, DisconnectTexts));

        if (evidence is not null)
        {
            var button = textRegions.FirstOrDefault(region =>
                !ReferenceEquals(region, evidence) && MatchesAny(region.Text, ConfirmTexts));
            if (button is not null)
            {
                RecoveryLogger.LogInformation("检测到断线弹窗“{Evidence}”，点击按钮“{Button}”",
                    evidence.Text, button.Text);
                button.Click();
                return DialogResult.Clicked;
            }

            // OCR 按钮文案可能因语言或合框失败；复用上游黑/白/联机确认按钮模板兜底。
            if (Bv.ClickConfirmButton(screen))
            {
                RecoveryLogger.LogInformation("检测到断线弹窗“{Evidence}”，已通过通用确认按钮关闭", evidence.Text);
                return DialogResult.Clicked;
            }

            RecoveryLogger.LogWarning("检测到断线弹窗“{Evidence}”，但未识别到可点击的确认按钮", evidence.Text);
            return DialogResult.DetectedButNotClicked;
        }

        if (!Bv.IsInPromptDialog(screen)) return DialogResult.None;

        if (Bv.ClickConfirmButton(screen))
        {
            RecoveryLogger.LogInformation("检测到提示弹窗，已通过通用确认按钮关闭");
            return DialogResult.Clicked;
        }

        RecoveryLogger.LogWarning("检测到提示弹窗外观，但未识别到可点击的确认按钮");
        return DialogResult.DetectedButNotClicked;
    }

    private static bool MatchesAny(string? text, string[] candidates) =>
        !string.IsNullOrWhiteSpace(text) && candidates.Any(candidate =>
            text.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private enum DialogResult
    {
        None,
        Clicked,
        DetectedButNotClicked
    }
}
