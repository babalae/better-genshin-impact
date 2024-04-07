using BetterGenshinImpact.Service;
using OpenCvSharp;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace BetterGenshinImpact.Test.Simple.AllMap;

public class MapTeleportPointDraw
{
    public static void Draw()
    {
        var pList = LoadTeleportPoint(@"E:\HuiTask\更好的原神\地图匹配\地图点位\Json_Integration\锚点&神像\");
        var map = new Mat(@"E:\HuiTask\更好的原神\地图匹配\genshin_map_带标记.png");
        DrawTeleportPoint(map, pList.ToArray());
        Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\genshin_map_带标记_传送点.png", map);
    }

    public static void DrawTeleportPoint(Mat map, Point[] points)
    {
        foreach (var point in points)
        {
            Cv2.Circle(map, new Point(point.X, point.Y), 10, Scalar.Red, 2);
        }
    }

    public static List<Point> LoadTeleportPoint(string folderPath)
    {
        string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
        List<Point> points = new();
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
    public static Point Transform(decimal[] position)
    {
        // 四舍六入五取偶
        var a = (int)Math.Round(position[0]); // 上
        var c = (int)Math.Round(position[2]); // 左

        // 转换1024区块坐标，大地图坐标系正轴是往左上方向的
        // 这里写最左上角的区块坐标(m,n)/(上,左),截止4.5版本，最左上角的区块坐标是(5,7)

        int m = 5, n = 7;
        return new Point((n + 1) * 1024 - c, (m + 1) * 1024 - a);
    }

    class GamePoint
    {
        public string Description { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal[] Position { get; set; } = new decimal[3];
    }
}
