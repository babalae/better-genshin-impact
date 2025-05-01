using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

public class TeyvatMap() : IndependentBaseMap(name: "提瓦特大陆",
    mapSize: new Size(MapCoordinate.Main2048Width, MapCoordinate.Main2048Height),
    splitRow: MapCoordinate.GameMapRows * 2,
    splitCol: MapCoordinate.GameMapCols * 2)
{
    public new Point2f GetBigMapPosition(Mat greyBigMapMat)
    {
        return SiftMatcher.Match(MainLayer.TrainKeyPoints, MainLayer.TrainDescriptors, greyBigMapMat);
    }

    public new Rect GetBigMapRect(Mat greyBigMapMat)
    {
        return SiftMatcher.KnnMatchRect(MainLayer.TrainKeyPoints, MainLayer.TrainDescriptors, greyBigMapMat);
    }
}