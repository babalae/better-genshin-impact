using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Service.Interface;

public interface IInferenceBenchmarkService
{
    Task<InferenceBenchmarkResult> RunAsync(
        HardwareAccelerationConfig config,
        int iterations,
        IProgress<InferenceBenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record InferenceBenchmarkProgress(int Completed, int Total, string Stage)
{
    public double Percentage => Total > 0 ? Math.Clamp(Completed * 100d / Total, 0, 100) : 0;
}

public sealed record InferenceBenchmarkResult(
    string Provider,
    string Device,
    int Iterations,
    double SessionInitializationMilliseconds,
    double WarmupMilliseconds,
    double AverageMilliseconds,
    double MinimumMilliseconds,
    double MaximumMilliseconds,
    double P50Milliseconds,
    double P95Milliseconds,
    double FramesPerSecond,
    int DetectionCount,
    TimeSpan TotalElapsed)
{
    public string ToDisplayText()
    {
        return $"Provider：{Provider}\n" +
               $"设备：{Device}\n" +
               $"会话初始化：{SessionInitializationMilliseconds:0.00} ms\n" +
               $"预热：{WarmupMilliseconds:0.00} ms\n" +
               $"推理次数：{Iterations}\n" +
               $"平均：{AverageMilliseconds:0.00} ms（{FramesPerSecond:0.00} FPS）\n" +
               $"最小 / P50 / P95 / 最大：{MinimumMilliseconds:0.00} / {P50Milliseconds:0.00} / " +
               $"{P95Milliseconds:0.00} / {MaximumMilliseconds:0.00} ms\n" +
               $"最后一次检测结果：{DetectionCount} 个；测试总耗时：{TotalElapsed.TotalMilliseconds:0.00} ms";
    }
}
