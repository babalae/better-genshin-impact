using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class GenshinHdrHelperTests
{
    [Theory]
    [InlineData("AutoHDREnable=2097;", "AutoHDREnable=2096;", "2097")]
    [InlineData("AutoHDREnable=6193;", "AutoHDREnable=2096;", "6193")]
    [InlineData("AutoHDREnable=unknown;", "AutoHDREnable=2096;", "unknown")]
    [InlineData("AutoHDREnable=;", "AutoHDREnable=2096;", "")]
    [InlineData("AutoHDREnable=2096;", "AutoHDREnable=2096;", "2096")]
    [InlineData("GpuPreference=2;AutoHDREnable=6193;SwapEffectUpgradeEnable=1;",
        "GpuPreference=2;AutoHDREnable=2096;SwapEffectUpgradeEnable=1;", "6193")]
    [InlineData("GpuPreference=2;", "GpuPreference=2;AutoHDREnable=2096;", null)]
    [InlineData("GpuPreference=2", "GpuPreference=2;AutoHDREnable=2096", null)]
    [InlineData("", "AutoHDREnable=2096;", null)]
    public void DisableAutoHdr_PreservesOtherPreferencesAndOriginalValue(
        string preferences, string expected, string? expectedOriginal)
    {
        Assert.True(GenshinHdrHelper.TryUpdateAutoHdrPreference(preferences, "2096", out var updated, out var original));
        Assert.Equal(expected, updated);
        Assert.Equal(expectedOriginal, original);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GpuPreference=2;")]
    [InlineData("GpuPreference=2")]
    [InlineData("GpuPreference=2;;Unknown=a=b;")]
    [InlineData("AutoHDREnable=6193;GpuPreference=2;")]
    [InlineData("GpuPreference=2;autohdrenable=unknown;")]
    [InlineData("AutoHDREnable=;GpuPreference=2;")]
    public void RestoreAutoHdr_RestoresExactOriginalFieldAndMissingState(string preferences)
    {
        Assert.True(GenshinHdrHelper.TryUpdateAutoHdrPreference(preferences, "2096", out var disabled, out var original));
        Assert.True(GenshinHdrHelper.TryUpdateAutoHdrPreference(disabled, original, out var restored, out _));
        Assert.Equal(preferences, restored);
    }

    [Theory]
    [InlineData("AutoHDREnable=6193;", "6193")]
    [InlineData("", null)]
    public void RestoreAutoHdr_PreservesPreferencesChangedDuringStartup(string preferences, string? original)
    {
        Assert.True(GenshinHdrHelper.TryUpdateAutoHdrPreference(preferences, "2096", out var disabled, out _));
        var current = disabled + "GpuPreference=1;Unknown=a=b;";

        Assert.True(GenshinHdrHelper.TryUpdateAutoHdrPreference(current, original, out var restored, out var currentAutoHdr));
        Assert.Equal("2096", currentAutoHdr);
        Assert.Equal(preferences + "GpuPreference=1;Unknown=a=b;", restored);
    }

    [Theory]
    [InlineData("MalformedEntry;")]
    [InlineData("=value;")]
    [InlineData("AutoHDREnable=2097;AutoHDREnable=6193;")]
    [InlineData("AutoHDREnable=2097;autohdrenable=6193;")]
    public void UpdateAutoHdr_RejectsMalformedOrDuplicateFieldsWithoutChangingInput(string preferences)
    {
        Assert.False(GenshinHdrHelper.TryUpdateAutoHdrPreference(preferences, "2096", out var updated, out _));
        Assert.Equal(preferences, updated);
    }
}
