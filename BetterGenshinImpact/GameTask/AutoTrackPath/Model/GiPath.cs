using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoTrackPath.Model;

[Serializable]
public class GiPath
{
    public List<GiPathPoint> WayPointList { get; set; } = new();

    public void AddPoint(Rect matchRect)
    {
        // 长宽比例大于 1.5 的矩形不加入
        var r = matchRect.Width / (double)matchRect.Height;
        if (r is > 1.5 or < 0.66)
        {
            Debug.WriteLine($"长宽比例不符合要求: {r}");
            return;
        }

        WayPointList.Add(GiPathPoint.BuildFrom(matchRect, WayPointList.Count));
    }
}
