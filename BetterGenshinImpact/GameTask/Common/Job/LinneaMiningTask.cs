using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.View.Drawable;
using Compunet.YoloSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 莉奈娅挖矿
/// </summary>
public class LinneaMiningTask
{
    #region 配置参数

    // 聚类距离阈值（基于宽度缩放）
    private const double BaseClusterDistance = 300;
    // 聚类面积基准值（1920宽度下的标准矿石面积）
    private const double BaseClusterArea = 1800;
    // 对准判定阈值（基于宽度缩放）
    private const double BaseArrivalThreshold = 50;
    // 屏幕边缘忽略区域宽度（基于宽度缩放）
    private const double BaseEdgeIgnore = 200;
    // 瞄准模式X轴灵敏度补偿系数
    private const double AimSensitivityFactorX = 0.45;
    // 瞄准模式Y轴灵敏度补偿系数
    private const double AimSensitivityFactorY = 0.80;
    // 检测置信度阈值
    private const float ConfidenceThreshold = 0.78f;
    // 聚类面积差异倍率
    private const double AreaRatioThreshold = 4;
    // 左转步长
    private const int LeftTurnStep = -250;
    // 内层最大检测次数
    private const int MaxInnerRetry = 7;
    // 默认大循环次数
    private const int DefaultScanRounds = 5;
    // 元素视野刷新间隔
    private const int ElementSightRefreshMs = 3000;

    #endregion

    private readonly BgiYoloPredictor _predictor;
    private readonly double _dpi = TaskContext.Instance().DpiScale;
    private readonly double _widthScale = TaskContext.Instance().SystemInfo.CaptureAreaRect.Width / 1920.0;
    private readonly double _heightScale = TaskContext.Instance().SystemInfo.CaptureAreaRect.Height / 1080.0;
    private readonly double ClusterDistanceThreshold;
    private readonly double ArrivalThreshold;
    private readonly double EdgeIgnore;

    private readonly int _scanRounds;
    private readonly int _mineCount;

    public LinneaMiningTask(int scanRounds = DefaultScanRounds, int mineCount = 1)
    {
        _scanRounds = scanRounds;
        _mineCount = mineCount;
        _predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>()
            .CreateYoloPredictor(BgiOnnxModel.BgiMine);
        ClusterDistanceThreshold = BaseClusterDistance * _widthScale;
        ArrivalThreshold = BaseArrivalThreshold * _widthScale;
        EdgeIgnore = BaseEdgeIgnore * _widthScale;
    }

    public async Task Start(CancellationToken ct)
    {
        var aimingModeEntered = false;
        try
        {
            Logger.LogInformation("开始寻矿");

            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_R);
            aimingModeEntered = true;
            await Delay(400, ct);

            var minedCount = 0;

            for (var round = 0; round < _scanRounds && !ct.IsCancellationRequested; round++)
            {
                Simulation.SendInput.Mouse.MiddleButtonDown();
                await Delay(1200, ct);
                _lastRefreshTime = Environment.TickCount64;

                var (cluster, centerX, centerY) = FindNearestMineralCluster();

                if (cluster != null)
                {
                    var (aligned, compensateDx, compensateDy) = await AlignAndMine(cluster, centerX, centerY, ct);
                    if (aligned)
                    {
                        minedCount++;
                        if (minedCount >= _mineCount) break;
                        continue;
                    }

                    Simulation.SendInput.Mouse.MiddleButtonUp();
                    await Delay(300, ct);

                    if (compensateDx != 0 || compensateDy != 0)
                    {
                        Simulation.SendInput.Mouse.MiddleButtonDown();
                        await Delay(1500, ct);
                        _lastRefreshTime = Environment.TickCount64;
                        Simulation.SendInput.Mouse.MoveMouseBy(-compensateDx, -compensateDy);
                        await Delay(800, ct);
                        Simulation.SendInput.Mouse.MiddleButtonUp();
                        await Delay(300, ct);
                    }

                    Simulation.SendInput.Mouse.MoveMouseBy((int)(LeftTurnStep * _dpi * _widthScale), 0);
                    await Delay(800, ct);
                    continue;
                }

                Simulation.SendInput.Mouse.MiddleButtonUp();
                await Delay(300, ct);

                Simulation.SendInput.Mouse.MoveMouseBy((int)(LeftTurnStep * _dpi * _widthScale), 0);
                await Delay(800, ct);
            }

            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_R);
            aimingModeEntered = false;
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("取消挖矿");
        }
        catch (Exception e)
        {
            Logger.LogError("挖矿异常: {Msg}", e.Message);
        }
        finally
        {
            if (aimingModeEntered)
            {
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_R);
            }

            Simulation.SendInput.Mouse.MiddleButtonUp();
            VisionContext.Instance().DrawContent.ClearAll();
        }
    }

    private long _lastRefreshTime;

    private async Task<(bool aligned, int compensateDx, int compensateDy)> AlignAndMine(
        MineralCluster cluster, double centerX, double centerY, CancellationToken ct)
    {
        var totalDx = 0;
        var totalDy = 0;

        for (var retry = 0; retry < MaxInnerRetry && !ct.IsCancellationRequested; retry++)
        {
            if (Environment.TickCount64 - _lastRefreshTime >= ElementSightRefreshMs)
            {
                Simulation.SendInput.Mouse.MiddleButtonUp();
                await Delay(100, ct);
                Simulation.SendInput.Mouse.MiddleButtonDown();
                await Delay(1500, ct);
                _lastRefreshTime = Environment.TickCount64;
            }

            var offsetX = cluster.TargetX - centerX;
            var offsetY = cluster.TargetY - centerY;

            if (Math.Abs(offsetX) <= ArrivalThreshold / 2 && Math.Abs(offsetY) <= ArrivalThreshold / 2)
            {
                Simulation.SendInput.Mouse.MiddleButtonUp();
                await Delay(300, ct);
                Logger.LogInformation("开始挖矿");
                await Mine(ct);
                return (true, 0, 0);
            }

            var mouseDx = (int)(offsetX * _dpi * AimSensitivityFactorX / _widthScale);
            var mouseDy = (int)(offsetY * _dpi * AimSensitivityFactorY / _heightScale);
            Simulation.SendInput.Mouse.MoveMouseBy(mouseDx, mouseDy);
            totalDx += mouseDx;
            totalDy += mouseDy;
            await Delay(100, ct);

            (cluster, centerX, centerY) = FindNearestMineralCluster();
            if (cluster == null)
            {
                return (false, totalDx, totalDy);
            }
        }

        return (false, totalDx, totalDy);
    }

    /// <summary>
    /// 执行挖矿操作
    /// </summary>
    private static async Task Mine(CancellationToken ct)
    {
        Simulation.SendInput.Mouse.MoveMouseBy(0, -20);
        await Delay(10, ct);
        Simulation.SendInput.Mouse.LeftButtonClick();
        await Delay(2000, ct);
    }

    /// <summary>
    /// 检测矿物，返回距屏幕中心最近的聚堆
    /// </summary>
    private (MineralCluster? cluster, double centerX, double centerY) FindNearestMineralCluster()
    {
        var systemInfo = TaskContext.Instance().SystemInfo;
        var image = CaptureGameImage(TaskTriggerDispatcher.GlobalGameCapture);
        var ra = systemInfo.DesktopRectArea.Derive(image, systemInfo.CaptureAreaRect.X, systemInfo.CaptureAreaRect.Y);

        // Letterbox预处理
        const int targetSize = 640;
        var srcW = ra.SrcMat.Width;
        var srcH = ra.SrcMat.Height;
        var scale = Math.Max((double)srcW, srcH) / targetSize;
        var newW = (int)(srcW / scale);
        var newH = (int)(srcH / scale);
        var padX = (targetSize - newW) / 2;
        var padY = (targetSize - newH) / 2;

        using var resizedMat = new Mat();
        Cv2.Resize(ra.SrcMat, resizedMat, new OpenCvSharp.Size(newW, newH));
        using var letterboxMat = new Mat();
        Cv2.CopyMakeBorder(resizedMat, letterboxMat, padY, targetSize - newH - padY, padX, targetSize - newW - padX,
            BorderTypes.Constant, new Scalar(114, 114, 114));

        var inputRa = new ImageRegion(letterboxMat, 0, 0);
        var rawResult = _predictor.Predictor.Detect(inputRa.CacheImage);

        var centerX = ra.CacheImage.Width / 2.0;
        var centerY = ra.CacheImage.Height / 2.0;

        // 检测框坐标在640空间，扣除pad后映射回原图
        var oreBoxes = rawResult
            .Where(box => box.Name.Name is "ore" && box.Confidence >= ConfidenceThreshold)
            .Select(box => new Rect(
                (int)((box.Bounds.X - padX) * scale),
                (int)((box.Bounds.Y - padY) * scale),
                (int)(box.Bounds.Width * scale),
                (int)(box.Bounds.Height * scale)))
            .ToList();

        // 画框
        var drawList = oreBoxes.Select(r => ra.ToRectDrawable(r, "ore")).ToList();
        VisionContext.Instance().DrawContent.PutOrRemoveRectList("BgiMine", drawList);

        if (oreBoxes.Count == 0)
        {
            VisionContext.Instance().DrawContent.PutOrRemoveRectList("MiningCluster", null);
            return (null, centerX, centerY);
        }

        var clusters = ClusterMinerals(oreBoxes);

        // 画聚类中心标记框
        var markerSize = ArrivalThreshold;
        var clusterDrawList = new List<RectDrawable>();
        foreach (var cluster in clusters)
        {
            var half = (int)markerSize / 2;
            var mark = new Rect((int)cluster.TargetX - half, (int)cluster.TargetY - half, (int)markerSize, (int)markerSize);
            clusterDrawList.Add(ra.ToRectDrawable(mark,
                $"({(int)cluster.TargetX},{(int)cluster.TargetY})",
                new Pen(Color.DodgerBlue, 2)
            ));
        }
        VisionContext.Instance().DrawContent.PutOrRemoveRectList("MiningCluster", clusterDrawList);

        // 忽略屏幕边缘聚类，仅当中间区域存在聚类时生效
        var imgW = ra.CacheImage.Width;
        var imgH = ra.CacheImage.Height;
        var centerClusters = clusters
            .Where(c => c.CenterX >= EdgeIgnore && c.CenterX <= imgW - EdgeIgnore
                        && c.CenterY >= EdgeIgnore && c.CenterY <= imgH - EdgeIgnore)
            .ToList();
        var candidates = centerClusters.Count > 0 ? centerClusters : clusters;

        var nearest = candidates
            .OrderBy(c => Math.Pow(c.CenterX - centerX, 2) + Math.Pow(c.CenterY - centerY, 2))
            .First();
        return (nearest, centerX, centerY);
    }

    /// <summary>
    /// 贪心聚类：距离小于阈值的检测框归入同一簇，阈值根据元素面积动态缩放
    /// </summary>
    private List<MineralCluster> ClusterMinerals(List<Rect> rects)
    {
        if (rects.Count == 0) return [];

        var refArea = BaseClusterArea * _widthScale * _widthScale;
        var clusters = new List<MineralCluster>();

        foreach (var rect in rects)
        {
            var cx = rect.X + rect.Width / 2.0;
            var cy = rect.Y + rect.Height / 2.0;

            MineralCluster? nearest = null;
            double nearestDist = double.MaxValue;

            foreach (var cluster in clusters)
            {
                var dist = Math.Sqrt(Math.Pow(cx - cluster.CenterX, 2) + Math.Pow(cy - cluster.CenterY, 2));
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = cluster;
                }
            }

            if (nearest != null)
            {
                var clusterAvgArea = nearest.Rects.Average(r => (double)r.Width * r.Height);
                var rectArea = (double)rect.Width * rect.Height;
                var combinedAvg = (clusterAvgArea * nearest.Rects.Count + rectArea) / (nearest.Rects.Count + 1);
                var effectiveThreshold = ClusterDistanceThreshold * Math.Sqrt(combinedAvg / Math.Max(1, refArea));

                if (nearestDist < effectiveThreshold && nearest.TryAddRect(rect))
                {
                    continue;
                }
            }

            clusters.Add(new MineralCluster(rect, AreaRatioThreshold));
        }

        return clusters;
    }
}

/// <summary>
/// 矿物聚堆，维护一组相近矿物的检测框及质心
/// </summary>
public class MineralCluster
{
    public List<Rect> Rects { get; } = new();
    public double CenterX { get; private set; }
    public double CenterY { get; private set; }
    public double TargetX { get; private set; }
    public double TargetY { get; private set; }

    public MineralCluster(Rect firstRect, double areaRatioThreshold = 5)
    {
        AreaRatioThreshold = areaRatioThreshold;
        Rects.Add(firstRect);
        RecalculateCenter();
    }

    private readonly double AreaRatioThreshold;

    public bool TryAddRect(Rect rect)
    {
        var avgArea = Rects.Average(r => (double)r.Width * r.Height);
        var newArea = (double)rect.Width * rect.Height;
        if (newArea > avgArea * AreaRatioThreshold || newArea < avgArea / AreaRatioThreshold) return false;
        Rects.Add(rect);
        RecalculateCenter();
        return true;
    }

    private void RecalculateCenter()
    {
        CenterX = Rects.Average(r => r.X + r.Width / 2.0);
        CenterY = Rects.Average(r => r.Y + r.Height / 2.0);

        Rect? nearestRect = null;
        var minDist = double.MaxValue;
        foreach (var r in Rects)
        {
            var cx = r.X + r.Width / 2.0;
            var cy = r.Y + r.Height / 2.0;
            var d = Math.Pow(cx - CenterX, 2) + Math.Pow(cy - CenterY, 2);
            if (d < minDist)
            {
                minDist = d;
                nearestRect = r;
            }
        }

        TargetX = nearestRect!.Value.X + nearestRect!.Value.Width / 2.0;
        TargetY = nearestRect!.Value.Y + nearestRect!.Value.Height / 2.0;
    }
}
