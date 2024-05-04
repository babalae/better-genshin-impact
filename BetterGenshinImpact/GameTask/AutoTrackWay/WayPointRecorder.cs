using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map;
using OpenCvSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoTrackWay.Model;
using BetterGenshinImpact.Model;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.GameTask.Model.Enum;

namespace BetterGenshinImpact.GameTask.AutoTrackWay;

public class WayPointRecorder : Singleton<WayPointRecorder>
{
    private readonly Lazy<EntireMap> _bigMap = new();

    private Task? _recordTask;
    private CancellationTokenSource? _recordTaskCts;

    public void Switch()
    {
        try
        {
            if (_recordTask == null)
            {
                TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.OnlyCacheCapture);

                _recordTaskCts = new CancellationTokenSource();
                _recordTask = RecordTask(_recordTaskCts);
                _recordTask.Start();
            }
            else
            {
                TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.OnlyTrigger);

                _recordTaskCts?.Cancel();
                _recordTask = null;
            }
        }
        catch (NormalEndException)
        {
            Logger.LogInformation("关闭路线录制");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public Task RecordTask(CancellationTokenSource cts)
    {
        return new Task(() =>
        {
            Way way = new();

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    Sleep(10, cts);
                    var ra = GetRectAreaFromDispatcher();

                    // 小地图匹配
                    var tar = ElementAssets.Instance.PaimonMenuRo.TemplateImageGreyMat!;
                    var p = MatchTemplateHelper.MatchTemplate(ra.SrcGreyMat, tar, TemplateMatchModes.CCoeffNormed, null, 0.9);
                    if (p.X == 0 || p.Y == 0)
                    {
                        Sleep(50, cts);
                        continue;
                    }

                    var rect = _bigMap.Value.GetMapPositionByFeatureMatch(new Mat(ra.SrcGreyMat, new Rect(p.X + 24, p.Y - 15, 210, 210)));
                    if (rect != Rect.Empty)
                    {
                        way.AddPoint(rect);
                        Debug.WriteLine($"AddPoint: {rect}");
                    }
                    else
                    {
                        Sleep(50, cts);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
#if DEBUG
            File.WriteAllText(Global.Absolute($@"log\way\{DateTime.Now:yyyy-MM-dd HH：mm：ss：ffff}.json"), JsonSerializer.Serialize(way, ConfigService.JsonOptions));
#endif
            Logger.LogInformation("路线录制结束");
        }, cts.Token);
    }
}
