// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text.Json;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;
using Serilog;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using SkiaSharp;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;

/// <summary>Measures only selected name/body ink, with one catalog snapshot per observation.</summary>
public sealed class ContinuousFrameInspector : IContinuousFrameInspector
{
    /// <summary>当前实例采用的配置快照。</summary>
    private readonly ContinuousSettings _settings;
    /// <summary>场景目录路径设置。</summary>
    private readonly SceneSettings _sceneSettings;
    /// <summary>场景目录读取器。</summary>
    private readonly SceneCatalog _catalog;
    /// <summary>原神对白布局选择器。</summary>
    private readonly GenshinSceneSelector _selector;

    /// <summary>建立 ContinuousFrameInspector 的业务依赖与初始状态。</summary>
    public ContinuousFrameInspector(ContinuousSettings settings, SceneSettings sceneSettings,
        SceneCatalog catalog, GenshinSceneSelector selector)
    {
        ContinuousSettingsLoader.Validate(settings);
        _settings = settings;
        _sceneSettings = sceneSettings;
        _catalog = catalog;
        _selector = selector;
    }

    /// <summary>读取场景并量测对白区域墨迹。</summary>
    public async Task<ContinuousFrameInspection> InspectAsync(ContinuousFrameSnapshot frame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = await _catalog.LoadAsync(_sceneSettings.CatalogPath, cancellationToken).ConfigureAwait(false);
        var bytes = frame.Bytes;
        var selection = await _selector.SelectAsync(bytes, loaded, cancellationToken).ConfigureAwait(false);
        var inspection = await Task.Run(() => InspectPixels(frame, bytes, loaded, selection, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        QuestVoiceLog.Diagnostic.Debug("ContinuousFrameInspected {SourceSequence} {ImageSha256} {Kind} {LayoutId} {CatalogSha256} {SignatureIdentity}",
            frame.Sequence, frame.Sha256, selection.Decision.Kind, selection.Decision.LayoutId, loaded.Sha256,
            inspection.Signature?.Identity);
        return inspection;
    }

    /// <summary>基于同一场景目录快照计算布局身份与墨迹签名。</summary>
    private ContinuousFrameInspection InspectPixels(ContinuousFrameSnapshot frame, ReadOnlyMemory<byte> bytes,
        LoadedSceneCatalog loaded, SceneSelection selection, CancellationToken token)
    {
        using var data = SKData.CreateCopy(bytes.Span);
        using var codec = SKCodec.Create(data) ?? throw new OcrException(OcrErrorCode.Image, "无法解码连续帧。");
        var source = new ImageSourceSnapshot(frame.Sha256, codec.Info.Width, codec.Info.Height);
        if (selection.Decision.Kind != SceneDecisionKind.Selected || selection.Profile is not { } profile)
            return new(source, selection, null);

        using var bitmap = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new OcrException(OcrErrorCode.Image, "连续帧像素数据不完整。");
        var regions = profile.Regions.OrderBy(region => region.Kind).ThenBy(region => region.Order).ToArray();
        var bounds = regions.Select(region => ImagePreprocessor.Scale(region.Bounds, profile.Width, profile.Height,
            source.Width, source.Height)).ToArray();
        // Structured serialization prevents ambiguous layout/geometry concatenations.
        var identityBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            source.Width, source.Height, loaded.Sha256, loaded.AlgorithmVersion, selection.Decision.LayoutId,
            ReferenceWidth = profile.Width, ReferenceHeight = profile.Height,
            _settings.SignatureColumns, _settings.SignatureRows,
            Regions = regions.Select((region, index) => new { region.Label, region.Kind, region.Order,
                region.Bounds, ActualBounds = bounds[index] })
        });
        var identity = Convert.ToHexString(SHA256.HashData(identityBytes)).ToLowerInvariant();
        var grids = new List<int[]>(regions.Length);
        for (var index = 0; index < regions.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var range = regions[index].Kind == OcrRegionKind.Speaker
                ? loaded.Definition.Colors.NameGold : loaded.Definition.Colors.White;
            grids.Add(MeasureInk(bitmap, bounds[index], range, token));
        }
        return new(source, selection, new(identity, grids));
    }

    /// <summary>只量测名字和正文区域中的目标颜色墨迹。</summary>
    private int[] MeasureInk(SKBitmap bitmap, PixelRect bounds, SceneColorRange range, CancellationToken token)
    {
        var grid = new int[_settings.SignatureColumns * _settings.SignatureRows];
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            token.ThrowIfCancellationRequested();
            var row = (int)((long)(y - bounds.Top) * _settings.SignatureRows / bounds.Height);
            for (var x = bounds.Left; x < bounds.Right; x++)
            {
                if (!ScenePixelAnalysis.IsColor(bitmap.GetPixel(x, y), range)) continue;
                var column = (int)((long)(x - bounds.Left) * _settings.SignatureColumns / bounds.Width);
                grid[row * _settings.SignatureColumns + column]++;
            }
        }
        return grid;
    }
}
