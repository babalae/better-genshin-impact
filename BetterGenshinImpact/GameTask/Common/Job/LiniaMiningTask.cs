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
using OpenCvSharp;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 莉奈娅挖矿
/// </summary>
public class LiniaMiningTask
{
    #region 配置参数

    // 聚类距离阈值（基于宽度缩放）
    private const double BaseClusterDistance = 600;
    // 对准判定阈值（基于宽度缩放）
    private const double BaseArrivalThreshold = 45;
    // 屏幕边缘忽略区域宽度（基于宽度缩放）
    private const double BaseEdgeIgnore = 200;
    // 瞄准模式灵敏度补偿系数
    private const double AimSensitivityFactor = 0.82;
    // 检测置信度阈值
    private const float ConfidenceThreshold = 0.8f;
    // 聚类面积差异倍率
    private const double AreaRatioThreshold = 4;
    // 目标X偏移
    private const int TargetXOffset = -10;
    // 左转步长
    private const int LeftTurnStep = -30;
    // 外层最大重试次数
    private const int MaxOuterRetry = 5;
    // 内层最大检测次数
    private const int MaxInnerRetry = 5;
    // 开始左转的外层重试次数
    private const int ScanFromRetry = 3;

    #endregion

    private readonly BgiYoloPredictor _predictor;
    private readonly double _dpi = TaskContext.Instance().DpiScale;
    private readonly double _widthScale = TaskContext.Instance().SystemInfo.CaptureAreaRect.Width / 1920.0;
    private readonly double ClusterDistanceThreshold;
    private readonly double ArrivalThreshold;
    private readonly double EdgeIgnore;

    public LiniaMiningTask()
    {
        _predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>()
            .CreateYoloPredictor(BgiOnnxModel.BgiOre);
        ClusterDistanceThreshold = BaseClusterDistance * _widthScale;
        ArrivalThreshold = BaseArrivalThreshold * _widthScale;
        EdgeIgnore = BaseEdgeIgnore * _widthScale;
    }

    public async Task Start(CancellationToken ct)
    {
        try
        {
            Logger.LogInformation("开始寻矿");

            // R进入瞄准状态
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_R);
            await Delay(400, ct);

            var outerRetry = 0;

            while (!ct.IsCancellationRequested && outerRetry < MaxOuterRetry)
            {
                outerRetry++;

                // 中键进入元素视野
                Simulation.SendInput.Mouse.MiddleButtonDown();
                await Delay(1500, ct);

                var aligned = false;

                // 检测+移动
                for (var retry = 0; retry < MaxInnerRetry; retry++)
                {
                    // 元素视野刷新
                    if (retry > 0 && retry % 2 == 0)
                    {
                        Simulation.SendInput.Mouse.MiddleButtonUp();
                        await Delay(300, ct);
                        Simulation.SendInput.Mouse.MiddleButtonDown();
                        await Delay(1500, ct);
                    }

                    // 第ScanFromRetry次外层重试起，每次检测前左转寻矿
                    if (outerRetry >= ScanFromRetry)
                    {
                        Simulation.SendInput.Mouse.MoveMouseBy((int)(LeftTurnStep * _dpi), 0);
                        await Delay(800, ct);
                    }

                    var (cluster, centerX, centerY) = FindNearestMineralCluster();

                    if (cluster == null)
                    {
                        Logger.LogInformation("[{Retry}] 未检测到矿物", retry);
                        continue;
                    }

                    var offsetX = cluster.CenterX - centerX + TargetXOffset;
                    var offsetY = cluster.CenterY - centerY;

                    if (Math.Abs(offsetX) <= ArrivalThreshold && Math.Abs(offsetY) <= ArrivalThreshold)
                    {
                        Logger.LogInformation("已对准矿物");
                        aligned = true;
                        break;
                    }

                    var dist = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                    if (dist < 1) dist = 1;

                    var mouseDx = (int)(offsetX * _dpi * AimSensitivityFactor);
                    var mouseDy = (int)(offsetY * _dpi * AimSensitivityFactor);
                    Simulation.SendInput.Mouse.MoveMouseBy(mouseDx, mouseDy);
                    await Delay(200, ct);

                    // 后验对准
                    var (postCluster, postCenterX, postCenterY) = FindNearestMineralCluster();
                    if (postCluster != null)
                    {
                        var postOffsetX = postCluster.CenterX - postCenterX + TargetXOffset;
                        var postOffsetY = postCluster.CenterY - postCenterY;
                        if (Math.Abs(postOffsetX) <= ArrivalThreshold && Math.Abs(postOffsetY) <= ArrivalThreshold)
                        {
                            aligned = true;
                            break;
                        }
                    }
                }

                // 松中键退出元素视野
                Simulation.SendInput.Mouse.MiddleButtonUp();
                await Delay(300, ct);

                if (aligned)
                {
                    await Mine(ct);
                    break;
                }
            }

            // R退出瞄准状态
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_R);
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("挖矿取消");
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "挖矿异常");
            Logger.LogError("挖矿异常: {Msg}", e.Message);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            Simulation.ReleaseAllKey();
        }
    }

    /// <summary>
    /// 执行挖矿操作
    /// </summary>
    private static async Task Mine(CancellationToken ct)
    {
        // TODO: 挖矿逻辑
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
        var rawResult = _predictor.Predictor.Detect(ra.CacheImage);

        var centerX = ra.CacheImage.Width / 2.0;
        var centerY = ra.CacheImage.Height / 2.0;

        // 只保留置信度达标的 ore 检测框
        var oreBoxes = rawResult
            .Where(box => box.Name.Name is "ore" && box.Confidence >= ConfidenceThreshold)
            .Select(box => new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height))
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
        var markerSize = 50 * _widthScale;
        var clusterDrawList = new List<RectDrawable>();
        foreach (var cluster in clusters)
        {
            var half = (int)markerSize / 2;
            var mark = new Rect((int)cluster.CenterX - half, (int)cluster.CenterY - half, (int)markerSize, (int)markerSize);
            clusterDrawList.Add(ra.ToRectDrawable(mark,
                $"({(int)cluster.CenterX},{(int)cluster.CenterY})",
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
    /// 贪心聚类：距离小于阈值的检测框归入同一簇
    /// </summary>
    private List<MineralCluster> ClusterMinerals(List<Rect> rects)
    {
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

            if (nearest != null && nearestDist < ClusterDistanceThreshold)
            {
                nearest.TryAddRect(rect);
            }
            else
            {
                clusters.Add(new MineralCluster(rect, AreaRatioThreshold));
            }
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
    }
}
