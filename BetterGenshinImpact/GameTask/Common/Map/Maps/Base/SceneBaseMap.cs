using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

/// <summary>
/// 独立地图
/// </summary>
public abstract class SceneBaseMap : ISceneMap
{
    public MapTypes Type { get; set; }

    /// <summary>
    /// 地图大小
    /// 当前只用于切割特征点
    /// </summary>
    public Size MapSize { get; set; }

    /// <summary>
    /// 地图原点位置 (在图像坐标系中)
    /// </summary>
    public Point2f MapOriginInImageCoordinate { get; set; }

    /// <summary>
    /// 特征地图图像的块大小
    /// 2048 或者 1024
    /// </summary>
    public Point2f MapImageBlockWidth { get; set; }

    /// <summary>
    /// 特征点拆分行数
    /// </summary>
    public readonly int SplitRow;

    /// <summary>
    /// 特征点拆分列数
    /// </summary>
    public readonly int SplitCol;

    /// <summary>
    /// 特征地图图像的块大小 / 1024 的值，用于坐标系转换
    /// </summary>
    private readonly float _mapImageBlockWidthScale = 0;


    // ReSharper disable once ConvertToPrimaryConstructor
    protected SceneBaseMap(MapTypes type, Size mapSize, Point2f mapOriginInImageCoordinate, int mapImageBlockWidth, int splitRow, int splitCol)
    {
        Type = type;
        MapSize = mapSize;
        MapOriginInImageCoordinate = mapOriginInImageCoordinate;
        _mapImageBlockWidthScale = mapImageBlockWidth / 1024f;
        SplitRow = splitRow;
        SplitCol = splitCol;
    }

    /// <summary>
    /// 分层地图特征列表
    /// 0 是主地图
    /// </summary>
    public List<BaseMapLayer> Layers { get; set; } = [];

    protected BaseMapLayer MainLayer => Layers[0];

    protected readonly Feature2D SiftMatcher = Feature2DFactory.Get(Feature2DType.SIFT);

    protected void ExtractAndSaveFeature(string basePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(basePath);
        var folder = Path.GetDirectoryName(basePath)!;

        string trainKeyPointsPath = Path.Combine(folder, $"{fileName}_SIFT.kp.bin");
        string trainDescriptorsPath = Path.Combine(folder, $"{fileName}_SIFT.mat.png");

        if (File.Exists(trainKeyPointsPath) && File.Exists(trainDescriptorsPath))
        {
            return;
        }

        SiftMatcher.SaveFeatures(basePath, trainKeyPointsPath, trainDescriptorsPath);
    }

    public virtual Point2f GetBigMapPosition(Mat greyBigMapMat)
    {
        return SiftMatcher.Match(MainLayer.TrainKeyPoints, MainLayer.TrainDescriptors, greyBigMapMat);
    }

    public virtual Rect GetBigMapRect(Mat greyBigMapMat)
    {
        return SiftMatcher.KnnMatchRect(MainLayer.TrainKeyPoints, MainLayer.TrainDescriptors, greyBigMapMat);
    }

    public virtual Point2f GetMiniMapPosition(Mat greyMiniMapMat)
    {
        // 从表到里逐层匹配
        foreach (var layer in Layers)
        {
            try
            {
                var result = SiftMatcher.KnnMatch(layer.TrainKeyPoints, layer.TrainDescriptors, greyMiniMapMat);
                if (result != default)
                {
                    return result;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"地图{Type}层数{layer.Floor},特征匹配失败:{e.Message}");
            }
        }

        return default;
    }

    public virtual Point2f GetMiniMapPosition(Mat greyMiniMapMat, float prevX, float prevY)
    {
        if (prevX <= 0 && prevY <= 0)
        {
            return GetMiniMapPosition(greyMiniMapMat);
        }

        foreach (var layer in Layers)
        {
            try
            {
                var (keyPoints, descriptors) = (layer.TrainKeyPoints, layer.TrainDescriptors);
                if (SplitRow > 0 || SplitCol > 0)
                {
                    (keyPoints, descriptors) = layer.ChooseBlocks(prevX, prevY);
                }

                var result = SiftMatcher.KnnMatch(keyPoints, descriptors, greyMiniMapMat, null, DescriptorMatcherType.BruteForce);
                if (result != default)
                {
                    return result;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"地图{Type}层数{layer.Floor},特征匹配失败:{e.Message}");
            }
        }

        return default;
    }

    #region 坐标系转换

    public Point2f ConvertImageCoordinatesToGenshinMapCoordinates(Point2f imageCoordinates)
    {
        // 原神坐标系是 1024 级别的，当图像坐标系不是 1024 级别的时候要做转换
        return new Point2f((MapOriginInImageCoordinate.X - imageCoordinates.X) / _mapImageBlockWidthScale,
            (MapOriginInImageCoordinate.Y - imageCoordinates.Y) / _mapImageBlockWidthScale);
    }

    public (float x, float y) ConvertImageCoordinatesToGenshinMapCoordinates(float x, float y)
    {
        return new((MapOriginInImageCoordinate.X - x) / _mapImageBlockWidthScale,
            (MapOriginInImageCoordinate.Y - y) / _mapImageBlockWidthScale);
    }

    public Rect ConvertImageCoordinatesToGenshinMapCoordinates(Rect rect)
    {
        var center = rect.GetCenterPoint();
        var (x, y) = ConvertImageCoordinatesToGenshinMapCoordinates(center.X, center.Y);
        return new Rect((int)(x - rect.Width / 2f / _mapImageBlockWidthScale), (int)(y - rect.Height / 2f / _mapImageBlockWidthScale),
            (int)(rect.Width / _mapImageBlockWidthScale), (int)(rect.Height / _mapImageBlockWidthScale));
    }

    public Point2f ConvertGenshinMapCoordinatesToImageCoordinates(Point2f genshinMapCoordinates)
    {
        return new Point2f(MapOriginInImageCoordinate.X - genshinMapCoordinates.X * _mapImageBlockWidthScale,
            MapOriginInImageCoordinate.Y - genshinMapCoordinates.Y * _mapImageBlockWidthScale);
    }

    public (float x, float y) ConvertGenshinMapCoordinatesToImageCoordinates(float c, float a)
    {
        return new(MapOriginInImageCoordinate.X - c * _mapImageBlockWidthScale,
            MapOriginInImageCoordinate.Y - a * _mapImageBlockWidthScale);
    }

    public Rect ConvertGenshinMapCoordinatesToImageCoordinates(Rect rect)
    {
        var center = rect.GetCenterPoint();
        var (x, y) = ConvertGenshinMapCoordinatesToImageCoordinates(center.X, center.Y);
        return new Rect((int)Math.Round(x - rect.Width / 2f * _mapImageBlockWidthScale),
            (int)Math.Round(y - rect.Height / 2f * _mapImageBlockWidthScale),
            (int)Math.Round(rect.Width * _mapImageBlockWidthScale),
            (int)Math.Round(rect.Height * _mapImageBlockWidthScale));
    }

    #endregion
}