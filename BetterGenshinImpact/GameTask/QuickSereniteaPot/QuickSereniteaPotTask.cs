using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

public class QuickSereniteaPotTask
{
    private static readonly SemaphoreSlim ExecutionLock = new(1, 1);

    /// <summary>
    /// 快捷键兼容入口。
    /// </summary>
    public static void Done()
    {
        new TaskRunner().FireAndForget(() => Start(CancellationContext.Instance.Cts.Token));
    }

    /// <summary>
    /// 尝试放置尘歌壶并触发进入或离开交互。
    /// </summary>
    /// <param name="ct">用于取消任务的令牌。</param>
    /// <returns>成功触发进入或离开尘歌壶时返回 true。</returns>
    public static async Task<bool> Start(CancellationToken ct)
    {
        if (!await ExecutionLock.WaitAsync(0, ct))
        {
            TaskControl.Logger.LogWarning("快速进出尘歌壶:已有任务正在执行，忽略重复触发");
            return false;
        }

        try
        {
            if (!TaskContext.Instance().IsInitialized)
            {
                UIDispatcherHelper.Invoke(() => Toast.Warning("请先启动"));
                return false;
            }

            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                return false;
            }

            if (!await OpenGadgetPage(ct))
            {
                TaskControl.Logger.LogWarning("快速进出尘歌壶:未能打开背包小道具页");
                return false;
            }

            if (!await FindAndClickPotIcon(ct))
            {
                SereniteaPotUi.SaveFailure("pot-icon");
                return false;
            }

            var confirmClicked = await SereniteaPotTaskControl.WaitForAction(() =>
            {
                using var capture = TaskControl.CaptureToRectArea(forceNew: true);
                return Bv.ClickWhiteConfirmButton(capture);
            }, ct, 5, 400);
            if (!confirmClicked)
            {
                TaskControl.Logger.LogWarning("快速进出尘歌壶:未找到放置按钮");
                SereniteaPotUi.SaveFailure("place-button");
                return false;
            }

            if (!await SereniteaPotUi.WaitForMainUi(ct, "after-placement"))
            {
                TaskControl.Logger.LogWarning("快速进出尘歌壶:放置后未返回主界面");
                return false;
            }

            string? action = null;
            var interactionFound = await SereniteaPotTaskControl.WaitForAction(() =>
            {
                using var capture = TaskControl.CaptureToRectArea(forceNew: true);
                if (Bv.FindF(capture, "进入", "尘歌壶"))
                {
                    action = "进入";
                    return true;
                }

                if (Bv.FindF(capture, "离开", "尘歌壶"))
                {
                    action = "离开";
                    return true;
                }

                return false;
            }, ct, 8, 500);
            if (!interactionFound)
            {
                TaskControl.Logger.LogWarning("快速进出尘歌壶:未识别到进入或离开尘歌壶交互");
                SereniteaPotUi.SaveFailure("pot-interaction");
                return false;
            }

            TaskControl.Logger.LogInformation("快速进出尘歌壶:识别到 {Action}尘歌壶", action);
            Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
            TaskControl.Logger.LogInformation("快速进出尘歌壶:F{Action}尘歌壶", action);
            await SereniteaPotTaskControl.Delay(500, ct);

            // 联机状态下需要额外点击进入/离开选项；单人状态下该点击不会影响传送。
            GameCaptureRegion.GameRegion1080PPosClick(1010, 760);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NormalEndException)
        {
            throw;
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogWarning(e, "快速进出尘歌壶失败");
            return false;
        }
        finally
        {
            try { VisionContext.Instance().DrawContent.ClearAll(); }
            finally { ExecutionLock.Release(); }
        }
    }

    /// <summary>
    /// 打开并确认小道具页。点击未选中标签只是开始切页，不能视为切页成功。
    /// </summary>
    private static async Task<bool> OpenGadgetPage(CancellationToken ct)
    {
        bool inBag;
        using (var capture = TaskControl.CaptureToRectArea(forceNew: true))
        {
            using var selected = capture.Find(ElementRecognition.Get("BagGadgetChecked", capture));
            using var unselected = capture.Find(ElementRecognition.Get("BagGadgetUnchecked", capture));
            inBag = selected.IsExist() || unselected.IsExist();
        }

        // 热键也可能从已打开的背包触发；这种情况不能再次按 B 把背包关掉。
        if (!inBag)
        {
            await new ReturnMainUiTask().Start(ct);
            if (!await SereniteaPotUi.WaitForMainUi(ct, "before-bag"))
            {
                return false;
            }
        }

        var watch = Stopwatch.StartNew();
        long nextInputAt = 0;
        var ready = await SereniteaPotWaiter.WaitAsync(() =>
        {
            using var capture = TaskControl.CaptureToRectArea(forceNew: true);
            using var selected = capture.Find(ElementRecognition.Get("BagGadgetChecked", capture));
            if (selected.IsExist())
            {
                return true;
            }

            if (watch.ElapsedMilliseconds < nextInputAt)
            {
                return false;
            }

            using var unselected = capture.Find(ElementRecognition.Get("BagGadgetUnchecked", capture));
            if (unselected.IsExist())
            {
                unselected.Click();
                TaskControl.Logger.LogDebug("快速进出尘歌壶:点击小道具标签，等待选中状态");
            }
            else if (Bv.IsInPromptDialog(capture))
            {
                Bv.ClickWhiteConfirmButton(capture);
            }
            else if (Bv.IsInMainUi(capture))
            {
                Simulation.SendInput.SimulateAction(GIActions.OpenInventory);
                TaskControl.Logger.LogDebug("快速进出尘歌壶:从主界面打开背包");
            }

            nextInputAt = watch.ElapsedMilliseconds + 1500;
            return false;
        }, SereniteaPotTaskControl.Delay, ct, TimeSpan.FromSeconds(20));

        if (!ready)
        {
            SereniteaPotUi.SaveFailure("gadget-page");
        }
        return ready;
    }

    private static async Task<bool> FindAndClickPotIcon(CancellationToken ct)
    {
        var gridParams = GridParams.Templates[GridScreenName.Gadget];
        var found = await SereniteaPotInventorySearch.FindAsync(() =>
        {
            using var capture = TaskControl.CaptureToRectArea(forceNew: true);
            using var selected = capture.Find(ElementRecognition.Get("BagGadgetChecked", capture));
            if (!selected.IsExist()) return false;
            using var pot = capture.Find(RecognitionAssets.Get("QuickSereniteaPot", "SereniteaPotIcon", capture));
            if (!pot.IsExist()) return false;
            ct.ThrowIfCancellationRequested();
            pot.Click();
            return true;
        }, (direction, token) => ScrollGadgetList(gridParams, direction, token), ct);
        if (!found) TaskControl.Logger.LogWarning("快速进出尘歌壶:未找到壶或无法确认列表边界，停止扫描");
        return found;
    }

    private static async Task<PotScrollObservation> ScrollGadgetList(GridParams gridParams, int direction, CancellationToken ct)
    {
        await SereniteaPotTaskControl.Delay(0, ct);
        using var previousCapture = TaskControl.CaptureToRectArea(forceNew: true);
        using var previousSelected = previousCapture.Find(ElementRecognition.Get("BagGadgetChecked", previousCapture));
        using var previousGrid = previousCapture.DeriveCrop(gridParams.Roi);
        if (!previousSelected.IsExist() || !GridScreen.GridEnumerator.GetGridItems(previousGrid.SrcMat, gridParams.Columns).Any())
        {
            await SereniteaPotTaskControl.Delay(300, ct);
            return PotScrollObservation.Unknown;
        }
        previousGrid.Move();
        // 小步滚动并检查每一屏，避免回顶时跳过当前页已有的壶。
        for (var i = 0; i < gridParams.S1Round; i++)
        {
            ct.ThrowIfCancellationRequested();
            Simulation.SendInput.Mouse.VerticalScroll(direction * 2);
            await SereniteaPotTaskControl.Delay(gridParams.RoundMilliseconds, ct);
        }
        await SereniteaPotTaskControl.Delay(300, ct);
        using var currentCapture = TaskControl.CaptureToRectArea(forceNew: true);
        using var currentSelected = currentCapture.Find(ElementRecognition.Get("BagGadgetChecked", currentCapture));
        using var currentGrid = currentCapture.DeriveCrop(gridParams.Roi);
        if (!currentSelected.IsExist() || !GridScreen.GridEnumerator.GetGridItems(currentGrid.SrcMat, gridParams.Columns).Any())
            return PotScrollObservation.Unknown;
        using var previous = new Mat();
        using var current = new Mat();
        using var window = new Mat();
        previousGrid.CacheGreyMat.ConvertTo(previous, MatType.CV_32FC1);
        currentGrid.CacheGreyMat.ConvertTo(current, MatType.CV_32FC1);
        var shift = Cv2.PhaseCorrelate(previous, current, window, out var response);
        var difference = Cv2.Norm(previous, current, NormTypes.L1) / previous.Total();
        return SereniteaPotInventorySearch.Classify(true, response, shift.X, shift.Y, difference);
    }
}
