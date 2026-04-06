using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Service;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.Common.Element.Assets;

public class MapAssets : BaseAssets<MapAssets>
{
    public Rect MimiMapRect { get; }
    
    public static Rect MimiMapRect1080P =  new Rect(62, 19,212,212);


    public MapAssets()
    {
        MimiMapRect = new Rect((int)Math.Round(62 * AssetScale), (int)Math.Round(19 * AssetScale), (int)Math.Round(212 * AssetScale), (int)Math.Round(212 * AssetScale));
    }
}
