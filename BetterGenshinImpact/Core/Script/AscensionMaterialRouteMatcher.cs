using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Core.Script;

/// <summary>
/// Matches online material names and acquisition sources to script repository folders.
/// </summary>
internal static class AscensionMaterialRouteMatcher
{
    /// <summary>Combines acquisition sources for duplicate material names.</summary>
    public static Dictionary<string, HashSet<string>> BuildMaterialSourceCatalog(
        IEnumerable<(string? Name, IEnumerable<string?> Sources)> entries)
    {
        return entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .GroupBy(entry => entry.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(entry => entry.Sources ?? [])
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Select(source => source!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Finds materials whose names or acquisition sources match a route folder.</summary>
    public static HashSet<string> FindRelatedMaterials(
        string directoryName,
        IEnumerable<string> materials,
        IReadOnlyDictionary<string, HashSet<string>> materialSources)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return materials.Where(material =>
                material.Equals(directoryName, StringComparison.OrdinalIgnoreCase)
                || (directoryName.Length >= 3
                    && materialSources.TryGetValue(material, out var sources)
                    && sources.Any(source => source.Contains(directoryName, StringComparison.OrdinalIgnoreCase))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
