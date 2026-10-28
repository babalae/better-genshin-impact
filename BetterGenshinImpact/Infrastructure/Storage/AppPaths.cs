using System.IO;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Infrastructure.Storage;

/// <summary>
/// 基础设施目录约定。程序生成的数据（缓存、下载、临时文件、网页数据）只从这里取路径，
/// 见 Docs/design/infrastructure-foundation.md 第 3.3 节。
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// 程序生成数据的根目录，与 User/（用户数据）分开；目录名只在这里定义
    /// </summary>
    public static string Root => Global.Absolute("Data");

    /// <summary>
    /// 缓存根目录，下面只放按用途划分的子目录，不直接写文件
    /// </summary>
    public static string Cache => Path.Combine(Root, "Cache");

    /// <summary>
    /// ONNX Runtime 编译缓存目录（TensorRT engine/timing、OpenVINO），与通用文件缓存分开
    /// </summary>
    public static string InferenceCache => Path.Combine(Cache, "Inference");

    /// <summary>
    /// 通用文件缓存目录（图片、接口数据），按缓存分区分子目录，受文件缓存上限约束
    /// </summary>
    public static string FileCache => Path.Combine(Cache, "Files");

    /// <summary>
    /// 下载目录，按下载分类分子目录；与缓存分开，不计入文件缓存上限
    /// </summary>
    public static string Downloads => Path.Combine(Root, "Downloads");

    /// <summary>
    /// 临时文件根目录，按进程分子目录
    /// </summary>
    public static string Temp => Path.Combine(Root, "Temp");

    /// <summary>
    /// WebView2 用户数据根目录，普通环境按环境 Id 分子目录
    /// </summary>
    public static string WebView => Path.Combine(Root, "WebView2");

    /// <summary>
    /// 云原神网页版实例数据根目录，一个实例一个子目录
    /// </summary>
    public static string WebViewCloudGame => Path.Combine(WebView, "CloudGame");

    /// <summary>
    /// 旧目录清理标记，记录最近一次全部清理成功的版本号
    /// </summary>
    public static string LegacyCleanupStateFile => Path.Combine(Root, "legacy-cleanup.json");

    /// <summary>
    /// 推迟到下次启动执行的清理记录
    /// </summary>
    public static string PendingCleanupFile => Path.Combine(Root, "pending-cleanup.json");
}
