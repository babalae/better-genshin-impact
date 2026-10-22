using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>
/// 独立任务专属设置视图的公共基类：载入当前有效参数快照，应用时只提交真实变化的字段。
/// 不依赖任何宿主（任务计划节点、独立任务页），宿主负责提供快照并提交变化。
/// </summary>
public abstract class SoloTaskSettingsViewModel : ObservableObject
{
    /// <summary>载入时的有效参数快照；“重置草稿”和来源变化检测依赖它。</summary>
    protected JObject Original { get; }

    /// <summary>保存当前有效参数快照，具体字段由派生类填充。</summary>
    /// <param name="original">当前有效参数；内部保存独立副本，调用方后续修改不影响快照。</param>
    protected SoloTaskSettingsViewModel(JObject original)
    {
        Original = (JObject)original.DeepClone();
    }

    /// <summary>把输入相对快照的真实变化写入 changes；由宿主的“应用设置”统一校验提交。</summary>
    /// <param name="changes">接收变化字段的对象，键为参数类的 snake_case 名称。</param>
    public abstract void CollectChanges(JObject changes);

    /// <summary>按名称返回快照中的原始参数，供来源变化检测。</summary>
    /// <param name="name">参数名。</param>
    /// <returns>快照中的值；不存在时为 null。</returns>
    public JToken? GetOriginalValue(string name) => Original[name];

    /// <summary>读取字符串参数；缺失或空值返回空字符串，与旧一条龙“留空”语义一致。</summary>
    /// <param name="values">参数对象。</param>
    /// <param name="name">参数名。</param>
    /// <returns>字符串值。</returns>
    protected static string ReadString(JObject values, string name) => values[name] switch
    {
        JValue value when value.Value is string text => text,
        JValue value when value.Value is not null => value.Value.ToString() ?? string.Empty,
        _ => string.Empty
    };

    /// <summary>读取布尔参数；缺失或类型不符返回回退值。</summary>
    /// <param name="values">参数对象。</param>
    /// <param name="name">参数名。</param>
    /// <param name="fallback">回退值。</param>
    /// <returns>布尔值。</returns>
    protected static bool ReadBool(JObject values, string name, bool fallback = false)
    {
        var token = values[name];
        return token is { Type: JTokenType.Boolean } ? token.Value<bool>() : fallback;
    }

    /// <summary>读取整数参数；缺失或类型不符返回回退值。</summary>
    /// <param name="values">参数对象。</param>
    /// <param name="name">参数名。</param>
    /// <param name="fallback">回退值。</param>
    /// <returns>整数值。</returns>
    protected static int ReadInt(JObject values, string name, int fallback = 0)
    {
        var token = values[name];
        return token is { Type: JTokenType.Integer } ? token.Value<int>() : fallback;
    }

    /// <summary>字符串输入与快照不同才写入变更。</summary>
    /// <param name="changes">接收变化的对象。</param>
    /// <param name="name">参数名。</param>
    /// <param name="original">快照值。</param>
    /// <param name="current">当前输入。</param>
    protected static void WriteString(JObject changes, string name, string original, string current)
    {
        if (!string.Equals(original, current, StringComparison.Ordinal)) changes[name] = current;
    }

    /// <summary>布尔输入与快照不同才写入变更。</summary>
    /// <param name="changes">接收变化的对象。</param>
    /// <param name="name">参数名。</param>
    /// <param name="original">快照值。</param>
    /// <param name="current">当前输入。</param>
    protected static void WriteBool(JObject changes, string name, bool original, bool current)
    {
        if (original != current) changes[name] = current;
    }

    /// <summary>整数输入与快照不同才写入变更。</summary>
    /// <param name="changes">接收变化的对象。</param>
    /// <param name="name">参数名。</param>
    /// <param name="original">快照值。</param>
    /// <param name="current">当前输入。</param>
    protected static void WriteInt(JObject changes, string name, int original, int current)
    {
        if (original != current) changes[name] = current;
    }

    /// <summary>
    /// 把参数里的战斗策略名或路径转换为下拉框显示的策略名：
    /// 自动战斗策略目录本身显示为“根据队伍自动选择”，目录下的策略文件显示为文件名（不含扩展名），
    /// 策略名原样显示，目录之外的路径无法用名称表示也原样显示。
    /// </summary>
    /// <param name="nameOrPath">参数中的策略名或路径；为空时视为自动选择，与任务的解析规则一致。</param>
    /// <returns>策略显示名。</returns>
    protected static string ToStrategyName(string? nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return CombatStrategyResolver.AutoSelectName;
        // 非绝对路径即策略名（包括“根据队伍自动选择”和连招固定名）
        if (!Path.IsPathRooted(nameOrPath)) return nameOrPath;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Global.Absolute(@"User\AutoFight\")));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(nameOrPath));
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return CombatStrategyResolver.AutoSelectName;
        if (string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(full);
        return nameOrPath;
    }

    /// <summary>当前值不在选项中时追加到末尾，外部导入的暂不可用选项仍能显示，用户可以主动改选。</summary>
    /// <param name="choices">基础选项。</param>
    /// <param name="current">当前值。</param>
    /// <returns>包含当前值的选项。</returns>
    protected static string[] WithCurrentChoice(string[] choices, string current)
        => string.IsNullOrEmpty(current) || choices.Contains(current, StringComparer.Ordinal) ? choices : [.. choices, current];
}
