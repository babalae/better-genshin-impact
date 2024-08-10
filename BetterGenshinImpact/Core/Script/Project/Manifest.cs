using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Documents;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.Core.Script.Project;

[Serializable]
public class Manifest
{
    public int ManifestVersion { get; set; } = 1;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Author> Authors { get; set; } = [];
    public string Main { get; set; } = string.Empty;
    public string[] Scripts { get; set; } = [];
    public string[] Library { get; set; } = [];

    public static Manifest FromJson(string json)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(json, ConfigService.JsonOptions) ?? throw new Exception("Failed to deserialize JSON.");
        return manifest;
    }

    public void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new Exception("manifest.json: id is not supported.");
        }

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
}
