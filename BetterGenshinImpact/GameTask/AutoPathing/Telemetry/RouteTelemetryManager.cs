using BetterGenshinImpact.GameTask.AutoPathing.Model;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Maps;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

/// <summary>
/// 记录和持久化玩家在提瓦特世界的成功寻路轨迹，
/// 通过收集大量玩家跑图数据，最终构建出真正的“提瓦特主干道路网”拓扑。
///
/// 解决方案处理了Z轴缺失的问题：
/// 同一XY平面可能会有不同高度的分层（例如山崖上下或地下洞穴）。
/// 本收集器通过起终点坐标、前置相连路段信息、跑图移动模式（飞行/游泳等）
/// 以及切入该路段的角度（InboundAngle）作为补充特征，来进行路段合并与去重。
/// </summary>
public class RouteTelemetryManager
{
    private static readonly string SaveDir = Global.Absolute(Path.Combine("User", "AutoPathing", "Routes"));
    private readonly ConcurrentBag<RouteTelemetryRecord> _records = new();
    private bool _isSaving = false;

    // 当前正在执行的路径片段对应的锚点（起点），常常是最近使用过的传送点
    public WaypointForTrack? CurrentAnchorContext { get; set; }

    // 记录最近拾取的物品以及拾取时间，用于和轨迹终点关联
    private static readonly ConcurrentQueue<(DateTime Time, string ItemName)> _recentPicks = new();

    static RouteTelemetryManager()
    {
        // 订阅自动拾取的全局事件，记录最近拾取的时间日志
        BetterGenshinImpact.GameTask.AutoPick.AutoPickTrigger.OnItemPicked += (item) =>
        {
            _recentPicks.Enqueue((DateTime.UtcNow, item));
            
            // 清理超过30秒的拾取日志
            while (_recentPicks.TryPeek(out var prev) && (DateTime.UtcNow - prev.Time).TotalSeconds > 30)
            {
                _recentPicks.TryDequeue(out _);
            }
        };
    }

    public RouteTelemetryManager()
    {
        if (!Directory.Exists(SaveDir))
        {
            Directory.CreateDirectory(SaveDir);
        }
    }

    /// <summary>
    /// 当一条寻路指令成功结束时，记录其真实轨迹并异步落盘
    /// </summary>
    public void RecordSuccessfulRoute(WaypointForTrack? previous, WaypointForTrack target, List<Point2f> actualTrajectory)
    {
        if (!TaskContext.Instance().Config.PathingConditionConfig.EnableRouteTelemetry) return;
        if (actualTrajectory == null || actualTrajectory.Count < 2) return;

        var startPoint = actualTrajectory.First();
        var endPoint = actualTrajectory.Last();

        // 计算切入角度 inbound angle (起终点的方向，用于辅助区分同一XY但由于高度不同可能存在的反重力层)
        var dx = endPoint.X - startPoint.X;
        var dy = endPoint.Y - startPoint.Y;
        var inboundAngle = Math.Atan2(dy, dx) * 180 / Math.PI;

        // 获取该路段末尾（15秒内）拾取的所有物品
        var recentPicked = _recentPicks
            .Where(p => (DateTime.UtcNow - p.Time).TotalSeconds <= 15)
            .Select(p => p.ItemName)
            .Distinct()
            .ToList();

        // 获取并构建起点的传送门锚点标识
        string anchorId = "Unknown";
        if (CurrentAnchorContext != null)
        {
            var cx = CurrentAnchorContext.X;
            var cy = CurrentAnchorContext.Y;
            var mapName = target.MapName ?? "Teyvat";

            // 执行坐标转换：将 UI 图像坐标转换为游戏内实际 1024 缩放比例的真坐标，对齐 tp.json
            var mapProvider = MapManager.GetMap(mapName, null);
            if (mapProvider != null)
            {
                var gamePoint = mapProvider.ConvertImageCoordinatesToGenshinMapCoordinates(new Point2f((float)cx, (float)cy));
                if (gamePoint.HasValue)
                {
                    cx = gamePoint.Value.X;
                    cy = gamePoint.Value.Y;
                }
            }

            if (CurrentAnchorContext.Type == Model.Enum.WaypointType.Teleport.Code)
            {
                if (MapLazyAssets.Instance.ScenesDic.TryGetValue(mapName, out var scene) && scene.Points.Count > 0)
                {
                    // 由于 tp.json 已经包含了所有的传送点，这里不再做距离容差限制，直接无条件采用最近传送点的绝对真实坐标
                    var nearest = scene.Points.MinBy(tp => Math.Pow(tp.X - cx, 2) + Math.Pow(tp.Y - cy, 2))!;
                    anchorId = $"TP_{Math.Round(nearest.X)}_{Math.Round(nearest.Y)}";
                }
                else
                {
                    anchorId = $"TP_{Math.Round(cx)}_{Math.Round(cy)}";
                }
            }
            else
            {
                anchorId = $"START_{Math.Round(cx)}_{Math.Round(cy)}";
            }
        }

        var record = new RouteTelemetryRecord
        {
            RecordId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            MapName = target.MapName ?? "Teyvat",
            AnchorId = anchorId,
            // 使用1位小数四舍五入作为段标识的特征聚类
            SegmentKey = $"({Math.Round(startPoint.X, 1)}, {Math.Round(startPoint.Y, 1)})->({Math.Round(endPoint.X, 1)}, {Math.Round(endPoint.Y, 1)})",
            MoveMode = target.MoveMode,
            InboundAngle = Math.Round(inboundAngle, 1),
            PickedItems = recentPicked,
            // 保存整条有效轨线
            Points = actualTrajectory.Select(p => new TelemetryPoint2D { X = p.X, Y = p.Y }).ToList()
        };

        TryBindSemanticResource(record, endPoint);

        _records.Add(record);

        // 无论收集几条，只要有新的路段成功时，尝试调度一次异步存盘。
        if (_records.Count >= 1 && !_isSaving)
        {
            _ = SaveRecordsAsync();
        }
    }

    /// <summary>
    /// 【核心功能升级】尝试将轨线的物理终点，与玩家正在追踪的大地图资源进行模糊匹配。
    /// 从而达成“误差自我修正”与“语意化标签（如这不仅仅是段路，而是一条成功采摘了绝云椒椒的路）”结合。
    /// </summary>
    private void TryBindSemanticResource(RouteTelemetryRecord record, Point2f endPoint)
    {
        try
        {
            List<BetterGenshinImpact.Model.MaskMap.MaskMapPoint> activeMapPoints = new();
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var vm = BetterGenshinImpact.View.MaskWindow.InstanceNullable()?.DataContext as BetterGenshinImpact.ViewModel.MaskWindowViewModel;
                if (vm != null && vm.MapPoints != null)
                {
                    activeMapPoints = vm.MapPoints.ToList();
                }
            });

            if (activeMapPoints.Count == 0) return;

            // ImageX/Y 的偏差容差：由于人工标记或定位漂移，放宽到大概 10 像素距离
            double tolerance = 10.0;
            var closestResource = activeMapPoints
                .Select(p => new { Point = p, Distance = Math.Sqrt(Math.Pow(p.ImageX - endPoint.X, 2) + Math.Pow(p.ImageY - endPoint.Y, 2)) })
                .Where(x => x.Distance <= tolerance)
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            if (closestResource != null)
            {
                record.TargetResourceId = closestResource.Point.Id;
                record.TargetResourceLabelId = closestResource.Point.LabelId;
            }
        }
        catch
        {
            // 失败时直接忽略，保证不阻塞保存
        }
    }

    public async Task FlushAsync()
    {
        await SaveRecordsAsync();
    }

    private async Task SaveRecordsAsync()
    {
        if (_isSaving || _records.IsEmpty) return;
        _isSaving = true;

        try
        {
            var recordsToSave = new List<RouteTelemetryRecord>();
            while (_records.TryTake(out var record))
            {
                recordsToSave.Add(record);
            }

            if (recordsToSave.Count == 0) return;

            // 根据地图进行分组落地
            var groups = recordsToSave.GroupBy(r => new { r.MapName, r.AnchorId });
            foreach (var group in groups)
            {
                string safeMapName = string.Join("_", group.Key.MapName.Split(Path.GetInvalidFileNameChars()));
                string anchorId = string.IsNullOrEmpty(group.Key.AnchorId) ? "NO_ANCHOR" : group.Key.AnchorId;
                string filePath = Path.Combine(SaveDir, $"{safeMapName}_{anchorId}_Telemetry.json");

                List<RouteTelemetryRecord> existing = new();
                if (File.Exists(filePath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(filePath);
                        existing = JsonSerializer.Deserialize<List<RouteTelemetryRecord>>(json) ?? new();
                    }
                    catch
                    {
                        // JSON parsing might fail if file corrupted, backup old
                        File.Copy(filePath, filePath + ".bak", true);
                    }
                }

                existing.AddRange(group);

                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(existing, options));
            }
        }
        catch (Exception)
        {
            // Error handling ignored to not interrupt core game task thread
        }
        finally
        {
            _isSaving = false;
        }
    }
}

public class RouteTelemetryRecord
{
    public string RecordId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string MapName { get; set; } = string.Empty;
    
    // 原点标识：通常表明该段路线属于哪一个起步的传送门节点
    public string AnchorId { get; set; } = string.Empty;
    
    public string SegmentKey { get; set; } = string.Empty;
    public string MoveMode { get; set; } = string.Empty;
    public double InboundAngle { get; set; }
    
    // 是否为双向路径：绝大多数常规移动（步行、奔跑、冲刺、游泳等）是双向的，而像飞行、下落等有高低差限制的通常是单向的。
    public bool IsBidirectional
    {
        get
        {
            if (string.IsNullOrEmpty(MoveMode)) return true;
            // 飞行、跳跃、攀爬 等一般具有高度单向性限制
            if (MoveMode.Contains("fly", StringComparison.OrdinalIgnoreCase) ||
                MoveMode.Contains("jump", StringComparison.OrdinalIgnoreCase) ||
                MoveMode.Contains("climb", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }
    }
    
    // 如果该路段的终点靠近了玩家正在追踪的大地图资源，则关联它，使路段具备“语义”属性。
    public string TargetResourceId { get; set; } = string.Empty;
    public string TargetResourceLabelId { get; set; } = string.Empty;
    
    // 轨迹运行末尾成功拾取（F交互）的物品名称列表
    public List<string> PickedItems { get; set; } = new();

    public List<TelemetryPoint2D> Points { get; set; } = new();
}

public class TelemetryPoint2D
{
    public float X { get; set; }
    public float Y { get; set; }
}
