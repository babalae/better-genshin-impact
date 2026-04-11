using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPathing.Movement;

/// <summary>
/// 陷阱与卡死状态检测器
/// 负责连续多帧定位和原地循环或长时静止等阻碍卡死问题检测
/// </summary>
public class StuckDetector
{
    private readonly Queue<Point2f> _prevPositions = new(8);
    private DateTime _lastPositionRecord = DateTime.UtcNow;

    /// <summary>
    /// 被困的记录次数
    /// </summary>
    public int InTrapCount { get; private set; }

    /// <summary>
    /// 重置状态，清空队列与卡死统计
    /// </summary>
    public void Reset()
    {
        ClearQueue();
        _lastPositionRecord = DateTime.UtcNow;
        InTrapCount = 0;
    }

    /// <summary>
    /// 仅清理记录队列
    /// </summary>
    public void ClearQueue()
    {
        _prevPositions.Clear();
    }

    /// <summary>
    /// 执行坐标录入与阻碍判断。
    /// 一旦发现物理碰撞或跑墙导致的位置停滞不前，抛出卡死预警。
    /// </summary>
    public bool CheckStuck(Point2f position, int additionalTimeInMs)
    {
        if ((DateTime.UtcNow - _lastPositionRecord).TotalMilliseconds > 1000 + additionalTimeInMs)
        {
            _lastPositionRecord = DateTime.UtcNow;
            _prevPositions.Enqueue(position);
            
            if (_prevPositions.Count > 8)
            {
                var oldestPosition = _prevPositions.Dequeue();
                
                // 物理校验：使用欧几里得距离平方规避开销 
                var dx = position.X - oldestPosition.X;
                var dy = position.Y - oldestPosition.Y;
                if (dx * dx + dy * dy < 25)
                {
                    InTrapCount++;
                    return true;
                }
            }
        }
        return false;
    }
}
