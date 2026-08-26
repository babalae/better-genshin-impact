namespace Fischless.GameCapture.Graphics.Helpers;

public static class HdrToSdrShader
{
    public static string Content =>
"""
// HLSL Compute Shader
Texture2D<half4> hdrTexture : register(t0);
RWTexture2D<unorm float4> sdrTexture : register(u0);

cbuffer HdrParameters : register(b0)
{
    float SdrWhiteScale;
    float3 Padding;
};

float3 LinearToSrgb(float3 linearColor)
{
    linearColor = max(linearColor, 0.0f);
    float3 low = linearColor * 12.92f;
    float3 high = 1.055f * pow(linearColor, 1.0f / 2.4f) - 0.055f;
    return lerp(low, high, step(0.0031308f, linearColor));
}

[numthreads(16, 16, 1)]
void CS_HDRtoSDR(uint3 id : SV_DispatchThreadID)
{
    uint width;
    uint height;
    hdrTexture.GetDimensions(width, height);
    if (id.x >= width || id.y >= height)
    {
        return;
    }

    half4 hdrColor = hdrTexture[id.xy];
    float3 normalizedLinearColor = saturate((float3)hdrColor.rgb * SdrWhiteScale);
    float3 srgbColor = LinearToSrgb(normalizedLinearColor);

    // The UAV is RGBA8. Swap R/B so the CPU can keep decoding its bytes as BGRA -> BGR.
    sdrTexture[id.xy] = (unorm float4)saturate(float4(srgbColor.b, srgbColor.g, srgbColor.r, hdrColor.a));
}
""";
}
