using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;
using SkiaSharp;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：ContinuousFrameInspectorTests。</summary>
public sealed class ContinuousFrameInspectorTests
{
    /// <summary>移植回归验证：Background motion outside and non ink inside rois does not change signature。</summary>
    [Fact]
    public async Task Background_motion_outside_and_non_ink_inside_rois_does_not_change_signature()
    {
        var original = SceneSelectionTests.Draw();
        var changed = Edit(original, canvas =>
        {
            using var background = new SKPaint { Color = new SKColor(75, 100, 125) };
            canvas.DrawRect(100, 100, 1000, 600, background);
            canvas.DrawRect(410, 1220, 200, 20, background);
        });
        var first = await Inspect(original);
        var second = await Inspect(changed);
        Assert.NotEqual(first.Source.Sha256, second.Source.Sha256);
        Assert.Equal(SceneDecisionKind.Selected, second.Selection.Decision.Kind);
        Assert.Equal(0, DialogueFrameGate.Difference(first.Signature!, second.Signature!));
    }

    /// <summary>移植回归验证：A short body or name stroke change is visible。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_short_body_or_name_stroke_change_is_visible(bool name)
    {
        var original = SceneSelectionTests.Draw(bodyGlyphCount: 1, nameGlyphCount: 1);
        var changed = Edit(original, canvas =>
        {
            using var background = new SKPaint { Color = new SKColor(35, 42, 51) };
            // Remove a small part of an existing vertical stroke, preserving the supported layout.
            canvas.DrawRect(name ? 1180 : 1268, name ? 1145 : 1219, 4, 5, background);
        });
        var first = await Inspect(original);
        var second = await Inspect(changed);
        Assert.Equal(SceneDecisionKind.Selected, first.Selection.Decision.Kind);
        Assert.Equal(SceneDecisionKind.Selected, second.Selection.Decision.Kind);
        Assert.Equal(first.Signature!.Identity, second.Signature!.Identity);
        Assert.True(DialogueFrameGate.Difference(first.Signature, second.Signature)
            > ContinuousFrameGateTests.Settings.SignatureDifferenceThreshold);
    }

    /// <summary>移植回归验证：Dimensions and layout form part of signature identity。</summary>
    [Fact]
    public async Task Dimensions_and_layout_form_part_of_signature_identity()
    {
        var first = await Inspect(SceneSelectionTests.Draw());
        var resized = await Inspect(SceneSelectionTests.Draw(width: 1280, height: 720));
        var title = await Inspect(SceneSelectionTests.Draw(title: true));
        Assert.NotEqual(first.Signature!.Identity, resized.Signature!.Identity);
        Assert.NotEqual(first.Signature.Identity, title.Signature!.Identity);
        Assert.Equal(1280, resized.Source.Width);
        Assert.Equal(720, resized.Source.Height);
    }

    /// <summary>移植回归验证：Rejected or unknown frames have no signature。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_or_unknown_frames_have_no_signature(bool unknown)
    {
        var result = await Inspect(unknown ? SceneSelectionTests.Draw(border: true) : SceneSelectionTests.Draw(body: false));
        Assert.Equal(unknown ? SceneDecisionKind.Unknown : SceneDecisionKind.Rejected, result.Selection.Decision.Kind);
        Assert.Null(result.Signature);
    }

    /// <summary>移植回归验证：Loaded catalog overload does not reopen a changed file。</summary>
    [Fact]
    public async Task Loaded_catalog_overload_does_not_reopen_a_changed_file()
    {
        await WithCatalog(async path =>
        {
            var catalog = new SceneCatalog();
            var loaded = await catalog.LoadAsync(path, default);
            await File.WriteAllTextAsync(path, "{}");
            var selector = new GenshinSceneSelector(catalog);
            var result = await selector.SelectAsync(SceneSelectionTests.Draw(), loaded, default);
            Assert.Equal(SceneDecisionKind.Selected, result.Decision.Kind);
            Assert.Equal(loaded.Sha256, result.Decision.CatalogSha256);
        });
    }

    /// <summary>移植回归验证：Next observation reloads catalog and uses its actual color ranges。</summary>
    [Fact]
    public async Task Next_observation_reloads_catalog_and_uses_its_actual_color_ranges()
    {
        await WithCatalog(async path =>
        {
            var image = SceneSelectionTests.Draw(bodyGlyphCount: 1);
            var first = await Inspect(image, path);
            var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            root["Colors"]!["White"]!["MinRed"] = 220;
            root["Colors"]!["White"]!["MinGreen"] = 220;
            root["Colors"]!["White"]!["MinBlue"] = 220;
            await File.WriteAllTextAsync(path, root.ToJsonString());
            var second = await Inspect(image, path);
            Assert.Equal(SceneDecisionKind.Selected, second.Selection.Decision.Kind);
            Assert.NotEqual(first.Selection.Decision.CatalogSha256, second.Selection.Decision.CatalogSha256);
            Assert.NotEqual(first.Signature!.Identity, second.Signature!.Identity);

            // New ink meets the original white range but not this observation's stricter catalog.
            var dimInk = Edit(image, canvas =>
            {
                using var dim = new SKPaint { Color = new SKColor(215, 215, 215) };
                canvas.DrawRect(1274, 1221, 4, 4, dim);
            });
            var third = await Inspect(dimInk, path);
            Assert.Equal(0, DialogueFrameGate.Difference(second.Signature, third.Signature!));
        });
    }

    /// <summary>移植回归验证：Signature tracks pixel distribution within the selected roi。</summary>
    [Fact]
    public async Task Signature_tracks_pixel_distribution_within_the_selected_roi()
    {
        var original = SceneSelectionTests.Draw(bodyGlyphCount: 1);
        var changed = Edit(original, canvas =>
        {
            using var background = new SKPaint { Color = new SKColor(35, 42, 51) };
            using var white = new SKPaint { Color = new SKColor(245, 245, 245) };
            canvas.DrawRect(1268, 1219, 4, 5, background);
            canvas.DrawRect(1280, 1222, 4, 5, white);
        });
        var first = await Inspect(original);
        var second = await Inspect(changed);
        Assert.Equal(first.Signature!.Regions[1].Sum(), second.Signature!.Regions[1].Sum());
        Assert.True(DialogueFrameGate.Difference(first.Signature, second.Signature) > .01);
    }

    /// <summary>移植回归验证：Inspect。</summary>
    private static Task<ContinuousFrameInspection> Inspect(byte[] bytes, string? path = null)
    {
        var catalog = new SceneCatalog();
        var inspector = new ContinuousFrameInspector(ContinuousFrameGateTests.Settings,
            new SceneSettings(path ?? SceneSelectionTests.CatalogPath), catalog, new GenshinSceneSelector(catalog));
        return inspector.InspectAsync(new(0, TimeSpan.Zero, bytes), default);
    }

    /// <summary>移植回归验证：Edit。</summary>
    private static byte[] Edit(byte[] bytes, Action<SKCanvas> draw)
    {
        using var bitmap = SKBitmap.Decode(bytes);
        using var canvas = new SKCanvas(bitmap);
        draw(canvas);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    /// <summary>移植回归验证：WithCatalog。</summary>
    private static async Task WithCatalog(Func<string, Task> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "QuestVoiceFrameInspection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "catalog.json");
        File.Copy(SceneSelectionTests.CatalogPath, path);
        await action(path);
    }
}
