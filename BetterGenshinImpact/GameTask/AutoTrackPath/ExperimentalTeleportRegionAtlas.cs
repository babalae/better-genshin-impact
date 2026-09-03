using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 从单一图集中匹配实验传送的地区菜单项。
/// </summary>
internal sealed class ExperimentalTeleportRegionAtlas : IDisposable
{
    private const string AssetName = "ExperimentalTeleportRegions.png";
    private const int Columns = 2;
    private const int Rows = 8;
    private const double GridStartX = 1326d;
    private const double GridStartY = 120d;
    private const double GridStepX = 300d;
    private const double GridStepY = 105d;
    private const double GridValidationTolerance = 75d;
    private const int MenuOpenTimeoutMilliseconds = 1000;
    private const int MenuOpenPollIntervalMilliseconds = 80;

    private static readonly IReadOnlyDictionary<string, int> AreaIndexes =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["蒙德"] = 0,
            ["璃月"] = 1,
            ["稻妻"] = 2,
            ["须弥"] = 3,
            ["枫丹"] = 4,
            ["纳塔"] = 5,
            ["挪德卡莱"] = 6,
            ["至冬"] = 7,
            ["层岩巨渊"] = 8,
            ["渊下宫"] = 9,
            ["旧日之海"] = 10,
            ["远古圣山"] = 11,
            ["空之神殿"] = 12,
            ["霜月"] = 13,
            ["尘歌壶"] = 14,
            ["千星奇域"] = 15,
        };

    private readonly TpConfig _config;
    private readonly CancellationToken _ct;
    private readonly Mat? _atlas;

    public ExperimentalTeleportRegionAtlas(TpConfig config, CancellationToken ct)
    {
        _config = config;
        _ct = ct;
        var rect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        try
        {
            _atlas = GameTaskManager.LoadAssetImage("QuickTeleport", AssetName, rect.Width, rect.Height);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "实验传送地区图集加载失败，将使用 OCR 切换地区");
        }
    }

    public async Task<bool> TrySwitchAsync(string areaName)
    {
        if (_atlas == null || !AreaIndexes.TryGetValue(areaName, out var areaIndex))
        {
            return false;
        }

        GameCaptureRegion.GameRegionClick((rect, scale) => (rect.Width - 160 * scale, rect.Height - 60 * scale));

        try
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < MenuOpenTimeoutMilliseconds)
            {
                _ct.ThrowIfCancellationRequested();
                using var capture = CaptureToRectArea();
                if (TryFind(capture.SrcMat, areaIndex, out var hit))
                {
                    GameCaptureRegion.GameRegionClick((_, scale) =>
                        ((hit.X + hit.Width / 2d) * scale, (hit.Y + hit.Height / 2d) * scale));
                    await Delay(GetOperationDelay(160), _ct);
                    Logger.LogInformation(
                        "实验传送通过图集切换到区域：{Area}，菜单等待 {ElapsedMilliseconds}ms",
                        areaName,
                        stopwatch.ElapsedMilliseconds);
                    return true;
                }

                await Delay(MenuOpenPollIntervalMilliseconds, _ct);
            }

            Logger.LogDebug(
                "实验传送地区菜单等待达到上限，图集未命中：area={Area} timeout={TimeoutMilliseconds}ms",
                areaName,
                MenuOpenTimeoutMilliseconds);
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await Delay(GetOperationDelay(80), _ct);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogDebug(ex, "实验传送地区模板匹配失败：{Area}", areaName);
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await Delay(GetOperationDelay(80), _ct);
            return false;
        }
    }

    private bool TryFind(Mat source, int areaIndex, out Rect hit)
    {
        hit = default;
        if (_atlas == null || _atlas.Empty() || _atlas.Width < 16 || _atlas.Height < 2)
        {
            return false;
        }

        var tileWidth = _atlas.Width / 16;
        var tileHeight = _atlas.Height / 2;
        var threshold = Math.Clamp(_config.ExperimentalTeleportTemplateMatchThreshold, 0.5d, 0.99d);
        for (var variant = 0; variant < 2; variant++)
        {
            if (variant == 1 && areaIndex == 15)
            {
                continue;
            }

            using var template = new Mat(
                _atlas,
                new Rect(areaIndex * tileWidth, variant * tileHeight, tileWidth, tileHeight));
            var searchRect = new Rect(source.Width * 2 / 3, 0, source.Width / 3, source.Height);
            using var search = new Mat(source, searchRect);
            using var result = new Mat();
            Cv2.MatchTemplate(search, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var score, out _, out var location);
            if (score < threshold)
            {
                continue;
            }

            var candidate = new Rect(
                location.X + searchRect.X,
                location.Y,
                template.Width,
                template.Height);
            if (IsOnMenuGrid(candidate, source.Width, source.Height))
            {
                hit = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool IsOnMenuGrid(Rect hit, int width, int height)
    {
        var scaleX = width / 1920d;
        var scaleY = height / 1080d;
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var expectedX = (GridStartX + column * GridStepX) * scaleX;
                var expectedY = (GridStartY + row * GridStepY) * scaleY;
                if (Math.Abs(hit.X - expectedX) <= GridValidationTolerance * scaleX &&
                    Math.Abs(hit.Y - expectedY) <= GridValidationTolerance * scaleY)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private int GetOperationDelay(int baseDelay)
    {
        var configured = Math.Clamp(
            _config.TeleportOperationDelayMilliseconds,
            TpConfig.MinTeleportOperationDelayMilliseconds,
            TpConfig.MaxTeleportOperationDelayMilliseconds);
        return Math.Max(1, (int)Math.Round(
            baseDelay * configured / (double)TpConfig.DefaultTeleportOperationDelayMilliseconds));
    }

    public void Dispose()
    {
        _atlas?.Dispose();
    }
}
