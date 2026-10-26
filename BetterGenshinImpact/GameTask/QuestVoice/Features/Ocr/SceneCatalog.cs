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
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>SceneCatalog：原神自动配音的业务类型。</summary>
public sealed class SceneCatalog
{
    /// <summary>保存 Options 对应的本轮状态或配置，不跨会话复用。</summary>
    private static readonly JsonSerializerOptions _options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { info => { foreach (var property in info.Properties.Where(p => p.Set is not null)) property.IsRequired = true; } }
        }
    };

    /// <summary>异步读取并严格校验外置配置或布局。</summary>
    public async Task<LoadedSceneCatalog> LoadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            // Resource limits are parser safety bounds, not scene-classification thresholds.
            if (stream.Length > 1_000_000) throw Invalid("目录文件过大。");
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            CheckDuplicateProperties(json.RootElement);
            var definition = JsonSerializer.Deserialize<SceneCatalogDefinition>(bytes, _options)
                ?? throw Invalid("目录不能为空。");
            Validate(definition);
            cancellationToken.ThrowIfCancellationRequested();
            return new(definition.AlgorithmVersion, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), definition);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            throw new OcrException(OcrErrorCode.Configuration, $"无法读取自动场景目录：{exception.Message}", exception);
        }
    }

    /// <summary>CheckDuplicateProperties：执行SceneCatalog中的对应校验或计算步骤。</summary>
    private static void CheckDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid($"重复字段：{property.Name}");
                CheckDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CheckDuplicateProperties(child);
    }

    /// <summary>校验配置的边界、引用和参数关系。</summary>
    private static void Validate(SceneCatalogDefinition c)
    {
        if (string.IsNullOrWhiteSpace(c.AlgorithmVersion) || c.Width <= 0 || c.Height <= 0 || (long)c.Width * c.Height > 80_000_000
            || c.Transform is null || c.Colors is null || c.Geometry is null || c.Layouts is not { Length: > 0 }
            || c.Separators is not { Length: > 0 }) throw Invalid("缺少版本、尺寸或目录定义。");
        var t = c.Transform;
        if (!double.IsFinite(t.MinScale) || !double.IsFinite(t.MaxScale) || t.MinScale <= 0 || t.MaxScale < t.MinScale
            || !Fraction(t.AspectTolerance) || t.AspectTolerance >= 1 || t.MaximumPixels <= 0 || t.MaximumPixels > 80_000_000
            || t.BorderDepth <= 0 || t.BorderDepth >= Math.Min(c.Width, c.Height) / 2
            || t.BorderDarkMaximum is < 0 or > 255 || !Fraction(t.BorderDarkFraction) || t.BorderDarkFraction == 0)
            throw Invalid("非法画面变换参数。");
        foreach (var color in new[] { c.Colors.NameGold, c.Colors.SeparatorGold, c.Colors.White })
        {
            if (color is null || new[] { color.MinRed, color.MinGreen, color.MinBlue, color.MaxBlue, color.MaxSpread }.Any(v => v is < 0 or > 255)
                || color.MaxBlue < color.MinBlue || new[] { color.MinRedGreen, color.MinGreenBlue, color.MinRedBlue }.Any(v => v is < -255 or > 255))
                throw Invalid("非法颜色范围。");
        }
        Rect(c.NameArea, c); Rect(c.TitleArea, c); Rect(c.TextArea, c);
        if (c.NameArea.Bottom > c.TitleArea.Top || c.NameArea.Bottom > c.TextArea.Top)
            throw Invalid("NameArea 必须位于 TitleArea 和 TextArea 上方，不能交叠。");
        var g = c.Geometry;
        if (new[] { g.ComponentMinimumPixels, g.ComponentMaximumWidth, g.ComponentMinimumHeight, g.ComponentMaximumHeight,
            g.NameMinimumPixels, g.NameMinimumTallComponents, g.NameTallHeight, g.TitleMinimumPixels, g.TitleMinimumTallComponents,
            g.TitleTallHeight, g.BodyMinimumPixels, g.BodyMinimumComponents, g.BodyTallHeight, g.BodyMinimumTallComponents,
            g.PunctuationMinimumComponents, g.PunctuationMinimumPixels, g.PunctuationBaselineDepth, g.RubyMinimumPixels,
            g.RubyMinimumComponents, g.RubyMinimumHeight, g.RubyMaximumHeight, g.ExtraMinimumAlignedComponents,
            g.BodyCenterTolerance }.Any(v => v <= 0 || v > Math.Max(c.Width, c.Height))
            || g.ExtraPixelAllowance < 0 || g.BandTolerance < 0 || !Fraction(g.ExtraPixelFraction)
            || g.ComponentMinimumHeight > g.ComponentMaximumHeight || g.RubyMinimumHeight > g.RubyMaximumHeight)
            throw Invalid("非法字形几何参数。");
        var separatorKinds = new HashSet<bool>();
        foreach (var separator in c.Separators)
        {
            if (separator is null) throw Invalid("空分隔线定义。");
            Rect(separator.Bounds, c);
            if (c.NameArea.Bottom > separator.Bounds.Top)
                throw Invalid("NameArea 必须位于 Separators.Bounds 上方，不能交叠。");
            if (!separatorKinds.Add(separator.HasTitle) || separator.CenterGap < 0 || separator.CenterGap >= separator.Bounds.Width / 2
                || separator.MinimumSideRun <= 0 || separator.MinimumSideRun > separator.Bounds.Width / 2
                || separator.MinimumRows <= 0 || separator.MaximumThickness < separator.MinimumRows
                || separator.MaximumThickness > separator.Bounds.Height || separator.MinimumContrast is <= 0 or > 765
                || separator.ContrastOffset <= 0 || separator.Bounds.Top < separator.ContrastOffset
                || separator.Bounds.Bottom + separator.ContrastOffset > c.Height) throw Invalid("非法或重复分隔线。");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layout in c.Layouts)
        {
            if (layout is null || string.IsNullOrWhiteSpace(layout.Id) || !ids.Add(layout.Id)
                || layout.Bands is not { Length: > 0 } || layout.Dialogue is not { Length: > 0 }
                || layout.Bands.Any(band => band is null)
                || !separatorKinds.Contains(layout.HasTitle)) throw Invalid("缺失或重复布局。");
            Rect(layout.Speaker, c);
            if (layout.Speaker != c.NameArea)
                throw Invalid($"布局 {layout.Id}：Speaker ROI 必须完整覆盖 NameArea，且不能超出姓名带。");
            foreach (var rect in layout.Dialogue)
            {
                Rect(rect, c);
                if (rect.Left != c.TextArea.Left || rect.Right != c.TextArea.Right)
                    throw Invalid($"布局 {layout.Id}：Dialogue ROI 必须横向完整覆盖 TextArea，且不能超出正文带。");
            }
            var bodyIndex = 0;
            var previousBottom = -1;
            foreach (var band in layout.Bands)
            {
                if (band is null || band.Top < c.TextArea.Top || band.Bottom > c.TextArea.Bottom || band.Top >= band.Bottom
                    || band.Top < previousBottom || band.Kind is not ("Body" or "Ruby")) throw Invalid("非法或交叠字形带。");
                previousBottom = band.Bottom;
                var acceptedTop = (long)band.Top - g.BandTolerance;
                var acceptedBottom = (long)band.Bottom + g.BandTolerance;
                if (acceptedTop < c.TextArea.Top || acceptedBottom > c.TextArea.Bottom)
                    throw Invalid($"布局 {layout.Id}：BandTolerance 扩展后的字形带必须位于 TextArea 内。");
                if (band.Kind != "Body") continue;
                if (bodyIndex >= layout.Dialogue.Length) throw Invalid("正文带和 ROI 数量不匹配。");
                var rect = layout.Dialogue[bodyIndex++];
                if (rect.Top > acceptedTop || rect.Bottom < acceptedBottom)
                    throw Invalid($"布局 {layout.Id}：Dialogue ROI 必须完整覆盖 BandTolerance 扩展后的正文带。");
                if (!Contains(c.TextArea, rect) || rect.Top <= c.Separators.Single(s => s.HasTitle == layout.HasTitle).Bounds.Bottom
                    || (layout.HasTitle && rect.Top <= c.TitleArea.Bottom)
                    || layout.Bands.Any(other => other != band && other.Top < rect.Bottom && other.Bottom > rect.Top))
                    throw Invalid($"布局 {layout.Id}：正文 ROI 与分隔线、头衔或其他字形带交叠。");
            }
            if (bodyIndex != layout.Dialogue.Length) throw Invalid("正文带和 ROI 数量不匹配。");
        }
    }

    /// <summary>Fraction：执行SceneCatalog中的对应校验或计算步骤。</summary>
    private static bool Fraction(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
    /// <summary>Contains：执行SceneCatalog中的对应校验或计算步骤。</summary>
    private static bool Contains(PixelRect outer, PixelRect inner) => inner.Left >= outer.Left && inner.Top >= outer.Top
        && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
    /// <summary>Rect：执行SceneCatalog中的对应校验或计算步骤。</summary>
    private static void Rect(PixelRect? rect, SceneCatalogDefinition c)
    {
        if (rect is null || rect.Left < 0 || rect.Top < 0 || rect.Right > c.Width || rect.Bottom > c.Height
            || rect.Left >= rect.Right || rect.Top >= rect.Bottom) throw Invalid("布局坐标越界或为空。");
    }
    /// <summary>Invalid：执行SceneCatalog中的对应校验或计算步骤。</summary>
    private static OcrException Invalid(string reason) => new(OcrErrorCode.Configuration, reason);
}
