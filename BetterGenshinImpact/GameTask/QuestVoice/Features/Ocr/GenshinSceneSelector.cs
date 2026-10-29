// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using Serilog;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using SkiaSharp;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>GenshinSceneSelector：原神自动配音的业务类型。</summary>
public sealed class GenshinSceneSelector(SceneCatalog catalog) : ISceneSelector
{
    /// <summary>在后台判断当前画面是否满足受支持的原神对白布局。</summary>
    public async Task<SceneSelection> SelectAsync(ReadOnlyMemory<byte> image, SceneSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = await catalog.LoadAsync(settings.CatalogPath, cancellationToken).ConfigureAwait(false);
        return await SelectAsync(image, loaded, cancellationToken).ConfigureAwait(false);
    }

    // A continuous observation uses this exact catalog for selection and its ink signature.
    public async Task<SceneSelection> SelectAsync(ReadOnlyMemory<byte> image, LoadedSceneCatalog loaded,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await Task.Run(() => Select(image, loaded, cancellationToken), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        QuestVoiceLog.Diagnostic.Debug("SceneSelected {Kind} {LayoutId} {Reason} {AlgorithmVersion} {CatalogSha256} {@Evidence}",
            result.Decision.Kind, result.Decision.LayoutId, result.Decision.Reason, result.Decision.AlgorithmVersion,
            result.Decision.CatalogSha256, result.Decision.Evidence);
        return result;
    }

    /// <summary>依据颜色、分隔线和字形几何证据选择对白布局。</summary>
    private static SceneSelection Select(ReadOnlyMemory<byte> image, LoadedSceneCatalog loaded, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var data = SKData.CreateCopy(image.Span);
        using var codec = SKCodec.Create(data) ?? throw new OcrException(OcrErrorCode.Image, "无法解码图片。");
        var c = loaded.Definition;
        var info = codec.Info;
        var evidence = new Dictionary<string, double> { ["ImageWidth"] = info.Width, ["ImageHeight"] = info.Height };
        var scaleX = (double)info.Width / c.Width;
        var scaleY = (double)info.Height / c.Height;
        evidence["ScaleX"] = scaleX;
        evidence["ScaleY"] = scaleY;
        if (scaleX < c.Transform.MinScale || scaleX > c.Transform.MaxScale
            || scaleY < c.Transform.MinScale || scaleY > c.Transform.MaxScale
            || Math.Abs(scaleX / scaleY - 1) > c.Transform.AspectTolerance
            || (long)info.Width * info.Height > c.Transform.MaximumPixels)
            return Finish(SceneDecisionKind.Unknown, "UnsupportedImageTransform");

        using var bitmap = new SKBitmap(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new OcrException(OcrErrorCode.Image, "图片像素数据不完整。");
        token.ThrowIfCancellationRequested();
        var pixels = new ScenePixelAnalysis(bitmap, c, token);
        if (pixels.HasBorder(evidence)) return Finish(SceneDecisionKind.Unknown, "UnsupportedImageBorder");
        var name = pixels.Components(c.NameArea, c.Colors.NameGold);
        var title = pixels.Components(c.TitleArea, c.Colors.NameGold);
        var text = pixels.Components(c.TextArea, c.Colors.White);
        var g = c.Geometry;
        evidence["NamePixels"] = name.Sum(x => x.Pixels);
        evidence["NameTallComponents"] = name.Count(x => x.Height >= g.NameTallHeight);
        evidence["TitlePixels"] = title.Sum(x => x.Pixels);
        evidence["TitleTallComponents"] = title.Count(x => x.Height >= g.TitleTallHeight);
        evidence["TextPixels"] = text.Sum(x => x.Pixels);
        evidence["TextComponents"] = text.Count;
        var separators = c.Separators.Where(s => pixels.HasSeparator(s, evidence)).ToArray();
        var hasName = evidence["NamePixels"] >= g.NameMinimumPixels && evidence["NameTallComponents"] >= g.NameMinimumTallComponents;
        var hasTitle = evidence["TitlePixels"] >= g.TitleMinimumPixels && evidence["TitleTallComponents"] >= g.TitleMinimumTallComponents;
        if (!hasName || separators.Length == 0 || evidence["TextPixels"] < g.BodyMinimumPixels)
            return Finish(SceneDecisionKind.Rejected, "NoSupportedDialogueFrame");
        if (separators.Length != 1 || separators[0].HasTitle != hasTitle)
            return Finish(SceneDecisionKind.Unknown, "ConflictingDialogueAnchors");

        var candidates = new List<SceneLayout>();
        var hasUnsafeRoi = false;
        foreach (var layout in c.Layouts.Where(x => x.HasTitle == hasTitle))
        {
            token.ThrowIfCancellationRequested();
            if (Matches(layout, text, c, evidence, token)) candidates.Add(layout);
            hasUnsafeRoi |= evidence.ContainsKey(layout.Id + ".RoiConflict");
        }
        evidence["CandidateCount"] = candidates.Count;
        if (hasUnsafeRoi) return Finish(SceneDecisionKind.Unknown, "UnsafeDialogueRoi");
        if (candidates.Count != 1) return Finish(SceneDecisionKind.Unknown,
            candidates.Count == 0 ? "UnsupportedTextGeometry" : "AmbiguousTextGeometry");
        var selected = candidates[0];
        var regions = new List<LayoutRegion> { new("name", OcrRegionKind.Speaker, 0, selected.Speaker) };
        regions.AddRange(selected.Dialogue.Select((bounds, index) => new LayoutRegion($"dialogue_line{index + 1}",
            OcrRegionKind.Dialogue, index + 1, bounds)));
        foreach (var region in regions)
        {
            var actual = ImagePreprocessor.Scale(region.Bounds, c.Width, c.Height, info.Width, info.Height);
            evidence[$"Roi.{region.Label}.Left"] = actual.Left;
            evidence[$"Roi.{region.Label}.Top"] = actual.Top;
            evidence[$"Roi.{region.Label}.Right"] = actual.Right;
            evidence[$"Roi.{region.Label}.Bottom"] = actual.Bottom;
        }
        return Finish(SceneDecisionKind.Selected, "UniqueSupportedLayout", selected.Id, new(c.Width, c.Height, regions));

        SceneSelection Finish(SceneDecisionKind kind, string reason, string? id = null, SceneProfile? profile = null)
        {
            token.ThrowIfCancellationRequested();
            var color = c.Colors.NameGold;
            var speakerInk = kind == SceneDecisionKind.Selected
                ? new SpeakerInkPolicy("name-gold-mask-1", color.MinRed, color.MinGreen, color.MinBlue,
                    color.MaxBlue, color.MinRedGreen, color.MinGreenBlue, color.MinRedBlue, color.MaxSpread)
                : null;
            return new(new(kind, id, reason, loaded.AlgorithmVersion, loaded.Sha256, evidence), profile, speakerInk);
        }
    }

    /// <summary>Matches：执行GenshinSceneSelector中的对应校验或计算步骤。</summary>
    private static bool Matches(SceneLayout layout, IReadOnlyList<SceneInkComponent> text, SceneCatalogDefinition catalog,
        Dictionary<string, double> evidence, CancellationToken token)
    {
        var g = catalog.Geometry;
        var matchedPixels = 0;
        var valid = true;
        var bodyIndex = 0;
        var hasRoiConflict = false;
        for (var index = 0; index < layout.Bands.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var band = layout.Bands[index];
            var ink = Filter(text, x => x.Top >= band.Top - g.BandTolerance && x.Bottom <= band.Bottom + g.BandTolerance, token);
            hasRoiConflict |= CrossesOtherDialogueRoi(ink, layout, band.Kind == "Body" ? bodyIndex++ : -1, token);
            var count = ink.Sum(x => x.Pixels);
            matchedPixels += count;
            var prefix = layout.Id + ".Band" + index;
            evidence[prefix + ".Pixels"] = count;
            evidence[prefix + ".Components"] = ink.Length;
            evidence[prefix + ".TallComponents"] = ink.Count(x => x.Height >= g.BodyTallHeight);
            if (band.Kind == "Ruby")
                valid &= count >= g.RubyMinimumPixels && ink.Count(x => x.Height >= g.RubyMinimumHeight
                    && x.Height <= g.RubyMaximumHeight) >= g.RubyMinimumComponents
                    && ink.All(x => x.Height <= g.RubyMaximumHeight);
            else
            {
                valid &= HasBody(ink, band, catalog);
            }
        }
        var outside = Filter(text, x => !layout.Bands.Any(band => x.Top >= band.Top - g.BandTolerance
            && x.Bottom <= band.Bottom + g.BandTolerance), token);
        var extraBody = false;
        foreach (var band in catalog.Layouts.SelectMany(x => x.Bands).Where(x => x.Kind == "Body").Distinct())
        {
            token.ThrowIfCancellationRequested();
            if (layout.Bands.Any(x => x.Top < band.Bottom && x.Bottom > band.Top)) continue;
            extraBody |= HasBody(Filter(outside, x => x.Top >= band.Top - g.BandTolerance
                && x.Bottom <= band.Bottom + g.BandTolerance, token), band, catalog);
        }
        var intervals = new SceneInkIntervalIndex(outside, catalog.Height, token);
        long extra = 0;
        foreach (var component in outside)
        {
            token.ThrowIfCancellationRequested();
            var aligned = intervals.Overlapping(component);
            extraBody |= aligned.Pixels >= g.BodyMinimumPixels
                && Math.Abs(aligned.TwiceHorizontalMoment / (2.0 * aligned.Pixels) - catalog.Width / 2.0) <= g.BodyCenterTolerance;
            // Isolated glare is not evidence of another text row; count intersecting components together.
            if (aligned.Count >= g.ExtraMinimumAlignedComponents) extra += component.Pixels;
        }
        evidence[layout.Id + ".UnassignedPixels"] = outside.Sum(x => x.Pixels);
        evidence[layout.Id + ".ExtraPixels"] = extra;
        evidence[layout.Id + ".ExtraBody"] = extraBody ? 1 : 0;
        var matches = valid && !extraBody && extra <= Math.Max(g.ExtraPixelAllowance, matchedPixels * g.ExtraPixelFraction);
        if (matches && hasRoiConflict) evidence[layout.Id + ".RoiConflict"] = 1;
        return matches && !hasRoiConflict;
    }

    /// <summary>CrossesOtherDialogueRoi：执行GenshinSceneSelector中的对应校验或计算步骤。</summary>
    private static bool CrossesOtherDialogueRoi(SceneInkComponent[] ink, SceneLayout layout, int ownBody,
        CancellationToken token)
    {
        // Tolerated bands may overlap in empty space; only actual ink crossing another crop is unsafe.
        foreach (var component in ink)
        {
            for (var index = 0; index < layout.Dialogue.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var rect = layout.Dialogue[index];
                if (index != ownBody && component.Top < rect.Bottom && component.Bottom > rect.Top) return true;
            }
        }
        return false;
    }

    /// <summary>Filter：执行GenshinSceneSelector中的对应校验或计算步骤。</summary>
    private static SceneInkComponent[] Filter(IReadOnlyList<SceneInkComponent> components,
        Func<SceneInkComponent, bool> predicate, CancellationToken token)
    {
        var result = new List<SceneInkComponent>();
        foreach (var component in components)
        {
            token.ThrowIfCancellationRequested();
            if (predicate(component)) result.Add(component);
        }
        token.ThrowIfCancellationRequested();
        return result.ToArray();
    }

    /// <summary>HasBody：执行GenshinSceneSelector中的对应校验或计算步骤。</summary>
    private static bool HasBody(SceneInkComponent[] ink, SceneInkBand band, SceneCatalogDefinition catalog)
    {
        var g = catalog.Geometry;
        if (ink.Length < g.BodyMinimumComponents || ink.Sum(x => x.Pixels) < g.BodyMinimumPixels || !Centered(ink, catalog)) return false;
        var punctuation = ink.Where(x => x.Top >= band.Bottom - g.PunctuationBaselineDepth).ToArray();
        return ink.Count(x => x.Height >= g.BodyTallHeight) >= g.BodyMinimumTallComponents
            || (punctuation.Length >= g.PunctuationMinimumComponents && punctuation.Sum(x => x.Pixels) >= g.PunctuationMinimumPixels);
    }

    /// <summary>Centered：执行GenshinSceneSelector中的对应校验或计算步骤。</summary>
    private static bool Centered(SceneInkComponent[] ink, SceneCatalogDefinition catalog) => ink.Length > 0
        && Math.Abs(ink.Sum(x => (x.Left + x.Right) / 2.0 * x.Pixels) / ink.Sum(x => x.Pixels)
            - catalog.Width / 2.0) <= catalog.Geometry.BodyCenterTolerance;
}
