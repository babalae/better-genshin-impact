using System.Collections.Generic;
using BetterGenshinImpact.Infrastructure.Storage;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>
/// 把推理编译缓存登记为存储区域。缓存文件在推理会话存续期间被占用，清理推迟到下次启动；
/// 重新编译耗时较长，不参与一键清理
/// </summary>
public sealed class InferenceCacheStorageAreaProvider : IStorageAreaProvider
{
    /// <inheritdoc />
    public IEnumerable<StorageArea> GetAreas()
    {
        yield return new StorageArea("cache:inference", "识别模型编译缓存", StorageGroup.Cache,
            [ResolvedInferenceRuntime.ModelCacheDirectory])
        {
            Description = "启用 TensorRT 或 OpenVINO 加速时生成的模型编译结果",
            CleanupEffect = "下次启动时清理，之后首次识别需要重新编译，会变慢",
            Timing = StorageCleanupTiming.NextStartup,
            IncludedInQuickCleanup = false,
        };
    }
}
