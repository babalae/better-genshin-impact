using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace BetterGenshinImpact.View.Controls;

internal sealed record RouteMapBackground(
    BitmapSource Bitmap,
    double LogicalWidth,
    double LogicalHeight);

internal static class RouteMapBackgroundLoader
{
    private static readonly ConcurrentDictionary<string, RouteMapBackground?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static RouteMapBackground? Load(string mapName)
    {
        var normalized = RouteGraphGeometry.NormalizeMapName(mapName);
        return Cache.GetOrAdd(normalized, LoadCore);
    }

    private static RouteMapBackground? LoadCore(string mapName)
    {
        if (!RouteMapGeometryCatalog.TryGet(mapName, out var geometry))
        {
            return null;
        }

        var recorderFileName = mapName switch
        {
            nameof(MapTypes.Teyvat) => "1024_map.jpg",
            nameof(MapTypes.TheChasm) => "thechasm_1024_map.jpg",
            nameof(MapTypes.Enkanomiya) => "enkanomiya_1024_map.jpg",
            nameof(MapTypes.SeaOfBygoneEras) => "seaofbygoneeras_1024_map.jpg",
            nameof(MapTypes.AncientSacredMountain) => "ancientsacredmountain_1024.jpg",
            nameof(MapTypes.TempleOfSpace) => "templeofspace_1024.jpg",
            _ => string.Empty
        };
        var fallbackRelativePath = mapName switch
        {
            nameof(MapTypes.Teyvat) => @"Assets\Map\Teyvat\Teyvat_0_256.png",
            nameof(MapTypes.TheChasm) => @"Assets\Map\TheChasm\TheChasm_0_1024.png",
            nameof(MapTypes.Enkanomiya) => @"Assets\Map\Enkanomiya\Enkanomiya_0_1024.png",
            nameof(MapTypes.SeaOfBygoneEras) => @"Assets\Map\SeaOfBygoneEras\SeaOfBygoneEras_0_1024.png",
            nameof(MapTypes.AncientSacredMountain) => @"Assets\Map\AncientSacredMountain\AncientSacredMountain_0_1024.png",
            nameof(MapTypes.TempleOfSpace) => @"Assets\Map\TempleOfSpace\TempleOfSpace_0_1024.png",
            _ => string.Empty
        };
        var candidates = new[]
        {
            Global.Absolute(Path.Combine("Assets", "Map", "Editor", recorderFileName)),
            Global.Absolute(Path.Combine("Assets", "Map", "Tracker", recorderFileName)),
            string.IsNullOrWhiteSpace(fallbackRelativePath) ? string.Empty : Global.Absolute(fallbackRelativePath),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "..",
                "bettergi-map", "public", recorderFileName))
        };
        var path = candidates.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
        if (path == null)
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelHeight = mapName == nameof(MapTypes.Teyvat) ? 4096 : 2048;
            image.EndInit();
            image.Freeze();
            return new RouteMapBackground(image, geometry.ImageWidth, geometry.ImageHeight);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }
}
