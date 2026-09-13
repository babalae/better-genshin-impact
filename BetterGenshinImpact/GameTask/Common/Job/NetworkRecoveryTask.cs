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
    public static NetworkRecoveryController CreateController(CancellationToken ct) => new(
        () =>
        {
            var config = TaskContext.Instance().Config.OtherConfig;
            return config.NetworkHealthMonitoringEnabled ? config.NetworkProbeTarget : null;
        },
        token => new NetworkRecoveryTask().Start(token), ct,
        e => Logger.LogWarning(e, "网络探测或登录恢复失败，等待下次重试"));

    public async Task<bool> Start(CancellationToken ct)
    {
        // 恢复栈只豁免网络暂停，手动暂停仍然有效。
        TrySuspend(ct);
        SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
        using var screen = CaptureToRectArea();
        if (Bv.IsInMainUi(screen)) return true;

        // 此流程只会在连续三次探测失败、随后重新连通后执行。复用已有按钮素材，
        // 避免把登录恢复限定为中文 OCR 文本；一次恢复只点击一次确认按钮。
        using (var confirm = screen.Find(ElementRecognition.Get("BtnWhiteConfirm", screen)))
        {
            if (confirm.IsExist())
            {
                TrySuspend(ct);
                confirm.Click();
                await Delay(1000, ct);
            }
        }

        using var current = CaptureToRectArea();
        if (IsPlayableUi(current)) return true;
        using var enter = current.Find(RecognitionAssets.Get("GameLoading", "EnterGame", current));
        using var choose = current.Find(RecognitionAssets.Get("GameLoading", "ChooseEnterGame", current));
        if (choose.IsExist())
        {
            Logger.LogInformation("网络已连通，确认重新进入游戏");
            choose.Click();
            await Delay(1000, ct);
            using var afterChoose = CaptureToRectArea();
            return IsPlayableUi(afterChoose);
        }

        if (enter.IsExist())
        {
            Logger.LogInformation("网络已连通，尝试重新进入游戏");
            return await new ExitAndReloginJob().EnterGameAsync(ct);
        }

        // 未识别界面不发送键鼠输入，等下一轮恢复再判断。
        return false;
    }

    private static bool IsPlayableUi(ImageRegion image) =>
        Bv.IsInMainUi(image) || Bv.IsInAnyClosableUi(image) || Bv.IsInDomain(image);
}
