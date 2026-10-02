using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Core.Script;

/// <summary>
/// Matches online material names and acquisition sources to script repository folders.
/// </summary>
internal static class AscensionMaterialRouteMatcher
{
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
