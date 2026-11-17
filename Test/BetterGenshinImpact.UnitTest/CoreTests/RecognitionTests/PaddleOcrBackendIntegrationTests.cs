using System;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.Ncnn;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using Xunit;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

/// <summary>
///     PaddleOcrService 完整链路集成测试：独立后端（ncnn）装配 Det + Rec →
///     构造服务（含预热）→ 对真实测试图片执行完整 OCR（检测 + 识别）。
///     运行库缺失时跳过，不作为失败。
/// </summary>
[Collection("Sequential")]
public class PaddleOcrBackendIntegrationTests
{
    private static string TestImagePath
        => Path.Combine(AppContext.BaseDirectory, "Assets", "Model", "PaddleOCR", "test_pp_ocr.png");

    private static bool NcnnReady => NcnnShim.IsSupported();

    private static string NcnnOcrModelDir
        => Path.Combine(AppContext.BaseDirectory, "Assets", "Model", "PaddleOCR", "Ncnn");

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
}
