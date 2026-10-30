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
using OpenCvSharp;
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
        try
        {
            using var expected = new SKBitmap(20, 5);
            expected.Erase(SKColors.White);
            for (var y = 1; y < 4; y++) for (var x = 7; x < 10; x++) expected.SetPixel(x, y, SKColors.Black);
            using var expectedLine = ImagePreprocessor.ToMat(expected);
            Assert.True(SameImage(expectedLine, masked.Lines[0]));
            Assert.True(SameImage(original.Lines[1], masked.Lines[1]));
            Assert.Equal(original.Bounds, masked.Bounds);
            Assert.Equal(original.Width, masked.Width); Assert.Equal(original.Height, masked.Height);
            Assert.False(SameImage(original.Lines[0], masked.Lines[0]));
            Assert.Equal(bytes, Encode(bitmap));
        }
        finally
        {
            foreach (var line in original.Lines.Concat(masked.Lines)) line.Dispose();
        }
    }

    /// <summary>两张图像尺寸、类型和每个像素都相同。</summary>
    private static bool SameImage(Mat first, Mat second)
    {
        if (first.Size() != second.Size() || first.Type() != second.Type()) return false;
        using var difference = new Mat();
        Cv2.Absdiff(first, second, difference);
        using var singleChannel = difference.Reshape(1);
        return Cv2.CountNonZero(singleChannel) == 0;
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
