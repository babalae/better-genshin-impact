using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

[Obsolete]
public class PathPointRecorder : Singleton<PathPointRecorder>
{
    /// <summary>
    /// 配置根。单例没有创建方可以传参，暂时读配置根（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigService.Config!;

    private Task? _recordTask;
    private CancellationTokenSource? _recordTaskCts;

    /// <summary>
    /// 开始或结束路线录制
    /// </summary>
    /// <param name="runtime">录制所用的运行环境，运行环境停止时录制随之结束</param>
    public void Switch(GameRuntime runtime)
    {
        try
        {
            if (_recordTask == null)
            {
                // 与运行环境的停止信号关联，截图器停止后不再继续录制
                _recordTaskCts = CancellationTokenSource.CreateLinkedTokenSource(runtime.Stopping);
                _recordTask = RecordTask(runtime, _recordTaskCts.Token);
                _recordTask.Start();
            }
            else
            {
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

    /// <summary>
    /// 创建路线录制任务（未启动）
    /// </summary>
    /// <param name="runtime">录制所用的运行环境</param>
    /// <param name="ct">取消令牌</param>
    public Task RecordTask(GameRuntime runtime, CancellationToken ct)
    {
        var matchingMethod = Config.PathingConditionConfig.MapMatchingMethod;

        return new Task(() =>
        {
            GiPath way = new();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    runtime.Sleep(10, ct);
                    var ra = runtime.CaptureToRectArea();

                    // 小地图匹配
                    var tar = ElementRecognition.Get("PaimonMenu", ra).TemplateImageGreyMat!;
                    var p = MatchTemplateHelper.MatchTemplate(ra.CacheGreyMat, tar, TemplateMatchModes.CCoeffNormed, null, 0.9);
                    if (p.X == 0 || p.Y == 0)
                    {
                        runtime.Sleep(50, ct);
                        continue;
                    }

                    var p2 = MapManager.GetMap(MapTypes.Teyvat, matchingMethod).GetMiniMapPosition(new Mat(ra.SrcMat, new Rect(p.X + 24, p.Y - 15, 210, 210)));
                    if (!p2.IsEmpty())
                    {
                        way.AddPoint(p2);
                        Debug.WriteLine($"AddPoint: {p2}");
                    }
                    else
                    {
                        runtime.Sleep(50, ct);
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
        }, ct);
    }
}
