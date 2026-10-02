using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>区分可规划的材料秘境、已知但不支持的目标和未识别文字。</summary>
public sealed class TrainingGuideDomainMatcher
{
    private readonly string[] _knownNames;
    private readonly HashSet<string> _supportedNames;
    private readonly Func<string, string> _normalize;

    public TrainingGuideDomainMatcher(IEnumerable<string> knownNames, Func<string, string> normalize)
    {
        _knownNames = knownNames.Distinct().ToArray();
        _supportedNames = TrainingGuideEntryCatalog.Entries.Select(e => e.Domain).ToHashSet();
        _normalize = normalize;
    }

    public (string Name, bool Supported)? Match(string text)
    {
        var normalized = _normalize(text);
        var matches = _knownNames.Where(name => normalized.Contains(_normalize(name), StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) return null;
        return (matches[0], _supportedNames.Contains(matches[0]));
    }
}
