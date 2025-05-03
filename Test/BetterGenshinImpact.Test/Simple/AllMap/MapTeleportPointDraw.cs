using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.Service;
using OpenCvSharp;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.Test.Simple.AllMap;

public class MapTeleportPointDraw
{
    public static void Draw()
    {
        // var pList = LoadTeleportPoint(@"E:\HuiTask\更好的原神\地图匹配\地图点位\5.0");
        // pList.AddRange(MapAssets.Instance.TpPositions);
        var map = new Mat(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.0\mainMap1024BlockColor.png");
        DrawTeleportPoint(map, MapLazyAssets.Instance.ScenesDic[MapTypes.Teyvat.ToString()].Points);
        Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.0\传送点_1024_0.34.3.png", map);
    }

    public static void DrawTeleportPoint(Mat map, List<GiTpPosition> points)
    {
        foreach (var point in points)
        {
            var p = TeyvatMapCoordinate.GameToMain1024(point.Position);
            Cv2.Circle(map, p, 4, Scalar.Red, 2);
            // 写文字
            Cv2.PutText(map, $"[{point.X:F2},{point.Y:F2}]", new Point(p.X + 10, p.Y + 5), HersheyFonts.HersheySimplex, 1, Scalar.Red, 2);
        }
    }

    public static List<GiWorldPosition> LoadTeleportPoint(string folderPath)
    {
        string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
        List<GiWorldPosition> points = new();
        foreach (var file in files)
        {
            var gamePoint = JsonSerializer.Deserialize<GamePoint>(File.ReadAllText(file), ConfigService.JsonOptions);
            if (gamePoint == null)
            {
                Debug.WriteLine($"{file} json is null");
                continue;
            }

            points.Add(Transform(gamePoint.Position));
        }

        return points;
    }

    /// <summary>
    /// 坐标系转换
    /// </summary>
    /// <param name="position">[a,b,c]</param>
    /// <returns></returns>
    public static GiWorldPosition Transform(decimal[] position)
    {
        return new GiWorldPosition
        {
            Position = position,
        };
    }

    class GamePoint
    {
        public string Description { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal[] Position { get; set; } = new decimal[3];
    }
}
