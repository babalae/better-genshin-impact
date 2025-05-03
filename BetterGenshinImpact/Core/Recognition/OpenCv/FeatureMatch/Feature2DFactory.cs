using System;
using System.Collections.Generic;
using OpenCvSharp;
using OpenCvSharp.Features2D;
using OpenCvSharp.XFeatures2D;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;

public class Feature2DFactory
{
    private static readonly Dictionary<Feature2DType, Feature2D> Instances = new();
    private static readonly object Lock = new();
    
    public static Feature2D Get(Feature2DType type)
    {

        lock (Lock)
        {
            
            if (Instances.TryGetValue(type, out var instance))
            {
                return instance;
            }

            instance = type switch
            {
                Feature2DType.SIFT => SIFT.Create(),
                Feature2DType.SURF => SURF.Create(100, 4, 3, false, true),
                _ => throw new ArgumentException($"不支持的特征检测器类型: {type}")
            };

            Instances[type] = instance;
            return instance;
        }
    }
}