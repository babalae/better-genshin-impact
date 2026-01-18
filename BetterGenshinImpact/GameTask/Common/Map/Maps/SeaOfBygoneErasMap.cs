using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using System.Collections.Generic;
using System.Linq;
using System;
using Newtonsoft.Json.Linq;


namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 旧日之海
/// </summary>
public class SeaOfBygoneErasMap : SceneBaseMap
{
    #region 地图参数

    static readonly int GameMapRows = 3; // 游戏坐标下地图块的行数
    static readonly int GameMapCols = 4; // 游戏坐标下地图块的列数
    static readonly int GameMapUpRows = 2; // 游戏坐标下 左上角离地图原点的行数(注意原点在块的右下角)
    static readonly int GameMapLeftCols = 5; // 游戏坐标下 左上角离地图原点的列数(注意原点在块的右下角)

    #endregion 地图参数

    static readonly int SeaOfBygoneErasMapImageBlockWidth = 1024;

    private static Mat TeleportTemplate;
    private static Mat TeleportTemplateMask;
    private List<Point> MapTeleports;

    static SeaOfBygoneErasMap()
    {
        Mat img = GameTaskManager.LoadAssetImage("QuickTeleport", "TeleportTransparentBackground.png", ImreadModes.Unchanged);

        TeleportTemplate = new Mat();
        Cv2.CvtColor(img, TeleportTemplate, ColorConversionCodes.BGRA2GRAY);

        Mat[] channels = Cv2.Split(img);
        TeleportTemplateMask = channels[3];
    }

    public SeaOfBygoneErasMap() : base(type: MapTypes.SeaOfBygoneEras,
        mapSize: new Size(GameMapCols * SeaOfBygoneErasMapImageBlockWidth, GameMapRows * SeaOfBygoneErasMapImageBlockWidth),
        mapOriginInImageCoordinate: new Point2f((GameMapLeftCols + 1) * SeaOfBygoneErasMapImageBlockWidth, (GameMapUpRows + 1) * SeaOfBygoneErasMapImageBlockWidth),
        mapImageBlockWidth: SeaOfBygoneErasMapImageBlockWidth,
        splitRow: 0,
        splitCol: 0)
    {
        ExtractAndSaveFeature(Global.Absolute("Assets/Map/SeaOfBygoneEras/SeaOfBygoneEras_0_1024.png"));
        ExtractAndSaveFeature(Global.Absolute("Assets/Map/SeaOfBygoneEras/SeaOfBygoneEras_-1_1024.webp"));
        ExtractAndSaveFeature(Global.Absolute("Assets/Map/SeaOfBygoneEras/SeaOfBygoneEras_-2_1024.webp"));
        Layers = BaseMapLayer.LoadLayers(this);

        var mapTeleports = new List<Point>();
        var tpJson = System.IO.File.ReadAllText(Global.Absolute(@"GameTask\AutoTrackPath\Assets\tp.json"));

        JObject j = JObject.Parse(tpJson);
        foreach (JObject i in j["data"]!)
        {
            var sceneId = i["sceneId"];
            if (sceneId != null && (int)sceneId == 11)
            {
                foreach (var p in i["points"]!)
                {
                    if ((string)p["type"]! != "Teleport")
                    {
                        continue;
                    }
                    var x = (float)p["position"]![2]!;
                    var y = (float)p["position"]![0]!;
                    var (x1, y1) = ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f (x, y));
                    mapTeleports.Add(new Point(x1, y1));
                }
            }
        }
        mapTeleports = mapTeleports.OrderBy(i => i.X).ThenBy(i => i.Y).ToList();
        MapTeleports = mapTeleports;
    }

    public override Point2f GetBigMapPosition(Mat greyBigMapMat)
    {
        var rect = GetBigMapRectByTeleports(greyBigMapMat);
        if (rect != default)
        {
            return rect.GetCenterPoint();
        }

        return base.GetBigMapPosition(greyBigMapMat);
    }

    public override Rect GetBigMapRect(Mat greyBigMapMat)
    {
        var rect = GetBigMapRectByTeleports(greyBigMapMat);
        if (rect != default)
        {
            return rect;
        }

        return base.GetBigMapRect(greyBigMapMat);
    }

    private Rect GetBigMapRectByTeleports(Mat greyBigMapMat)
    {
        // It's fine to miss some, but definitely no false positive results.
        const double threshold = 0.99;
        using Mat result = new Mat();
        Cv2.MatchTemplate(greyBigMapMat, TeleportTemplate, result, TemplateMatchModes.CCorrNormed, TeleportTemplateMask);

        var teleportPoints = new List<Point>();

        // Step 1: Get all teleport positions from current screenshot
        for (int i = 1; i < result.Rows - 1; ++i)
        {
            for (int j = 1; j < result.Cols - 1; ++j)
            {
                float val = result.At<float>(i, j);

                if (val > threshold)
                {
                    if (val >= result.At<float>(i - 1, j - 1) &&
                        val >= result.At<float>(i - 1, j) &&
                        val >= result.At<float>(i - 1, j + 1) &&
                        val >= result.At<float>(i, j - 1) &&
                        val >= result.At<float>(i, j + 1) &&
                        val >= result.At<float>(i + 1, j - 1) &&
                        val >= result.At<float>(i + 1, j) &&
                        val >= result.At<float>(i + 1, j + 1))
                    {
                        var newPoint = new Point(j, i);
                        bool tooClose = false;
                        foreach (var p in teleportPoints)
                        {
                            if (p.DistanceTo(newPoint) < 50)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                        if (!tooClose)
                        {
                            teleportPoints.Add(newPoint);
                        }
                    }
                }
            }
        }

        if (teleportPoints.Count < 2)
        {
            return default;
        }
        teleportPoints = teleportPoints.OrderBy(i => i.X).ThenBy(i => i.Y).ToList();
        /*
        foreach (var p in teleportPoints) {
            Logger.LogInformation("Teleport point: {a}", p);
        }
        Logger.LogInformation("Total telepoints: {c}", teleportPoints.Count);
        */

        Func<Point, Point, double> GetAngleOfTwoPoints = (p0, p1) =>
        {
            double deltaX = p0.X - p1.X;
            int deltaY = p0.Y - p1.Y;
            if (deltaY == 0) { return 90; }
            var val = Math.Atan(deltaX / deltaY);
            return val / Math.PI * 180;
        };

        // Step 2: find a diagonal determined by two of the teleports, let's call these two teleports reference points
        Point rp0 = new Point();
        Point rp1 = new Point();
        double refAngle = 0.0;
        {
            double minAngleDiff = 180;
            for (int i = 0; i < teleportPoints.Count; ++i)
            {
                for (int j = i + 1; j < teleportPoints.Count; ++j)
                {
                    var p0 = teleportPoints[i];
                    var p1 = teleportPoints[j];
                    var angle = GetAngleOfTwoPoints(p0, p1);
                    if (Math.Abs(Math.Abs(angle) - 45) < minAngleDiff)
                    {
                        rp0 = p0;
                        rp1 = p1;
                        minAngleDiff = Math.Abs(Math.Abs(angle) - 45);
                    }
                }
            }
            refAngle = GetAngleOfTwoPoints(rp0, rp1);
            // Logger.LogInformation("Reference points {a} and {b}, Angle {c}", rp0, rp1, refAngle);
        }

        {
            /*
            var debugMat = new Mat();
            Cv2.CvtColor(greyBigMapMat, debugMat, ColorConversionCodes.GRAY2BGR);
            foreach (var p in teleportPoints)
            {
                Cv2.DrawMarker(debugMat, p, new Scalar(255, 0, 0), MarkerTypes.Cross, 20, 2);
            }
            Cv2.DrawMarker(debugMat, rp0, new Scalar(0, 255, 0), MarkerTypes.TriangleUp, 20, 2);
            Cv2.DrawMarker(debugMat, rp1, new Scalar(0, 255, 0), MarkerTypes.TriangleUp, 20, 2);
            Cv2.ImWrite(((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds().ToString() + ".png", debugMat);
            */
        }

        // Step 3: For all diagonals determined by pairs of teleports on this map.
        var minDeviation = double.MaxValue;
        var transformParamScale = 0.0;
        var transformParamDeltaX = 0.0;
        var transformParamDeltaY = 0.0;
        for (int i = 0; i < MapTeleports.Count; ++i)
        {
            for (int j = i + 1; j < MapTeleports.Count; ++j)
            {
                var mp0 = MapTeleports[i];
                var mp1 = MapTeleports[j];
                var angle = GetAngleOfTwoPoints(mp0, mp1);
                if (Math.Abs(angle - refAngle) < 5)
                {
                    // Step 4: Assuming this pair corresponds to the reference points
                    var mpDist = mp0.DistanceTo(mp1);
                    var rpDist = rp0.DistanceTo(rp1);
                    var scale = mpDist / rpDist;
                    var deltaX = mp0.X - rp0.X * scale;
                    var deltaY = mp0.Y - rp0.Y * scale;

                    Func<Point, Point> transformPoint = i =>
                    {
                        return new Point(i.X * scale + deltaX, i.Y * scale + deltaY);
                    };

                    var transformedPoints = teleportPoints.Select(i => transformPoint(i)).ToList();
                    // Step 5: Check how close the fit is
                    double totalDeviation = 0;
                    foreach (var p in transformedPoints)
                    {
                        var minDist = MapTeleports.Select(i => i.DistanceTo(p)).Min();
                        totalDeviation += minDist;
                    }
                    if (totalDeviation < minDeviation)
                    {
                        minDeviation = totalDeviation;
                        transformParamScale = scale;
                        transformParamDeltaX = deltaX;
                        transformParamDeltaY = deltaY;
                    }
                }
            }
        }
        // Logger.LogInformation("Min deviation: {d}", minDeviation);
        if (minDeviation < 200)
        {
            Func<Point, Point> transformPoint = i =>
            {
                return new Point(i.X * transformParamScale + transformParamDeltaX, i.Y * transformParamScale + transformParamDeltaY);
            };
            var pTopLeft = transformPoint(new Point(0, 0));
            var pBottomRight = transformPoint(new Point(greyBigMapMat.Width, greyBigMapMat.Height));
            // Logger.LogInformation("Rect: {a}, {b}", pTopLeft, pBottomRight);
            return new Rect(pTopLeft.X, pTopLeft.Y, pBottomRight.X - pTopLeft.X, pBottomRight.Y - pTopLeft.Y);
        }


        return default;
    }

}