namespace Fischless.GameCapture.Graphics.Helpers;

public static class HdrToSdrShader
{
    public static string Content =>
"""
// HLSL Compute Shader
Texture2D<half4> hdrTexture : register(t0);
RWTexture2D<unorm float4> sdrTexture : register(u0);

// http://chilliant.blogspot.com/2012/08/srgb-approximations-for-hlsl.html
float3 LinearToSRGB(float3 RGB)
{
    float3 S1 = sqrt(RGB);
    float3 S2 = sqrt(S1);
    float3 S3 = sqrt(S2);
    return 0.662002687f * S1 + 0.684122060f * S2 - 0.323583601f * S3 - 0.0225411470f * RGB;
}

[numthreads(16, 16, 1)]
void CS_HDRtoSDR(uint3 id : SV_DispatchThreadID)
{
    // Load color
    half4 hdrColor = hdrTexture[id.xy];

    // HDR -> SDR (exposure)
    float4 exposedColor = saturate(float4(hdrColor.rgb * 0.25, hdrColor.a));

    // Linear RGB -> sRGB
    float4 srgbColor = float4(LinearToSRGB(exposedColor.rgb), exposedColor.a);

    // Store color
    sdrTexture[id.xy] = (unorm float4)saturate(srgbColor);
}
""";
}
