using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;
using SkiaSharp;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：SceneSelectionTests。</summary>
public sealed class SceneSelectionTests
{
    /// <summary>移植回归验证：CatalogPath。</summary>
    internal static string CatalogPath => Path.Combine(AppContext.BaseDirectory, "Resources", "QuestVoice", "auto-scene.json");

    /// <summary>移植回归验证：Selects six independently drawn dialogue structures。</summary>
    [Theory]
    [InlineData("standard.1", false, false, false)]
    [InlineData("standard.2", false, false, true)]
    [InlineData("title.1", true, false, false)]
    [InlineData("title.2", true, false, true)]
    [InlineData("ruby.1", false, true, false)]
    [InlineData("title-ruby.1", true, true, false)]
    public async Task Selects_six_independently_drawn_dialogue_structures(string id, bool title, bool ruby, bool second)
    {
        var result = await Select(Draw(title, ruby, second));
        Assert.Equal(SceneDecisionKind.Selected, result.Decision.Kind);
        Assert.Equal("genshin." + id, result.Decision.LayoutId);
        Assert.NotNull(result.Profile);
        Assert.Equal(second ? 3 : 2, result.Profile.Regions.Count);
        Assert.All(result.Profile.Regions.Where(r => r.Kind == OcrRegionKind.Dialogue), r => Assert.True(r.Bounds.Width >= 1740));
        Assert.True(result.Profile.Regions.Single(r => r.Kind == OcrRegionKind.Speaker).Bounds.Width >= 900);
    }

    /// <summary>移植回归验证：Missing necessary anchor rejects without a profile。</summary>
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Missing_necessary_anchor_rejects_without_a_profile(bool separator, bool name, bool body)
    {
        var result = await Select(Draw(separator: separator, name: name, body: body));
        Assert.Equal(SceneDecisionKind.Rejected, result.Decision.Kind);
        Assert.Equal("NoSupportedDialogueFrame", result.Decision.Reason);
        Assert.Null(result.Profile);
    }

    /// <summary>移植回归验证：Conflicting separator structures are unknown without fallback。</summary>
    [Fact]
    public async Task Conflicting_separator_structures_are_unknown_without_fallback()
    {
        var result = await Select(Draw(title: true, conflict: true));
        Assert.Equal(SceneDecisionKind.Unknown, result.Decision.Kind);
        Assert.Null(result.Profile);
    }

    /// <summary>移植回归验证：Unsupported ruby with two body lines is unknown。</summary>
    [Fact]
    public async Task Unsupported_ruby_with_two_body_lines_is_unknown()
    {
        var result = await Select(Draw(ruby: true, second: true));
        Assert.Equal(SceneDecisionKind.Unknown, result.Decision.Kind);
        Assert.Null(result.Profile);
    }

    /// <summary>移植回归验证：Scales full frames and refuses unknown aspect ratios。</summary>
    [Theory]
    [InlineData(1280, 720, SceneDecisionKind.Selected)]
    [InlineData(1920, 1080, SceneDecisionKind.Selected)]
    [InlineData(1280, 800, SceneDecisionKind.Unknown)]
    public async Task Scales_full_frames_and_refuses_unknown_aspect_ratios(int width, int height, SceneDecisionKind expected)
    {
        var result = await Select(Draw(width: width, height: height));
        Assert.Equal(expected, result.Decision.Kind);
    }

    /// <summary>移植回归验证：Letterboxed frame is unknown。</summary>
    [Fact]
    public async Task Letterboxed_frame_is_unknown()
    {
        var result = await Select(Draw(border: true));
        Assert.Equal(SceneDecisionKind.Unknown, result.Decision.Kind);
        Assert.Null(result.Profile);
    }

    /// <summary>移植回归验证：Punctuation is a dialogue structure even without tall body glyphs。</summary>
    [Fact]
    public async Task Punctuation_is_a_dialogue_structure_even_without_tall_body_glyphs()
    {
        var result = await Select(Draw(punctuation: true));
        Assert.Equal(SceneDecisionKind.Selected, result.Decision.Kind);
        Assert.Equal("genshin.standard.1", result.Decision.LayoutId);
    }

    /// <summary>移植回归验证：Reports actual scaled roi and catalog provenance。</summary>
    [Fact]
    public async Task Reports_actual_scaled_roi_and_catalog_provenance()
    {
        var result = await Select(Draw(width: 1280, height: 720));
        Assert.Equal(375, result.Decision.Evidence["Roi.name.Left"]);
        Assert.Equal(567, result.Decision.Evidence["Roi.name.Top"]);
        Assert.Equal(905, result.Decision.Evidence["Roi.name.Right"]);
        Assert.Equal(594, result.Decision.Evidence["Roi.name.Bottom"]);
        Assert.Equal(200, result.Decision.Evidence["Roi.dialogue_line1.Left"]);
        Assert.Equal(1080, result.Decision.Evidence["Roi.dialogue_line1.Right"]);
        Assert.Equal(64, result.Decision.CatalogSha256!.Length);
        Assert.Equal("genshin-pixel-1", result.Decision.AlgorithmVersion);
    }

    /// <summary>移植回归验证：Incomplete image is not classified from partial pixels。</summary>
    [Fact]
    public async Task Incomplete_image_is_not_classified_from_partial_pixels()
    {
        var bytes = Draw();
        var error = await Assert.ThrowsAsync<OcrException>(() => Select(bytes[..(bytes.Length / 2)]));
        Assert.Equal(OcrErrorCode.Image, error.Code);
    }

    /// <summary>移植回归验证：Third body line below a title layout is unknown。</summary>
    [Fact]
    public async Task Third_body_line_below_a_title_layout_is_unknown()
    {
        using var bitmap = SKBitmap.Decode(Draw(title: true, second: true));
        using var canvas = new SKCanvas(bitmap);
        using var white = new SKPaint { Color = new SKColor(245, 245, 245) };
        Glyphs(canvas, white, 1100, 1345, 9, 24, 34);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var result = await Select(data.ToArray());
        Assert.Equal(SceneDecisionKind.Unknown, result.Decision.Kind);
        Assert.Null(result.Profile);
    }

    /// <summary>移植回归验证：Thin separator remains visible over gold background。</summary>
    [Fact]
    public async Task Thin_separator_remains_visible_over_gold_background()
    {
        var result = await Select(Draw(goldBackground: true));
        Assert.Equal(SceneDecisionKind.Selected, result.Decision.Kind);
        Assert.Equal("genshin.standard.1", result.Decision.LayoutId);
    }

    /// <summary>移植回归验证：One tall name component and short title separator are sufficient with other anchors。</summary>
    [Fact]
    public async Task One_tall_name_component_and_short_title_separator_are_sufficient_with_other_anchors()
    {
        var result = await Select(Draw(title: true, nameGlyphCount: 1, shortTitleSeparator: true));
        Assert.Equal(SceneDecisionKind.Selected, result.Decision.Kind);
        Assert.Equal("genshin.title.1", result.Decision.LayoutId);
    }

    /// <summary>移植回归验证：Midline ellipsis is body ink。</summary>
    [Fact]
    public async Task Midline_ellipsis_is_body_ink()
    {
        var result = await Select(Draw(punctuation: true, punctuationOffset: 14));
        Assert.Equal(SceneDecisionKind.Selected, result.Decision.Kind);
        Assert.Equal("genshin.standard.1", result.Decision.LayoutId);
    }

    /// <summary>移植回归验证：A single body glyph is supported。</summary>
    [Fact]
    public async Task A_single_body_glyph_is_supported()
    {
        var result = await Select(Draw(bodyGlyphCount: 1));
        Assert.Equal(SceneDecisionKind.Selected, result.Decision.Kind);
    }

    /// <summary>移植回归验证：Isolated background glare below body does not form a second line。</summary>
    [Fact]
    public async Task Isolated_background_glare_below_body_does_not_form_a_second_line()
    {
        using var bitmap = SKBitmap.Decode(Draw());
        using var canvas = new SKCanvas(bitmap);
        using var white = new SKPaint { Color = new SKColor(245, 245, 245) };
        canvas.DrawOval(445, 1314, 17, 16, white);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var result = await Select(data.ToArray());
        Assert.Equal(SceneDecisionKind.Selected, result.Decision.Kind);
        Assert.Equal("genshin.standard.1", result.Decision.LayoutId);
    }

    /// <summary>移植回归验证：A short second line is not ignored relative to a long first line。</summary>
    [Theory]
    [InlineData(false, "genshin.standard.2", 1269)]
    [InlineData(true, "genshin.title.2", 1295)]
    public async Task A_short_second_line_is_not_ignored_relative_to_a_long_first_line(bool title, string id, int y)
    {
        using var bitmap = SKBitmap.Decode(Draw(title: title));
        using var canvas = new SKCanvas(bitmap);
        using var white = new SKPaint { Color = new SKColor(245, 245, 245) };
        Glyphs(canvas, white, 1268, y, 1, 24, 34);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var result = await Select(data.ToArray());
        Assert.Equal(SceneDecisionKind.Selected, result.Decision.Kind);
        Assert.Equal(id, result.Decision.LayoutId);
    }

    /// <summary>移植回归验证：One centered extra glyph after two lines is still unsupported。</summary>
    [Fact]
    public async Task One_centered_extra_glyph_after_two_lines_is_still_unsupported()
    {
        using var bitmap = SKBitmap.Decode(Draw(title: true, second: true));
        using var canvas = new SKCanvas(bitmap);
        using var white = new SKPaint { Color = new SKColor(245, 245, 245) };
        Glyphs(canvas, white, 1268, 1345, 1, 24, 34);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var result = await Select(data.ToArray());
        Assert.Equal(SceneDecisionKind.Unknown, result.Decision.Kind);
    }

    /// <summary>移植回归验证：A remote bright speck does not move the body ink center。</summary>
    [Fact]
    public async Task A_remote_bright_speck_does_not_move_the_body_ink_center()
    {
        using var bitmap = SKBitmap.Decode(Draw(bodyGlyphCount: 1));
        using var canvas = new SKCanvas(bitmap);
        using var white = new SKPaint { Color = new SKColor(245, 245, 245) };
        canvas.DrawRect(450, 1240, 5, 5, white);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var result = await Select(data.ToArray());
        Assert.Equal(SceneDecisionKind.Selected, result.Decision.Kind);
        Assert.Equal("genshin.standard.1", result.Decision.LayoutId);
    }

    /// <summary>移植回归验证：Invalid image is an image error and precancel takes precedence。</summary>
    [Fact]
    public async Task Invalid_image_is_an_image_error_and_precancel_takes_precedence()
    {
        var error = await Assert.ThrowsAsync<OcrException>(() => Select([1, 2, 3]));
        Assert.Equal(OcrErrorCode.Image, error.Code);
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Select([], source.Token));
    }

    /// <summary>依据颜色、分隔线和字形几何证据选择对白布局。</summary>
    private static Task<SceneSelection> Select(byte[] image, CancellationToken token = default) =>
        new GenshinSceneSelector(new SceneCatalog()).SelectAsync(image, new SceneSettings(CatalogPath), token);

    // Geometry is drawn from the visible UI specification, independently of the catalog and selector.
    internal static byte[] Draw(bool title = false, bool ruby = false, bool second = false, bool separator = true,
        bool name = true, bool body = true, bool conflict = false, bool punctuation = false, bool border = false,
        int width = 2560, int height = 1440, bool goldBackground = false, int nameGlyphCount = 5,
        bool shortTitleSeparator = false, int punctuationOffset = 20, int bodyGlyphCount = 30)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(goldBackground ? new SKColor(110, 80, 20) : new SKColor(35, 42, 51));
        canvas.Scale(width / 2560f, height / 1440f);
        using var gold = new SKPaint { Color = new SKColor(240, 195, 50) };
        using var line = new SKPaint { Color = new SKColor(150, 123, 46) };
        using var white = new SKPaint { Color = new SKColor(245, 245, 245) };
        if (name) Glyphs(canvas, gold, 1180, 1145, nameGlyphCount, 27, 36);
        if (title) Glyphs(canvas, gold, 1160, 1200, 9, 20, 23);
        if (separator)
        {
            if (!title || conflict) canvas.DrawRect(600, 1198, 1360, 3, line);
            if (title)
            {
                canvas.DrawRect(shortTitleSeparator ? 1060 : 600, 1211, shortTitleSeparator ? 55 : 520, 3, line);
                canvas.DrawRect(1450, 1211, shortTitleSeparator ? 55 : 510, 3, line);
            }
        }
        var y = title ? 1245 : 1219;
        if (ruby)
        {
            Glyphs(canvas, white, 1100, y, 4, 12, 14);
            y += 24;
        }
        if (body)
        {
            if (punctuation)
                for (var i = 0; i < 6; i++) canvas.DrawRect(1210 + i * 10, y + punctuationOffset, 5, 5, white);
            else Glyphs(canvas, white, bodyGlyphCount == 1 ? 1268 : 700, y, bodyGlyphCount, 24, 34);
            if (second) Glyphs(canvas, white, 1100, y + 50, 9, 24, 34);
        }
        if (border)
        {
            using var black = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(0, 0, 2560, 60, black);
            canvas.DrawRect(0, 1380, 2560, 60, black);
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>移植回归验证：Glyphs。</summary>
    private static void Glyphs(SKCanvas canvas, SKPaint paint, int x, int y, int count, int width, int height)
    {
        for (var i = 0; i < count; i++)
        {
            var left = x + i * (width + 7);
            canvas.DrawRect(left, y, 4, height, paint);
            canvas.DrawRect(left + width - 4, y, 4, height, paint);
            canvas.DrawRect(left, y + height / 2, width, 4, paint);
        }
    }
}
