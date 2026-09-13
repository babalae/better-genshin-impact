using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>只在一次实际断网后的恢复窗口中处理确认按钮和登录页面。</summary>
public sealed class NetworkRecoveryTask
{
    private static readonly ILogger RecoveryLogger = App.GetLogger<NetworkRecoveryTask>();

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
        using var screen = CaptureToRectArea();

        // 此流程只会在连续三次探测失败、随后重新连通后执行。复用已有按钮素材，
        // 避免把登录恢复限定为中文 OCR 文本。必须先处理遮挡在主界面上的断网确认框，
        // 否则主界面特征可能仍然命中并被误判为无需恢复。
        using (var confirm = screen.Find(ElementRecognition.Get("BtnWhiteConfirm", screen)))
        {
            if (confirm.IsExist())
            {
                TrySuspend(ct);
                RecoveryLogger.LogInformation("检测到确认按钮，点击后重新判断游戏状态");
                confirm.Click();
                await Delay(1000, ct);
            }
            else if (Bv.IsInMainUi(screen))
            {
                RecoveryLogger.LogInformation("游戏仍处于主界面，无需重新登录");
                return true;
            }
        }

        using var current = CaptureToRectArea();
        if (IsPlayableUi(current)) return true;
        using var enter = current.Find(RecognitionAssets.Get("GameLoading", "EnterGame", current));
        using var choose = current.Find(RecognitionAssets.Get("GameLoading", "ChooseEnterGame", current));
        if (choose.IsExist())
        {
            RecoveryLogger.LogInformation("检测到重新进入按钮，确认重新进入游戏");
            choose.Click();
            await Delay(1000, ct);
            using var afterChoose = CaptureToRectArea();
            return IsPlayableUi(afterChoose);
        }

        if (enter.IsExist())
        {
            RecoveryLogger.LogInformation("检测到登录界面，复用现有登录流程重新进入游戏");
            return await new ExitAndReloginJob().EnterGameAsync(ct);
        }

        // 未识别界面不发送键鼠输入，等下一轮恢复再判断。
        RecoveryLogger.LogWarning("未识别当前游戏界面，本轮不发送额外输入");
        return false;
    }

    private static bool IsPlayableUi(ImageRegion image) =>
        Bv.IsInMainUi(image) || Bv.IsInAnyClosableUi(image) || Bv.IsInDomain(image);
}
