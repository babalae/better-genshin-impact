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
    
    public MatchResult PrevSuccessResult;
    
    public struct MatchResult
    {
        public BaseMapLayerByTemplateMatch? Layer = null; // 地图信息
        public Point2f MapPos = new Point2f(0, 0);        // 匹配位置
        public double Confidence = 0;

        public readonly bool IsSuccess(int rank)
        {
            var index = Math.Clamp(rank, 0, ConfidenceThresholds.Length - 1);
            return Confidence <= 1 && Confidence >= ConfidenceThresholds[index];
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
        var result= new MatchResult();
        var (miniMap, mask) = _miniMapPreprocessor.GetMiniMapAndMask(colorMiniMapMat);
        using (miniMap)
        using (mask)
        {
            GlobalMatch(miniMap, mask, ref result);
            return UpdateResult(result, 2);
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
        return GetMiniMapPosition(colorMiniMapMat, prevX, prevY, 2);
    }

    public Point2f GetMiniMapPosition(Mat colorMiniMapMat, float prevX, float prevY, int rank)
    {
        if (prevX <= 0 || prevY <= 0)
        {
            return GetMiniMapPosition(colorMiniMapMat);
        }
        var curResult = new MatchResult();
        var (miniMap, mask) = _miniMapPreprocessor.GetMiniMapAndMask(colorMiniMapMat);
        using (miniMap)
        using (mask)
        {
            LocalMatch(miniMap, mask, ConvertImageCoordinatesToGenshinMapCoordinates(new Point2f(prevX, prevY)), ref curResult);
            return UpdateResult(curResult, rank);
        }
    }
    
    private Point2f UpdateResult(in MatchResult result, int rank)
    {
        if (!result.IsSuccess(rank)) return default;
        PrevSuccessResult = result;
        return ConvertGenshinMapCoordinatesToImageCoordinates(PrevSuccessResult.MapPos);
    }
    
    [Conditional("DEBUG")]
    private static void LogMatchResult(string stage, in MatchResult result)
    {
        Debug.WriteLine($"{stage}: 坐标 ({result.MapPos.X:F4}, {result.MapPos.Y:F4}), 置信度 {result.Confidence:F4}");
    }

    #region 模板匹配
    
    public void GlobalMatch(Mat miniMap, Mat mask, ref MatchResult result)
    {
        SpeedTimer speedTimer = new SpeedTimer("全局匹配");
        using var context = new MatchContext(miniMap, mask);
        RoughMatchGlobal(context, ref result);
        speedTimer.Record("全局粗匹配"); 
        ExactMatch(context, ref result);
        speedTimer.Record("精确匹配");
        speedTimer.DebugPrint();
    }

    // 局部匹配：在上一次匹配位置附近进行搜索
    public void LocalMatch(Mat miniMap, Mat mask, Point2f pos, ref MatchResult result)
    {
        SpeedTimer speedTimer = new SpeedTimer("局部匹配");
        using var context = new MatchContext(miniMap, mask);
        RoughMatchLocal(context, pos, ref result);
        speedTimer.Record("局部粗匹配");
        ExactMatch(context, ref result);
        speedTimer.Record("精确匹配");
        speedTimer.DebugPrint();
    }
    
    public void RoughMatchGlobal(MatchContext context, ref MatchResult result)
    {
        foreach (var layer in Layers)
        {
            var (tempPos, tempVal) = layer.RoughMatch(context.MaskedMiniMapRoughs, context.MaskRoughF);
            if (!context.NormalizerRough.Update(tempVal + context.TplSumSq)) continue;
            result.Layer = layer;
            result.MapPos = tempPos;
        }
        result.Confidence = context.NormalizerRough.Confidence();
        LogMatchResult("全局粗匹配", result);
    }
    
    public void RoughMatchLocal(MatchContext context, Point2f pos, ref MatchResult result)
    {
        result.MapPos = pos;
        if (PrevSuccessResult.MapPos.Equals(pos))
        {
            result.Layer = PrevSuccessResult.Layer;
        }
        if (result.Layer != null)
        {
            var (tempPos, tempVal) = result.Layer.RoughMatch(context.MaskedMiniMapRoughs, context.MaskRoughF, pos);
            if (tempPos != default && context.NormalizerRough.Update(tempVal + context.TplSumSq))
            {
                result.MapPos = tempPos;
                result.Confidence = context.NormalizerRough.Confidence();
            }
        }

        if (result.IsSuccess(0))
        {
            LogMatchResult("局部粗匹配", result);
            return;
        }
        
        var flag = false;
        foreach (var layer in (result.Layer == null)? Layers : Layers.Where(layer => layer != PrevSuccessResult.Layer))
        {
            var (tempPos, tempVal) = layer.RoughMatch(context.MaskedMiniMapRoughs, context.MaskRoughF, pos);
            if (tempPos == default || !context.NormalizerRough.Update(tempVal + context.TplSumSq)) continue;
            result.Layer = layer;
            result.MapPos = tempPos;
            flag = true;
        }
        if (flag) result.Confidence = context.NormalizerRough.Confidence();
        LogMatchResult("局部粗匹配", result);
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
    /// <param name="result"></param>
    public void RoughMatchLocalChan(MatchContext context, Point2f pos, ref MatchResult result)
    {
        result = default;
        var flag = false;
        foreach (var layer in Layers)
        {
            var (tempPos, tempVal) = layer.RoughMatch(context.MaskedMiniMapRoughs, context.MaskRoughF, pos, context.Channels);
            if (!context.NormalizerRoughChan.Update(tempVal + context.TplSumSqChan)) continue;
            result.Layer = layer;
            result.MapPos = tempPos;
            flag = true;
        }
        if (flag) result.Confidence = context.NormalizerRough.Confidence();
    }
    
    public void ExactMatch(MatchContext context, ref MatchResult result)
    {
        if (result.Layer == null || !result.IsSuccess(2)) return;
        var (tempPos, tempVal) = result.Layer.ExactMatch(context.MiniMapExact, context.MaskExact, result.MapPos);
        if (tempPos != default && context.NormalizerExact.Update(tempVal))
        {
            result.MapPos = tempPos;
            result.Confidence = context.NormalizerExact.Confidence();
        }
        else
        {
            result.Confidence = 0;
        }
        LogMatchResult("精确匹配", result);
    }
    #endregion
    
}