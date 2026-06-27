using Newtonsoft.Json;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Recognition;

public sealed class RecognitionObjectJsonFile
{
    [JsonProperty("version")]
    public int Version { get; set; } = 1;

    [JsonProperty("vars")]
    public Dictionary<string, string> Vars { get; set; } = [];

    [JsonProperty("regions")]
    public Dictionary<string, string> Regions { get; set; } = [];

    [JsonProperty("templates")]
    public Dictionary<string, string> Templates { get; set; } = [];

    [JsonProperty("objects")]
    public Dictionary<string, RecognitionObjectJsonConfig> Objects { get; set; } = [];
}

public sealed class RecognitionObjectJsonConfig
{
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("template")]
    public string? Template { get; set; }

    [JsonProperty("templateMode")]
    public string? TemplateMode { get; set; }

    [JsonProperty("roi")]
    public string? Roi { get; set; }

    [JsonProperty("threshold")]
    public double? Threshold { get; set; }

    [JsonProperty("use3Channels")]
    public bool? Use3Channels { get; set; }

    [JsonProperty("templateMatchMode")]
    public string? TemplateMatchMode { get; set; }

    [JsonProperty("useMask")]
    public bool? UseMask { get; set; }

    [JsonProperty("maskColor")]
    public string? MaskColor { get; set; }

    [JsonProperty("draw")]
    public bool? Draw { get; set; }

    [JsonProperty("drawColor")]
    public string? DrawColor { get; set; }

    [JsonProperty("drawWidth")]
    public float? DrawWidth { get; set; }

    [JsonProperty("maxMatchCount")]
    public int? MaxMatchCount { get; set; }

    [JsonProperty("useBinaryMatch")]
    public bool? UseBinaryMatch { get; set; }

    [JsonProperty("binaryThreshold")]
    public int? BinaryThreshold { get; set; }

    [JsonProperty("colorCode")]
    public string? ColorCode { get; set; }

    [JsonProperty("lowerColor")]
    public List<double>? LowerColor { get; set; }

    [JsonProperty("upperColor")]
    public List<double>? UpperColor { get; set; }

    [JsonProperty("matchCount")]
    public int? MatchCount { get; set; }

    [JsonProperty("ocrEngine")]
    public string? OcrEngine { get; set; }

    [JsonProperty("replace")]
    public Dictionary<string, List<string>> Replace { get; set; } = [];

    [JsonProperty("allContains")]
    public List<string> AllContains { get; set; } = [];

    [JsonProperty("oneContains")]
    public List<string> OneContains { get; set; } = [];

    [JsonProperty("regex")]
    public List<string> Regex { get; set; } = [];

    [JsonProperty("text")]
    public string? Text { get; set; }

    [JsonProperty("reference")]
    public RecognitionReferenceJsonConfig? Reference { get; set; }

    [JsonProperty("search")]
    public RecognitionSearchJsonConfig? Search { get; set; }
}

public sealed class RecognitionReferenceJsonConfig
{
    [JsonProperty("size")]
    public List<int>? Size { get; set; }

    [JsonProperty("bbox")]
    public string? Bbox { get; set; }
}

public sealed class RecognitionSearchJsonConfig
{
    [JsonProperty("anchor")]
    public string? Anchor { get; set; }

    [JsonProperty("expand")]
    public List<int>? Expand { get; set; }
}
