using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.TravelLog.Model;

/// <summary>
/// 单个轨迹点（原神游戏坐标系，1024 级别，1 单位 ≈ 1 米）
/// </summary>
[Serializable]
public class TravelLogPoint
{
    /// <summary>
    /// Unix 时间戳（秒）
    /// </summary>
    public long T { get; set; }

    /// <summary>
    /// 游戏坐标 X
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// 游戏坐标 Y
    /// </summary>
    public float Y { get; set; }
}

/// <summary>
/// 一天的移动轨迹数据
/// </summary>
[Serializable]
public class TravelLogData
{
    /// <summary>
    /// 日期，格式 yyyy-MM-dd
    /// </summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// 当日累计移动里程（米，不含传送跳变）
    /// </summary>
    public double TotalDistance { get; set; }

    /// <summary>
    /// 轨迹点列表（按时间顺序）
    /// </summary>
    public List<TravelLogPoint> Points { get; set; } = [];

    /// <summary>
    /// 轨迹分段。每段是 Points 中的起始索引：
    /// 第 i 段 = Points[Segments[i] .. Segments[i+1]-1]（最后一段到 Points 末尾）。
    /// 传送/长时间无定位会导致开新段，绘制时段间不连线。
    /// </summary>
    public List<int> Segments { get; set; } = [];
}
