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

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

using static MiniMapMatchConfig;
public class BaseMapLayerByTemplateMatch
{
    public string LayerGroupId { get; set; } = string.Empty;
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
        SpeedTimer speedTimer = new($"加载 {LayerGroupId} 地图图片");
        var colorMapFileName = "color_" + LayerGroupId + ".webp";
        var colorMapPath = Path.Combine(layerDir, colorMapFileName);
        var coarseColorMap = Cv2.ImRead(colorMapPath)?? throw new Exception($"彩色分层地图 {LayerGroupId} 读取失败");
        speedTimer.Record("精确匹配用彩图");
        CoarseColorMatcher = new FastSqDiffMatcher(coarseColorMap, new Size(52, 52));
        var grayMapFileName = "gray_" + LayerGroupId + (IsOverSize ? ".png" : ".webp");
        var grayMapPath = Path.Combine(layerDir, grayMapFileName);
        FineGrayMap = Cv2.ImRead(grayMapPath, ImreadModes.Grayscale)?? throw new Exception($"灰度分层地图 {LayerGroupId} 读取失败");
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
        var jsonFiles = Directory.GetFiles(layerDir, "*.json", SearchOption.AllDirectories);
        foreach (var jsonFile in jsonFiles)
        {
            var json = File.ReadAllText(jsonFile);
            var tempLayers = JsonSerializer.Deserialize<List<BaseMapLayerByTemplateMatch>>(json) ?? throw new Exception("Failed to deserialize JSON.");
            layers.AddRange(tempLayers);
        }
        foreach (var layer in layers)
        {
            layer.LoadLayer(layerDir);
        }
        return layers;
    }
    
    public (Point2f, double) RoughMatch(Mat[] maskedMiniMaps, Mat maskF)
    {
        var (pos, val) = CoarseColorMatcher.Match(maskedMiniMaps, maskF);
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
            return (new Point2f(0, 0), -1);
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
        return new Point((int)Math.Round((pos.X / GlobalScale - Left) * Scale / zoom), (int)Math.Round((pos.Y / GlobalScale - Top) * Scale / zoom));
    }
    private Point2f MapToWorld(Point2f pos, float zoom, int miniMapSize)
    {
        return new Point2f(GlobalScale * ((pos.X + miniMapSize / 2.0f) * zoom / Scale + Left), GlobalScale * ( (pos.Y + miniMapSize / 2.0f ) * zoom / Scale + Top));
    }
}