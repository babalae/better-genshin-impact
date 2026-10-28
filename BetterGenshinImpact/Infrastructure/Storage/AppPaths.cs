using System.IO;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Infrastructure.Storage;

/// <summary>
/// 基础设施目录约定。程序生成的数据（缓存、下载、临时文件、网页数据）只从这里取路径，
/// 见 Docs/design/infrastructure-foundation.md 第 3.3 节。缓存、下载与网页数据目录随后续阶段补充。
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// 程序生成数据的根目录，与 User/（用户数据）分开；目录名只在这里定义
    /// </summary>
    public static string Root => Global.Absolute("Data");

    /// <summary>
    /// 临时文件根目录，按进程分子目录
    /// </summary>
    public static string Temp => Path.Combine(Root, "Temp");

    /// <summary>
    /// 旧目录清理标记，记录最近一次全部清理成功的版本号
    /// </summary>
    public static string LegacyCleanupStateFile => Path.Combine(Root, "legacy-cleanup.json");

    /// <summary>
    /// 推迟到下次启动执行的清理记录
    /// </summary>
    public static string PendingCleanupFile => Path.Combine(Root, "pending-cleanup.json");
}
