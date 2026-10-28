using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Infrastructure.Storage;

namespace BetterGenshinImpact.Service.Instance;

/// <summary>
/// 把每个云原神网页版实例的网页缓存登记为存储区域：只清理缓存，保留 Cookie 和登录状态；实例运行中时不能清理。
/// 只在主实例登记，与“子实例不操作其他实例”的边界一致（见设计文档 8.5 节）
/// </summary>
/// <param name="store">实例存储</param>
/// <param name="instanceBootstrap">当前实例信息</param>
public sealed class CloudGameStorageAreaProvider(
    WebViewInstanceStore store,
    InstanceBootstrap instanceBootstrap) : IStorageAreaProvider
{
    /// <inheritdoc />
    public IEnumerable<StorageArea> GetAreas()
    {
        if (!instanceBootstrap.Context.IsRoot)
        {
            yield break;
        }

        foreach (var name in store.List())
        {
            yield return new StorageArea($"webview:cloud-game:{name}", $"云原神 · {name}", StorageGroup.WebData,
                WebViewInstanceStore.GetCacheFolders(name))
            {
                Description = "该实例的网页缓存，保留登录状态",
                CleanupEffect = "下次打开时重新加载页面资源",
                IncludedInQuickCleanup = false,
                GetBusyReason = () => WebViewInstanceStore.IsRunning(name) ? "实例正在运行，请先关闭该实例" : null,
                CleanupAsync = ct => Task.Run(() => store.ClearCache(name), ct),
            };
        }
    }
}
