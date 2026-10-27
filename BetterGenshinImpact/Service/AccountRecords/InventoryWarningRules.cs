using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>核实来源提醒的可编辑参数；不执行代码，不影响红灯校验。</summary>
public sealed class InventoryWarningRule
{
    /// <summary>是否启用本条规则。</summary>
    [JsonProperty(Required = Required.Always)]
    public bool Enabled { get; set; } = true;
    /// <summary>来源标识精确匹配，星号表示所有来源。</summary>
    [JsonProperty(Required = Required.Always)]
    public string Source { get; set; } = "*";
    /// <summary>检查最近多少次上报，范围 1～20。</summary>
    [JsonProperty(Required = Required.Always)]
    public int WindowSize { get; set; } = 10;
    /// <summary>窗口中触发提醒的错误次数，范围 1～WindowSize。</summary>
    [JsonProperty(Required = Required.Always)]
    public int ErrorThreshold { get; set; } = 5;
    /// <summary>橘灯提示语，最多 200 个字符。</summary>
    [JsonProperty(Required = Required.Always)]
    public string Message { get; set; } = "该核实来源近期多次报错，请自查或联系开发者。";
}

public sealed partial class InventoryStore
{
    /// <summary>首次打开生成默认规则；已有文件即使有误也保留供用户修订。</summary>
    public Task<string> GetWarningRulesPathAsync() => Locked(() =>
    {
        var path = Path.Combine(_root, "warning-rules.json");
        if (!File.Exists(path)) Write(path, new InventoryWarningRule());
        return path;
    });

    /// <summary>每批上报读取一次；无文件使用默认规则，非法配置明确报错且不覆盖原文。</summary>
    private InventoryWarningRule ReadWarningRule()
    {
        var path = Path.Combine(_root, "warning-rules.json");
        if (!File.Exists(path)) return new InventoryWarningRule();
        try
        {
            using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path)));
            var json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            var rule = json.ToObject<InventoryWarningRule>(JsonSerializer.Create(new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }))!;
            if (string.IsNullOrWhiteSpace(rule.Source) || rule.Source != rule.Source.Trim() || rule.Source.Length > 1024 ||
                rule.WindowSize is < 1 or > 20 || rule.ErrorThreshold < 1 || rule.ErrorThreshold > rule.WindowSize ||
                string.IsNullOrWhiteSpace(rule.Message) || rule.Message.Length > 200)
                throw new InvalidDataException("Source 必须为来源标识或 *；WindowSize 为 1～20；ErrorThreshold 为 1～WindowSize；Message 为 1～200 字符。");
            return rule;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"Warning 规则无效，请修订 {path}：{ex.Message}", ex);
        }
    }

    /// <summary>仅统计用户确认后的上报，规则中的来源使用实际标识。</summary>
    private static bool SourceWarning(AccountRecordSource source, InventoryWarningRule rule) => rule.Enabled &&
        (rule.Source == "*" || rule.Source == source.Reference) && source.Attempts
        .Where(item => source.WarningTrackingSince == null || item.CreatedAt > source.WarningTrackingSince)
        .TakeLast(rule.WindowSize).Count(item => item.Failed) >= rule.ErrorThreshold;
}
