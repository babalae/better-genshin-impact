using Fischless.GameCapture.Graphics.Helpers;
using Fischless.GameCapture.Graphics;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace BetterGenshinImpact.UnitTest.CoreTests.CaptureTests;

public class HdrDisplayInformationTests
{
    [Fact]
    public void HdrToSdrShader_CompilesForShaderModel5()
    {
        using var shader = ShaderBytecode.Compile(
            HdrToSdrShader.Content,
            "CS_HDRtoSDR",
            "cs_5_0");

        Assert.False(shader.HasErrors, shader.Message);
    }

    [Fact]
    public void HdrOutputTexture_SupportsTypedUnorderedAccessView()
    {
        using var device = new SharpDX.Direct3D11.Device(
            SharpDX.Direct3D.DriverType.Warp,
            DeviceCreationFlags.BgraSupport);
        using var texture = Direct3D11Helper.CreateOutputTexture(device, 16, 16);
        using var unorderedAccessView = new UnorderedAccessView(device, texture);

        Assert.Equal(Format.R8G8B8A8_UNorm, texture.Description.Format);
        Assert.False(unorderedAccessView.IsDisposed);
    }

    [Theory]
    [InlineData(80f, 1f)]
    [InlineData(200f, 0.4f)]
    [InlineData(320f, 0.25f)]
    [InlineData(400f, 0.2f)]
    public void CalculateSdrWhiteScale_NormalizesSceneReferredWhite(float whiteLevelNits, float expected)
    {
        var actual = HdrDisplayInformation.CalculateSdrWhiteScale(whiteLevelNits);

        Assert.Equal(expected, actual, 5);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void CalculateSdrWhiteScale_InvalidValue_UsesCompatibilityFallback(float whiteLevelNits)
    {
        var actual = HdrDisplayInformation.CalculateSdrWhiteScale(whiteLevelNits);

        Assert.Equal(HdrDisplayInformation.FallbackSdrWhiteScale, actual);
    }
}

public class GraphicsCapturePerformancePolicyTests
{
    [Theory]
    [InlineData(null, 16)]
    [InlineData(0, 16)]
    [InlineData(10, 16)]
    [InlineData(50, 50)]
    [InlineData(1500, 1000)]
    public void ResolveTargetFrameInterval_ClampsToSafeRange(int? requested, int expected)
    {
        Dictionary<string, object>? settings = requested is null
            ? null
            : new Dictionary<string, object>
            {
                [GraphicsCapture.TargetFrameIntervalSettingName] = requested.Value
            };

        var actual = GraphicsCapture.ResolveTargetFrameInterval(settings);

        Assert.Equal(expected, actual);
    }
}
