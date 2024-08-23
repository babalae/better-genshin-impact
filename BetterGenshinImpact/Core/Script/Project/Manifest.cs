using BetterGenshinImpact.Service;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.Core.Script.Project;

[Serializable]
public class Manifest
{
    public int ManifestVersion { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Author> Authors { get; set; } = [];
    public string Main { get; set; } = string.Empty;
    public string SettingsUi { get; set; } = string.Empty;
    public string[] Scripts { get; set; } = [];
    public string[] Library { get; set; } = [];

    public static Manifest FromJson(string json)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(json, Global.ManifestJsonOptions) ?? throw new Exception("Failed to deserialize JSON.");
        return manifest;
    }

    public void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new Exception("manifest.json: name is required.");
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new Exception("manifest.json: version is required.");
        }

        if (string.IsNullOrWhiteSpace(Main))
        {
            throw new Exception("manifest.json: main script is required.");
        }

        if (!File.Exists(Path.Combine(path, Main)))
        {
            throw new FileNotFoundException("main js file not found.");
        }
    }

    public List<SettingItem> LoadSettingItems(string path)
    {
        if (string.IsNullOrWhiteSpace(SettingsUi))
        {
            return [];
        }

        var settingItems = new List<SettingItem>();
        var settingFile = Path.Combine(path, SettingsUi);
        if (File.Exists(settingFile))
        {
            var json = File.ReadAllText(settingFile);
            settingItems = JsonSerializer.Deserialize<List<SettingItem>>(json, ConfigService.JsonOptions) ?? [];
        }
        return settingItems;
    }
}
