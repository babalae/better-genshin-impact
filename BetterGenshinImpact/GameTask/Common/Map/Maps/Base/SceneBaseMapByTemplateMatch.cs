using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;
using OpenCvSharp;
using BetterGenshinImpact.GameTask.Common.Map.MiniMap;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

using static MiniMapMatchConfig;

public abstract class SceneBaseMapByTemplateMatch : SceneBaseMap
{
    private readonly MiniMapPreprocessor _miniMapPreprocessor = new();
    
    public new List<BaseMapLayerByTemplateMatch> Layers { get; set; } = [];
    
    public MatchResult CurResult;
    
    public struct MatchResult
    {
        public BaseMapLayerByTemplateMatch? Layer = null; // 地图信息
        public Point2f MapPos = new Point2f(0, 0);        // 匹配位置
        public double Confidence = 0;

        public bool IsSuccess(int rank)
        {
            var r = Math.Clamp(rank, 0, ConfidenceThresholds.Length);
            return Confidence <= 1 && Confidence >= ConfidenceThresholds[r];
        }
        public MatchResult() {}
    }
    
    protected SceneBaseMapByTemplateMatch(
        MapTypes type, 
        Size mapSize, 
        Point2f mapOriginInImageCoordinate, 
        int mapImageBlockWidth, 
        int splitRow, 
        int splitCol)
        : base(type, mapSize, mapOriginInImageCoordinate, mapImageBlockWidth, splitRow, splitCol)
    {
    }

    protected void SetBaseLayers(List<BaseMapLayer> layers)
    {
        base.Layers = layers;
    }
    
    public override Point2f GetMiniMapPosition(Mat colorMiniMapMat)
    {
        var (miniMap, mask) = _miniMapPreprocessor.GetMiniMapAndMask(colorMiniMapMat);
        using (miniMap)
        using (mask)
        {
            GlobalMatch(miniMap, mask);
            Debug.WriteLine($"全局匹配, 坐标 {CurResult.MapPos}, 置信度 {CurResult.Confidence}");
            return CurResult.IsSuccess(2) ? ConvertGenshinMapCoordinatesToImageCoordinates(CurResult.MapPos) : default;
        }
    }

    /// <summary>
    /// 小地图局部匹配，失败不进行全局匹配，若需要全局请用全局匹配
    /// </summary>
    /// <param name="colorMiniMapMat"></param>
    /// <param name="prevX"></param>
    /// <param name="prevY"></param>
    /// <returns></returns>
    public override Point2f GetMiniMapPosition(Mat colorMiniMapMat, float prevX, float prevY)
    {
        if (prevX <= 0 || prevY <= 0)
        {
            return GetMiniMapPosition(colorMiniMapMat);
        }
        var (miniMap, mask) = _miniMapPreprocessor.GetMiniMapAndMask(colorMiniMapMat);
        using (miniMap)
        using (mask)
        {
            LocalMatch(miniMap, mask, ConvertImageCoordinatesToGenshinMapCoordinates(new Point2f(prevX, prevY)));
            Debug.WriteLine($"局部匹配, 坐标 {CurResult.MapPos}, 置信度 {CurResult.Confidence}");
            return CurResult.IsSuccess(2) ? ConvertGenshinMapCoordinatesToImageCoordinates(CurResult.MapPos) : default;
        }
    }
    
/*
    public SceneBaseMapByTemplateMatch FromJsonFiles(string filePath)
    {
        string json = File.ReadAllText(filePath);
        var sceneBaseMap = JsonSerializer.Deserialize<SceneBaseMapByTemplateMatch>(json) ?? throw new Exception("Failed to deserialize JSON.");
        sceneBaseMap.Type = SceneBaseMapByTemplateMatch.Type;
        return sceneBaseMap;
    }
*/    
    

    #region 模板匹配
    
    public void GlobalMatch(Mat miniMap, Mat mask)
    {
        SpeedTimer speedTimer = new SpeedTimer("全局匹配");
        using var context = new MatchContext(miniMap, mask);
        RoughMatchGlobal(context);
        speedTimer.Record("全局粗匹配");
        ExactMatch(context);
        speedTimer.Record("精确匹配");
        speedTimer.DebugPrint();
    }

    // 局部匹配：在上一次匹配位置附近进行搜索
    public void LocalMatch(Mat miniMap, Mat mask, Point2f pos)
    {
        SpeedTimer speedTimer = new SpeedTimer("局部匹配");
        using var context = new MatchContext(miniMap, mask);
        RoughMatchLocal(context, pos);
        speedTimer.Record("局部粗匹配");
        ExactMatch(context);
        speedTimer.Record("精确匹配");
        speedTimer.DebugPrint();
    }
    
    public void RoughMatchGlobal(MatchContext context)
    {
        CurResult = default;
        var flag = false;
        foreach (var layer in Layers)
        {
            var (tempPos, tempVal) = layer.RoughMatch(context.MaskedMiniMapRoughs, context.MaskRoughF);
            if (!context.NormalizerRough.Update(tempVal + context.TplSumSq)) continue;
            CurResult.Layer = layer;
            CurResult.MapPos = tempPos;
            flag = true;
        }
        if (flag)
        {
            CurResult.Confidence = context.NormalizerRough.Confidence();
            Debug.WriteLine($"粗匹配成功, 坐标 {CurResult.MapPos}, 置信度 {CurResult.Confidence}");
        }
        Debug.WriteLine($"粗匹配失败, 坐标 {CurResult.MapPos}, 置信度 {CurResult.Confidence}");
    }
    
    public void RoughMatchLocal(MatchContext context, Point2f pos)
    {
        if (!CurResult.MapPos.Equals(pos))
        {
            CurResult.Layer = null;
            CurResult.MapPos = pos;
        }
        CurResult.Confidence = 0;
        if (CurResult.Layer != null)
        {
            var (tempPos, tempVal) = CurResult.Layer.RoughMatch(context.MaskedMiniMapRoughs, context.MaskRoughF, pos);
            if (context.NormalizerRough.Update(tempVal + context.TplSumSq))
            {
                CurResult.MapPos = tempPos;
                CurResult.Confidence = context.NormalizerRough.Confidence();
            }
        }
        if (CurResult.IsSuccess(0)) return;
        
        var flag = false;
        foreach (var layer in (CurResult.Layer == null)? Layers : Layers.Where(layer => layer != CurResult.Layer))
        {
            var (tempPos, tempVal) = layer.RoughMatch(context.MaskedMiniMapRoughs, context.MaskRoughF, pos);
            if (!context.NormalizerRough.Update(tempVal + context.TplSumSq)) continue;
            CurResult.Layer = layer;
            CurResult.MapPos = tempPos;
            flag = true;
        }
        if (flag) CurResult.Confidence = context.NormalizerRough.Confidence();
        //if (CurResult.IsSuccess) return;
        //RoughMatchLocalChan(context, pos);
        //if (CurResult.IsSuccess) return;
        //RoughMatchGlobal(context);
    }

    /// <summary>
    /// 指定通道匹配，用于边缘位置匹配，暂时不用，等后续优化
    /// </summary>
    /// <param name="context"></param>
    /// <param name="pos"></param>
    public void RoughMatchLocalChan(MatchContext context, Point2f pos)
    {
        CurResult = default;
        var flag = false;
        foreach (var layer in Layers)
        {
            var (tempPos, tempVal) = layer.RoughMatch(context.MaskedMiniMapRoughs, context.MaskRoughF, pos, context.Channels);
            if (!context.NormalizerRoughChan.Update(tempVal + context.TplSumSqChan)) continue;
            CurResult.Layer = layer;
            CurResult.MapPos = tempPos;
            flag = true;
        }
        if (flag) CurResult.Confidence = context.NormalizerRough.Confidence();
    }
    
    public void ExactMatch(MatchContext context)
    {
        if (CurResult.Layer == null) return;
        if (!CurResult.IsSuccess(2)) return;
        var (tempPos, tempVal) = CurResult.Layer.ExactMatch(context.MiniMapExact, context.MaskExact, CurResult.MapPos);
        if (context.NormalizerExact.Update(tempVal))
        {
            CurResult.MapPos = tempPos;
            CurResult.Confidence = context.NormalizerExact.Confidence();
        }
        else
        {
            CurResult = default;
        }
        Debug.WriteLine($"粗匹配, 坐标 {CurResult.MapPos}, 置信度 {CurResult.Confidence}");
    }
    #endregion
    
}