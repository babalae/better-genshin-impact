using Fischless.GameCapture;

namespace BetterGenshinImpact.UnitTest.CoreTests.CaptureTests;

public class CaptureModeExtensionsTests
{
    /// <summary>
    /// 验证 <c>ToCaptureMode_KnownName_ReturnsMode</c> 所描述的行为。
    /// </summary>
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

    /// <summary>
    /// 验证 <c>ToCaptureMode_UnknownValue_Throws</c> 所描述的行为。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("999")]
    public void ToCaptureMode_UnknownValue_Throws(string? value)
    {
        Assert.Throws<ArgumentException>(() => value.ToCaptureMode());
    }

    /// <summary>
    /// 验证转换失败时 <c>TryToCaptureMode</c> 会把输出参数恢复为默认值。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("999")]
    public void TryToCaptureMode_UnknownValue_ResetsOutParameter(string? value)
    {
        var success = value.TryToCaptureMode(out var mode);

        Assert.False(success);
        Assert.Equal(default, mode);
    }
}
