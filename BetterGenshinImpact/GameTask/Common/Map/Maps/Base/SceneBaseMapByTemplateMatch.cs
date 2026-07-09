using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;
using OpenCvSharp;
using BetterGenshinImpact.GameTask.Common.Map.MiniMap;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

using static MiniMapMatchConfig;

public abstract class SceneBaseMapByTemplateMatch : SceneBaseMap
{
    private readonly MiniMapPreprocessor _miniMapPreprocessor = new();
    
    private List<BaseMapLayerByTemplateMatch> _layers = [];
    private readonly object _layersLock = new();
    private readonly Dictionary<string, MatchResult> _prevSuccessResultsBySelectorKey = new();

    public new List<BaseMapLayerByTemplateMatch> Layers
    {
        get
        {
            if (_layers.Count == 0)
            {
                lock (_layersLock)
                {
                    if (_layers.Count == 0)
                    {
                        TaskControl.Logger.LogInformation("[TemplateMatch]提瓦特大陆地图模板加载中，可能耗时较久，请耐心等待...");
                        Layers = BaseMapLayerByTemplateMatch.LoadLayers(this);
                        TaskControl.Logger.LogInformation("地图特征点加载完成！");
                    }
                }
            }
            return _layers;
        }
        set => _layers = value ?? [];
    }
    
    public MatchResult PrevSuccessResult
    {
        get => GetPrevSuccessResult(MapLayerSelector.Empty);
        set => SetPrevSuccessResult(MapLayerSelector.Empty, value);
    }
    
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
    
    public override void WarmUp()
    {
        Console.WriteLine("提前加载地图，层数：" + Layers.Count);
    }
    
    public override Point2f GetMiniMapPosition(Mat colorMiniMapMat)
    {
        return GetMiniMapPosition(colorMiniMapMat, MapLayerSelector.Empty);
    }

    public Point2f GetMiniMapPosition(Mat colorMiniMapMat, MapLayerSelector? selector)
    {
        var result = new MatchResult();
        var (miniMap, mask) = _miniMapPreprocessor.GetMiniMapAndMask(colorMiniMapMat);
        using (miniMap)
        using (mask)
        {
            GlobalMatch(miniMap, mask, ref result, selector);
            return UpdateResult(result, 2, selector);
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
        return GetMiniMapPosition(colorMiniMapMat, prevX, prevY, 2, MapLayerSelector.Empty);
    }

    public Point2f GetMiniMapPosition(Mat colorMiniMapMat, float prevX, float prevY, MapLayerSelector? selector)
    {
        return GetMiniMapPosition(colorMiniMapMat, prevX, prevY, 2, selector);
    }

    public Point2f GetMiniMapPosition(Mat colorMiniMapMat, float prevX, float prevY, int rank)
    {
        return GetMiniMapPosition(colorMiniMapMat, prevX, prevY, rank, MapLayerSelector.Empty);
    }

    public Point2f GetMiniMapPosition(Mat colorMiniMapMat, float prevX, float prevY, int rank, MapLayerSelector? selector)
    {
        if (prevX <= 0 || prevY <= 0)
        {
            return GetMiniMapPosition(colorMiniMapMat, selector);
        }
        var curResult = new MatchResult();
        var (miniMap, mask) = _miniMapPreprocessor.GetMiniMapAndMask(colorMiniMapMat);
        using (miniMap)
        using (mask)
        {
            LocalMatch(miniMap, mask, ConvertImageCoordinatesToGenshinMapCoordinates(new Point2f(prevX, prevY))!.Value, ref curResult, selector);
            return UpdateResult(curResult, rank, selector);
        }
    }
    
    private Point2f UpdateResult(in MatchResult result, int rank, MapLayerSelector? selector)
    {
        if (!result.IsSuccess(rank)) return default;
        SetPrevSuccessResult(selector, result);
        return ConvertGenshinMapCoordinatesToImageCoordinates(result.MapPos);
    }
    
    [Conditional("DEBUG")]
    private static void LogMatchResult(string stage, in MatchResult result)
    {
        Debug.WriteLine($"{stage}: 坐标 ({result.MapPos.X:F4}, {result.MapPos.Y:F4}), 置信度 {result.Confidence:F4}");
    }

    #region 模板匹配
    
    public void GlobalMatch(Mat miniMap, Mat mask, ref MatchResult result)
    {
        GlobalMatch(miniMap, mask, ref result, MapLayerSelector.Empty);
    }

    public void GlobalMatch(Mat miniMap, Mat mask, ref MatchResult result, MapLayerSelector? selector)
    {
        var normalizedSelector = selector ?? MapLayerSelector.Empty;
        var candidateLayers = GetCandidateLayers(normalizedSelector);

        SpeedTimer speedTimer = new SpeedTimer("全局匹配");
        using (var context = new MatchContext(miniMap, mask))
        {
            RoughMatchGlobal(context, ref result, candidateLayers);
            speedTimer.Record("全局粗匹配"); 
            ExactMatch(context, ref result);
            speedTimer.Record("精确匹配");
        }

        if (ShouldPreferFallback(normalizedSelector, candidateLayers, result, 2))
        {
            TaskControl.Logger.LogWarning("地图图层 prefer 选择器 {Selector} 未命中，回退到旧版全图层匹配", normalizedSelector.StateKey);
            result = new MatchResult();
            using var fallbackContext = new MatchContext(miniMap, mask);
            RoughMatchGlobal(fallbackContext, ref result, Layers);
            ExactMatch(fallbackContext, ref result);
        }

        speedTimer.DebugPrint();
    }

    // 局部匹配：在上一次匹配位置附近进行搜索
    public void LocalMatch(Mat miniMap, Mat mask, Point2f pos, ref MatchResult result)
    {
        LocalMatch(miniMap, mask, pos, ref result, MapLayerSelector.Empty);
    }

    public void LocalMatch(Mat miniMap, Mat mask, Point2f pos, ref MatchResult result, MapLayerSelector? selector)
    {
        var normalizedSelector = selector ?? MapLayerSelector.Empty;
        var candidateLayers = GetCandidateLayers(normalizedSelector);

        SpeedTimer speedTimer = new SpeedTimer("局部匹配");
        using (var context = new MatchContext(miniMap, mask))
        {
            RoughMatchLocal(context, pos, ref result, GetPrevSuccessResult(normalizedSelector), candidateLayers);
            speedTimer.Record("局部粗匹配");
            ExactMatch(context, ref result);
            speedTimer.Record("精确匹配");
        }

        if (ShouldPreferFallback(normalizedSelector, candidateLayers, result, 2))
        {
            TaskControl.Logger.LogWarning("地图图层 prefer 选择器 {Selector} 局部匹配未命中，回退到旧版全图层匹配", normalizedSelector.StateKey);
            result = new MatchResult();
            using var fallbackContext = new MatchContext(miniMap, mask);
            RoughMatchLocal(fallbackContext, pos, ref result, default, Layers);
            ExactMatch(fallbackContext, ref result);
        }

        speedTimer.DebugPrint();
    }
    
    public void RoughMatchGlobal(MatchContext context, ref MatchResult result)
    {
        RoughMatchGlobal(context, ref result, Layers);
    }

    private static void RoughMatchGlobal(MatchContext context, ref MatchResult result, IReadOnlyList<BaseMapLayerByTemplateMatch> candidateLayers)
    {
        foreach (var layer in candidateLayers)
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
        RoughMatchLocal(context, pos, ref result, GetPrevSuccessResult(MapLayerSelector.Empty), Layers);
    }

    private static void RoughMatchLocal(
        MatchContext context,
        Point2f pos,
        ref MatchResult result,
        MatchResult prevSuccessResult,
        IReadOnlyList<BaseMapLayerByTemplateMatch> candidateLayers)
    {
        result.MapPos = pos;
        if (prevSuccessResult.MapPos.Equals(pos) && prevSuccessResult.Layer != null && candidateLayers.Contains(prevSuccessResult.Layer))
        {
            result.Layer = prevSuccessResult.Layer;
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
        var previousLayer = result.Layer;
        foreach (var layer in candidateLayers)
        {
            if (previousLayer != null && layer == previousLayer) continue;
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
        RoughMatchLocalChan(context, pos, ref result, Layers);
    }

    private static void RoughMatchLocalChan(
        MatchContext context,
        Point2f pos,
        ref MatchResult result,
        IReadOnlyList<BaseMapLayerByTemplateMatch> candidateLayers)
    {
        result = default;
        var flag = false;
        foreach (var layer in candidateLayers)
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

    private IReadOnlyList<BaseMapLayerByTemplateMatch> GetCandidateLayers(MapLayerSelector selector)
    {
        var candidateLayers = MapLayerSelector.FilterAndOrderLayers(Layers, selector);
        if (candidateLayers.Count == 0 && selector.IsRequire)
        {
            TaskControl.Logger.LogWarning("地图图层 require 选择器 {Selector} 没有匹配的候选图层，停止回退旧版全图层匹配", selector.StateKey);
        }

        return candidateLayers;
    }

    private static bool ShouldPreferFallback(
        MapLayerSelector selector,
        IReadOnlyList<BaseMapLayerByTemplateMatch> candidateLayers,
        MatchResult result,
        int rank)
    {
        return selector.IsPrefer && (candidateLayers.Count == 0 || !result.IsSuccess(rank));
    }

    private MatchResult GetPrevSuccessResult(MapLayerSelector? selector)
    {
        var key = MapManager.GetSelectorStateKey(selector);
        return _prevSuccessResultsBySelectorKey.TryGetValue(key, out var result) ? result : default;
    }

    private void SetPrevSuccessResult(MapLayerSelector? selector, MatchResult result)
    {
        _prevSuccessResultsBySelectorKey[MapManager.GetSelectorStateKey(selector)] = result;
    }
}
