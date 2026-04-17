using System;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPathing.Movement;

/// <summary>
/// 惯性导航与位移推算器
/// 处理由于视觉遮挡或瞬间卡顿导致的坐标丢失问题，并利用历史速度向量平滑推算预估坐标
/// </summary>
public class InertialTracker
{
    private Point2f _lastValidPosition;
    private DateTime _lastValidTime;
    private Point2f _currentVelocity;

    /// <summary>
    /// 定位连续丢失的重试容忍计数
    /// </summary>
    public int DistanceTooFarRetryCount { get; private set; }

    /// <summary>
    /// 上一次有效视觉基准坐标
    /// </summary>
    public Point2f LastValidPosition => _lastValidPosition;

    /// <summary>
    /// 初始化导航推算器
    /// </summary>
    public void Reset(Point2f initialPosition)
    {
        _lastValidPosition = initialPosition;
        _lastValidTime = DateTime.UtcNow;
        _currentVelocity = new Point2f(0f, 0f);
        DistanceTooFarRetryCount = 0;
    }

    /// <summary>
    /// 记录或刷新合规的视觉反馈坐标，更新速度向量
    /// </summary>
    public void MarkValid(Point2f position, DateTime now)
    {
        var dt = (float)(now - _lastValidTime).TotalSeconds;
        if (dt > 0.1f)
        {
            var vx = (position.X - _lastValidPosition.X) / dt;
            var vy = (position.Y - _lastValidPosition.Y) / dt;
            
            // 速度限幅过滤异常跳跃点（防抖抖动）
            if (vx * vx + vy * vy < 2500) 
            {
                _currentVelocity = new Point2f(
                    _currentVelocity.X * 0.5f + vx * 0.5f, 
                    _currentVelocity.Y * 0.5f + vy * 0.5f);
            }
        }
        
        _lastValidPosition = position;
        _lastValidTime = now;
        DistanceTooFarRetryCount = 0;
    }

    /// <summary>
    /// 在丢失视觉定位时介入惯性导航接管，进行航迹平滑推算
    /// </summary>
    public Point2f TrackLost(DateTime now)
    {
        DistanceTooFarRetryCount++;
        var dt = (float)(now - _lastValidTime).TotalSeconds;
        
        return new Point2f(
            _lastValidPosition.X + _currentVelocity.X * dt, 
            _lastValidPosition.Y + _currentVelocity.Y * dt);
    }
}
