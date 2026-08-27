using BetterGenshinImpact.GameTask;
using Microsoft.Win32;

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

    [Fact]
    public void FullRegistryTarget_IsEditionSpecific()
    {
        Assert.Equal(
            @"HKEY_CURRENT_USER\Software\miHoYo\原神\WINDOWS_HDR_ON_h3132281285",
            GenshinHdrRegistryHelper.GetHdrRegistryFullValuePath(GenshinGameEdition.Cn));
        Assert.Equal(
            @"HKEY_CURRENT_USER\Software\miHoYo\Genshin Impact\WINDOWS_HDR_ON_h3132281285",
            GenshinHdrRegistryHelper.GetHdrRegistryFullValuePath(GenshinGameEdition.Global));
        Assert.Null(GenshinHdrRegistryHelper.GetHdrRegistryFullValuePath(GenshinGameEdition.Unknown));
    }

    [Fact]
    public void TryDisableHdr_Enabled_PersistsMarkerBeforeRegistryWrite()
    {
        var operations = new List<string>();

        var result = GenshinHdrRegistryHelper.TryDisableHdr(
            GenshinGameEdition.Cn,
            _ =>
            {
                operations.Add("marker");
                return true;
            },
            _ => new GenshinHdrRegistryReadResult(
                GenshinHdrRegistryValueState.Enabled,
                RegistryValueKind.DWord),
            (_, _) =>
            {
                operations.Add("registry");
                return new GenshinHdrRegistryWriteResult(true);
            });

        Assert.Equal(GenshinHdrDisableStatus.Disabled, result.Status);
        Assert.Equal(["marker", "registry"], operations);
    }

    [Fact]
    public void TryDisableHdr_MarkerPersistenceFails_DoesNotWriteRegistry()
    {
        var registryWriteCalled = false;

        var result = GenshinHdrRegistryHelper.TryDisableHdr(
            GenshinGameEdition.Cn,
            _ => false,
            _ => new GenshinHdrRegistryReadResult(
                GenshinHdrRegistryValueState.Enabled,
                RegistryValueKind.DWord),
            (_, _) =>
            {
                registryWriteCalled = true;
                return new GenshinHdrRegistryWriteResult(true);
            });

        Assert.Equal(GenshinHdrDisableStatus.PreparationFailed, result.Status);
        Assert.False(registryWriteCalled);
    }

    [Fact]
    public void TryDisableHdr_AlreadyDisabled_DoesNotPersistOrWrite()
    {
        var markerCalled = false;
        var registryWriteCalled = false;

        var result = GenshinHdrRegistryHelper.TryDisableHdr(
            GenshinGameEdition.Global,
            _ =>
            {
                markerCalled = true;
                return true;
            },
            _ => new GenshinHdrRegistryReadResult(GenshinHdrRegistryValueState.Disabled),
            (_, _) =>
            {
                registryWriteCalled = true;
                return new GenshinHdrRegistryWriteResult(true);
            });

        Assert.Equal(GenshinHdrDisableStatus.AlreadyDisabled, result.Status);
        Assert.False(markerCalled);
        Assert.False(registryWriteCalled);
    }

    [Fact]
    public void TryDisableHdr_RegistryReadFails_ReturnsExplicitFailure()
    {
        var expected = new UnauthorizedAccessException("denied");

        var result = GenshinHdrRegistryHelper.TryDisableHdr(
            GenshinGameEdition.Cn,
            prepareBeforeWrite: null,
            _ => new GenshinHdrRegistryReadResult(
                GenshinHdrRegistryValueState.ReadFailed,
                Error: expected),
            (_, _) => new GenshinHdrRegistryWriteResult(true));

        Assert.Equal(GenshinHdrDisableStatus.ReadFailed, result.Status);
        Assert.Same(expected, result.Error);
    }

    [Fact]
    public void TryDisableHdr_RegistryWriteFails_ReturnsExplicitFailure()
    {
        var expected = new UnauthorizedAccessException("denied");

        var result = GenshinHdrRegistryHelper.TryDisableHdr(
            GenshinGameEdition.Cn,
            prepareBeforeWrite: null,
            _ => new GenshinHdrRegistryReadResult(
                GenshinHdrRegistryValueState.Enabled,
                RegistryValueKind.DWord),
            (_, _) => new GenshinHdrRegistryWriteResult(false, expected));

        Assert.Equal(GenshinHdrDisableStatus.WriteFailed, result.Status);
        Assert.Same(expected, result.Error);
    }
}
