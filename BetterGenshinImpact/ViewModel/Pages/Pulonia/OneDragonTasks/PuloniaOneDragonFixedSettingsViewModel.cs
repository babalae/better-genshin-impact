using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia.OneDragonTasks;

/// <summary>固定任务专属设置视图的公共基类：载入节点有效参数快照，应用时只提交真实变化的字段。</summary>
public abstract class PuloniaOneDragonFixedSettingsViewModel : ObservableObject
{
    /// <summary>当前编辑的清单节点；提交与来源变化检测都以它为准。</summary>
    public PuloniaTaskNodeViewModel Node { get; }

    /// <summary>载入时的有效参数快照；“重置草稿”和来源变化检测依赖它。</summary>
    protected JObject Original { get; }

    /// <summary>读取节点当前有效参数建立快照，具体字段由派生类填充。</summary>
    protected PuloniaOneDragonFixedSettingsViewModel(PuloniaTaskNodeViewModel node)
    {
        Node = node;
        Original = node.Document.GetOneDragonParameters(node);
    }

    /// <summary>把输入相对快照的真实变化写入 changes；由“应用设置”统一校验提交。</summary>
    public abstract void CollectChanges(JObject changes);

    /// <summary>按名称返回快照中的原始参数，供来源变化检测。</summary>
    public JToken? GetOriginalValue(string name) => Original[name];

    /// <summary>读取字符串参数；缺失或空值返回空字符串，与旧一条龙“留空”语义一致。</summary>
    protected static string ReadString(JObject values, string name) => values[name] switch
    {
        JValue value when value.Value is string text => text,
        JValue value when value.Value is not null => value.Value.ToString() ?? string.Empty,
        _ => string.Empty
    };

    /// <summary>读取布尔参数；缺失或类型不符返回回退值。</summary>
    protected static bool ReadBool(JObject values, string name, bool fallback = false)
    {
        var token = values[name];
        return token is { Type: JTokenType.Boolean } ? token.Value<bool>() : fallback;
    }

    /// <summary>读取整数参数；缺失或类型不符返回回退值。</summary>
    protected static int ReadInt(JObject values, string name, int fallback = 0)
    {
        var token = values[name];
        return token is { Type: JTokenType.Integer } ? token.Value<int>() : fallback;
    }

    /// <summary>字符串输入与快照不同才写入变更。</summary>
    protected static void WriteString(JObject changes, string name, string original, string current)
    {
        if (!string.Equals(original, current, System.StringComparison.Ordinal)) changes[name] = current;
    }

    /// <summary>布尔输入与快照不同才写入变更。</summary>
    protected static void WriteBool(JObject changes, string name, bool original, bool current)
    {
        if (original != current) changes[name] = current;
    }

    /// <summary>整数输入与快照不同才写入变更。</summary>
    protected static void WriteInt(JObject changes, string name, int original, int current)
    {
        if (original != current) changes[name] = current;
    }
}
