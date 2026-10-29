using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：OcrAlgorithmTests。</summary>
public sealed class OcrAlgorithmTests
{
    /// <summary>移植回归验证：Ctc collapses adjacent tokens before blanks and keeps unicode tokens。</summary>
    [Fact]
    public void Ctc_collapses_adjacent_tokens_before_blanks_and_keeps_unicode_tokens()
    {
        string[] tokens = ["", "𠮷", "好", " "];
        float[] probabilities = [.1f,.9f,0,0, 0,.8f,.2f,0, .9f,.1f,0,0, 0,.7f,.3f,0, 0,0,.6f,.4f];
        var result = CtcDecoder.Decode(probabilities, 5, tokens);
        Assert.Equal("𠮷𠮷好", result.Text);
        Assert.Equal((.9+.7+.6)/3, result.Confidence, 6);
    }

    /// <summary>移植回归验证：Ctc empty blanks and invalid shape are explicit。</summary>
    [Fact]
    public void Ctc_empty_blanks_and_invalid_shape_are_explicit()
    {
        Assert.Equal(new OcrText("", 0), CtcDecoder.Decode([1,0,1,0], 2, ["", "字"]));
        Assert.Throws<OcrException>(() => CtcDecoder.Decode([1], 2, ["", "字"]));
    }

    /// <summary>移植回归验证：Scaling uses floor left ceil right and preserves image edges。</summary>
    [Fact]
    public void Scaling_uses_floor_left_ceil_right_and_preserves_image_edges()
    {
        Assert.Equal(new PixelRect(0,0,101,57), ImagePreprocessor.Scale(new(0,0,2560,1440),2560,1440,101,57));
        Assert.Equal(new PixelRect(0,0,2,2), ImagePreprocessor.Scale(new(1,1,3,3),4,4,2,2));
    }

    /// <summary>移植回归验证：Assembly orders lines ignores empty regions and keeps raw punctuation。</summary>
    [Fact]
    public void Assembly_orders_lines_ignores_empty_regions_and_keeps_raw_punctuation()
    {
        var regions = new[] { Region(OcrRegionKind.Dialogue,2,"第二行！", .99), Region(OcrRegionKind.Speaker,0,"幻影",.5),
            Region(OcrRegionKind.Dialogue,1,"  第一行…  ",.98), Region(OcrRegionKind.Dialogue,3,"",1) };
        var accepted = OcrResultAssembler.Accept(regions, .9, .8);
        Assert.Equal("第一行…第二行！", OcrResultAssembler.Dialogue(accepted));
        Assert.Null(OcrResultAssembler.Speaker(accepted));
        Assert.Equal(OcrRejectionReason.LowConfidence, accepted[1].RejectionReason);
        Assert.Equal("  第一行…  ", accepted[2].RawText);
        Assert.Equal(OcrRejectionReason.Empty, accepted[3].RejectionReason);
    }

    /// <summary>移植回归验证：Region。</summary>
    private static OcrRegionResult Region(OcrRegionKind kind,int order,string raw,double confidence) =>
        new(new(kind.ToString(),kind,order,new(0,0,1,1)),new(0,0,1,1),raw,"",confidence,OcrRejectionReason.None);

    /// <summary>移植回归验证：Low confidence dialogue is recorded but not spoken and threshold is inclusive。</summary>
    [Fact]
    public void Low_confidence_dialogue_is_recorded_but_not_spoken_and_threshold_is_inclusive()
    {
        var accepted=OcrResultAssembler.Accept([Region(OcrRegionKind.Dialogue,1,"幻影",.79),
            Region(OcrRegionKind.Dialogue,2,"有效。",.8)],.9,.8);
        Assert.Equal("幻影",accepted[0].RawText);
        Assert.Equal(OcrRejectionReason.LowConfidence,accepted[0].RejectionReason);
        Assert.Equal("有效。",OcrResultAssembler.Dialogue(accepted));
    }
}
