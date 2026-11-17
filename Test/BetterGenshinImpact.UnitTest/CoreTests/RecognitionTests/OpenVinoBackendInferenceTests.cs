using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenVino;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Compunet.YoloSharp.Metadata;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Generic;
using Xunit;
using Image = SixLabors.ImageSharp.Image;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

/// <summary>
///     OpenVINO（C# API）独立后端的端到端推理验证：直接加载应用内置的 ONNX 模型文件，
///     验证 det / rec / YOLO 三条链路的会话创建、动态形状推理与输出形状。
///     运行库未安装时（Plugins/OpenVINO 缺失）跳过，不作为失败。
/// </summary>
[Collection("Sequential")]
public class OpenVinoBackendInferenceTests : IDisposable
{
    private readonly string _appBase = AppContext.BaseDirectory;

    public void Dispose() { }

    private bool RuntimeAvailable()
    {
        foreach (var file in OpenVinoRuntime.RequiredFiles)
        {
            if (!File.Exists(Path.Combine(_appBase, "Plugins", "OpenVINO", file))) return false;
        }
        return true;
    }

    private static string ModelPath(string relative)
        => Path.Combine(AppContext.BaseDirectory, relative.Replace('\\', Path.DirectorySeparatorChar));

    [Fact]
    public void RuntimeInstaller_Inspect_MatchesRuntimeLayout()
    {
        var installer = new OpenVinoRuntimeInstaller(_appBase, null!, null!);
        var installation = installer.Inspect();
        if (!RuntimeAvailable())
        {
            Assert.Equal(InferenceInstallationState.Missing, installation.State);
        }
        else
        {
            Assert.Equal(InferenceInstallationState.Installed, installation.State);
        }
    }

    [Fact]
    public void DetSession_OpenVino_RunsDynamicShapes()
    {
        if (!RuntimeAvailable()) return;
        var model = BgiOnnxModel.PaddleOcrDetV6;
        Assert.True(File.Exists(model.ModalPath), $"det 模型不存在: {model.ModalPath}");

        using var session = new OpenVinoSession(model.ModalPath, "CPU", enableCache: false);
        Assert.False(string.IsNullOrWhiteSpace(session.InputName));

        // 320x320 随机归一化输入（det 动态形状）：输出应为 [1,1,H,W] 且数值有限
        const int side = 320;
        var tensor = new DenseTensor<float>(new[] { 1, 3, side, side });
        for (var i = 0; i < tensor.Length; i++) tensor.Buffer.Span[i] = (i % 7) / 7f - 0.5f;
        var outputs = session.Run(tensor);
        var output = Assert.Single(outputs);
        Assert.Equal(new[] { 1, 1, side, side }, output.Shape);
        Assert.All(output.Data, v => Assert.True(float.IsFinite(v)));
    }

    [Fact]
    public void RecSession_OpenVino_SupportsBatch()
    {
        if (!RuntimeAvailable()) return;
        var model = BgiOnnxModel.PaddleOcrRecV6;
        Assert.True(File.Exists(model.ModalPath), $"rec 模型不存在: {model.ModalPath}");

        using var session = new OpenVinoSession(model.ModalPath, "CPU", enableCache: false);
        // 批量 2、宽度 320：输出应为 [2, 类数, 时间步]
        const int width = 320;
        const int height = 48;
        var tensor = new DenseTensor<float>(new[] { 2, 3, height, width });
        for (var i = 0; i < tensor.Length; i++) tensor.Buffer.Span[i] = (i % 11) / 11f - 0.5f;
        var outputs = session.Run(tensor);
        var output = Assert.Single(outputs);
        Assert.Equal(2, output.Shape[0]);
        Assert.Equal(3, output.Shape.Length);
        Assert.All(output.Data, v => Assert.True(float.IsFinite(v)));
    }

    [Fact]
    public void RecSession_OpenVino_FixedShape_PadsNarrowInput()
    {
        if (!RuntimeAvailable()) return;
        var model = BgiOnnxModel.PaddleOcrRecV6;
        Assert.True(File.Exists(model.ModalPath), $"rec 模型不存在: {model.ModalPath}");

        // 模拟 NPU 静态形状路径（在 CPU 上验证 reshape + padding 机制）：固定 [1,3,48,320]
        const int width = 320;
        const int height = 48;
        using var fixedSession = new OpenVinoSession(model.ModalPath, "CPU", enableCache: false,
            fixedInputShape: [1, 3, height, width]);

        // 窄输入（160 宽）自动补零到 320：输出时间步按固定宽度展开，数值有限
        var narrow = new DenseTensor<float>(new[] { 1, 3, height, 160 });
        for (var i = 0; i < narrow.Length; i++) narrow.Buffer.Span[i] = (i % 11) / 11f - 0.5f;
        var paddedOutput = Assert.Single(fixedSession.Run(narrow));
        Assert.Equal(1, paddedOutput.Shape[0]);
        Assert.Equal(3, paddedOutput.Shape.Length);
        Assert.All(paddedOutput.Data, v => Assert.True(float.IsFinite(v)));

        // 恰好满宽输入与动态形状会话结果一致（无 padding 分支的正确性基准）
        var full = new DenseTensor<float>(new[] { 1, 3, height, width });
        for (var i = 0; i < full.Length; i++) full.Buffer.Span[i] = (i % 11) / 11f - 0.5f;
        var fixedOutput = Assert.Single(fixedSession.Run(full));
        using var dynamicSession = new OpenVinoSession(model.ModalPath, "CPU", enableCache: false);
        var dynamicOutput = Assert.Single(dynamicSession.Run(full));
        Assert.Equal(dynamicOutput.Shape, fixedOutput.Shape);
        Assert.Equal(dynamicOutput.Data.Length, fixedOutput.Data.Length);
        for (var i = 0; i < fixedOutput.Data.Length; i++)
            Assert.True(Math.Abs(fixedOutput.Data[i] - dynamicOutput.Data[i]) < 1e-4f,
                $"固定形状与动态形状输出不一致（索引 {i}）");

        // 补零后的时间步应多于窄输入动态推理的时间步（padding 区参与了解码长度）
        var narrowDynamic = Assert.Single(dynamicSession.Run(narrow));
        Assert.True(paddedOutput.Shape[1] > narrowDynamic.Shape[1]);
    }

    [Fact]
    public void RecSession_OpenVino_FixedShape_RejectsOversizeInput()
    {
        if (!RuntimeAvailable()) return;
        var model = BgiOnnxModel.PaddleOcrRecV6;
        using var fixedSession = new OpenVinoSession(model.ModalPath, "CPU", enableCache: false,
            fixedInputShape: [1, 3, 48, 320]);
        // 超出固定宽度：明确报错而不是静默截断（Rec 预处理会先把宽度压到固定值以内）
        var oversize = new DenseTensor<float>(new[] { 1, 3, 48, 400 });
        Assert.Throws<InvalidOperationException>(() => fixedSession.Run(oversize));
    }

    [Fact]
    public void RecSession_OpenVino_Bucketed_PicksSmallestSufficientBucket()
    {
        if (!RuntimeAvailable()) return;
        var model = BgiOnnxModel.PaddleOcrRecV6;
        Assert.True(File.Exists(model.ModalPath), $"rec 模型不存在: {model.ModalPath}");

        // 模拟 GPU/NPU 分桶路径（在 CPU 上验证选桶与补零机制）：桶 [160, 320]
        const int height = 48;
        using var bucketed = new OpenVinoBucketedSession(model.ModalPath, "CPU", enableCache: false, height, [320, 160]);
        Assert.Equal(320, bucketed.FixedInputWidth);

        using var fixed160 = new OpenVinoSession(model.ModalPath, "CPU", enableCache: false,
            fixedInputShape: [1, 3, height, 160]);
        using var fixed320 = new OpenVinoSession(model.ModalPath, "CPU", enableCache: false,
            fixedInputShape: [1, 3, height, 320]);
        using var dynamic = new OpenVinoSession(model.ModalPath, "CPU", enableCache: false);

        // 100 宽 → 最小够用桶 160：与直接固定 160 的会话输出一致
        var narrow = Pattern([1, 3, height, 100]);
        AssertOutputEqual(fixed160.Run(narrow)[0], bucketed.Run(narrow)[0]);
        // 200 宽 → 桶 320
        var middle = Pattern([1, 3, height, 200]);
        AssertOutputEqual(fixed320.Run(middle)[0], bucketed.Run(middle)[0]);
        // 恰好满桶 320 → 与动态形状会话输出一致
        var full = Pattern([1, 3, height, 320]);
        AssertOutputEqual(dynamic.Run(full)[0], bucketed.Run(full)[0]);
        // 超出最大桶：明确报错（Rec 预处理会先压缩到 FixedInputWidth 以内）
        Assert.Throws<InvalidOperationException>(() => bucketed.Run(Pattern([1, 3, height, 400])));

        static DenseTensor<float> Pattern(int[] dims)
        {
            var tensor = new DenseTensor<float>(dims);
            for (var i = 0; i < tensor.Length; i++) tensor.Buffer.Span[i] = (i % 11) / 11f - 0.5f;
            return tensor;
        }

        static void AssertOutputEqual(OcrInferenceResult expected, OcrInferenceResult actual)
        {
            Assert.Equal(expected.Shape, actual.Shape);
            Assert.Equal(expected.Data.Length, actual.Data.Length);
            for (var i = 0; i < expected.Data.Length; i++)
                Assert.True(Math.Abs(expected.Data[i] - actual.Data[i]) < 1e-5f, $"分桶输出与固定形状输出不一致（索引 {i}）");
        }
    }

    [Fact]
    public void YoloPredictor_OpenVino_DetectsDomainTree()
    {
        if (!RuntimeAvailable()) return;
        var treeImagePath = Path.Combine(_appBase, @"Assets\Model\Domain\test_petrified_trees.jpg".Replace('\\', Path.DirectorySeparatorChar));
        if (!File.Exists(treeImagePath)) return; // 测试图片缺失时跳过
        using var image = Image.Load<Rgb24>(treeImagePath);

        using var predictor = new OpenVinoYoloPredictor(
            BgiOnnxModel.BgiTree.ModalPath, "CPU", enableCache: false,
            new YoloName[] { new(0, "tree") }, 640, 640, isClassifier: false);
        var result = predictor.Detect(image);
        var boxes = result.ToList();
        Assert.All(boxes, b =>
        {
            Assert.True(b.Bounds.X >= 0 && b.Bounds.Y >= 0);
            Assert.True(b.Confidence is > 0 and <= 1);
        });
    }

    [Fact]
    public void DeviceDiscovery_OpenVino_ListsCpu()
    {
        if (!RuntimeAvailable()) return;
        var devices = OpenVinoDeviceDiscovery.GetDevices(OpenVinoRuntime.RuntimeDirectory);
        Assert.NotEmpty(devices);
        Assert.Contains(devices, d => d.DeviceId == "CPU");
    }

    [Fact]
    public void BackendRouter_WithoutConfig_FallsBackToOnnx()
    {
        // 单元测试宿主未初始化全局配置：路由必须回落 ONNX，不抛异常
        Assert.Equal(InferenceBackendRouter.OcrBackend.Onnx,
            InferenceBackendRouter.ResolveOcr(PaddleOcrService.PaddleOcrModelType.V6));
        Assert.Equal(InferenceBackendRouter.YoloBackend.Onnx,
            InferenceBackendRouter.ResolveYolo(BgiOnnxModel.BgiTree));
        Assert.Null(InferenceBackendRouter.CreateYoloBackend(BgiOnnxModel.BgiTree,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
    }
}
