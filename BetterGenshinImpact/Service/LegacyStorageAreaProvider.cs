using System.Collections.Generic;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Infrastructure.Storage;
using BetterGenshinImpact.Service.Instance;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 尚未迁移到基础设施服务的目录，先在存储空间窗口中展示和清理。
/// 对应阶段完成迁移后，由正式的存储区域取代并删除这里的条目（见设计文档 3.5 节）
/// </summary>
public sealed class LegacyStorageAreaProvider(
    WebViewInstanceStore webViewInstanceStore,
    InstanceBootstrap instanceBootstrap) : IStorageAreaProvider
{
    /// <inheritdoc />
    public IEnumerable<StorageArea> GetAreas()
    {
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
