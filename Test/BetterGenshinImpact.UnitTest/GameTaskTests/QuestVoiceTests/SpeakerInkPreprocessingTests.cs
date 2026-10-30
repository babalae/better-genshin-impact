using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;
using SkiaSharp;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：SpeakerInkPreprocessingTests。</summary>
public sealed class SpeakerInkPreprocessingTests
{
    /// <summary>移植回归验证：Policy。</summary>
    private static SpeakerInkPolicy Policy => new("test-configured-ink", 175, 120, 0, 160, 10, 35, 75, 255);
    /// <summary>移植回归验证：Profile。</summary>
    private static SceneProfile Profile => new(20, 10,
        [new("name", OcrRegionKind.Speaker, 0, new(0, 0, 20, 5)), new("body", OcrRegionKind.Dialogue, 1, new(0, 5, 20, 10))]);

    /// <summary>移植回归验证：ConfiguredMaskChangesOnlySpeakerTensorAndPreservesSourceBoundsAndBody。</summary>
    [Fact]
    public async Task ConfiguredMaskChangesOnlySpeakerTensorAndPreservesSourceBoundsAndBody()
    {
        using var bitmap = Draw();
        var bytes = Encode(bitmap);
        var preprocessor = new ImagePreprocessor();
        var original = await preprocessor.PrepareAsync(bytes, Profile, default);
        var masked = await preprocessor.PrepareAsync(bytes, Profile, default, Policy);
        using var expected = new SKBitmap(20, 5);
        expected.Erase(SKColors.White);
        for (var y = 1; y < 4; y++) for (var x = 7; x < 10; x++) expected.SetPixel(x, y, SKColors.Black);
        Assert.Equal(ImagePreprocessor.ToTensor(expected).Data, masked.Tensors[0].Data);
        Assert.Equal(original.Tensors[1].Data, masked.Tensors[1].Data);
        Assert.Equal(original.Bounds, masked.Bounds);
        Assert.Equal(original.Width, masked.Width); Assert.Equal(original.Height, masked.Height);
        Assert.NotEqual(original.Tensors[0].Data, masked.Tensors[0].Data);
        Assert.Equal(bytes, Encode(bitmap));
    }

    /// <summary>移植回归验证：RealSelectorAttachesConfiguredPolicyOnlyToSelectedLayout。</summary>
    [Fact]
    public async Task RealSelectorAttachesConfiguredPolicyOnlyToSelectedLayout()
    {
        var catalog = new SceneCatalog();
        var loaded = await catalog.LoadAsync(SceneSelectionTests.CatalogPath, default);
        var selector = new GenshinSceneSelector(catalog);
        var selected = await selector.SelectAsync(SceneSelectionTests.Draw(), loaded, default);
        Assert.Equal(SceneDecisionKind.Selected, selected.Decision.Kind);
        var policy = Assert.IsType<SpeakerInkPolicy>(selected.SpeakerInk);
        var gold = loaded.Definition.Colors.NameGold;
        Assert.Equal(gold.MinRed, policy.MinRed); Assert.Equal(gold.MinGreen, policy.MinGreen);
        Assert.Equal(gold.MinBlue, policy.MinBlue); Assert.Equal(gold.MaxBlue, policy.MaxBlue);
        Assert.Equal(gold.MinRedGreen, policy.MinRedGreen); Assert.Equal(gold.MinGreenBlue, policy.MinGreenBlue);
        Assert.Equal(gold.MinRedBlue, policy.MinRedBlue); Assert.Equal(gold.MaxSpread, policy.MaxSpread);
        var rejected = await selector.SelectAsync(SceneSelectionTests.Draw(name: false), loaded, default);
        Assert.Null(rejected.SpeakerInk);
    }

    /// <summary>移植回归验证：Draw。</summary>
    private static SKBitmap Draw()
    {
        var bitmap = new SKBitmap(20, 10); bitmap.Erase(SKColors.DarkSlateBlue);
        for (var y = 1; y < 4; y++) for (var x = 7; x < 10; x++) bitmap.SetPixel(x, y, new(255, 200, 0));
        for (var y = 1; y < 4; y++) bitmap.SetPixel(15, y, SKColors.White);
        return bitmap;
    }
    /// <summary>移植回归验证：Encode。</summary>
    private static byte[] Encode(SKBitmap bitmap)
    { using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray(); }
}
