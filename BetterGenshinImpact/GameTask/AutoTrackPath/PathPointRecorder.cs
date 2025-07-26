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
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

[Obsolete]
public class PathPointRecorder : Singleton<PathPointRecorder>
{
    private Task? _recordTask;
    private CancellationTokenSource? _recordTaskCts;

    public void Switch()
    {
        try
        {
            if (_recordTask == null)
            {
                _recordTaskCts = new CancellationTokenSource();
                _recordTask = RecordTask(_recordTaskCts.Token);
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

    public Task RecordTask(CancellationToken ct)
    {
        return new Task(() =>
        {
            GiPath way = new();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    Sleep(10, ct);
                    var ra = CaptureToRectArea();

                    // 小地图匹配
                    var tar = ElementAssets.Instance.PaimonMenuRo.TemplateImageGreyMat!;
                    var p = MatchTemplateHelper.MatchTemplate(ra.CacheGreyMat, tar, TemplateMatchModes.CCoeffNormed, null, 0.9);
                    if (p.X == 0 || p.Y == 0)
                    {
                        Sleep(50, ct);
                        continue;
                    }

                    var p2 = MapManager.GetMap(MapTypes.Teyvat).GetMiniMapPosition(new Mat(ra.SrcMat, new Rect(p.X + 24, p.Y - 15, 210, 210)));
                    if (!p2.IsEmpty())
                    {
                        way.AddPoint(p2);
                        Debug.WriteLine($"AddPoint: {p2}");
                    }
                    else
                    {
                        Sleep(50, ct);
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
