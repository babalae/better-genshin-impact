using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class GenshinHdrRegistryHelperTests
{
    [Theory]
    [InlineData("YuanShen", 1)]
    [InlineData("yuanshen.exe", 1)]
    [InlineData("GenshinImpact", 2)]
    [InlineData("GENSHINIMPACT.EXE", 2)]
    public void TryResolveEditionFromProcessName_DesktopClient_ReturnsEdition(
        string processName,
        int expected)
    {
        var found = GenshinHdrRegistryHelper.TryResolveEditionFromProcessName(
            processName,
            out var actual);

        Assert.True(found);
        Assert.Equal((GenshinGameEdition)expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chrome")]
    [InlineData("Genshin Impact Cloud Game")]
    [InlineData("Genshin Impact Cloud")]
    public void TryResolveEditionFromProcessName_NonDesktopClient_ReturnsUnknown(string? processName)
    {
        var found = GenshinHdrRegistryHelper.TryResolveEditionFromProcessName(
            processName,
            out var edition);

        Assert.False(found);
        Assert.Equal(GenshinGameEdition.Unknown, edition);
    }

    [Theory]
    [InlineData(@"D:\Games\YuanShen.exe", 1)]
    [InlineData(@"D:\Games\GenshinImpact.exe", 2)]
    public void TryResolveEditionFromExecutablePath_OfficialExecutable_ReturnsEdition(
        string executablePath,
        int expected)
    {
        var found = GenshinHdrRegistryHelper.TryResolveEditionFromExecutablePath(
            executablePath,
            out var actual);

        Assert.True(found);
        Assert.Equal((GenshinGameEdition)expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"D:\Games\CustomClient.exe")]
    [InlineData(@"D:\Games\Genshin Impact Cloud Game.exe")]
    public void TryResolveEditionFromExecutablePath_UnknownExecutable_ReturnsUnknown(string? executablePath)
    {
        var found = GenshinHdrRegistryHelper.TryResolveEditionFromExecutablePath(
            executablePath,
            out var edition);

        Assert.False(found);
        Assert.Equal(GenshinGameEdition.Unknown, edition);
    }

    [Fact]
    public void RegistryTarget_IsEditionSpecific()
    {
        Assert.Equal(
            GenshinHdrRegistryHelper.CnHdrRegistryParentKeyPath,
            GenshinHdrRegistryHelper.GetHdrRegistryParentKeyPath(GenshinGameEdition.Cn));
        Assert.Equal(
            GenshinHdrRegistryHelper.GlobalHdrRegistryParentKeyPath,
            GenshinHdrRegistryHelper.GetHdrRegistryParentKeyPath(GenshinGameEdition.Global));
        Assert.Null(GenshinHdrRegistryHelper.GetHdrRegistryParentKeyPath(GenshinGameEdition.Unknown));
    }
}
