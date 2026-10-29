using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：ContinuousFrameGateTests。</summary>
public sealed class ContinuousFrameGateTests
{
    /// <summary>移植回归验证：Settings。</summary>
    internal static ContinuousSettings Settings => new(350, 3, 1500, 600, 2000, 256, 32, .01);
    /// <summary>移植回归验证：Frame。</summary>
    internal static ContinuousFrameSnapshot Frame(long n, int ms) => new(n, TimeSpan.FromMilliseconds(ms), new byte[] { 1, 2, 3 });
    /// <summary>移植回归验证：Inspection。</summary>
    internal static ContinuousFrameInspection Inspection(int ink = 100, string identity = "same", bool selected = true) => new(
        new("sha", 1920, 1080), new(new(selected ? SceneDecisionKind.Selected : SceneDecisionKind.Rejected,
            selected ? "layout" : null, "test", "v1", "catalog", new Dictionary<string, double>()),
            selected ? new(1920, 1080, []) : null), selected ? new(identity, new[] { new[] { ink, 0, 0 } }) : null);
    /// <summary>移植回归验证：SnapshotCopiesBytesAndDoesNotExposeItsStorage。</summary>
    [Fact] public void SnapshotCopiesBytesAndDoesNotExposeItsStorage()
    {
        var bytes = new byte[] { 1, 2, 3 }; var frame = new ContinuousFrameSnapshot(1, TimeSpan.Zero, bytes);
        bytes[0] = 8;
        Assert.Equal(1, frame.Bytes.Span[0]);
        Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81", frame.Sha256);
    }
    /// <summary>移植回归验证：StabilityRequiresDurationAndCount。</summary>
    [Fact] public void StabilityRequiresDurationAndCount()
    {
        var gate = new DialogueFrameGate(Settings);
        Assert.True(gate.Observe(Frame(0, 0), Inspection()).Invalidate);
        Assert.False(gate.Observe(Frame(1, 400), Inspection()).Recognize);
        Assert.True(gate.Observe(Frame(2, 401), Inspection()).Recognize);
        Assert.False(gate.Observe(Frame(3, 402), Inspection()).Recognize);
    }
    /// <summary>移植回归验证：RecheckDoesNotInvalidateAndAnchorDoesNotDrift。</summary>
    [Fact] public void RecheckDoesNotInvalidateAndAnchorDoesNotDrift()
    {
        var gate = new DialogueFrameGate(Settings with { SignatureDifferenceThreshold = .1 });
        gate.Observe(Frame(0, 0), Inspection()); gate.Observe(Frame(1, 200), Inspection(105));
        Assert.True(gate.Observe(Frame(2, 400), Inspection(109)).Recognize);
        Assert.True(gate.Observe(Frame(3, 500), Inspection(115)).Invalidate);
        gate.Observe(Frame(4, 700), Inspection(115)); gate.Observe(Frame(5, 900), Inspection(115));
        var recheck = gate.Observe(Frame(6, 2200), Inspection(115));
        Assert.False(recheck.Invalidate); Assert.False(recheck.Recognize);
        Assert.True(gate.Observe(Frame(7, 2900), Inspection(115)).Recognize);
    }
    /// <summary>移植回归验证：AbsenceJitterThenSustainedDepartureResetsOnce。</summary>
    [Fact] public void AbsenceJitterThenSustainedDepartureResetsOnce()
    {
        var gate = new DialogueFrameGate(Settings);
        gate.Observe(Frame(0, 0), Inspection());
        Assert.False(gate.Observe(Frame(1, 100), Inspection(selected: false)).Leave);
        Assert.False(gate.Observe(Frame(2, 700), Inspection(selected: false)).Leave);
        Assert.False(gate.Observe(Frame(3, 900), Inspection(selected: false)).Leave);
        Assert.True(gate.Observe(Frame(4, 1000), Inspection()).Invalidate);
    }
    /// <summary>移植回归验证：GapAndIdentityChangeResetStability。</summary>
    [Fact] public void GapAndIdentityChangeResetStability()
    {
        var gate = new DialogueFrameGate(Settings);
        gate.Observe(Frame(0, 0), Inspection());
        var gap = gate.Observe(Frame(1, 1600), Inspection());
        Assert.True(gap.Invalidate); Assert.False(gap.Leave);
        Assert.False(gate.Stable);
        Assert.True(gate.Observe(Frame(2, 1700), Inspection(identity: "catalog2")).Invalidate);
    }
    /// <summary>移植回归验证：IdentityChangesAlwaysResetEvenAtMaximumDifferenceThreshold。</summary>
    [Fact] public void IdentityChangesAlwaysResetEvenAtMaximumDifferenceThreshold()
    {
        var gate = new DialogueFrameGate(Settings with { SignatureDifferenceThreshold = 1 });
        gate.Observe(Frame(0, 0), Inspection());
        gate.Observe(Frame(1, 200), Inspection());
        Assert.True(gate.Observe(Frame(2, 400), Inspection()).Recognize);
        var changed = gate.Observe(Frame(3, 500), Inspection(identity: "new-geometry"));
        Assert.True(changed.Invalidate);
        Assert.False(changed.Recognize);
        Assert.False(gate.Stable);
    }
    /// <summary>移植回归验证：TypewriterChangesRestartDurationAndMinimumFrameCount。</summary>
    [Fact] public void TypewriterChangesRestartDurationAndMinimumFrameCount()
    {
        var gate = new DialogueFrameGate(Settings);
        gate.Observe(Frame(0, 0), Inspection(10));
        Assert.False(gate.Observe(Frame(1, 200), Inspection(20)).Recognize);
        Assert.False(gate.Observe(Frame(2, 400), Inspection(30)).Recognize);
        Assert.False(gate.Observe(Frame(3, 600), Inspection(30)).Recognize);
        Assert.True(gate.Observe(Frame(4, 800), Inspection(30)).Recognize);
    }
    /// <summary>移植回归验证：OrderingIsStrict。</summary>
    [Fact] public void OrderingIsStrict()
    {
        var gate = new DialogueFrameGate(Settings); gate.Observe(Frame(2, 10), Inspection());
        Assert.Throws<ArgumentException>(() => gate.Observe(Frame(2, 11), Inspection()));
        Assert.Throws<ArgumentException>(() => gate.Observe(Frame(3, 10), Inspection()));
    }
    /// <summary>移植回归验证：StrictSettingsRejectMalformed。</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"StableMilliseconds\":350,\"StableMilliseconds\":351}")]
    [InlineData("{\"Extra\":1}")]
    public void StrictSettingsRejectMalformed(string json) => Assert.ThrowsAny<ArgumentException>(() => ContinuousSettingsLoader.Parse(Encoding.UTF8.GetBytes(json)));
    /// <summary>移植回归验证：StrictSettingsAcceptCompleteInputAndRejectExtraOrDuplicateFields。</summary>
    [Fact] public void StrictSettingsAcceptCompleteInputAndRejectExtraOrDuplicateFields()
    {
        var json = JsonSerializer.Serialize(Settings);
        Assert.Equal(Settings, ContinuousSettingsLoader.Parse(Encoding.UTF8.GetBytes(json)));
        var completeFields = json[..^1];
        Assert.Throws<ArgumentException>(() => ContinuousSettingsLoader.Parse(Encoding.UTF8.GetBytes(completeFields + ",\"Extra\":1}")));
        Assert.Throws<ArgumentException>(() => ContinuousSettingsLoader.Parse(Encoding.UTF8.GetBytes(completeFields + ",\"StableMilliseconds\":350}")));
    }
    /// <summary>移植回归验证：SettingsValidateAllBoundsAndRelationships。</summary>
    [Fact] public void SettingsValidateAllBoundsAndRelationships()
    {
        ContinuousSettingsLoader.Validate(Settings);
        Assert.Throws<ArgumentException>(() => ContinuousSettingsLoader.Validate(Settings with { SignatureDifferenceThreshold = double.NaN }));
        Assert.Throws<ArgumentException>(() => ContinuousSettingsLoader.Validate(Settings with { SignatureRows = 10000 }));
        Assert.Throws<ArgumentException>(() => ContinuousSettingsLoader.Validate(Settings with { RecheckMilliseconds = 100 }));
    }
}
