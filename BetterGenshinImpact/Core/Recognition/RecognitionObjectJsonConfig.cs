using Newtonsoft.Json;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Recognition;

/// <summary>
/// `Recognition.json` 文件根对象。
/// </summary>
public sealed class RecognitionObjectJsonFile
{
    /// <summary>
    /// 配置文件版本号。
    /// 当前实现固定使用 `1`。
    /// </summary>
    [JsonProperty("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// 公共变量表。
    /// 值为表达式字符串，可在 `roi`、`reference.bbox` 等表达式中复用。
    /// 常用内置变量有 `cw`、`ch`、`cx`、`cy`、`s`。
    /// </summary>
    [JsonProperty("vars")]
    public Dictionary<string, string> Vars { get; set; } = [];

    /// <summary>
    /// 公共区域表达式表。
    /// 值为返回 `Rect` 的表达式字符串，其他字段可通过 `@别名` 引用。
    /// 常用于复用 ROI 定义。
    /// </summary>
    [JsonProperty("regions")]
    public Dictionary<string, string> Regions { get; set; } = [];

    /// <summary>
    /// 公共模板别名表。
    /// 值为模板图片文件名或路径，`template` 可通过 `@别名` 引用。
    /// </summary>
    [JsonProperty("templates")]
    public Dictionary<string, string> Templates { get; set; } = [];

    /// <summary>
    /// 识别对象集合。
    /// Key 为对象名，也就是调用 `RecognitionAssets.Get(taskName, objectName, ...)` 时的 `objectName`。
    /// </summary>
    [JsonProperty("objects")]
    public Dictionary<string, RecognitionObjectJsonConfig> Objects { get; set; } = [];
}

/// <summary>
/// 单个识别对象的 JSON 配置。
/// </summary>
public sealed class RecognitionObjectJsonConfig
{
    /// <summary>
    /// 可选的运行时名称。
    /// 不填时：
    /// 模板匹配默认取模板文件名（不含扩展名）；
    /// 其他类型默认取 objects 下的对象名。
    /// </summary>
    [JsonProperty("name")]
    public string? Name { get; set; }

    /// <summary>
    /// 识别类型。
    /// 文本必须与 `RecognitionTypes` 枚举名一致，例如 `TemplateMatch`、`OcrMatch`、`ColorMatch`。
    /// </summary>
    [JsonProperty("type")]
    public string? Type { get; set; }

    /// <summary>
    /// 模板图片文件名、相对路径或模板别名。
    /// 仅 `TemplateMatch` 使用；支持写成 `@模板别名`。
    /// </summary>
    [JsonProperty("template")]
    public string? Template { get; set; }

    /// <summary>
    /// 模板图片加载模式。
    /// 文本必须与 OpenCV `ImreadModes` 枚举名一致；不填默认 `Color`。
    /// </summary>
    [JsonProperty("templateMode")]
    public string? TemplateMode { get; set; }

    /// <summary>
    /// 感兴趣区域（ROI）。
    /// 值为返回 `Rect` 的表达式字符串，也可写成 `@区域别名`。
    /// </summary>
    [JsonProperty("roi")]
    public string? Roi { get; set; }

    /// <summary>
    /// 模板匹配阈值。
    /// 仅模板匹配常用；不填时沿用 `RecognitionObject` 默认值。
    /// </summary>
    [JsonProperty("threshold")]
    public double? Threshold { get; set; }

    /// <summary>
    /// 是否启用三通道模板匹配。
    /// 仅 `TemplateMatch` 使用。
    /// </summary>
    [JsonProperty("use3Channels")]
    public bool? Use3Channels { get; set; }

    /// <summary>
    /// 模板匹配算法。
    /// 文本必须与 OpenCV `TemplateMatchModes` 枚举名一致，例如 `CCoeffNormed`、`CCorrNormed`。
    /// </summary>
    [JsonProperty("templateMatchMode")]
    public string? TemplateMatchMode { get; set; }

    /// <summary>
    /// 是否启用模板遮罩。
    /// 仅 `TemplateMatch` 使用。
    /// </summary>
    [JsonProperty("useMask")]
    public bool? UseMask { get; set; }

    /// <summary>
    /// 遮罩颜色，使用 HTML 颜色格式，例如 `#00FF00`。
    /// 当 `useMask=true` 时用于生成遮罩。
    /// </summary>
    [JsonProperty("maskColor")]
    public string? MaskColor { get; set; }

    /// <summary>
    /// 识别成功时是否绘制调试框。
    /// </summary>
    [JsonProperty("draw")]
    public bool? Draw { get; set; }

    /// <summary>
    /// 调试框颜色，使用 HTML 颜色格式，例如 `#FF0000`。
    /// </summary>
    [JsonProperty("drawColor")]
    public string? DrawColor { get; set; }

    /// <summary>
    /// 调试框线宽。
    /// </summary>
    [JsonProperty("drawWidth")]
    public float? DrawWidth { get; set; }

    /// <summary>
    /// 单次模板匹配最多返回多少个结果。
    /// 仅 `TemplateMatch` 使用；`-1` 表示不限制。
    /// </summary>
    [JsonProperty("maxMatchCount")]
    public int? MaxMatchCount { get; set; }

    /// <summary>
    /// 是否先做二值化再进行模板匹配。
    /// 仅 `TemplateMatch` 使用。
    /// </summary>
    [JsonProperty("useBinaryMatch")]
    public bool? UseBinaryMatch { get; set; }

    /// <summary>
    /// 二值化阈值。
    /// 当 `useBinaryMatch=true` 时使用。
    /// </summary>
    [JsonProperty("binaryThreshold")]
    public int? BinaryThreshold { get; set; }

    /// <summary>
    /// 颜色转换方式。
    /// 文本必须与 OpenCV `ColorConversionCodes` 枚举名一致，例如 `BGR2RGB`、`BGR2HSV`、`BGR2GRAY`。
    /// </summary>
    [JsonProperty("colorCode")]
    public string? ColorCode { get; set; }

    /// <summary>
    /// 颜色下界。
    /// 用于 `ColorMatch` / `ColorRangeAndOcr` 等颜色范围识别，长度支持 1 到 4 个数字。
    /// </summary>
    [JsonProperty("lowerColor")]
    public List<double>? LowerColor { get; set; }

    /// <summary>
    /// 颜色上界。
    /// 用于 `ColorMatch` / `ColorRangeAndOcr` 等颜色范围识别，长度支持 1 到 4 个数字。
    /// </summary>
    [JsonProperty("upperColor")]
    public List<double>? UpperColor { get; set; }

    /// <summary>
    /// 颜色匹配要求的最小命中点数。
    /// 主要用于 `ColorMatch`。
    /// </summary>
    [JsonProperty("matchCount")]
    public int? MatchCount { get; set; }

    /// <summary>
    /// OCR 引擎。
    /// 文本必须与 `OcrEngineTypes` 枚举名一致；当前通常写 `Paddle`。
    /// </summary>
    [JsonProperty("ocrEngine")]
    public string? OcrEngine { get; set; }

    /// <summary>
    /// OCR 结果替换表。
    /// Key 为期望文本，Value 为会被替换到该文本的一组误识别写法。
    /// </summary>
    [JsonProperty("replace")]
    public Dictionary<string, List<string>> Replace { get; set; } = [];

    /// <summary>
    /// OCR 全包含匹配。
    /// 列表中的所有文本都命中时才算成功。
    /// </summary>
    [JsonProperty("allContains")]
    public List<string> AllContains { get; set; } = [];

    /// <summary>
    /// OCR 任一包含匹配。
    /// 列表中任意一个文本命中就算成功。
    /// </summary>
    [JsonProperty("oneContains")]
    public List<string> OneContains { get; set; } = [];

    /// <summary>
    /// OCR 正则匹配列表。
    /// 列表中的所有正则都命中时才算成功。
    /// </summary>
    [JsonProperty("regex")]
    public List<string> Regex { get; set; } = [];

    /// <summary>
    /// OCR 目标文本。
    /// 常用于多 OCR 结果筛选，或与其他 OCR 匹配配置配合使用。
    /// </summary>
    [JsonProperty("text")]
    public string? Text { get; set; }

    /// <summary>
    /// 模板参考信息。
    /// 用于描述模板来源截图尺寸和原始包围盒，方便后续定位或搜索扩展。
    /// </summary>
    [JsonProperty("reference")]
    public RecognitionReferenceJsonConfig? Reference { get; set; }

    /// <summary>
    /// 搜索扩展配置。
    /// 用于指定锚点和扩展范围。
    /// </summary>
    [JsonProperty("search")]
    public RecognitionSearchJsonConfig? Search { get; set; }
}

/// <summary>
/// 模板参考信息配置。
/// </summary>
public sealed class RecognitionReferenceJsonConfig
{
    /// <summary>
    /// 模板来源截图尺寸，格式为 `[width, height]`。
    /// </summary>
    [JsonProperty("size")]
    public List<int>? Size { get; set; }

    /// <summary>
    /// 模板在来源截图中的包围盒，值为返回 `Rect` 的表达式字符串。
    /// </summary>
    [JsonProperty("bbox")]
    public string? Bbox { get; set; }
}

/// <summary>
/// 搜索扩展配置。
/// </summary>
public sealed class RecognitionSearchJsonConfig
{
    /// <summary>
    /// 搜索锚点。
    /// 文本必须与 `SearchAnchorMode` 枚举名一致。
    /// </summary>
    [JsonProperty("anchor")]
    public string? Anchor { get; set; }

    /// <summary>
    /// 在锚点附近额外扩展的搜索尺寸，格式为 `[width, height]`。
    /// </summary>
    [JsonProperty("expand")]
    public List<int>? Expand { get; set; }
}
