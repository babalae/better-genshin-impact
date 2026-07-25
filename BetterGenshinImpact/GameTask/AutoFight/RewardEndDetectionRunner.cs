using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight;

internal sealed class RewardEndDetectionRunner : IAsyncDisposable
{
    private readonly RewardEndDetectionConfig _config;
    private readonly Action _onFound;
    private readonly CancellationTokenSource _cts;
    private Task? _task;

    public RewardEndDetectionRunner(
        RewardEndDetectionConfig config,
        CancellationToken externalToken,
        Action onFound)
    {
        _config = config;
        _onFound = onFound;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
    }

    public void Start()
    {
        _task = Task.Run(DetectionLoopAsync, _cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_task is not null)
        {
            try
            {
                await _task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }

    private async Task DetectionLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (CheckRewardEndDetection())
                {
                    _onFound();
                    return;
                }

                await Task.Delay(200, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "地图追踪奖励结束检测异常");
                try
                {
                    await Task.Delay(200, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private bool CheckRewardEndDetection()
    {
        using var capture = CaptureToRectArea();
        var assets = AutoFightAssets.Get(capture);
        var recognitions = _config.Type == RewardEndDetectionType.Experience
            ? assets.ExperienceRewardRecognitionObjects
            : assets.MoraRewardRecognitionObjects;

        foreach (var recognition in recognitions)
        {
            if (_config.Type == RewardEndDetectionType.Mora &&
                (!int.TryParse(recognition.Name?.Replace("mora_", ""), out var mora) ||
                 !_config.IsMoraValueEnabled(mora)))
            {
                continue;
            }

            var found = capture.Find(recognition);
            if (!found.IsExist())
            {
                continue;
            }

            if (_config.Type == RewardEndDetectionType.Experience)
            {
                var iconX = found.X - 147;
                if (iconX < 0 || found.Y < 0 || found.Y >= capture.SrcMat.Rows || iconX >= capture.SrcMat.Cols)
                {
                    continue;
                }

                var pixelValue = capture.SrcMat.At<Vec3b>(found.Y, iconX);
                if (pixelValue[0] != 253 || pixelValue[1] != 247 || pixelValue[2] != 172)
                {
                    continue;
                }
            }

            return true;
        }

        return false;
    }
}
