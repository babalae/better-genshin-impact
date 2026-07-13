using BetterGenshinImpact.GameTask.Model.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;

namespace BetterGenshinImpact.GameTask.Common.Element.Assets;

public sealed class MapAssets
{
    private static readonly CaptureAssetsCache<MapAssets> Cache = new(static size => new MapAssets(size));

    public Rect MimiMapRect { get; }
    
    public static Rect MimiMapRect1080P =  new Rect(62, 19,212,212);


    private MapAssets(CaptureSize captureSize)
    {
        MimiMapRect = new Rect(
            (int)Math.Round(62 * captureSize.AssetScale),
            (int)Math.Round(19 * captureSize.AssetScale),
            (int)Math.Round(212 * captureSize.AssetScale),
            (int)Math.Round(212 * captureSize.AssetScale));
    }

    public static MapAssets Get(Region region)
    {
        return Cache.Get(region);
    }

    public static MapAssets Get(int captureWidth, int captureHeight)
    {
        return Cache.Get(captureWidth, captureHeight);
    }
}
