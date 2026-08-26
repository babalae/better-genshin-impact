using Fischless.GameCapture;

namespace BetterGenshinImpact.UnitTest.CoreTests.CaptureTests;

public class CaptureModeExtensionsTests
{
    [Theory]
    [InlineData("BitBlt", CaptureModes.BitBlt)]
    [InlineData("bitblt", CaptureModes.BitBlt)]
    [InlineData("WindowsGraphicsCaptureHdr", CaptureModes.WindowsGraphicsCaptureHdr)]
    [InlineData(" WindowsGraphicsCaptureHdr ", CaptureModes.WindowsGraphicsCaptureHdr)]
    [InlineData("3", CaptureModes.WindowsGraphicsCaptureHdr)]
    public void ToCaptureMode_KnownName_ReturnsMode(string value, CaptureModes expected)
    {
        Assert.Equal(expected, value.ToCaptureMode());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("999")]
    public void ToCaptureMode_UnknownValue_Throws(string? value)
    {
        Assert.Throws<ArgumentException>(() => value.ToCaptureMode());
    }
}
