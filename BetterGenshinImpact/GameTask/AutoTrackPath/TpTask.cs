using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 传送任务
/// </summary>
public class TpTask(CancellationTokenSource cts)
{
    private readonly QuickTeleportAssets _assets = QuickTeleportAssets.Instance;

    /// <summary>
    /// 通过大地图传送到指定坐标最近的传送点，然后移动到指定坐标
    /// </summary>
    /// <param name="tpX"></param>
    /// <param name="tpY"></param>
    /// <param name="force">强制以当前的tpX,tpY坐标进行自动传送</param>
    public async Task<(double, double)> TpOnce(double tpX, double tpY, bool force = false)
    {
        var (x, y) = (tpX, tpY);

        if (!force)
        {
            // 获取最近的传送点位置
            (x, y) = GetRecentlyTpPoint(tpX, tpY);
            Logger.LogInformation("({TpX},{TpY}) 最近的传送点位置 ({X},{Y})", $"{tpX:F1}", $"{tpY:F1}", $"{x:F1}", $"{y:F1}");
        }

        // M 打开地图识别当前位置，中心点为当前位置
        using var ra1 = CaptureToRectArea();
        if (!Bv.IsInBigMapUi(ra1))
        {
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_M);
            await Delay(1000, cts);
        }

        // 计算传送点位置离哪个地图切换后的中心点最近，切换到该地图
        await SwitchRecentlyCountryMap(x, y);

        // 计算坐标后点击
        var bigMapInAllMapRect = GetBigMapRect();
        while (!bigMapInAllMapRect.Shrink(115).Contains(x, y))
        {
            Debug.WriteLine($"({x},{y}) 不在 {bigMapInAllMapRect} 内，继续移动");
            Logger.LogInformation("传送点不在当前大地图范围内，继续移动");
            await MoveMapTo(x, y);
            bigMapInAllMapRect = GetBigMapRect();
        }

        // Debug.WriteLine($"({x},{y}) 在 {bigMapInAllMapRect} 内，计算它在窗体内的位置");
        // 注意这个坐标的原点是中心区域某个点，所以要转换一下点击坐标（点击坐标是左上角为原点的坐标系），不能只是缩放
        var (picX, picY) = MapCoordinate.GameToMain2048(x, y);
        var picRect = MapCoordinate.GameToMain2048(bigMapInAllMapRect);
        Debug.WriteLine($"({picX},{picY}) 在 {picRect} 内，计算它在窗体内的位置");
        var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var clickX = (int)((picX - picRect.X) / picRect.Width * captureRect.Width);
        var clickY = (int)((picY - picRect.Y) / picRect.Height * captureRect.Height);
        Logger.LogInformation("点击传送点：({X},{Y})", clickX, clickY);
        using var ra = CaptureToRectArea();
        ra.ClickTo(clickX, clickY);

        // 触发一次快速传送功能
        await Delay(500, cts);
        await ClickTpPoint(CaptureToRectArea());

        // 等待传送完成
        for (var i = 0; i < 20; i++)
        {
            await Delay(1200, cts);
            using var ra3 = CaptureToRectArea();
            if (Bv.IsInMainUi(ra3))
            {
                break;
            }
        }

        Logger.LogInformation("传送完成");
        return (x, y);
    }

    public async Task<(double, double)> Tp(double tpX, double tpY, bool force = false)
    {
        // 重试3次
        for (var i = 0; i < 3; i++)
        {
            try
            {
                return await TpOnce(tpX, tpY, force);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "传送失败，重试 {I} 次", i + 1);
            }
        }
        throw new InvalidOperationException("传送失败");
    }

    /// <summary>
    /// 移动地图到指定传送点位置
    /// 可能会移动不对，所以可以重试此方法
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    public async Task MoveMapTo(double x, double y)
    {
        var bigMapCenterPoint = GetPositionFromBigMap();
        // 移动部分内容测试移动偏移
        var (xOffset, yOffset) = (x - bigMapCenterPoint.X, y - bigMapCenterPoint.Y);

        var diffMouseX = 200; // 每次移动的距离
        if (xOffset < 0)
        {
            diffMouseX = -diffMouseX;
        }

        var diffMouseY = 200; // 每次移动的距离
        if (yOffset < 0)
        {
            diffMouseY = -diffMouseY;
        }

        // 先移动到屏幕中心附近随机点位置，避免地图移动无效
        await MouseMoveMapX(diffMouseX);
        await MouseMoveMapY(diffMouseY);
        var newBigMapCenterPoint = GetPositionFromBigMap();
        var diffMapX = Math.Abs(newBigMapCenterPoint.X - bigMapCenterPoint.X);
        var diffMapY = Math.Abs(newBigMapCenterPoint.Y - bigMapCenterPoint.Y);
        Debug.WriteLine($"每单位移动的地图距离：({diffMapX},{diffMapY})");

        // 快速移动到目标传送点所在的区域
        if (diffMapX > 10 && diffMapY > 10)
        {
            // // 计算需要移动的次数
            var moveCount = (int)Math.Abs(xOffset / diffMapX); // 向下取整 本来还要加1的，但是已经移动了一次了
            Debug.WriteLine("X需要移动的次数：" + moveCount);
            for (var i = 0; i < moveCount; i++)
            {
                await MouseMoveMapX(diffMouseX);
            }

            moveCount = (int)Math.Abs(yOffset / diffMapY); // 向下取整 本来还要加1的，但是已经移动了一次了
            Debug.WriteLine("Y需要移动的次数：" + moveCount);
            for (var i = 0; i < moveCount; i++)
            {
                await MouseMoveMapY(diffMouseY);
            }
        }
    }

    public async Task MouseMoveMapX(int dx)
    {
        var moveUnit = dx > 0 ? 20 : -20;
        GameCaptureRegion.GameRegionMove((rect, _) => (rect.Width / 2d + Random.Shared.Next(-rect.Width / 6, rect.Width / 6), rect.Height / 2d + Random.Shared.Next(-rect.Height / 6, rect.Height / 6)));
        Simulation.SendInput.Mouse.LeftButtonDown();
        await Delay(200, cts);
        for (var i = 0; i < dx / moveUnit; i++)
        {
            Simulation.SendInput.Mouse.MoveMouseBy(moveUnit, 0).Sleep(60); // 60 保证没有惯性
        }

        Simulation.SendInput.Mouse.LeftButtonUp();
        await Delay(200, cts);
    }

    public async Task MouseMoveMapY(int dy)
    {
        var moveUnit = dy > 0 ? 20 : -20;
        GameCaptureRegion.GameRegionMove((rect, _) => (rect.Width / 2d + Random.Shared.Next(-rect.Width / 6, rect.Width / 6), rect.Height / 2d + Random.Shared.Next(-rect.Height / 6, rect.Height / 6)));
        Simulation.SendInput.Mouse.LeftButtonDown();
        await Delay(200, cts);
        // 原神地图在小范围内移动是无效的，所以先随便移动一下，所以肯定少移动一次
        for (var i = 0; i < dy / moveUnit; i++)
        {
            Simulation.SendInput.Mouse.MoveMouseBy(0, moveUnit).Sleep(60);
        }

        Simulation.SendInput.Mouse.LeftButtonUp();
        await Delay(200, cts);
    }

    public Point2f GetPositionFromBigMap()
    {
        return GetBigMapCenterPoint();
    }

    public Point2f? GetPositionFromBigMapNullable()
    {
        try
        {
            return GetBigMapCenterPoint();
        }
        catch
        {
            return null;
        }
    }

    public Rect GetBigMapRect()
    {
        var rect = new Rect();
        NewRetry.Do(() =>
        {
            // 判断是否在地图界面
            using var ra = CaptureToRectArea();
            using var mapScaleButtonRa = ra.Find(QuickTeleportAssets.Instance.MapScaleButtonRo);
            if (mapScaleButtonRa.IsExist())
            {
                rect = BigMap.Instance.GetBigMapRectByFeatureMatch(ra.SrcGreyMat);
                if (rect == Rect.Empty)
                {
                    // 滚轮调整后再次识别
                    Simulation.SendInput.Mouse.VerticalScroll(2);
                    Sleep(500);
                    throw new RetryException("识别大地图位置失败");
                }
            }
            else
            {
                throw new RetryException("当前不在地图界面");
            }
        }, TimeSpan.FromMilliseconds(500), 5);

        if (rect == Rect.Empty)
        {
            throw new InvalidOperationException("多次重试后，识别大地图位置失败");
        }

        Debug.WriteLine("识别大地图在全地图位置矩形：" + rect);
        const int s = BigMap.ScaleTo2048; // 相对1024做4倍缩放
        return MapCoordinate.Main2048ToGame(new Rect(rect.X * s, rect.Y * s, rect.Width * s, rect.Height * s));
    }

    public Point2f GetBigMapCenterPoint()
    {
        // 判断是否在地图界面
        using var ra = CaptureToRectArea();
        using var mapScaleButtonRa = ra.Find(QuickTeleportAssets.Instance.MapScaleButtonRo);
        if (mapScaleButtonRa.IsExist())
        {
            var p = BigMap.Instance.GetBigMapPositionByFeatureMatch(ra.SrcGreyMat);
            if (p.IsEmpty())
            {
                throw new InvalidOperationException("识别大地图位置失败");
            }

            Debug.WriteLine("识别大地图在全地图位置：" + p);
            return MapCoordinate.Main2048ToGame(new Point2f(BigMap.ScaleTo2048 * p.X, BigMap.ScaleTo2048 * p.Y));
        }
        else
        {
            throw new InvalidOperationException("当前不在地图界面");
        }
    }

    /// <summary>
    /// 获取最近的传送点位置
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public (double x, double y) GetRecentlyTpPoint(double x, double y)
    {
        double recentX = 0;
        double recentY = 0;
        var minDistance = double.MaxValue;
        foreach (var tpPosition in MapAssets.Instance.TpPositions)
        {
            var distance = Math.Sqrt(Math.Pow(tpPosition.X - x, 2) + Math.Pow(tpPosition.Y - y, 2));
            if (distance < minDistance)
            {
                minDistance = distance;
                recentX = tpPosition.X;
                recentY = tpPosition.Y;
            }
        }

        return (recentX, recentY);
    }

    public async Task<bool> SwitchRecentlyCountryMap(double x, double y)
    {
        // 可能是地下地图，切换到地上地图
        using var ra2 = CaptureToRectArea();
        if (Bv.BigMapIsUnderground(ra2))
        {
            ra2.Find(_assets.MapUndergroundToGroundButtonRo).Click();
            await Delay(200, cts);
        }

        // 识别当前位置
        var minDistance = double.MaxValue;
        var bigMapCenterPointNullable = GetPositionFromBigMapNullable();

        if (bigMapCenterPointNullable != null)
        {
            var bigMapCenterPoint = bigMapCenterPointNullable.Value;
            Logger.LogInformation("识别当前大地图位置：{Pos}", bigMapCenterPoint);
            minDistance = Math.Sqrt(Math.Pow(bigMapCenterPoint.X - x, 2) + Math.Pow(bigMapCenterPoint.Y - y, 2));
        }

        var minCountry = "当前位置";
        foreach (var (country, position) in MapAssets.Instance.CountryPositions)
        {
            var distance = Math.Sqrt(Math.Pow(position[0] - x, 2) + Math.Pow(position[1] - y, 2));
            if (distance < minDistance)
            {
                minDistance = distance;
                minCountry = country;
            }
        }

        Logger.LogInformation("离目标传送点最近的区域是：{Country}", minCountry);
        if (minCountry != "当前位置")
        {
            GameCaptureRegion.GameRegionClick((rect, scale) => (rect.Width - 160 * scale, rect.Height - 60 * scale));
            await Delay(300, cts);
            var ra = CaptureToRectArea();
            var list = ra.FindMulti(new RecognitionObject
            {
                RecognitionType = RecognitionTypes.Ocr,
                RegionOfInterest = new Rect(ra.Width / 2, 0, ra.Width / 2, ra.Height)
            });
            list.FirstOrDefault(r => r.Text.Length == minCountry.Length && !r.Text.Contains("委托") && r.Text.Contains(minCountry))?.Click();
            Logger.LogInformation("切换到区域：{Country}", minCountry);
            await Delay(500, cts);
            return true;
        }

        return false;
    }

    public async Task Tp(string name)
    {
        // 通过大地图传送到指定传送点
    }

    public async Task TpByF1(string name)
    {
        // 传送到指定传送点
    }

    public async Task ClickTpPoint(ImageRegion imageRegion)
    {
        // 1.判断是否在地图界面
        if (Bv.IsInBigMapUi(imageRegion))
        {
            // 2. 判断是否有传送按钮
            var hasTeleportButton = CheckTeleportButton(imageRegion);

            if (!hasTeleportButton)
            {
                // 存在地图关闭按钮，说明未选中传送点，直接返回
                var mapCloseRa = imageRegion.Find(_assets.MapCloseButtonRo);
                if (!mapCloseRa.IsEmpty())
                {
                    return;
                }

                // 存在地图选择按钮，说明未选中传送点，直接返回
                var mapChooseRa = imageRegion.Find(_assets.MapChooseRo);
                if (!mapChooseRa.IsEmpty())
                {
                    return;
                }

                // 3. 循环判断选项列表是否有传送点
                var hasMapChooseIcon = CheckMapChooseIcon(imageRegion);
                if (hasMapChooseIcon)
                {
                    var time = TaskContext.Instance().Config.QuickTeleportConfig.WaitTeleportPanelDelay;
                    time = time < 100 ? 100 : time;
                    await Delay(time, cts);
                    CheckTeleportButton(CaptureToRectArea());
                }
            }
        }
    }

    private bool CheckTeleportButton(ImageRegion imageRegion)
    {
        var hasTeleportButton = false;
        imageRegion.Find(_assets.TeleportButtonRo, ra =>
        {
            ra.Click();
            hasTeleportButton = true;
        });
        return hasTeleportButton;
    }

    /// <summary>
    /// 全匹配一遍并进行文字识别
    /// 60ms ~200ms
    /// </summary>
    /// <param name="imageRegion"></param>
    /// <returns></returns>
    private bool CheckMapChooseIcon(ImageRegion imageRegion)
    {
        var hasMapChooseIcon = false;

        // 全匹配一遍
        var rResultList = MatchTemplateHelper.MatchMultiPicForOnePic(imageRegion.SrcGreyMat[_assets.MapChooseIconRoi], _assets.MapChooseIconGreyMatList);
        // 按高度排序
        if (rResultList.Count > 0)
        {
            rResultList = [.. rResultList.OrderBy(x => x.Y)];
            // 点击最高的
            foreach (var iconRect in rResultList)
            {
                // 200宽度的文字区域
                using var ra = imageRegion.DeriveCrop(_assets.MapChooseIconRoi.X + iconRect.X + iconRect.Width, _assets.MapChooseIconRoi.Y + iconRect.Y, 200, iconRect.Height);
                using var textRegion = ra.Find(new RecognitionObject
                {
                    RecognitionType = RecognitionTypes.ColorRangeAndOcr,
                    LowerColor = new Scalar(249, 249, 249), // 只取白色文字
                    UpperColor = new Scalar(255, 255, 255),
                });
                if (string.IsNullOrEmpty(textRegion.Text) || textRegion.Text.Length == 1)
                {
                    continue;
                }

                Logger.LogInformation("传送：点击 {Option}", textRegion.Text);
                var time = TaskContext.Instance().Config.QuickTeleportConfig.TeleportListClickDelay;
                time = time < 200 ? 200 : time;
                Thread.Sleep(time);
                ra.Click();
                hasMapChooseIcon = true;
                break;
            }
        }

        return hasMapChooseIcon;
    }
}
