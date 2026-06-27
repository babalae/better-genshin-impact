using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Model;
using OpenCvSharp;
using System;

namespace BetterGenshinImpact.GameTask.Common.Element.Assets;

public class MapAssets : Singleton<MapAssets>
{
    public Rect MimiMapRect { get; }
    
    public static Rect MimiMapRect1080P =  new Rect(62, 19,212,212);


    public MapAssets()
    {
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        MimiMapRect = new Rect((int)Math.Round(62 * assetScale), (int)Math.Round(19 * assetScale), (int)Math.Round(212 * assetScale), (int)Math.Round(212 * assetScale));
    }
}
