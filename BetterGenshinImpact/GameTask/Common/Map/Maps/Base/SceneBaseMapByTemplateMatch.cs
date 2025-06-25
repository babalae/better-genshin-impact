﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;
using OpenCvSharp;
using BetterGenshinImpact.GameTask.Common.Map.MiniMap;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

using static MiniMapMatchConfig;

public abstract class SceneBaseMapByTemplateMatch : SceneBaseMap
{
    private readonly MiniMapPreprocessor _miniMapPreprocessor = new();
    
    public new List<BaseMapLayerByTemplateMatch> Layers { get; set; } = [];
    
    public MatchResult CurResult;
    
    public struct MatchResult
    {
        private double _confidence = 0;                   // 匹配置信度
        public BaseMapLayerByTemplateMatch? Layer = null; // 地图信息
        public Point2f MapPos = new Point2f(0, 0);        // 匹配位置
        public double Confidence
        {
            get => _confidence;
            set
            {
                IsFailed = value < LowThreshold || value > 1.0;
                IsSuccess = value >= HighThreshold && value <= 1.0;
                _confidence = value;
            }
        }
        public bool IsSuccess;
        public bool IsFailed;
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
    
    public override Point2f GetMiniMapPosition(Mat colorMiniMapMat)
    {
        var (miniMap, mask) = _miniMapPreprocessor.GetMiniMapAndMask(colorMiniMapMat);
        using (miniMap)
        using (mask)
        {
            GlobalMatch(miniMap, mask);
            return CurResult.IsSuccess ? CurResult.MapPos : default;
        }
    }

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
            LocalMatch(miniMap, mask, new Point2f(prevX, prevY));
            return CurResult.IsSuccess ? CurResult.MapPos : default;
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
        using var context = new MatchContext(miniMap, mask);
        RoughMatchGlobal(context);
        ExactMatch(context);
    }

    // 局部匹配：在上一次匹配位置附近进行搜索
    public void LocalMatch(Mat miniMap, Mat mask, Point2f pos)
    {
        using var context = new MatchContext(miniMap, mask);
        RoughMatchLocal(context, pos);
        ExactMatch(context);
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
        if (CurResult.IsSuccess) return;
        
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
        
        if (CurResult.IsSuccess) return;

        RoughMatchLocalChan(context, pos);
        
        if (CurResult.IsSuccess) return;
        
        RoughMatchGlobal(context);
    }

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
        if (CurResult.IsFailed) return;
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
    }
    #endregion
    
}