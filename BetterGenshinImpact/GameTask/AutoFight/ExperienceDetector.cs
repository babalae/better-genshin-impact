using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 经验值检测器：在战斗过程中异步检测屏幕上怪物死亡时掉落的经验值数字图标，
/// 用于判断是否有精英怪被击杀，从而决定战后是否执行拾取。
/// </summary>
public sealed class ExperienceDetector : IDisposable
{
    private static readonly ILogger Logger = TaskControl.Logger;

    /// <summary>
    /// 经验值图标左侧特征像素的水平偏移量（相对于模板匹配位置）
    /// </summary>
    private const int ExpIconPixelOffsetX = -147;

    /// <summary>
    /// 经验值图标特征颜色 BGR 范围（用于二次验证模板匹配结果）
    /// </summary>
    private static readonly Vec3b ExpColorMin = new(200, 200, 150);
    private static readonly Vec3b ExpColorMax = new(255, 255, 200);

    /// <summary>
    /// 检测循环间隔（毫秒）
    /// </summary>
    private const int DetectionIntervalMs = 100;

    private readonly IReadOnlyList<RecognitionObject> _experienceRos;
    private readonly CancellationTokenSource _linkedCts;
    private readonly TaskCompletionSource<bool> _resultTcs;
    private Task? _detectionTask;
    private bool _disposed;

    /// <summary>
    /// 获取检测结果：是否检测到经验值图标
    /// </summary>
    public bool HasDetectedExperience =>
        _resultTcs.Task.IsCompletedSuccessfully && _resultTcs.Task.Result;

    public ExperienceDetector(
        IReadOnlyList<RecognitionObject> experienceRos,
        CancellationToken externalToken)
    {
        _experienceRos = experienceRos ?? throw new ArgumentNullException(nameof(experienceRos));
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _resultTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// 启动后台经验值检测循环
    /// </summary>
    public void 
        Start()
    {
        if (_experienceRos.Count == 0)
        {
            Logger.LogWarning("经验值检测：无可用模板，跳过检测");
            return;
        }

        _detectionTask = Task.Run(() => DetectionLoop(_linkedCts.Token), _linkedCts.Token);
    }

    /// <summary>
    /// 停止检测并等待后台任务结束
    /// </summary>
    public async Task StopAsync()
    {
        _linkedCts.Cancel();
        if (_detectionTask != null)
        {
            try
            {
                await _detectionTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略
            }
        }

        // 确保结果有值（未检测到时设为 false）
        _resultTcs.TrySetResult(false);
    }

    /// <summary>
    /// 后台检测循环：截屏 → 模板匹配 → 像素颜色验证
    /// </summary>
    private void DetectionLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var capture = TaskControl.CaptureToRectArea();

                foreach (var ro in _experienceRos)
                {
                    ct.ThrowIfCancellationRequested();

                    var matchResult = capture.Find(ro);
                    if (!matchResult.IsExist())
                        continue;

                    // 模板匹配成功，执行像素颜色二次验证
                    if (ValidateExpPixelColor(capture, matchResult))
                    {
                        Logger.LogInformation("基于经验值判断：检测到 {Name} 经验值图标，启用战后拾取", ro.Name.Replace("Experience_", ""));
                        _resultTcs.TrySetResult(true);
                        return; // 检测到后退出循环
                    }
                }

                // 短暂等待后继续下一轮检测
                Thread.Sleep(DetectionIntervalMs);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "经验值检测循环中发生异常，继续下一轮");
            }
        }
    }

    /// <summary>
    /// 像素颜色验证：检查匹配位置左侧偏移处的像素是否符合经验值图标特征色
    /// </summary>
    private static bool ValidateExpPixelColor(ImageRegion capture, Region matchResult)
    {
        var checkX = matchResult.X + ExpIconPixelOffsetX;
        var checkY = matchResult.Y;

        // 边界检查
        if (checkX < 0 || checkX >= capture.SrcMat.Cols || checkY < 0 || checkY >= capture.SrcMat.Rows)
            return false;

        var pixel = capture.SrcMat.At<Vec3b>(checkY, checkX);
        return pixel[0] >= ExpColorMin[0] && pixel[0] <= ExpColorMax[0]  // B
            && pixel[1] >= ExpColorMin[1] && pixel[1] <= ExpColorMax[1]  // G
            && pixel[2] >= ExpColorMin[2] && pixel[2] <= ExpColorMax[2]; // R
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _linkedCts.Cancel();
        _linkedCts.Dispose();
        _resultTcs.TrySetResult(false);
        _detectionTask = null;
    }
}
