using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Infrastructure.Storage;
using Semver;

namespace BetterGenshinImpact.Core.Plugins.Hosting;

/// <summary>
/// 插件清单与版本兼容性检查，见 Docs/design/plugin-system.md 第 10.2 节。
/// 检查只覆盖清单能表达的情况，运行时的二进制不兼容由可插拔机制处理。
/// </summary>
internal static partial class PluginCompatibility
{
    /// <summary>
    /// 支持的清单格式版本。
    /// </summary>
    public const int ManifestVersion = 1;

    /// <summary>
    /// 插件编译时可使用的最低 BetterGI 版本。主程序对公开类型做了不兼容的修改时，把它提高到当前版本，
    /// 用旧版本编译的插件统一显示“需要更新插件”。
    /// </summary>
    public const string MinSdkVersion = "0.66.3-alpha.1";

    /// <summary>
    /// 保留的插件标识。
    /// </summary>
    private static readonly HashSet<string> ReservedIds = new(StringComparer.Ordinal) { "builtin", "bgi", "onnxruntime" };

    /// <summary>
    /// 检查清单是否可以在当前版本中使用。
    /// </summary>
    /// <param name="manifest">插件清单。</param>
    /// <param name="fileExists">判断插件目录（或安装包）内的相对路径是否存在。</param>
    /// <returns>不兼容的原因；兼容时为 null。</returns>
    public static string? Check(PluginManifest manifest, Func<string, bool> fileExists)
    {
        if (manifest.ManifestVersion != ManifestVersion)
        {
            return $"不支持的清单版本 {manifest.ManifestVersion}";
        }

        if (!IdRegex().IsMatch(manifest.Id) || ReservedIds.Contains(manifest.Id))
        {
            return $"插件标识“{manifest.Id}”不合法";
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            return "清单缺少插件名称";
        }

        if (!TryParse(manifest.Version, out _))
        {
            return $"插件版本“{manifest.Version}”不是有效的版本号";
        }

        if (!SafePath.IsSafeRelativePath(manifest.Entry)
            || !manifest.Entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return $"入口程序集“{manifest.Entry}”不合法";
        }

        if (!fileExists(manifest.Entry))
        {
            return $"找不到入口程序集 {manifest.Entry}";
        }

        if (!TryParse(manifest.SdkVersion, out var sdk))
        {
            return $"开发包版本“{manifest.SdkVersion}”不是有效的版本号";
        }

        if (TryParse(MinSdkVersion, out var minimum) && sdk.ComparePrecedenceTo(minimum) < 0)
        {
            return $"插件基于旧版本 BetterGI（{manifest.SdkVersion}）编译，请更新插件";
        }

        // 开发版等无法解析版本号的主程序跳过以下比较
        if (!TryParse(Global.Version, out var host))
        {
            return null;
        }

        if (sdk.ComparePrecedenceTo(host) > 0)
        {
            return $"需要 BetterGI {manifest.SdkVersion} 或更高版本";
        }

        if (!string.IsNullOrEmpty(manifest.MaxHostVersion))
        {
            if (!TryParse(manifest.MaxHostVersion, out var maximum))
            {
                return $"最高支持版本“{manifest.MaxHostVersion}”不是有效的版本号";
            }

            if (host.ComparePrecedenceTo(maximum) > 0)
            {
                return $"只支持到 BetterGI {manifest.MaxHostVersion}";
            }
        }

        return null;
    }

    /// <summary>
    /// 按 Semver 严格格式解析版本号。
    /// </summary>
    private static bool TryParse(string? value, out SemVersion version)
    {
        var parsed = SemVersion.TryParse(value, SemVersionStyles.Strict, out var result);
        version = result!;
        return parsed;
    }

    /// <summary>
    /// 插件标识规则：小写字母开头，只含小写字母、数字、下划线，最长 32 个字符。
    /// </summary>
    [GeneratedRegex("^[a-z][a-z0-9_]{0,31}$")]
    private static partial Regex IdRegex();
}
