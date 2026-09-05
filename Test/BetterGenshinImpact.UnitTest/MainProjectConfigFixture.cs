using BetterGenshinImpact.GameTask.AutoBuildCombo;
using BetterGenshinImpact.Service;
using System.Text.Json;

namespace BetterGenshinImpact.UnitTest;

/// <summary>
/// 读取主项目编译输出中的 User/config.json（注册于 InitCollection，需要读取主项目配置的测试统一注入）。
/// 只做纯文件解析：按节点单独反序列化所需配置（如 autoBuildComboConfig），全程不触及 ConfigService 实例链与 App 静态初始化，
/// 否则会连带主程序启动副作用（提权重启弹 UAC、占用单实例命名管道等）。
/// 定位失败或解析失败均不抛异常，由测试方根据 LoadError 决定跳过并提示
/// </summary>
public class MainProjectConfigFixture
{
    /// <summary>主项目 User/config.json 的完整路径，未找到时为 null</summary>
    public string? ConfigPath { get; }

    /// <summary>定位或解析失败原因（ConfigPath 与 AutoBuildComboConfig 均为 null 时给出）</summary>
    public string? LoadError { get; }

    /// <summary>主项目配置中的自动连招配置；节点缺失或解析失败时为 null</summary>
    public AutoBuildComboConfig? AutoBuildComboConfig { get; }

    public MainProjectConfigFixture()
    {
        var path = FindConfigPath();
        if (path == null)
        {
            LoadError = "未找到主项目 User/config.json（请先编译并运行一次主项目）";
            return;
        }

        ConfigPath = path;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("autoBuildComboConfig", out var node))
            {
                LoadError = "config.json 中没有 autoBuildComboConfig 节点";
                return;
            }

            AutoBuildComboConfig = node.Deserialize<AutoBuildComboConfig>(ConfigService.JsonOptions);
        }
        catch (Exception e)
        {
            AutoBuildComboConfig = null;
            LoadError = $"config.json 解析失败：{e.Message}";
        }
    }

    /// <summary>
    /// 定位主项目 User/config.json：从测试输出目录逐级上溯找到包含 BetterGenshinImpact\bin 的仓库根目录，
    /// 再取 bin 下所有 User\config.json 中最近修改的一个（不关心 x64/Debug/Release/TFM 组合）
    /// </summary>
    private static string? FindConfigPath()
    {
        var binRoot = FindMainProjectBinRoot(AppContext.BaseDirectory);
        if (binRoot == null)
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(binRoot, "config.json", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "User")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? FindMainProjectBinRoot(string? directory)
    {
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, "BetterGenshinImpact", "bin");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }
}
