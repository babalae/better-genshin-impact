using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.Ncnn;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Compunet.YoloSharp.Metadata;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Image = SixLabors.ImageSharp.Image;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

/// <summary>
///     ncnn 后端的端到端推理验证：shim 加载、动态 OCR 模型（det_dyn/rec_dyn）、
///     YOLO 检测（经共享的 YoloBackendShared 解码）。shim 缺失时跳过，不作为失败。
/// </summary>
[Collection("Sequential")]
public class NcnnBackendInferenceTests
{
    private static bool ShimAvailable() => NcnnShim.IsSupported();

    [Fact]
    public void Shim_IsSupported_InStandardNativeLayout()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "ncnn_shim.dll");
        Assert.True(File.Exists(dll), $"ncnn_shim.dll 不在标准原生布局: {dll}");
        Assert.True(ShimAvailable(), "ncnn_shim.dll 存在但加载失败");
    }

    [Fact]
    public void YoloPredictor_Ncnn_DetectsDomainTree()
    {
        if (!ShimAvailable()) return;
        var treeImagePath = Path.Combine(AppContext.BaseDirectory,
            @"Assets\Model\Domain\test_petrified_trees.jpg".Replace('\\', Path.DirectorySeparatorChar));
        if (!File.Exists(treeImagePath)) return;
        using var image = Image.Load<Rgb24>(treeImagePath);

        var (names, width, height, isClassifier) = YoloOnnxMetadata.GetOrRead(BgiOnnxModel.BgiTree.ModalPath);
        Assert.False(isClassifier);
        using var predictor = new NcnnYoloPredictor(new NcnnShim(),
            NcnnYoloBackendPaths(BgiOnnxModel.BgiTree).param, NcnnYoloBackendPaths(BgiOnnxModel.BgiTree).bin,
            names, width, height, tuning: NcnnTuningSettings.CreateDefault(), gpuIndex: -1, isClassifier: false);
        var result = predictor.Detect(image);
        var boxes = result.ToList();
        Assert.All(boxes, b =>
        {
            Assert.True(b.Bounds.X >= 0 && b.Bounds.Y >= 0);
            Assert.True(b.Confidence is > 0 and <= 1);
        });
    }

    [Fact]
    public void OcrDet_Ncnn_RunsDynamicSquareCanvas()
    {
        if (!ShimAvailable()) return;
        var modelDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Model", "PaddleOCR", "Ncnn");
        using var det = new NcnnDet(new NcnnShim(), modelDir, OcrVersionConfig.PpOcrV6,
            NcnnTuningSettings.CreateDefault(), gpuIndex: -1);
        // 噪声输入 96x96（≤ limit 960，无需缩放）：画布为内容对齐 32 的最小正方形 96x96
        using var src = new OpenCvSharp.Mat(96, 96, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(128, 64, 32));
        using var pred = det.RunRaw(src, out var contentSize);
        Assert.Equal(new OpenCvSharp.Size(96, 96), pred.Size());
        Assert.Equal(new OpenCvSharp.Size(96, 96), contentSize);
    }

    [Fact]
    public void OcrRec_Ncnn_RunsDynamicWidth()
    {
        if (!ShimAvailable()) return;
        var modelDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Model", "PaddleOCR", "Ncnn");
        var dictPath = Path.Combine(modelDir, "dict.txt");
        Assert.True(File.Exists(dictPath), "V6 字典缺失");
        var labels = File.ReadAllLines(dictPath);
        using var rec = new NcnnRec(new NcnnShim(), modelDir, labels,
            NcnnTuningSettings.CreateDefault(), gpuIndex: -1);
        using var line = new OpenCvSharp.Mat(48, 320, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(128, 64, 32));
        var result = rec.Run(line);
        Assert.True(float.IsFinite(result.Score));
    }

    [Fact]
    public void BackendRouter_RegisteredModels_ContainsSevenBuiltinModels()
    {
        var registered = new[]
        {
            BgiOnnxModel.BgiWorld, BgiOnnxModel.BgiFish, BgiOnnxModel.BgiMine, BgiOnnxModel.BgiTree,
            BgiOnnxModel.BgiQClassify, BgiOnnxModel.BgiAvatarSide, BgiOnnxModel.BgiEClassify
        };
        foreach (var model in registered)
        {
            Assert.True(NcnnYoloBackend.IsRegistered(model), $"{model.Name} 未登记 ncnn");
        }
    }

    private static (string param, string bin) NcnnYoloBackendPaths(BetterGenshinImpact.Core.Recognition.ONNX.BgiOnnxModel model)
    {
        var stem = Path.GetFileNameWithoutExtension(model.ModalPath);
        var ncnnDir = Path.Combine(Path.GetDirectoryName(model.ModalPath) ?? ".", "Ncnn");
        return (Path.Combine(ncnnDir, stem + ".param"), Path.Combine(ncnnDir, stem + ".bin"));
    }
}
