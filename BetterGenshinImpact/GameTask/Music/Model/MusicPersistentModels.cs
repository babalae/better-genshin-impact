using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.Music.Model;

public sealed class MusicLibraryState
{
    public List<string> PlaylistOrder { get; set; } = [];

    public Dictionary<string, MusicItemPreference> Items { get; set; } =
        new(System.StringComparer.OrdinalIgnoreCase);
}

public sealed class MusicItemPreference
{
    public string OutputProfileName { get; set; } = string.Empty;

    public int Transpose { get; set; }

    public List<int> DisabledTrackIndexes { get; set; } = [];
}
