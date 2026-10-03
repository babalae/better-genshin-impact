using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Script.Group.Preset;

public sealed class ConfigGroupPresetItem
{
    public required ConfigGroupPresetManifest Manifest { get; init; }
    public required string DirectoryPath { get; init; }
    public required string ManifestPath { get; init; }
    public required string ConfigGroupPath { get; init; }
    public string? ReadmePath { get; init; }

    public string Id => Manifest.Id;
    public string Name => Manifest.Name;
    public string Version => Manifest.Version;
    public string Description => Manifest.Description;
}

public sealed record ConfigGroupPresetApplyResult(
    ConfigGroupPresetApplyStatus Status,
    IReadOnlyList<string> MissingDependencies,
    string? ErrorMessage = null);

public enum ConfigGroupPresetApplyStatus
{
    Success,
    MissingDependencies,
    Conflict,
    Invalid,
    Failed
}
