using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.View.Drawable;
using Compunet.YoloSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using Vanara.PInvoke;
using static BetterGenshinImpact.Core.Simulator.Extensions.SimulateKeyHelper;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 莉奈娅挖矿
/// </summary>
public class LinneaMiningTask
{
    #region 配置参数

    // 聚类距离阈值（基于宽度缩放）
    private const double BaseClusterDistance = 400;
    // 聚类面积基准值（1920宽度下的标准矿石面积）
    private const double BaseClusterArea = 1800;
    // 对准判定：使用目标矿物框四周扩张像素（基于宽度缩放）
    private const double BaseAlignmentExpansion = 3;
    // 屏幕边缘忽略区域宽度（基于宽度缩放）
    private const double BaseEdgeIgnore = 200;
    // 瞄准模式X轴灵敏度补偿系数
    private const double AimSensitivityFactorX = 0.45;
    // 瞄准模式Y轴灵敏度补偿系数
    private const double AimSensitivityFactorY = 0.80;
    // 检测置信度阈值
    private const float ConfidenceThreshold = 0.70f;
    // 聚类面积差异倍率
    private const double AreaRatioThreshold = 4;
    // 左转步长
    private const int LeftTurnStep = -250;
    // 内层最大检测次数
    private const int MaxInnerRetry = 7;
    // 默认射箭次数
    public const int DefaultMineCount = 1;
    // 默认大循环次数
    public const int DefaultScanRounds = 1;
    // 元素视野刷新间隔
    private const int ElementSightRefreshMs = 3000;

    #endregion

    private readonly BgiYoloPredictor _predictor;
    private readonly double _dpi = TaskContext.Instance().DpiScale;
    private readonly double _widthScale = TaskContext.Instance().SystemInfo.CaptureAreaRect.Width / 1920.0;
    private readonly double _heightScale = TaskContext.Instance().SystemInfo.CaptureAreaRect.Height / 1080.0;
    private readonly double ClusterDistanceThreshold;
    private readonly double EdgeIgnore;
    private readonly double AlignmentExpansion;

    private readonly int _scanRounds;
    private readonly int _mineCount;
    private int _debugIndex;

    public LinneaMiningTask(int scanRounds = DefaultScanRounds, int mineCount = DefaultMineCount)
    {
        _scanRounds = scanRounds;
        _mineCount = mineCount;
        _predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>()
            .CreateYoloPredictor(BgiOnnxModel.BgiMine);
        ClusterDistanceThreshold = BaseClusterDistance * _widthScale;
        EdgeIgnore = BaseEdgeIgnore * _widthScale;
        AlignmentExpansion = BaseAlignmentExpansion * _widthScale;
    }

    public async Task Start(CancellationToken ct)
    {
        var aimingModeEntered = false;
        try
        {
            // Logger.LogInformation("开始寻矿");

            Simulation.SendInput.Keyboard.KeyPress(GIActions.SwitchAimingMode.ToActionKey().ToVK());
            aimingModeEntered = true;
            await Delay(400, ct);

            var minedCount = 0;

            for (var round = 0; round < _scanRounds && !ct.IsCancellationRequested; round++)
            {
                Simulation.SendInput.Mouse.MiddleButtonDown();
                await Delay(1500, ct);
                _lastRefreshTime = Environment.TickCount64;

                var (cluster, centerX, centerY) = FindNearestMineralCluster();

                if (cluster != null)
                {
                    var (aligned, counted, compensateDx, compensateDy) = await AlignAndMine(cluster, centerX, centerY, ct);
                    if (aligned)
                    {
                        if (counted) minedCount++;
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

                    if (round < _scanRounds - 1)
                    {
                        Simulation.SendInput.Mouse.MoveMouseBy((int)(LeftTurnStep * _dpi * _widthScale), 0);
                        await Delay(800, ct);
                    }
                    continue;
                }

                Simulation.SendInput.Mouse.MiddleButtonUp();
                await Delay(300, ct);

                if (round < _scanRounds - 1)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy((int)(LeftTurnStep * _dpi * _widthScale), 0);
                }
                await Delay(800, ct);
            }

            Simulation.SendInput.Keyboard.KeyPress(GIActions.SwitchAimingMode.ToActionKey().ToVK());
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
                Simulation.SendInput.Keyboard.KeyPress(GIActions.SwitchAimingMode.ToActionKey().ToVK());
            }

            Simulation.SendInput.Mouse.MiddleButtonUp();
            VisionContext.Instance().DrawContent.ClearAll();
        }
    }

    private long _lastRefreshTime;

    private async Task<(bool aligned, bool counted, int compensateDx, int compensateDy)> AlignAndMine(
        MineralCluster cluster, double centerX, double centerY, CancellationToken ct)
    {
        var totalDx = 0;
        var totalDy = 0;
        var hadResult = true;

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

            var isLast = retry == MaxInnerRetry - 1;
            var isAligned = Math.Abs(offsetX) <= (cluster.TargetWidth + AlignmentExpansion * 2) / 2
                         && Math.Abs(offsetY) <= (cluster.TargetHeight + AlignmentExpansion * 2) / 2;
            // 前面所有循环都检测成功时，以不计入总次数的方式兜底射击一次
            if (isAligned || (isLast && hadResult))
            {
                Simulation.SendInput.Mouse.MiddleButtonUp();
                await Delay(300, ct);
                Logger.LogInformation("开始挖矿");
                await Mine(ct, totalDy < 0);
                return (true, isAligned, 0, 0);
            }

            var mouseDx = (int)(offsetX * _dpi * AimSensitivityFactorX / _widthScale);
            var mouseDy = (int)(offsetY * _dpi * AimSensitivityFactorY / _heightScale);
            Simulation.SendInput.Mouse.MoveMouseBy(mouseDx, mouseDy);
            totalDx += mouseDx;
            totalDy += mouseDy;
            await Delay(150, ct);

            (cluster, centerX, centerY) = FindNearestMineralCluster();
            if (cluster == null)
            {
                hadResult = false;
                return (false, false, totalDx, totalDy);
            }
        }

        return (false, false, totalDx, totalDy);
    }

    /// <summary>
    /// 执行挖矿操作
    /// </summary>
    private static async Task Mine(CancellationToken ct, bool compensateUp)
    {
        if (compensateUp)
        {
            Simulation.SendInput.Mouse.MoveMouseBy(0, -25);
            await Delay(10, ct);
        }
        Simulation.SendInput.Mouse.LeftButtonClick();
        await Delay(2000, ct);
    }

    private void SaveDebugImage(Mat mat)
    {
        var debugDir = Path.Combine(Global.Absolute("log"), "DebugMine");
        if (!Directory.Exists(debugDir))
        {
            Directory.CreateDirectory(debugDir);
        }
        else
        {
            _debugIndex = Directory.GetFiles(debugDir, "detect_*.png")
                .Select(f => Path.GetFileNameWithoutExtension(f).Replace("detect_", ""))
                .Where(n => int.TryParse(n, out _))
                .Select(int.Parse)
                .DefaultIfEmpty(-1)
                .Max() + 1;
        }
        Cv2.ImWrite(Path.Combine(debugDir, $"detect_{_debugIndex++:D3}.png"), mat);
    }

    /// <summary>
    /// 检测矿物，返回距屏幕中心最近的聚堆
    /// </summary>
    private (MineralCluster? cluster, double centerX, double centerY) FindNearestMineralCluster()
    {
        var systemInfo = TaskContext.Instance().SystemInfo;
        var image = CaptureGameImage(TaskTriggerDispatcher.GlobalGameCapture);
        var ra = systemInfo.DesktopRectArea.Derive(image, systemInfo.CaptureAreaRect.X, systemInfo.CaptureAreaRect.Y);

        // SaveDebugImage(ra.SrcMat);

        var rawResult = _predictor.Predictor.Detect(ra.CacheImage);

        var centerX = ra.CacheImage.Width / 2.0;
        var centerY = ra.CacheImage.Height / 2.0;

        var oreBoxes = rawResult
            .Where(box => box.Name.Name is "ore" && box.Confidence >= ConfidenceThreshold)
            .Select(box => new Rect(
                (int)box.Bounds.X,
                (int)box.Bounds.Y,
                (int)box.Bounds.Width,
                (int)box.Bounds.Height))
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

        // 画聚类目标矿物框
        var expansion = (int)AlignmentExpansion;
        var clusterDrawList = clusters.Select(c =>
        {
            var mark = new Rect((int)(c.TargetX - c.TargetWidth / 2) - expansion, (int)(c.TargetY - c.TargetHeight / 2) - expansion,
                (int)c.TargetWidth + expansion * 2, (int)c.TargetHeight + expansion * 2);
            return ra.ToRectDrawable(mark,
                $"({(int)c.TargetX},{(int)c.TargetY})",
                new Pen(Color.DodgerBlue, 2)
            );
        }).ToList();
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
    public double TargetWidth { get; private set; }
    public double TargetHeight { get; private set; }

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

        // 按距离质心排序，取最近2个中靠右的
        var candidates = Rects
            .Select(r => (cx: r.X + r.Width / 2.0, cy: r.Y + r.Height / 2.0,
                dist: Math.Pow(r.X + r.Width / 2.0 - CenterX, 2) + Math.Pow(r.Y + r.Height / 2.0 - CenterY, 2),
                w: (double)r.Width, h: (double)r.Height))
            .OrderBy(t => t.dist)
            .Take(2)
            .OrderByDescending(t => t.cx)
            .First();

        TargetX = candidates.cx;
        TargetY = candidates.cy;
        TargetWidth = candidates.w;
        TargetHeight = candidates.h;
    }
}
