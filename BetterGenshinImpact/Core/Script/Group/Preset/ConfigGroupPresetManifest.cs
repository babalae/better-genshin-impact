using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Script.Group.Preset;

/// <summary>
/// 本体内置预制菜的描述文件。它与 Javascript 项目的 Manifest 分开，避免要求存在 main.js。
/// </summary>
public sealed class ConfigGroupPresetManifest
{
    public int ManifestVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? MinBgiVersion { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> Authors { get; set; } = [];
    public string ConfigGroupFile { get; set; } = string.Empty;
    public string ReadmeFile { get; set; } = "README.md";
    public List<ConfigGroupPresetDependency> Dependencies { get; set; } = [];
}

public sealed class ConfigGroupPresetDependency
{
    /// <summary>Javascript、Pathing 或 KeyMouse。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>用于提示用户的显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>相对于对应 User 子目录的路径，使用 / 作为分隔符。</summary>
    public string Path { get; set; } = string.Empty;
}
