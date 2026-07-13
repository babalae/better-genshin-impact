using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BetterGenshinImpact.GameTask.AutoTrackPath.Model;

[Serializable]
public class GiPath
{
    public List<GiPathPoint> WayPointList { get; set; } = [];

    public void AddPoint(Point2f point)
    {
        // 离上个点距离小于 10 的矩形不加入
        var giPathPoint = GiPathPoint.BuildFrom(point, WayPointList.Count);
        if (WayPointList.Count > 0)
        {
            var lastPoint = WayPointList[^1];
            var distance = MathHelper.Distance(giPathPoint.Pt, lastPoint.Pt);
            if (distance == 0 || distance > 50)
            {
                Debug.WriteLine($"距离上个点太近或者太远: {distance}，舍弃");
                return;
            }
        }

        WayPointList.Add(giPathPoint);
    }
}
