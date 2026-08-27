using Fischless.GameCapture.Graphics.Helpers;
using Fischless.GameCapture.Graphics;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace BetterGenshinImpact.UnitTest.CoreTests.CaptureTests;

public class HdrDisplayInformationTests
{
    /// <summary>
    /// 验证 <c>DisplayQueryFallback_RemainsUnknown</c> 所描述的行为。
    /// </summary>
    [Fact]
    public void DisplayQueryFallback_RemainsUnknown()
    {
        var fallback = HdrDisplayState.Unknown;

        Assert.Equal(HdrDisplayStateKind.Unknown, fallback.Kind);
        Assert.False(fallback.IsKnown);
        Assert.False(fallback.IsHdrEnabled);
        Assert.Equal(1f, fallback.SdrWhiteScale);
    }

    /// <summary>
    /// 验证 <c>DefaultDisplayState_IsUnknown</c> 所描述的行为。
    /// </summary>
    [Fact]
    public void DefaultDisplayState_IsUnknown()
    {
        Assert.Equal(HdrDisplayStateKind.Unknown, default(HdrDisplayState).Kind);
    }

    /// <summary>
    /// 验证 <c>HdrToSdrShader_CompilesForShaderModel5</c> 所描述的行为。
    /// </summary>
    [Fact]
    public void HdrToSdrShader_CompilesForShaderModel5()
    {
        using var shader = ShaderBytecode.Compile(
            HdrToSdrShader.Content,
            "CS_HDRtoSDR",
            "cs_5_0");

        Assert.False(shader.HasErrors, shader.Message);
    }

    /// <summary>
    /// 验证 <c>HdrOutputTexture_SupportsTypedUnorderedAccessView</c> 所描述的行为。
    /// </summary>
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

    /// <summary>
    /// 验证 <c>CalculateSdrWhiteScale_NormalizesSceneReferredWhite</c> 所描述的行为。
    /// </summary>
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

    /// <summary>
    /// 验证 <c>CalculateSdrWhiteScale_InvalidValue_UsesCompatibilityFallback</c> 所描述的行为。
    /// </summary>
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

    /// <summary>
    /// 验证 <c>ResolveHdrPipeline_ConfirmedSdr_UsesB8Pipeline</c> 所描述的行为。
    /// </summary>
    [Fact]
    public void ResolveHdrPipeline_ConfirmedSdr_UsesB8Pipeline()
    {
        var decision = GraphicsCapture.ResolveHdrPipeline(HdrDisplayState.Sdr);

        Assert.False(decision.IsHdrEnabled);
        Assert.Equal(1f, decision.SdrWhiteScale);
    }

    /// <summary>
    /// 验证 <c>ResolveHdrPipeline_ConfirmedHdr_UsesMeasuredWhiteLevel</c> 所描述的行为。
    /// </summary>
    [Fact]
    public void ResolveHdrPipeline_ConfirmedHdr_UsesMeasuredWhiteLevel()
    {
        var decision = GraphicsCapture.ResolveHdrPipeline(HdrDisplayState.CreateHdr(0.4f));

        Assert.True(decision.IsHdrEnabled);
        Assert.Equal(0.4f, decision.SdrWhiteScale);
    }

    /// <summary>
    /// 验证 <c>ResolveHdrPipeline_WhiteLevelUnavailable_KeepsFp16Pipeline</c> 所描述的行为。
    /// </summary>
    [Fact]
    public void ResolveHdrPipeline_WhiteLevelUnavailable_KeepsFp16Pipeline()
    {
        var decision = GraphicsCapture.ResolveHdrPipeline(HdrDisplayState.HdrWhiteLevelUnavailable);

        Assert.True(decision.IsHdrEnabled);
        Assert.Equal(HdrDisplayInformation.FallbackSdrWhiteScale, decision.SdrWhiteScale);
    }

    /// <summary>
    /// 验证 <c>ResolveHdrPipeline_Unknown_StopsCaptureStartup</c> 所描述的行为。
    /// </summary>
    [Fact]
    public void ResolveHdrPipeline_Unknown_StopsCaptureStartup()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GraphicsCapture.ResolveHdrPipeline(HdrDisplayState.Unknown));

        Assert.Contains("无法确认目标窗口所在显示器的 HDR 状态", exception.Message);
    }

    /// <summary>
    /// 验证稳定停留在同一显示器且没有错误时不会重复刷新 HDR 管线。
    /// </summary>
    [Fact]
    public void ShouldRefreshHdrDisplayPipeline_SameMonitorWithoutError_ReturnsFalse()
    {
        var monitor = new IntPtr(1);

        var shouldRefresh = GraphicsCapture.ShouldRefreshHdrDisplayPipeline(monitor, monitor, null);

        Assert.False(shouldRefresh);
    }

    /// <summary>
    /// 验证跨显示器移动时会刷新 HDR 管线。
    /// </summary>
    [Fact]
    public void ShouldRefreshHdrDisplayPipeline_DifferentMonitor_ReturnsTrue()
    {
        var shouldRefresh = GraphicsCapture.ShouldRefreshHdrDisplayPipeline(
            new IntPtr(1),
            new IntPtr(2),
            null);

        Assert.True(shouldRefresh);
    }

    /// <summary>
    /// 验证原显示器上的失败管线仍会进入恢复路径。
    /// </summary>
    [Fact]
    public void ShouldRefreshHdrDisplayPipeline_SameMonitorWithError_ReturnsTrue()
    {
        var monitor = new IntPtr(1);

        var shouldRefresh = GraphicsCapture.ShouldRefreshHdrDisplayPipeline(
            monitor,
            monitor,
            new InvalidOperationException("capture failed"));

        Assert.True(shouldRefresh);
    }
}

public class GraphicsCapturePerformancePolicyTests
{
    /// <summary>
    /// 验证 <c>ResolveTargetFrameInterval_ClampsToSafeRange</c> 所描述的行为。
    /// </summary>
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
