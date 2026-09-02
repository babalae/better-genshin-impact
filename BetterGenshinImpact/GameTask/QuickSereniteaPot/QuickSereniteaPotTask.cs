using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

public class QuickSereniteaPotTask
{
    private const int MaxGadgetPageCount = 20;
    private const int MaxScrollToTopPageCount = 20;

    /// <summary>
    /// 快捷键兼容入口。
    /// </summary>
    public static void Done()
    {
        _ = Task.Run(() => Start(CancellationToken.None));
    }

    /// <summary>
    /// 尝试放置尘歌壶并触发进入或离开交互。
    /// </summary>
    /// <param name="ct">用于取消任务的令牌。</param>
    /// <returns>成功触发进入或离开尘歌壶时返回 true。</returns>
    public static async Task<bool> Start(CancellationToken ct)
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

        try
        {
            await AutoArtifactSalvageTask.OpenInventory(
                GridScreenName.Gadget,
                Simulation.SendInput,
                TaskControl.Logger,
                ct);

            if (!IsGadgetPageOpen())
            {
                TaskControl.Logger.LogWarning("快速进出尘歌壶:未能打开背包小道具页");
                return false;
            }

            if (!await FindAndClickPotIcon(ct))
            {
                return false;
            }

            var confirmClicked = await NewRetry.WaitForAction(() =>
            {
                using var capture = TaskControl.CaptureToRectArea(forceNew: true);
                return Bv.ClickWhiteConfirmButton(capture);
            }, ct, 5, 400);
            if (!confirmClicked)
            {
                TaskControl.Logger.LogWarning("快速进出尘歌壶:未找到放置按钮");
                return false;
            }

            if (!await Bv.WaitForMainUi(ct, 8))
            {
                TaskControl.Logger.LogWarning("快速进出尘歌壶:放置后未返回主界面");
                return false;
            }

            string? action = null;
            var interactionFound = await NewRetry.WaitForAction(() =>
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
                return false;
            }

            TaskControl.Logger.LogInformation("快速进出尘歌壶:识别到 {Action}尘歌壶", action);
            Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
            TaskControl.Logger.LogInformation("快速进出尘歌壶:F{Action}尘歌壶", action);
            await TaskControl.Delay(500, ct);

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
            VisionContext.Instance().DrawContent.ClearAll();
        }
    }

    /// <summary>
    /// 检查背包当前是否位于小道具页。
    /// </summary>
    /// <returns>小道具页已选中时返回 true。</returns>
    private static bool IsGadgetPageOpen()
    {
        using var capture = TaskControl.CaptureToRectArea(forceNew: true);
        using var gadgetTab = capture.Find(ElementRecognition.Get("BagGadgetChecked", capture));
        return gadgetTab.IsExist();
    }

    /// <summary>
    /// 从小道具列表顶部开始逐页查找并点击尘歌壶。
    /// </summary>
    /// <param name="ct">用于取消任务的令牌。</param>
    /// <returns>找到并点击尘歌壶时返回 true。</returns>
    private static async Task<bool> FindAndClickPotIcon(CancellationToken ct)
    {
        var gridParams = GridParams.Templates[GridScreenName.Gadget];
        if (!await ScrollToTop(gridParams, ct))
        {
            return false;
        }

        var scroller = new GridScroller(gridParams, TaskControl.Logger, Simulation.SendInput, ct);
        for (var page = 1; page <= MaxGadgetPageCount; page++)
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await TaskControl.Delay(attempt == 1 ? 500 : 300, ct);
                using var capture = TaskControl.CaptureToRectArea(forceNew: true);
                using var potIcon = capture.Find(RecognitionAssets.Get("QuickSereniteaPot", "SereniteaPotIcon", capture));
                if (potIcon.IsExist())
                {
                    TaskControl.Logger.LogInformation("快速进出尘歌壶:在小道具第 {Page} 页找到尘歌壶", page);
                    potIcon.Click();
                    return true;
                }
            }

            if (!await scroller.TryVerticalScollDown((src, columns) => GridScreen.GridEnumerator.GetGridItems(src, columns)))
            {
                TaskControl.Logger.LogWarning("快速进出尘歌壶:检查小道具 {PageCount} 页后仍未检测到壶", page);
                return false;
            }
        }

        TaskControl.Logger.LogWarning("快速进出尘歌壶:达到小道具扫描安全上限 {PageCount} 页后仍未检测到壶", MaxGadgetPageCount);
        return false;
    }

    /// <summary>
    /// 持续向上滚动小道具列表，直到网格内容不再移动。
    /// </summary>
    /// <param name="gridParams">小道具网格参数。</param>
    /// <param name="ct">用于取消任务的令牌。</param>
    /// <returns>确认到达列表顶部时返回 true；达到安全上限时返回 false。</returns>
    private static async Task<bool> ScrollToTop(GridParams gridParams, CancellationToken ct)
    {
        using var capture = TaskControl.CaptureToRectArea(forceNew: true);
        using var grid = capture.DeriveCrop(gridParams.Roi);
        grid.Move();

        for (var page = 1; page <= MaxScrollToTopPageCount; page++)
        {
            using var previousCapture = TaskControl.CaptureToRectArea(forceNew: true);
            using var previousGrid = previousCapture.DeriveCrop(gridParams.Roi);

            for (var i = 0; i < gridParams.S1Round; i++)
            {
                Simulation.SendInput.Mouse.VerticalScroll(2);
                await TaskControl.Delay(gridParams.RoundMilliseconds, ct);
            }

            await TaskControl.Delay(300, ct);
            using var currentCapture = TaskControl.CaptureToRectArea(forceNew: true);
            using var currentGrid = currentCapture.DeriveCrop(gridParams.Roi);
            if (!GridScroller.IsScrolling(
                    previousGrid.CacheGreyMat,
                    currentGrid.CacheGreyMat,
                    out _,
                    logger: TaskControl.Logger))
            {
                await TaskControl.Delay(300, ct);
                return true;
            }

            for (var i = 0; i < gridParams.S2Round; i++)
            {
                Simulation.SendInput.Mouse.VerticalScroll(2);
                await TaskControl.Delay(gridParams.RoundMilliseconds, ct);
            }

            await TaskControl.Delay(300, ct);
        }

        TaskControl.Logger.LogWarning("快速进出尘歌壶:达到回顶安全上限 {PageCount} 页，无法确认已到达小道具列表顶部", MaxScrollToTopPageCount);
        return false;
    }
}
