using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoTrackWay.Model;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackWay;

public class WayPointRecorder
{
    private readonly Lazy<EntireMap> _bigMap = new();

    public void Switch()
    {
    }

    public Task RecordTask(CancellationTokenSource cts)
    {
        return new Task(() =>
        {
            Way way = new();

            while (!cts.Token.IsCancellationRequested)
            {
                Sleep(10, cts);
                var ra = GetRectAreaFromDispatcher();

                // 小地图匹配测试
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
                }
                else
                {
                    Sleep(50, cts);
                }
            }
        }, cts.Token);
    }
}
