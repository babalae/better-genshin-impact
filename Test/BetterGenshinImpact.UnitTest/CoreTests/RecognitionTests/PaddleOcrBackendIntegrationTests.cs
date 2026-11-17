using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.Ncnn;
using BetterGenshinImpact.Core.Recognition.OpenVino;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using Xunit;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

/// <summary>
///     PaddleOcrService 完整链路集成测试：独立后端（OpenVINO / ncnn）装配 Det + Rec →
///     构造服务（含预热）→ 对真实测试图片执行完整 OCR（检测 + 识别）。
///     运行库缺失时跳过，不作为失败。
/// </summary>
[Collection("Sequential")]
public class PaddleOcrBackendIntegrationTests
{
    private static string TestImagePath
        => Path.Combine(AppContext.BaseDirectory, "Assets", "Model", "PaddleOCR", "test_pp_ocr.png");

    private static bool OpenVinoReady => OpenVinoRuntime.IsInstalled();
    private static bool NcnnReady => NcnnShim.IsSupported();

    private static string NcnnOcrModelDir
        => Path.Combine(AppContext.BaseDirectory, "Assets", "Model", "PaddleOCR", "Ncnn");

    [Fact]
    public void OpenVinoBackend_FullOcrPipeline_ProducesResult()
    {
        if (!OpenVinoReady || !File.Exists(TestImagePath)) return;
        var modelType = PaddleOcrService.PaddleOcrModelType.V6;
        var backends = OpenVinoOcrBackend.Create(NullLogger.Instance, modelType,
            new OpenVinoBackendSettings(true, true, "CPU", false));
        using var service = new PaddleOcrService(backends, modelType);
        using var image = new Mat(TestImagePath);
        Assert.False(image.Empty());

        var result = service.OcrResult(image);
        Assert.NotEmpty(result.Regions);
        // 预热图上的文字应至少命中一行非空结果
        Assert.Contains(result.Regions, r => r.Text.Length > 0);
        Assert.All(result.Regions, r => Assert.True(r.Score >= 0 && r.Score <= 1));
        var (detName, recName) = service.GetConfigName;
        Assert.Contains("PP-OCRv6", detName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PP-OCRv6", recName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NcnnBackend_FullOcrPipeline_ProducesResult()
    {
        if (!NcnnReady || !File.Exists(TestImagePath)) return;
        if (!File.Exists(Path.Combine(NcnnOcrModelDir, "det_dyn.param"))) return;
        var modelType = PaddleOcrService.PaddleOcrModelType.V6;
        var settings = NcnnBackendSettings.FromConfig(new NcnnConfig()).Normalize();
        var backends = NcnnOcrBackend.Create(NullLogger.Instance, modelType, settings);
        using var service = new PaddleOcrService(backends, modelType);
        using var image = new Mat(TestImagePath);
        Assert.False(image.Empty());

        var result = service.OcrResult(image);
        Assert.NotEmpty(result.Regions);
        Assert.Contains(result.Regions, r => r.Text.Length > 0);
        var (detName, recName) = service.GetConfigName;
        Assert.Contains("ncnn", detName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ncnn", recName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenVinoBackend_OcrWithoutDetector_SingleLine()
    {
        if (!OpenVinoReady || !File.Exists(TestImagePath)) return;
        var modelType = PaddleOcrService.PaddleOcrModelType.V6;
        var backends = OpenVinoOcrBackend.Create(NullLogger.Instance, modelType,
            new OpenVinoBackendSettings(true, true, "CPU", false));
        using var service = new PaddleOcrService(backends, modelType);
        using var image = new Mat(TestImagePath);
        // 裁一小块区域做单行识别（不过检测模型）
        using var line = new Mat(image, new Rect(0, 0, Math.Min(600, image.Cols), Math.Min(48, image.Rows)));
        var lineResult = service.OcrResultWithoutDetector(line);
        Assert.True(float.IsFinite(lineResult.Score));
    }

    [Fact]
    public void OpenVinoBackend_FixedShapeRec_MatchesDynamicText()
    {
        if (!OpenVinoReady || !File.Exists(TestImagePath)) return;
        var modelType = PaddleOcrService.PaddleOcrModelType.V6;
        var backends = OpenVinoOcrBackend.Create(NullLogger.Instance, modelType,
            new OpenVinoBackendSettings(true, true, "CPU", false));
        using var service = new PaddleOcrService(backends, modelType);
        using var image = new Mat(TestImagePath);
        var regions = service.OcrResult(image).Regions;
        Assert.NotEmpty(regions);

        // 模拟 NPU 静态形状路径：rec 按固定 [1,3,48,640] 在 CPU 编译，
        // 验证补零 padding 不改变真实文本行的识别结果（与动态宽度路径逐行对比）。
        // 宽度取 640：覆盖测试图全部文本行（最宽约 412），避免超宽压缩引入的识别差异。
        using var fixedSession = new OpenVinoSession(modelType.RecognitionModel.ModalPath, "CPU", enableCache: false,
            fixedInputShape: [1, 3, modelType.RecognitionVersion.Shape.Height, 640]);
        using var fixedRec = new Rec(modelType.RecLabel(), modelType.RecognitionVersion, fixedSession);
        // 出厂默认宽度 320：超宽文本行应被压缩到固定宽度（官方行为），推理不得抛异常
        using var defaultSession = new OpenVinoSession(modelType.RecognitionModel.ModalPath, "CPU", enableCache: false,
            fixedInputShape: [1, 3, modelType.RecognitionVersion.Shape.Height, 320]);
        using var defaultRec = new Rec(modelType.RecLabel(), modelType.RecognitionVersion, defaultSession);
        var compared = 0;
        foreach (var region in regions)
        {
            // 文本框可能因扩边超出原图，先夹取到图像范围内
            var bounding = region.Rect.BoundingRect();
            var x = Math.Max(0, bounding.X);
            var y = Math.Max(0, bounding.Y);
            var w = Math.Min(bounding.Width, image.Cols - x);
            var h = Math.Min(bounding.Height, image.Rows - y);
            if (w <= 0 || h <= 0) continue;
            using var crop = new Mat(image, new Rect(x, y, w, h));
            var expected = service.OcrResultWithoutDetector(crop).Text;
            var actual = fixedRec.Run(crop).Text;
            Assert.Equal(expected, actual);
            var compressed = defaultRec.Run(crop);
            Assert.True(float.IsFinite(compressed.Score));
            compared++;
            if (compared >= 5) break;
        }

        Assert.True(compared > 0, "测试图未产生可对比的文本行");
    }

    [Fact]
    public void OpenVinoBackend_BucketedRec_MatchesDynamicText()
    {
        if (!OpenVinoReady || !File.Exists(TestImagePath)) return;
        var modelType = PaddleOcrService.PaddleOcrModelType.V6;
        var backends = OpenVinoOcrBackend.Create(NullLogger.Instance, modelType,
            new OpenVinoBackendSettings(true, true, "CPU", false));
        using var service = new PaddleOcrService(backends, modelType);
        using var image = new Mat(TestImagePath);
        var regions = service.OcrResult(image).Regions;
        Assert.NotEmpty(regions);

        // 模拟 GPU 宽度桶路径（在 CPU 上验证选桶 + 补零的端到端识别）：
        // 桶 [320,640] 覆盖测试图全部文本行（最宽约 412），窄行选 320 桶、宽行选 640 桶，
        // 补零不改变 CTC 解码结果，逐行文本应与动态宽度路径一致。
        using var bucketedSession = new OpenVinoBucketedSession(modelType.RecognitionModel.ModalPath, "CPU",
            enableCache: false, modelType.RecognitionVersion.Shape.Height, [320, 640]);
        using var bucketedRec = new Rec(modelType.RecLabel(), modelType.RecognitionVersion, bucketedSession);
        var compared = 0;
        foreach (var region in regions)
        {
            var bounding = region.Rect.BoundingRect();
            var x = Math.Max(0, bounding.X);
            var y = Math.Max(0, bounding.Y);
            var w = Math.Min(bounding.Width, image.Cols - x);
            var h = Math.Min(bounding.Height, image.Rows - y);
            if (w <= 0 || h <= 0) continue;
            using var crop = new Mat(image, new Rect(x, y, w, h));
            var expected = service.OcrResultWithoutDetector(crop).Text;
            var actual = bucketedRec.Run(crop).Text;
            Assert.Equal(expected, actual);
            compared++;
            if (compared >= 5) break;
        }

        Assert.True(compared > 0, "测试图未产生可对比的文本行");
    }
}
