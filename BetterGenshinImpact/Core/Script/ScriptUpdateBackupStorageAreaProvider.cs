using System.Collections.Generic;
using BetterGenshinImpact.Infrastructure.Storage;

namespace BetterGenshinImpact.Core.Script;

/// <summary>
/// 把订阅脚本更新的数据备份目录登记为存储区域。备份是用户数据，只在用户确认后清除，不参与一键清理
/// </summary>
public sealed class ScriptUpdateBackupStorageAreaProvider : IStorageAreaProvider
{
    /// <inheritdoc />
    public IEnumerable<StorageArea> GetAreas()
    {
        yield return new StorageArea("user:script-update-backup", "脚本更新备份", StorageGroup.UserData,
            [ScriptRepoUpdater.ScriptUpdateBackupPath])
        {
            Description = "订阅脚本更新中断或未能恢复时保留的脚本数据",
            CleanupEffect = "删除后无法再从备份中找回这些文件",
            RequiresConfirmation = true,
            IncludedInQuickCleanup = false,
        };
    }
}
