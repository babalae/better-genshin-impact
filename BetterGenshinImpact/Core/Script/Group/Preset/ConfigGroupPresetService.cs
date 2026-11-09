using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Project;
using Microsoft.Extensions.Logging;
using Semver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterGenshinImpact.Core.Script.Group.Preset;

/// <summary>
/// 扫描和应用本体内置预设配置组。依赖只做本地完整性检查，不修改脚本仓库订阅状态。
/// </summary>
public sealed class ConfigGroupPresetService
{
    public const string PresetRootRelativePath = @"Assets\Config\Preset";
    public const string ScriptGroupRelativePath = @"User\ScriptGroup";

    private readonly ILogger<ConfigGroupPresetService> _logger;

    public ConfigGroupPresetService()
    {
        _logger = App.GetLogger<ConfigGroupPresetService>();
    }

    public string PresetRootPath => Global.Absolute(PresetRootRelativePath);

    public IReadOnlyList<ConfigGroupPresetItem> Scan()
    {
        var result = new List<ConfigGroupPresetItem>();
        if (!Directory.Exists(PresetRootPath))
        {
            return result;
        }

        foreach (var directory in Directory.EnumerateDirectories(PresetRootPath))
        {
            try
            {
                var item = LoadItem(directory);
                if (item != null)
                {
                    result.Add(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取内置预设配置组失败: {Directory}", directory);
            }
        }

        return result.OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
    }

    public ConfigGroupPresetItem? LoadItem(string directoryPath)
    {
        var directory = Path.GetFullPath(directoryPath);
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("预设配置组 manifest.json 不存在", manifestPath);
        }

        var manifest = JsonSerializer.Deserialize<ConfigGroupPresetManifest>(
            File.ReadAllText(manifestPath), Global.ManifestJsonOptions)
            ?? throw new InvalidDataException("预设配置组 manifest.json 为空或格式错误");

        ValidateManifest(manifest);
        var directoryName = new DirectoryInfo(directory).Name;
        if (!string.Equals(directoryName, manifest.Name, StringComparison.Ordinal))
        {
            throw new InvalidDataException("预设配置组目录名必须与 manifest 中的 name 一致");
        }
        var configFile = string.IsNullOrWhiteSpace(manifest.ConfigGroupFile)
            ? $"{manifest.Name}.json"
            : manifest.ConfigGroupFile;
        var configPath = ResolveInside(directory, configFile);
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("预设配置组配置文件不存在", configPath);
        }

        var group = ScriptGroup.FromJson(File.ReadAllText(configPath));
        if (!string.Equals(group.Name, manifest.Name, StringComparison.Ordinal))
        {
            throw new InvalidDataException("预设配置组名称与配置组 name 不一致");
        }

        string? readmePath = null;
        if (!string.IsNullOrWhiteSpace(manifest.ReadmeFile))
        {
            var candidate = ResolveInside(directory, manifest.ReadmeFile);
            if (File.Exists(candidate))
            {
                readmePath = candidate;
            }
        }

        return new ConfigGroupPresetItem
        {
            Manifest = manifest,
            DirectoryPath = directory,
            ManifestPath = manifestPath,
            ConfigGroupPath = configPath,
            ReadmePath = readmePath
        };
    }

    public IReadOnlyList<string> CheckDependencies(ConfigGroupPresetItem item)
    {
        var missing = new List<string>();
        foreach (var dependency in item.Manifest.Dependencies)
        {
            if (!IsDependencyInstalled(dependency))
            {
                var displayName = string.IsNullOrWhiteSpace(dependency.Name)
                    ? dependency.Path
                    : dependency.Name;
                var versionRequirement = string.IsNullOrWhiteSpace(dependency.MinVersion)
                    ? string.Empty
                    : $"（需要版本 >= {dependency.MinVersion}）";
                missing.Add($"{dependency.Type}: {displayName}{versionRequirement}");
            }
        }

        return missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public ConfigGroupPresetApplyResult Apply(ConfigGroupPresetItem item)
    {
        try
        {
            var missing = CheckDependencies(item);
            if (missing.Count > 0)
            {
                return new ConfigGroupPresetApplyResult(
                    ConfigGroupPresetApplyStatus.MissingDependencies, missing);
            }

            var group = ScriptGroup.FromJson(File.ReadAllText(item.ConfigGroupPath));
            var outputDirectory = Global.Absolute(ScriptGroupRelativePath);
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, $"{group.Name}.json");
            if (File.Exists(outputPath) || HasGroupWithName(outputDirectory, group.Name))
            {
                return new ConfigGroupPresetApplyResult(
                    ConfigGroupPresetApplyStatus.Conflict, [], "同名配置组已存在");
            }

            group.Index = GetNextIndex(outputDirectory);
            group.WriteToFileAtomically(outputPath);
            return new ConfigGroupPresetApplyResult(ConfigGroupPresetApplyStatus.Success, []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用内置预设配置组失败: {Name}", item.Name);
            return new ConfigGroupPresetApplyResult(
                ConfigGroupPresetApplyStatus.Failed, [], ex.Message);
        }
    }

    private static void ValidateManifest(ConfigGroupPresetManifest manifest)
    {
        if (manifest.ManifestVersion <= 0) throw new InvalidDataException("manifest_version 无效");
        if (string.IsNullOrWhiteSpace(manifest.Id)) throw new InvalidDataException("manifest 缺少 id");
        if (string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException("manifest 缺少 name");
        if (string.IsNullOrWhiteSpace(manifest.Version)) throw new InvalidDataException("manifest 缺少 version");
    }

    private static string ResolveInside(string directory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new InvalidDataException("预设配置组文件路径不能是绝对路径");
        var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = directory.EndsWith(Path.DirectorySeparatorChar) ? directory : directory + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("预设配置组文件路径超出预设配置组目录");
        return fullPath;
    }

    private static bool IsDependencyInstalled(ConfigGroupPresetDependency dependency)
    {
        var relative = string.IsNullOrWhiteSpace(dependency.Path) ? dependency.Name : dependency.Path;
        if (string.IsNullOrWhiteSpace(relative)) return false;
        var type = dependency.Type.Trim().ToLowerInvariant();
        var root = type switch
        {
            "javascript" or "js" => Global.Absolute(@"User\JsScript"),
            "pathing" => Global.Absolute(@"User\AutoPathing"),
            "keymouse" or "key_mouse" => Global.Absolute(@"User\KeyMouseScript"),
            _ => null
        };
        if (root == null) return false;

        var fullPath = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        if (type is "javascript" or "js")
        {
            var manifestPath = Path.Combine(fullPath, "manifest.json");
            if (!Directory.Exists(fullPath) || !File.Exists(manifestPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(dependency.MinVersion))
            {
                return true;
            }

            try
            {
                var manifest = Manifest.FromJson(File.ReadAllText(manifestPath));
                if (string.IsNullOrWhiteSpace(manifest.Version))
                {
                    return false;
                }

                var requiredVersion = SemVersion.Parse(dependency.MinVersion);
                var installedVersion = SemVersion.Parse(manifest.Version);
                return installedVersion.ComparePrecedenceTo(requiredVersion) >= 0;
            }
            catch
            {
                // 版本要求或脚本版本无法解析时，按依赖不满足处理。
                return false;
            }
        }
        return File.Exists(fullPath) || Directory.Exists(fullPath);
    }

    private static int GetNextIndex(string directory)
    {
        var max = -1;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var group = ScriptGroup.FromJson(File.ReadAllText(file));
                max = Math.Max(max, group.Index);
            }
            catch
            {
                // 无效的用户文件不会阻止预设配置组应用。
            }
        }

        return max + 1;
    }

    private static bool HasGroupWithName(string directory, string name)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (string.Equals(ScriptGroup.FromJson(File.ReadAllText(file)).Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch
            {
                // 无效文件由调度器自行提示，不影响本次名称检查。
            }
        }

        return false;
    }
}
