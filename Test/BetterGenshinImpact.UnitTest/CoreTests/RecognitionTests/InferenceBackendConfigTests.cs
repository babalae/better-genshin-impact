using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Recognition.OpenVino;
using Xunit;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

/// <summary>
///     三后端插件化的配置与纯函数层测试：
///     硬件加速配置的 JSON 往返（含旧格式兼容）、YOLO 共享解码的数值行为、
///     OpenVINO 草稿的边界规范化。全部不依赖原生运行库。
/// </summary>
public class InferenceBackendConfigTests
{
    // ---------------- 配置 JSON 往返 ----------------

    [Fact]
    public void HardwareAccelerationConfig_JsonRoundTrip_PreservesBackendNodes()
    {
        var config = new HardwareAccelerationConfig
        {
            FormatVersion = 1,
            InferenceDevice = InferenceDeviceType.Cpu,
            PreferredBackend = InferenceBackend.OpenVino,
        };
        config.OpenVino.UseOcr = true;
        config.OpenVino.DeviceId = "GPU.0";
        config.OpenVino.EnableModelCache = true;
        config.Ncnn.UseYolo = true;
        config.Ncnn.ComputeMode = NcnnComputeMode.Cpu;
        config.Ncnn.GpuIndex = 2;

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(config, options);
        var roundTripped = JsonSerializer.Deserialize<HardwareAccelerationConfig>(json, options);

        Assert.NotNull(roundTripped);
        Assert.Equal(InferenceBackend.OpenVino, roundTripped.PreferredBackend);
        Assert.True(roundTripped.OpenVino.UseOcr);
        Assert.Equal("GPU.0", roundTripped.OpenVino.DeviceId);
        Assert.True(roundTripped.OpenVino.EnableModelCache);
        Assert.True(roundTripped.Ncnn.UseYolo);
        Assert.Equal(NcnnComputeMode.Cpu, roundTripped.Ncnn.ComputeMode);
        Assert.Equal(2, roundTripped.Ncnn.GpuIndex);
        // 旧引擎设置字段同样保留（round-trip 兼容）
        Assert.NotNull(roundTripped.OpenVINO);
    }

    [Fact]
    public void HardwareAccelerationConfig_JsonWithoutBackendNodes_FallsBackToDefaults()
    {
        // 旧版配置（没有 ncnn / openVino 嵌套节点）：读取必须回落默认而不抛异常
        const string legacyJson = """
            {
              "formatVersion": 1,
              "inferenceDevice": 1,
              "cpuOcr": true,
              "cpu": { "deviceId": "CPU" },
              "directML": { "deviceId": "DXGI:0" }
            }
            """;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var config = JsonSerializer.Deserialize<HardwareAccelerationConfig>(legacyJson, options);

        Assert.NotNull(config);
        Assert.Equal(InferenceDeviceType.DirectML, config.InferenceDevice);
        Assert.Equal(InferenceBackend.Onnx, config.PreferredBackend);
        Assert.False(config.OpenVino.UseOcr);
        Assert.Equal("CPU", config.OpenVino.DeviceId);
        Assert.False(config.Ncnn.UseOcr);
        Assert.NotNull(config.Ncnn.DetTuning);
    }

    [Fact]
    public void HardwareAccelerationConfig_JsonWithCorruptedBackendNode_FallsBackToDefaultBackendConfig()
    {
        // 嵌套节点损坏（ncnn.gpuIndex 为字符串）：只回落该后端子配置，不影响其他字段读取
        const string json = """
            {
              "formatVersion": 1,
              "inferenceDevice": 0,
              "cpuOcr": true,
              "cpu": { "deviceId": "CPU" },
              "ncnn": { "useOcr": true, "gpuIndex": "broken" },
              "openVino": { "deviceId": 42 }
            }
            """;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var config = JsonSerializer.Deserialize<HardwareAccelerationConfig>(json, options);

        Assert.NotNull(config);
        Assert.Equal(InferenceDeviceType.Cpu, config.InferenceDevice);
        // 两个后端节点都损坏 → 各自回落默认
        Assert.False(config.Ncnn.UseOcr);
        Assert.Equal(0, config.Ncnn.GpuIndex);
        Assert.Equal("CPU", config.OpenVino.DeviceId);
    }

    [Fact]
    public void HardwareAccelerationConfig_JsonWithUnknownBackendEnum_FallsBackToOnnx()
    {
        const string json = """
            { "formatVersion": 1, "inferenceDevice": 0, "preferredBackend": 99, "cpuOcr": true }
            """;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var config = JsonSerializer.Deserialize<HardwareAccelerationConfig>(json, options);

        Assert.NotNull(config);
        // 越界枚举回落 Onnx（脚本等调用方写入未知值时同理）
        Assert.Equal(InferenceBackend.Onnx, config.PreferredBackend);
    }

    // ---------------- OpenVINO 草稿边界 ----------------

    [Fact]
    public void OpenVinoBackendSettings_Normalize_ClampsInvalidDeviceId()
    {
        var empty = new OpenVinoBackendSettings(true, false, "   ", false).Normalize();
        Assert.Equal("CPU", empty.DeviceId);
        Assert.True(empty.IsValid);

        var longName = new string('G', 200);
        var clamped = new OpenVinoBackendSettings(false, true, longName, false).Normalize();
        Assert.Equal(OpenVinoBackendSettings.MaxDeviceIdLength, clamped.DeviceId.Length);
        Assert.True(clamped.IsValid);

        Assert.False(new OpenVinoBackendSettings(false, false, "", false).IsValid);
    }

    // ---------------- YOLO 共享解码（纯函数） ----------------

    [Fact]
    public void YoloBackendShared_ResolveOutputLayout_UsesTensorShapeFirst()
    {
        // [1, 4+nc, N] 在 OpenVINO 输出 shape 为 (N, 4+nc)：anchor=N、attributes=4+nc
        var (anchors, attributes) = YoloBackendShared.ResolveOutputLayout(infoWidth: 8400, infoHeight: 9, infoChannels: null,
            count: 8400 * 9, classCount: 5);
        Assert.Equal((8400, 9), (anchors, attributes));

        // 元信息缺失时按元素数整除回退
        var (anchors2, attributes2) = YoloBackendShared.ResolveOutputLayout(null, null, null, 8400 * 9, 5);
        Assert.Equal((8400, 9), (anchors2, attributes2));

        // 无法整除 → 异常
        Assert.Throws<InvalidOperationException>(() =>
            YoloBackendShared.ResolveOutputLayout(null, null, null, 12345, 5));
    }

    [Fact]
    public void YoloBackendShared_DecodeAndNms_SuppressesSameClassOverlappingBoxes()
    {
        // 3 个候选：两个同类高重叠（应抑制 1 个），1 个异类重叠（保留）
        const int anchors = 3, attributes = 4 + 2, classes = 2;
        var output = new float[anchors * attributes];
        void WriteBox(int anchor, int cls, float conf, float cx, float cy, float w, float h)
        {
            output[anchor] = cx;
            output[anchors + anchor] = cy;
            output[2 * anchors + anchor] = w;
            output[3 * anchors + anchor] = h;
            output[(4 + cls) * anchors + anchor] = conf;
        }

        WriteBox(0, cls: 0, conf: 0.9f, cx: 10, cy: 10, w: 10, h: 10);
        WriteBox(1, cls: 0, conf: 0.8f, cx: 11, cy: 11, w: 10, h: 10);   // 与 0 同类几乎重合 → IoU≈0.68 > 0.45
        WriteBox(2, cls: 1, conf: 0.7f, cx: 10.5f, cy: 10.5f, w: 10, h: 10); // 异类重叠 → 保留

        var kept = YoloBackendShared.DecodeAndNmsCore(output, anchors, attributes, classes,
            confidenceThreshold: 0.3f, iouThreshold: 0.45f);

        Assert.Equal(2, kept.Count);
        Assert.Contains(kept, b => b.ClassIndex == 0 && b.Confidence > 0.85f);
        Assert.Contains(kept, b => b.ClassIndex == 1);
        // 低于阈值的候选直接排除
        Assert.All(kept, b => Assert.True(b.Confidence > 0.3f));
    }

    [Fact]
    public void YoloBackendShared_ComputeAdjustment_MapsModelCoordsBackToImage()
    {
        // 1280x720 原图 → 640x640 模型：内接缩放 0.5，横向填充 (640-640)/2=0、纵向 (640-360)/2=140
        var (padX, padY, ratio) = YoloBackendShared.ComputeAdjustment(new SixLabors.ImageSharp.Size(1280, 720), 640, 640);
        Assert.Equal(0, padX);
        Assert.Equal(140, padY);
        Assert.Equal(2f, ratio, precision: 2);
    }
}
