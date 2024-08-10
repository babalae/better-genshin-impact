using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
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
    private static readonly Random _rd = new Random();

    private readonly QuickTeleportAssets _assets = QuickTeleportAssets.Instance;

    /// <summary>
    /// 通过大地图传送到指定坐标最近的传送点，然后移动到指定坐标
    /// </summary>
    /// <param name="tpX"></param>
    /// <param name="tpY"></param>
    public async Task Tp(double tpX, double tpY)
    {
        // 获取最近的传送点位置
        var (x, y) = GetRecentlyTpPoint(tpX, tpY);
        Logger.LogInformation("({TpX},{TpY}) 最近的传送点位置 ({X},{Y})", tpX, tpY, x, y);

        // M 打开地图识别当前位置，中心点为当前位置
        using var ra1 = CaptureToRectArea();
        if (!Bv.IsInBigMapUi(ra1))
        {
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_M);
            await Delay(1000, cts);
        }

        // 计算传送点位置离哪个地图切换后的中心点最近，切换到该地图
        var switched = await SwitchRecentlyCountryMap(x, y);
        if (!switched)
        {
            // 可能是地下地图，切换到地上地图
            using var ra2 = CaptureToRectArea();
            if (Bv.BigMapIsUnderground(ra2))
            {
                ra2.Find(_assets.MapUndergroundToGroundButtonRo).Click();
                await Delay(200, cts);
            }
        }

        // 计算坐标后点击
        var bigMapInAllMapRect = GetBigMapRect();
        while (!bigMapInAllMapRect.Contains(x, y))
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

        var diffMouseX = 100; // 每次移动的距离
        if (xOffset < 0)
        {
            diffMouseX = -diffMouseX;
        }

        var diffMouseY = 100; // 每次移动的距离
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
        Debug.WriteLine($"每100移动的地图距离：({diffMapX},{diffMapY})");

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
        GameCaptureRegion.GameRegionMove((rect, _) => (rect.Width / 2d + _rd.Next(-rect.Width / 6, rect.Width / 6), rect.Height / 2d + _rd.Next(-rect.Height / 6, rect.Height / 6)));
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
        GameCaptureRegion.GameRegionMove((rect, _) => (rect.Width / 2d + _rd.Next(-rect.Width / 6, rect.Width / 6), rect.Height / 2d + _rd.Next(-rect.Height / 6, rect.Height / 6)));
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

    public Point GetPositionFromBigMap()
    {
        var bigMapRect = GetBigMapRect();
        Debug.WriteLine("地图位置转换到游戏坐标：" + bigMapRect);
        var bigMapCenterPoint = bigMapRect.GetCenterPoint();
        Debug.WriteLine("地图中心坐标：" + bigMapCenterPoint);
        return bigMapCenterPoint;
    }

    public Rect GetBigMapRect()
    {
        // 判断是否在地图界面
        using var ra = CaptureToRectArea();
        using var mapScaleButtonRa = ra.Find(QuickTeleportAssets.Instance.MapScaleButtonRo);
        if (mapScaleButtonRa.IsExist())
        {
            var rect = BigMap.Instance.GetBigMapPositionByFeatureMatch(ra.SrcGreyMat);
            if (rect == Rect.Empty)
            {
                throw new InvalidOperationException("识别大地图位置失败");
            }

            Debug.WriteLine("识别大地图在全地图位置矩形：" + rect);
            const int s = 4 * 2; // 相对1024做4倍缩放
            return MapCoordinate.Main2048ToGame(new Rect(rect.X * s, rect.Y * s, rect.Width * s, rect.Height * s));
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
    public (int x, int y) GetRecentlyTpPoint(double x, double y)
    {
        var recentX = 0;
        var recentY = 0;
        var minDistance = double.MaxValue;
        foreach (var tpPosition in MapAssets.Instance.TpPositions)
        {
            var distance = Math.Sqrt(Math.Pow(tpPosition.X - x, 2) + Math.Pow(tpPosition.Y - y, 2));
            if (distance < minDistance)
            {
                minDistance = distance;
                recentX = (int)Math.Round(tpPosition.X);
                recentY = (int)Math.Round(tpPosition.Y);
            }
        }

        return (recentX, recentY);
    }

    public async Task<bool> SwitchRecentlyCountryMap(double x, double y)
    {
        var bigMapCenterPoint = GetPositionFromBigMap();
        Logger.LogInformation("识别当前位置：{Pos}", bigMapCenterPoint);

        var minDistance = Math.Sqrt(Math.Pow(bigMapCenterPoint.X - x, 2) + Math.Pow(bigMapCenterPoint.Y - y, 2));
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
                    await Delay(TaskContext.Instance().Config.QuickTeleportConfig.WaitTeleportPanelDelay, cts);
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
            rResultList = rResultList.OrderBy(x => x.Y).ToList();
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
                Thread.Sleep(TaskContext.Instance().Config.QuickTeleportConfig.TeleportListClickDelay);
                ra.Click();
                hasMapChooseIcon = true;
                break;
            }
        }

        return hasMapChooseIcon;
    }
}
