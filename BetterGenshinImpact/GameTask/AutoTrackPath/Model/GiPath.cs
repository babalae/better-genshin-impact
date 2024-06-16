using System;
using System.Collections.Generic;
using System.Diagnostics;
using BetterGenshinImpact.Helpers;
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

        // 离上个点距离小于 10 的矩形不加入
        var giPathPoint = GiPathPoint.BuildFrom(matchRect, WayPointList.Count);
        if (WayPointList.Count > 0)
        {
            var lastPoint = WayPointList[^1];
            var distance = MathHelper.Distance(giPathPoint.Pt, lastPoint.Pt);
            if (distance < 10)
            {
                Debug.WriteLine($"距离上个点太近: {distance}，舍弃");
                return;
            }
        }

        WayPointList.Add(giPathPoint);
    }
}
