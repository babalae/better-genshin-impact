using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public sealed class PaddleOcrCpuOptionsTests
{
    [Theory]
    [InlineData(false, ExecutionMode.ORT_SEQUENTIAL, 1)]
    [InlineData(true, ExecutionMode.ORT_PARALLEL, 0)]
    public void CustomOptions_AreAppliedToAllPaddleModels(bool parallel, ExecutionMode mode, int interOp)
    {
        var factory = new BgiOnnxSessionFactory(NullLogger<BgiOnnxFactory>.Instance,
            ResolvedInferenceRuntime.Cpu() with
            {
                PaddleOcrCpu = new()
                {
                    Mode = PaddleOcrCpuMode.Custom,
                    DetectionThreads = 1,
                    RecognitionThreads = 0,
                    InterOpThreads = 0,
                    ParallelExecution = parallel,
                    AllowSpinning = false
                }
            });

        // 所有已注册 PPOCR 版本及语言变体都应使用同一套 CPU 参数。
        foreach (var model in BgiOnnxModel.GetAll()
                     .Where(model => model.Name.StartsWith("PpOcr", StringComparison.Ordinal)))
        {
            using var options = new SessionOptions();
            factory.ConfigurePaddleOcrCpu(options, model, true);
            Assert.Equal(model.Name.StartsWith("PpOcrDet", StringComparison.Ordinal) ? 1 : 0,
                options.IntraOpNumThreads);
            Assert.Equal(interOp, options.InterOpNumThreads);
            Assert.Equal(mode, options.ExecutionMode);
        }
    }
}
