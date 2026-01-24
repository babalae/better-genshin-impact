using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Tavern.Model;

public sealed class MarkerExtraVo
{
    public sealed class UndergroundVo
    {
        [JsonProperty("is_underground")]
        public bool? IsUnderground { get; set; }

        [JsonProperty("is_global")]
        public bool? IsGlobal { get; set; }

        [JsonProperty("region_levels")]
        public List<string>? RegionLevels { get; set; }
    }

    public sealed class IconOverrideVo
    {
        [JsonProperty("id")]
        public long? Id { get; set; }

        [JsonProperty("minZoom")]
        public decimal? MinZoom { get; set; }

        [JsonProperty("maxZoom")]
        public decimal? MaxZoom { get; set; }
    }

    public sealed class V2_8_IslandVo
    {
        [JsonProperty("island_name")]
        public string? IslandName { get; set; }

        [JsonProperty("island_state")]
        public List<string>? IslandState { get; set; }
    }

    [JsonProperty("underground")]
    public UndergroundVo? Underground { get; set; }

    [JsonProperty("iconOverride")]
    public IconOverrideVo? IconOverride { get; set; }

    [JsonProperty("1_6_island")]
    public List<string>? V1_6_Island { get; set; }

    [JsonProperty("2_8_island")]
    public V2_8_IslandVo? V2_8_Island { get; set; }
}