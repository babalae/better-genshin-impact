using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Infrastructure.Storage;
using BetterGenshinImpact.Service.Instance;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 尚未迁移到基础设施服务的目录，先在存储空间窗口中展示和清理。
/// 对应阶段完成迁移后，由正式的存储区域取代并删除这里的条目（见设计文档 3.5 节）
/// </summary>
public sealed class LegacyStorageAreaProvider(
    IOnnxRuntimeInstaller onnxRuntimeInstaller,
    WebViewInstanceStore webViewInstanceStore,
    InstanceBootstrap instanceBootstrap) : IStorageAreaProvider
{
    /// <inheritdoc />
    public IEnumerable<StorageArea> GetAreas()
    {
        // 阶段 5 迁移到 IFileCache
        yield return new StorageArea("legacy:memory-file-cache", "地图点位与图标缓存", StorageGroup.Cache,
            [MemoryFileCache.CacheRootDirectory])
        {
            Description = "地图遮罩的点位图标和空荧酒馆点位数据",
            CleanupEffect = "需要时重新下载",
        };

        yield return new StorageArea("legacy:music-artwork", "音乐封面", StorageGroup.Cache,
            [Global.Absolute(@"User\Music\Artwork")])
        {
            Description = "自动演奏曲目的封面图片",
            CleanupEffect = "需要时重新下载",
        };

        // 阶段 4 迁移到下载服务
        yield return new StorageArea("legacy:onnxruntime-downloads", "运行库安装包", StorageGroup.Download,
            [Path.Combine(onnxRuntimeInstaller.PluginsDirectory, ".downloads")])
        {
            Description = "硬件加速运行库的下载包，安装成功后自动删除，留下的通常是未完成或失败的下载",
            CleanupEffect = "未完成的下载需要从头重新下载",
        };

        // 阶段 6 迁移到 WebView2 环境入口，届时提供只清理缓存的选项
        yield return new StorageArea("legacy:webview-shared", "软件内网页数据", StorageGroup.WebData,
            [Global.Absolute(@"WebView2Data\EBWebView")])
        {
            Description = "更新日志、脚本仓库、地图编辑器和脚本遮罩网页的缓存与数据",
            Timing = StorageCleanupTiming.None,
        };

        // 子实例不展示其他实例的数据，与“子实例不操作其他实例”的边界一致
        if (!instanceBootstrap.Context.IsRoot)
        {
            yield break;
        }

        foreach (var name in webViewInstanceStore.List())
        {
            yield return new StorageArea($"legacy:cloud-game:{name}", $"云原神 · {name}", StorageGroup.WebData,
                [WebViewInstanceStore.GetDataFolder(name)])
            {
                Description = "包含该实例的登录状态，在云原神实例管理中删除实例可释放这部分空间",
                Timing = StorageCleanupTiming.None,
            };
        }
    }
}
