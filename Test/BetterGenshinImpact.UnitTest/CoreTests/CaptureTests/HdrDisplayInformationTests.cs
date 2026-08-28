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

    [Fact]
    public void ResolveHdrPipeline_ConfirmedSdr_UsesB8Pipeline()
    {
        var decision = GraphicsCapture.ResolveHdrPipeline(HdrDisplayState.Sdr);

        Assert.False(decision.IsHdrEnabled);
        Assert.Equal(1f, decision.SdrWhiteScale);
    }

    [Fact]
    public void ResolveHdrPipeline_ConfirmedHdr_UsesMeasuredWhiteLevel()
    {
        var decision = GraphicsCapture.ResolveHdrPipeline(HdrDisplayState.CreateHdr(0.4f));

        Assert.True(decision.IsHdrEnabled);
        Assert.Equal(0.4f, decision.SdrWhiteScale);
    }

    [Fact]
    public void ResolveHdrPipeline_WhiteLevelUnavailable_KeepsFp16Pipeline()
    {
        var decision = GraphicsCapture.ResolveHdrPipeline(HdrDisplayState.HdrWhiteLevelUnavailable);

        Assert.True(decision.IsHdrEnabled);
        Assert.Equal(HdrDisplayInformation.FallbackSdrWhiteScale, decision.SdrWhiteScale);
    }

    [Fact]
    public void ResolveHdrPipeline_Unknown_UsesLegacyHdrExposure()
    {
        var decision = GraphicsCapture.ResolveHdrPipeline(HdrDisplayState.Unknown);

        Assert.True(decision.IsHdrEnabled);
        Assert.Equal(HdrDisplayInformation.FallbackSdrWhiteScale, decision.SdrWhiteScale);
    }
}
