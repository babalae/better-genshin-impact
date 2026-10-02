using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

public class QuickSereniteaPotTask
{
    private static void WaitForUi(Func<ImageRegion, bool> isReady, string failureMessage, CancellationToken ct)
    {
        // 慢速设备允许约 30 秒加载；暂停期间不消耗识别次数。
        for (var i = 0; i < 60; i++)
        {
            TaskControl.Sleep(500, ct);
            using var capture = TaskControl.CaptureToRectArea(forceNew: true);
            ct.ThrowIfCancellationRequested();
            var ready = isReady(capture);
            ct.ThrowIfCancellationRequested();
            if (ready) return;
        }
        throw new RetryException(failureMessage);
    }

    public static void Done() => TryEnter(CancellationToken.None, out _);

    // 返回值表示已触发进出壶交互；调用者仍需等待加载并确认壶内状态。
    internal static bool TryEnter(CancellationToken ct, out bool potUiVisibleBeforeInteraction)
    {
        potUiVisibleBeforeInteraction = false;
        if (!TaskContext.Instance().IsInitialized)
        {
            Toast.Warning("请先启动");
            return false;
        }

        if (!SystemControl.IsGenshinImpactActiveByProcess())
        {
            return false;
        }

        try
        {
            var bagRequested = false;
            WaitForUi(capture =>
            {
                using var close = capture.Find(RecognitionAssets.Get("QuickTeleport", "MapCloseButton", capture));
                if (close.IsExist() && !Bv.IsInBigMapUi(capture)) return true;
                // 等主界面加载后只按一次 B；已经打开背包时不再关闭它。
                if (!bagRequested && Bv.IsInMainUi(capture))
                {
                    ct.ThrowIfCancellationRequested();
                    Simulation.SendInput.SimulateAction(GIActions.OpenInventory);
                    bagRequested = true;
                }
                return false;
            }, "背包未打开", ct);

            // 等待道具页选中，不能在切页动画期间查找壶。
            var tabClicked = false;
            WaitForUi(capture =>
            {
                using var selected = capture.Find(ElementRecognition.Get("BagGadgetChecked", capture));
                if (selected.IsExist()) return true;
                using var tab = capture.Find(ElementRecognition.Get("BagGadgetUnchecked", capture));
                if (!tabClicked && tab.IsExist())
                {
                    ct.ThrowIfCancellationRequested();
                    tab.Click();
                    tabClicked = true;
                }
                return false;
            }, "道具页未打开", ct);

            WaitForUi(capture =>
            {
                using var pot = capture.Find(RecognitionAssets.Get("QuickSereniteaPot", "SereniteaPotIcon", capture));
                if (pot.IsEmpty()) return false;
                ct.ThrowIfCancellationRequested();
                pot.Click();
                return true;
            }, "未检测到壶", ct);

            WaitForUi(capture =>
            {
                using var confirm = capture.Find(ElementRecognition.Get("BtnWhiteConfirm", capture));
                if (confirm.IsEmpty()) return false;
                ct.ThrowIfCancellationRequested();
                confirm.Click();
                return true;
            }, "未找到尘歌壶放置按钮", ct);
            WaitForUi(Bv.IsInMainUi, "放置尘歌壶后未返回主界面", ct);

            bool isEnter = false;
            WaitForUi(capture =>
            {
                isEnter = Bv.FindF(capture, "进入", "尘歌壶");
                return isEnter || Bv.FindF(capture, "离开", "尘歌壶");
            }, "未识别到进入或离开尘歌壶", ct);

            string action = isEnter ? "进入" : "离开";
            TaskControl.Logger.LogInformation("快速进出尘歌壶:识别到 {Action}尘歌壶", action);
            // 记录交互前的状态，避免把未响应的原界面当作已经加载完成。
            using (var capture = TaskControl.CaptureToRectArea(forceNew: true))
            {
                using var finger = capture.Find(ElementRecognition.Get("FingerIcon", capture));
                potUiVisibleBeforeInteraction = Bv.IsInMainUi(capture) && finger.IsExist();
            }
            ct.ThrowIfCancellationRequested();
            Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
            TaskControl.Sleep(500, ct);
            // 联机状态下确认进入/离开；单人状态下此时已经开始传送。
            GameCaptureRegion.GameRegion1080PPosClick(1010, 760);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (NormalEndException) { throw; }
        catch (Exception e)
        {
            TaskControl.Logger.LogWarning(e.Message);
            return false;
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
        }
    }
}
