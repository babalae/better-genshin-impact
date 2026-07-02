using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Simulator.Cloud;

/// <summary>
/// C# 输入后端发送给 <c>window.__bgiCloudInput.dispatch</c> 的单条命令。
/// 不同命令只填写自身需要的字段，序列化时会忽略其余空字段。
/// </summary>
public sealed class CloudInputCommand
{
    /// <summary>
    /// 命令类型，例如 keyDown、move、click 或 scroll。
    /// </summary>
    [JsonProperty("type")]
    public required string Type { get; init; }

    /// <summary>
    /// 键盘命令使用的浏览器 KeyboardEvent code。
    /// </summary>
    [JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)]
    public string? Code { get; init; }

    /// <summary>
    /// 鼠标命令使用的按钮名称：left、right 或 middle。
    /// </summary>
    [JsonProperty("button", NullValueHandling = NullValueHandling.Ignore)]
    public string? Button { get; init; }

    /// <summary>
    /// 画面内归一化绝对横坐标，取值范围为 0～1。
    /// </summary>
    [JsonProperty("x", NullValueHandling = NullValueHandling.Ignore)]
    public double? X { get; init; }

    /// <summary>
    /// 画面内归一化绝对纵坐标，取值范围为 0～1。
    /// </summary>
    [JsonProperty("y", NullValueHandling = NullValueHandling.Ignore)]
    public double? Y { get; init; }

    /// <summary>
    /// RTC 相对鼠标横向位移。
    /// </summary>
    [JsonProperty("dx", NullValueHandling = NullValueHandling.Ignore)]
    public double? DeltaX { get; init; }

    /// <summary>
    /// RTC 相对鼠标纵向位移。
    /// </summary>
    [JsonProperty("dy", NullValueHandling = NullValueHandling.Ignore)]
    public double? DeltaY { get; init; }

    /// <summary>
    /// 垂直滚轮增量。
    /// </summary>
    [JsonProperty("delta", NullValueHandling = NullValueHandling.Ignore)]
    public double? Delta { get; init; }

    /// <summary>
    /// IME 文本输入内容。
    /// </summary>
    [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
    public string? Text { get; init; }

    /// <summary>
    /// 点击或按键动作保持的毫秒数。
    /// </summary>
    [JsonProperty("holdMs", NullValueHandling = NullValueHandling.Ignore)]
    public int? HoldMilliseconds { get; init; }
}
