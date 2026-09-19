using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.TravelLog.Model;
using BetterGenshinImpact.Model;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.GameTask.TravelLog;

/// <summary>
/// 移动轨迹记录服务（单例）
/// 负责轨迹点管理、里程统计、按天持久化
/// </summary>
public class TravelLogService : Singleton<TravelLogService>
{
    /// <summary>
    /// 位移小于该值（米）视为噪声，不记录
    /// </summary>
    private const double NoiseDistance = 1.5;

    /// <summary>
    /// 与上一点距离大于该值（米）视为传送，断开轨迹段且不计入里程
    /// </summary>
    private const double TeleportDistance = 80;

    /// <summary>
    /// 持久化节流间隔
    /// </summary>
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(15);

    private readonly object _lock = new();

    private TravelLogData? _today;

    /// <summary>
    /// 今日数据版本号，每新增一个点 +1，供 UI 轮询判断是否需要重绘
    /// </summary>
    private int _todayVersion;

    private bool _dirty;
    private DateTime _lastSaveTime = DateTime.MinValue;

    private static string TodayString => DateTime.Now.ToString("yyyy-MM-dd");

    private static string StorageDir => Global.Absolute(@"User\TravelLog");

    private static string GetFilePath(string date) => Path.Combine(StorageDir, $"{date}.json");

    /// <summary>
    /// 今日数据版本号（每次新增轨迹点递增）
    /// </summary>
    public int TodayVersion
    {
        get { lock (_lock) { return _todayVersion; } }
    }

    /// <summary>
    /// 记录一个轨迹点（原神游戏坐标系）
    /// </summary>
    public void AddPoint(Point2f gameCoordinates)
    {
        // (0,0) 是定位失败的默认值，直接丢弃
        if (gameCoordinates.X == 0 && gameCoordinates.Y == 0)
        {
            return;
        }

        lock (_lock)
        {
            EnsureTodayData();

            var data = _today!;
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();

            if (data.Points.Count > 0)
            {
                var last = data.Points[^1];
                var distance = Math.Sqrt(Math.Pow(gameCoordinates.X - last.X, 2) + Math.Pow(gameCoordinates.Y - last.Y, 2));

                if (distance < NoiseDistance)
                {
                    return;
                }

                if (distance > TeleportDistance)
                {
                    // 传送：开新段，不计里程
                    data.Segments.Add(data.Points.Count);
                }
                else
                {
                    data.TotalDistance += distance;
                }
            }
            else if (data.Segments.Count == 0)
            {
                data.Segments.Add(0);
            }

            data.Points.Add(new TravelLogPoint { T = now, X = gameCoordinates.X, Y = gameCoordinates.Y });
            _todayVersion++;
            _dirty = true;

            SaveIfNeeded();
        }
    }

    /// <summary>
    /// 获取今日轨迹数据的快照（用于 UI 展示）
    /// </summary>
    public TravelLogData GetTodaySnapshot()
    {
        lock (_lock)
        {
            EnsureTodayData();
            return Snapshot(_today!);
        }
    }

    /// <summary>
    /// 读取指定日期的轨迹数据（不存在时返回空数据）
    /// </summary>
    public TravelLogData LoadByDate(string date)
    {
        lock (_lock)
        {
            if (date == _today?.Date)
            {
                return Snapshot(_today);
            }

            return LoadFromFile(date) ?? new TravelLogData { Date = date };
        }
    }

    /// <summary>
    /// 列出已有轨迹记录的日期（倒序，最新在前）
    /// </summary>
    public List<string> ListAvailableDates()
    {
        try
        {
            if (!Directory.Exists(StorageDir))
            {
                return [TodayString];
            }

            var dates = Directory.GetFiles(StorageDir, "????-??-??.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();

            if (!dates.Contains(TodayString))
            {
                dates.Add(TodayString);
            }

            return [.. dates.OrderByDescending(d => d)];
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogWarning(e, "[TravelLog] 枚举轨迹文件失败");
            return [TodayString];
        }
    }

    /// <summary>
    /// 获取今日最近一段轨迹的快照（用于小地图遮罩等局部展示）
    /// </summary>
    /// <param name="maxPoints">最多返回的轨迹点数（从末尾往前取）</param>
    /// <returns>轨迹点列表 + 分段起始索引（相对于返回列表）</returns>
    public (List<TravelLogPoint> Points, List<int> SegmentStarts) GetRecentTrajectory(int maxPoints)
    {
        lock (_lock)
        {
            EnsureTodayData();
            var data = _today!;

            var startIndex = Math.Max(0, data.Points.Count - maxPoints);
            var points = data.Points
                .Skip(startIndex)
                .Select(p => new TravelLogPoint { T = p.T, X = p.X, Y = p.Y })
                .ToList();

            var segmentStarts = data.Segments
                .Where(s => s > startIndex)
                .Select(s => s - startIndex)
                .ToList();
            segmentStarts.Insert(0, 0);

            return (points, segmentStarts);
        }
    }

    /// <summary>
    /// 立即落盘（有未保存数据时），供退出时调用
    /// </summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_dirty && _today != null)
            {
                SaveToFile(_today);
                _dirty = false;
            }
        }
    }

    /// <summary>
    /// 确保持有的是今日数据，跨天时先落盘旧数据再切换
    /// 调用前必须已持有 _lock
    /// </summary>
    private void EnsureTodayData()
    {
        var today = TodayString;
        if (_today != null && _today.Date == today)
        {
            return;
        }

        if (_today != null && _dirty)
        {
            SaveToFile(_today);
        }

        _today = LoadFromFile(today) ?? new TravelLogData { Date = today };
        _dirty = false;
        _lastSaveTime = DateTime.MinValue;
        TaskControl.Logger.LogInformation("[TravelLog] 已加载 {Date} 的轨迹数据，历史里程 {Distance} 米，共 {Count} 个轨迹点",
            today, Math.Round(_today.TotalDistance, 1), _today.Points.Count);
    }

    /// <summary>
    /// 节流落盘，调用前必须已持有 _lock
    /// </summary>
    private void SaveIfNeeded()
    {
        if (!_dirty || DateTime.Now - _lastSaveTime < SaveInterval)
        {
            return;
        }

        SaveToFile(_today!);
        _dirty = false;
        _lastSaveTime = DateTime.Now;
    }

    private static void SaveToFile(TravelLogData data)
    {
        try
        {
            Directory.CreateDirectory(StorageDir);
            File.WriteAllText(GetFilePath(data.Date), JsonConvert.SerializeObject(data, Formatting.Indented));
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogWarning(e, "[TravelLog] 轨迹数据保存失败");
        }
    }

    private static TravelLogData? LoadFromFile(string date)
    {
        try
        {
            var path = GetFilePath(date);
            if (!File.Exists(path))
            {
                return null;
            }

            var data = JsonConvert.DeserializeObject<TravelLogData>(File.ReadAllText(path));
            if (data == null || data.Points.Count == 0)
            {
                return data;
            }

            // 兼容旧数据：没有分段信息时视为一整段
            if (data.Segments.Count == 0)
            {
                data.Segments.Add(0);
            }

            return data;
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogWarning(e, "[TravelLog] 读取轨迹文件失败：{Date}", date);
            return null;
        }
    }

    /// <summary>
    /// 深拷贝一份数据，避免 UI 读取时与写入线程竞争
    /// </summary>
    private static TravelLogData Snapshot(TravelLogData data)
    {
        return new TravelLogData
        {
            Date = data.Date,
            TotalDistance = data.TotalDistance,
            Points = [.. data.Points.Select(p => new TravelLogPoint { T = p.T, X = p.X, Y = p.Y })],
            Segments = [.. data.Segments]
        };
    }
}
