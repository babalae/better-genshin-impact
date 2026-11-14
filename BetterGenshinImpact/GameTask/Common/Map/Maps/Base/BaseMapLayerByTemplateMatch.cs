using System;
using System.Collections.Generic;
//using System.Diagnostics;
using System.IO;
//using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using System.Text.Json.Serialization;
using BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;
using BetterGenshinImpact.GameTask.Common.BgiVision;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

using static MiniMapMatchConfig;
public class BaseMapLayerByTemplateMatch
{
    public string LayerGroupId { get; set; } = string.Empty;
    public string LayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float Scale { get; set; } = 1;
    public int Floor { get; set; } = 0;
    public float Top { get; set; } = 0;
    public float Left { get; set; } = 0;
    public bool IsOverSize  { get; set; } = false;
    [JsonIgnore]
    public required FastSqDiffMatcher CoarseColorMatcher; // 小尺寸彩图
    [JsonIgnore]
    public Mat FineGrayMap = new Mat(); // 大尺寸灰度图
    
    public void LoadLayer(string layerDir)
    {
        SpeedTimer speedTimer = new($"加载 {LayerId} 地图图片");
        var colorMapFileName = LayerId + "_color" + ".webp";
        var colorMapPath = Path.Combine(layerDir, colorMapFileName);
        var coarseColorMap = Bv.ImRead(colorMapPath)?? throw new Exception($"彩色分层地图 {LayerId} 读取失败");
        speedTimer.Record("精确匹配用彩图");
        CoarseColorMatcher = new FastSqDiffMatcher(coarseColorMap, new Size(52, 52));
        var grayMapFileName = LayerId + "_gray" + (IsOverSize ? ".png" : ".webp");
        var grayMapPath = Path.Combine(layerDir, grayMapFileName);
        FineGrayMap = Bv.ImRead(grayMapPath, ImreadModes.Grayscale)?? throw new Exception($"灰度分层地图 {LayerId} 读取失败");
        speedTimer.Record("粗匹配用灰度图");
        speedTimer.DebugPrint();
    }

    public static List<BaseMapLayerByTemplateMatch> LoadLayers(SceneBaseMapByTemplateMatch sceneBaseMap)
    {
        var layers = new List<BaseMapLayerByTemplateMatch>();
        var layerDir = Path.Combine(Global.Absolute(@"Assets\Map\"), sceneBaseMap.Type.ToString());
        if (!Directory.Exists(layerDir))
        {
            return layers;
        }
        // TemplateMatch 描述文件统一放在场景目录顶层；子目录可能包含 SIFT 等其他格式的 JSON 清单。
        var jsonFiles = Directory.GetFiles(layerDir, "*.json", SearchOption.TopDirectoryOnly);
        foreach (var jsonFile in jsonFiles)
        {
            var json = File.ReadAllText(jsonFile);
            var tempLayers = JsonSerializer.Deserialize<List<BaseMapLayerByTemplateMatch>>(json) ?? throw new Exception("Failed to deserialize JSON.");
            layers.AddRange(tempLayers);
        }
        // 去重：上游 layer_map_info.json 里存在 9 个 LayerId 重复出现（最多重复 5 次），
        // 且各字段完全相同。这里若不去重，同一个图层会被加载成多个独立实例，
        // 它们在**同一帧**里必然给出完全相同的粗匹配得分 —— 于是
        // 「最优 - 次优」恒为 0，永远小于唯一性阈值 MinMatchUniqueness，
        // 那些区域就全都表现为「粗匹配结果不唯一：地形重复，不敢定」，永久无法定位。
        // 只丢弃**几何完全相同**的重复项；只要 Scale/Floor/Left/Top 有一项不同就全部保留。
        var unique = new List<BaseMapLayerByTemplateMatch>(layers.Count);
        var seen = new HashSet<(string, float, int, float, float)>();
        foreach (var layer in layers)
        {
            if (!seen.Add((layer.LayerId, layer.Scale, layer.Floor, layer.Left, layer.Top)))
            {
                continue;
            }

            unique.Add(layer);
        }

        foreach (var layer in unique)
        {
            layer.LoadLayer(layerDir);
        }
        return unique;
    }
    
    public (Point2f, double) RoughMatch(Mat[] maskedMiniMaps, Mat maskF)
    {
        var (pos, val) = CoarseColorMatcher.Match(maskedMiniMaps, maskF);
        return (MapToWorld(pos, RoughZoom, RoughSize), val);
    }

    /// <summary>
    /// 全局粗匹配（归一化互相关版）。
    ///
    /// 与 <see cref="RoughMatch(Mat[], Mat)"/> 的区别只在度量：那个返回 SSD 绝对值，
    /// 跨图层不可比；这个返回归一化相关系数，值域 [-1,1]，可直接跨图层比较取最优。
    /// 详细原因见 <see cref="FastSqDiffMatcher.MatchCCoeffNormed"/>。
    ///
    /// 返回的 val 是相关系数（越大越相似），失败时为 -1。
    /// </summary>
    public (Point2f, double) RoughMatchNormalized(Mat[] maskedMiniMaps, Mat maskF)
    {
        var (pos, val) = CoarseColorMatcher.MatchCCoeffNormed(maskedMiniMaps, maskF);
        if (val < 0)
        {
            return (default, -1);
        }
        return (MapToWorld(pos, RoughZoom, RoughSize), val);
    }

    public (Point2f, double) RoughMatch(Mat[] maskedMiniMaps, Mat maskF, Point2f preLoc, int[]? channels = null)
    {
        var roughPos = WorldToMap(preLoc, RoughZoom);
        var rect = GetRect(roughPos, (int)(RoughSearchRadius * Scale), RoughSize).Intersect(new Rect(0, 0, CoarseColorMatcher.Source[0].Width, CoarseColorMatcher.Source[0].Height));
        if (rect.Width < RoughSize || rect.Height < RoughSize)
        {
            return (default, -1);
        }
        var (pos, val) = CoarseColorMatcher.Match(maskedMiniMaps, maskF, rect, channels);
        return (MapToWorld(rect.TopLeft + pos, RoughZoom, RoughSize), val);
    }
    
    // 精确匹配直接返回世界坐标
    public (Point2f, double) ExactMatch(Mat miniMap, Mat mask, Point2f preLoc, TemplateMatchModes mode = TemplateMatchModes.SqDiff)
    {
        var exactPos = WorldToMap(preLoc, ExactZoom);
        var rect = GetRect(exactPos, ExactSearchRadius, ExactSize).Intersect(new Rect(0, 0, FineGrayMap.Width, FineGrayMap.Height));
        if (rect.Width < ExactSize || rect.Height < ExactSize)
        {
            return (default, -1);
        }
        var bigMap = FineGrayMap[rect];
        var (pos, val) = TemplateMatchHelper.MatchTemplateSubPix(bigMap, miniMap, mode, mask);
        return (MapToWorld( rect.TopLeft + pos, ExactZoom, ExactSize), val);
    }

    private static Rect GetRect(Point loc, int halfSide, int miniMapSize)
    {
        return new Rect(loc.X - halfSide - miniMapSize / 2, loc.Y - halfSide - miniMapSize / 2, halfSide * 2 + miniMapSize, halfSide * 2 + miniMapSize);
    }

    private Point WorldToMap(Point2f pos, float zoom)
    {
        return new Point((int)Math.Round((Left - pos.X) * Scale / zoom), (int)Math.Round((Top - pos.Y) * Scale / zoom));
    }
    private Point2f MapToWorld(Point2f pos, float zoom, int miniMapSize)
    {
        return new Point2f(Left - (pos.X + miniMapSize / 2.0f) * zoom / Scale ,  Top - (pos.Y + miniMapSize / 2.0f ) * zoom / Scale);
    }
}
