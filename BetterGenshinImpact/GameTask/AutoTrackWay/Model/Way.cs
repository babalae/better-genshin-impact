using System;
using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;

namespace BetterGenshinImpact.GameTask.AutoTrackWay.Model;

[Serializable]
public class Way
{
    public List<WayPoint> WayPointList { get; set; } = new();

    public void AddPoint(Rect matchRect)
    {
        // 长宽比例大于 1.5 的矩形不加入
        var r = matchRect.Width / (double)matchRect.Height;
        if (r is > 1.5 or < 0.66)
        {
            Debug.WriteLine($"长宽比例不符合要求: {r}");
            return;
        }

        WayPointList.Add(WayPoint.BuildFrom(matchRect, WayPointList.Count));
    }
}
