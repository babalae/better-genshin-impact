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
    private const int PostDialogWaitRounds = 15;
    private const int PostDialogWaitIntervalMs = 2000;
    private const int StablePlayableRounds = 3;
    private const int DisconnectClickCooldownMs = 5000;
    private string? _lastDisconnectClickKey;
    private long _lastDisconnectClickAt;

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
        SystemControl.RestoreWindow(TaskContext.Instance().GameHandle);
        RecoveryLogger.LogInformation("网络先于游戏弹窗恢复，最多等待 30 秒监测断线确认按钮");

        var consecutivePlayableRounds = 0;
        for (var round = 0; round < DialogWaitRounds; round++)
        {
            ct.ThrowIfCancellationRequested();
            using var screen = CaptureToRectArea();

            // 必须先处理遮挡在主界面上的断网确认框，否则背景中的主界面特征仍会命中。
            var dialogResult = await TryDismissDisconnectDialogAsync(screen, ct);
            if (dialogResult == DialogResult.Clicked)
            {
                return await WaitAfterDialogAsync(ct);
            }

            if (dialogResult == DialogResult.None)
            {
                var loginResult = await TryHandleLoginUiAsync(screen, ct);
                if (loginResult.HasValue) return loginResult.Value;
                consecutivePlayableRounds = IsPlayableUi(screen)
                    ? consecutivePlayableRounds + 1
                    : 0;
            }
            else
            {
                consecutivePlayableRounds = 0;
            }

            // DetectedButNotClicked 时同样保持暂停并继续采样，避免偶发 OCR/模板漏检。
            await Delay(DialogWaitIntervalMs, ct);
        }

        if (consecutivePlayableRounds >= StablePlayableRounds)
        {
            RecoveryLogger.LogInformation("等待 30 秒未出现断线弹窗，且最终连续识别到可用游戏界面，解除暂停");
            return true;
        }

        RecoveryLogger.LogWarning("等待断线确认按钮超时，且未识别到可用游戏界面，继续保持暂停");
        return false;
    }

    private static bool IsPlayableUi(ImageRegion image) =>
        Bv.IsInMainUi(image) || Bv.IsInDomain(image);

    private async Task<bool> WaitAfterDialogAsync(CancellationToken ct)
    {
        var consecutivePlayableRounds = 0;
        // 点掉断线窗口后，登录界面可能延迟数秒出现；不能马上被背景主界面误判成功。
        for (var round = 0; round < PostDialogWaitRounds; round++)
        {
            await Delay(PostDialogWaitIntervalMs, ct);
            using var screen = CaptureToRectArea();
            var dialogResult = await TryDismissDisconnectDialogAsync(screen, ct);
            if (dialogResult != DialogResult.None)
            {
                consecutivePlayableRounds = 0;
                continue;
            }

            var loginResult = await TryHandleLoginUiAsync(screen, ct);
            if (loginResult.HasValue) return loginResult.Value;
            consecutivePlayableRounds = IsPlayableUi(screen)
                ? consecutivePlayableRounds + 1
                : 0;
        }

        if (consecutivePlayableRounds >= StablePlayableRounds)
        {
            RecoveryLogger.LogInformation("断线弹窗已关闭，且最终连续识别到游戏仍可操作");
            return true;
        }

        RecoveryLogger.LogWarning("断线弹窗已关闭，但暂未识别到主界面或登录界面");
        return false;
    }

    private static async Task<bool?> TryHandleLoginUiAsync(ImageRegion screen, CancellationToken ct,
        bool refreshedAfterFocus = false)
    {
        using var enter = screen.Find(RecognitionAssets.Get("GameLoading", "EnterGame", screen));
        using var choose = screen.Find(RecognitionAssets.Get("GameLoading", "ChooseEnterGame", screen));
        if (!enter.IsExist() && !choose.IsExist()) return null;

        // 截图可能来自后台窗口。所有真实输入前先激活原神并重新截图，避免旧截图坐标
        // 被发送到 Explorer、BGI 或其他当前前台窗口。
        if (!refreshedAfterFocus)
        {
            if (!await FocusGameForInteractionAsync(ct)) return null;
            using var refreshed = CaptureToRectArea();
            return await TryHandleLoginUiAsync(refreshed, ct, true);
        }

        if (!SystemControl.IsGenshinImpactActiveByProcess())
        {
            RecoveryLogger.LogWarning("登录操作前原神已失去焦点，本轮不发送输入");
            return null;
        }

        if (choose.IsExist())
        {
            RecoveryLogger.LogInformation("检测到重新进入按钮，确认重新进入游戏，识别区域：({X},{Y},{Width},{Height})",
                choose.X, choose.Y, choose.Width, choose.Height);
            choose.Click();
            await Delay(1000, ct);
            // 点击后仍回到外层连续识别流程，不能用单帧主界面结果提前解除暂停。
            return null;
        }

        RecoveryLogger.LogInformation("检测到登录界面，复用现有登录流程重新进入游戏");
        return await new ExitAndReloginJob().EnterGameAsync(ct);
    }

    private async Task<DialogResult> TryDismissDisconnectDialogAsync(ImageRegion screen, CancellationToken ct,
        bool refreshedAfterFocus = false)
    {
        var textRegions = screen.FindMulti(RecognitionObject.Ocr(
            screen.Width * 0.25, screen.Height * 0.25,
            screen.Width * 0.5, screen.Height * 0.5));
        var evidence = textRegions.FirstOrDefault(region => MatchesAny(region.Text, DisconnectTexts));

        if (evidence is not null)
        {
            // 后台截图仍能识别到弹窗，但 Region.Click 使用的是系统级真实鼠标。
            // 点击前必须重新激活、验证并截图，绝不能沿用后台截图的坐标直接点击。
            if (!refreshedAfterFocus)
            {
                if (!await FocusGameForInteractionAsync(ct)) return DialogResult.DetectedButNotClicked;
                using var refreshed = CaptureToRectArea();
                return await TryDismissDisconnectDialogAsync(refreshed, ct, true);
            }

            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                RecoveryLogger.LogWarning("断线确认操作前原神已失去焦点，本轮不发送鼠标输入");
                return DialogResult.DetectedButNotClicked;
            }

            var evidenceCenterX = evidence.X + evidence.Width / 2d;
            var button = textRegions
                .Where(region => !ReferenceEquals(region, evidence) &&
                                 MatchesAny(region.Text, ConfirmTexts) &&
                                 region.Y >= evidence.Y &&
                                 Math.Abs(region.X + region.Width / 2d - evidenceCenterX) <= screen.Width * 0.3)
                .OrderBy(region => region.Y - evidence.Y)
                .FirstOrDefault();
            if (button is not null)
            {
                if (IsDisconnectClickCoolingDown(evidence.Text))
                    return DialogResult.DetectedButNotClicked;

                // OCR 与日志之间仍可能发生极短的焦点变化，发送输入前再做最后一次验证。
                if (!SystemControl.IsGenshinImpactActiveByProcess())
                {
                    RecoveryLogger.LogWarning("断线确认点击前原神已失去焦点，本轮不发送鼠标输入");
                    return DialogResult.DetectedButNotClicked;
                }

                RecoveryLogger.LogInformation(
                    "检测到断线弹窗“{Evidence}”，点击按钮“{Button}”，识别区域：({X},{Y},{Width},{Height})",
                    evidence.Text, button.Text, button.X, button.Y, button.Width, button.Height);
                button.Click();
                RecordDisconnectClick(evidence.Text);
                return DialogResult.Clicked;
            }

            // OCR 按钮文案可能因语言或合框失败；复用上游黑/白/联机确认按钮模板兜底。
            if (IsDisconnectClickCoolingDown(evidence.Text))
                return DialogResult.DetectedButNotClicked;

            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                RecoveryLogger.LogWarning("断线确认模板点击前原神已失去焦点，本轮不发送鼠标输入");
                return DialogResult.DetectedButNotClicked;
            }

            if (Bv.ClickConfirmButton(screen))
            {
                RecordDisconnectClick(evidence.Text);
                RecoveryLogger.LogInformation("检测到断线弹窗“{Evidence}”，已通过通用确认按钮关闭", evidence.Text);
                return DialogResult.Clicked;
            }

            RecoveryLogger.LogWarning("检测到断线弹窗“{Evidence}”，但未识别到可点击的确认按钮", evidence.Text);
            return DialogResult.DetectedButNotClicked;
        }

        // 通用提示框可能是树脂、购买、退出等确认；没有网络错误文字证据时不点击，
        // 也不把其背后的主界面当作已经恢复。
        return Bv.IsInPromptDialog(screen)
            ? DialogResult.DetectedButNotClicked
            : DialogResult.None;
    }

    private bool IsDisconnectClickCoolingDown(string evidence)
    {
        var key = evidence.Trim();
        return string.Equals(_lastDisconnectClickKey, key, StringComparison.OrdinalIgnoreCase) &&
               Environment.TickCount64 - _lastDisconnectClickAt < DisconnectClickCooldownMs;
    }

    private void RecordDisconnectClick(string evidence)
    {
        _lastDisconnectClickKey = evidence.Trim();
        _lastDisconnectClickAt = Environment.TickCount64;
    }

    private static async Task<bool> FocusGameForInteractionAsync(CancellationToken ct)
    {
        var previousForeground = SystemControl.GetActiveByProcess();
        SystemControl.RestoreWindow(TaskContext.Instance().GameHandle);
        await Task.Delay(200, ct);
        if (SystemControl.IsGenshinImpactActiveByProcess())
        {
            if (!string.Equals(previousForeground, SystemControl.GetActiveByProcess(), StringComparison.OrdinalIgnoreCase))
                RecoveryLogger.LogInformation("恢复操作前已将焦点从 {Previous} 切换到原神", previousForeground);
            return true;
        }

        RecoveryLogger.LogWarning("恢复操作前无法激活原神，当前前台窗口：{Foreground}，本轮不发送输入",
            SystemControl.GetActiveByProcess());
        return false;
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
