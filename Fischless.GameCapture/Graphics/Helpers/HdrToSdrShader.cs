namespace Fischless.GameCapture.Graphics.Helpers;

public static class HdrToSdrShader
{
    public static string Content =>
"""
// HLSL Compute Shader
Texture2D<half4> hdrTexture : register(t0);
RWTexture2D<unorm float4> sdrTexture : register(u0);

[numthreads(16, 16, 1)]
void CS_HDRtoSDR(uint3 id : SV_DispatchThreadID)
{
    // Load color
    half4 hdrColor = hdrTexture[id.xy];

    // HDR -> SDR (exposure)
    float4 exposedColor = float4(hdrColor.rgb * 0.25, hdrColor.a);

    // Linear RGB -> sRGB
    float4 srgbColor = float4(pow(exposedColor.rgb, 1 / 2.2), exposedColor.a);

    // Store color
    sdrTexture[id.xy] = (unorm float4)saturate(srgbColor);
}
""";
}
