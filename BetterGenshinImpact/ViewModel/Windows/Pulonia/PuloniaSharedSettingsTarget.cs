using System;
using System.Collections.Generic;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 设置项有效值的来源种类，编辑器据此显示来源图标；完整来源文字放在悬停说明中。
/// </summary>
public enum PuloniaSharedSettingSourceKind
{
    /// <summary>整条设置链都没有修改，使用默认值；方案中表示未设置。</summary>
    Default,

    /// <summary>来自上层节点（计划、分组、引用）自身的修改。</summary>
    Inherited,

    /// <summary>来自某一层所挂的设置方案。</summary>
    Profile,

    /// <summary>编辑对象自身的修改（本节点，或方案中已设置）。</summary>
    Own
}

/// <summary>
/// 设置项的有效值与来源。
/// </summary>
/// <param name="Value">有效值。</param>
/// <param name="Source">来源文字，例如“分组「蒙德」”“方案「深渊队」（本节点）”。</param>
/// <param name="Kind">来源种类。</param>
public sealed record PuloniaSharedSettingEffectiveValue(JToken Value, string Source, PuloniaSharedSettingSourceKind Kind);

/// <summary>
/// 共享设置编辑器的编辑对象：任务树节点（显示继承）或设置方案草稿（不显示继承）。
/// 编辑器只通过这里读取有效值和写入修改，不关心值存放在哪里。
/// </summary>
public abstract class PuloniaSharedSettingsTarget : IDisposable
{
    /// <summary>编辑器标题，例如“共享设置 · 分组「蒙德」”。</summary>
    public abstract string Title { get; }

    /// <summary>标题下的说明。</summary>
    public abstract string Description { get; }

    /// <summary>是否按设置链显示继承来源；方案草稿为 false。</summary>
    public abstract bool ShowsInheritance { get; }

    /// <summary>是否可以为编辑对象选择设置方案。</summary>
    public virtual bool SupportsProfile => false;

    /// <summary>编辑对象是否仍然存在；节点被删除后为 false，编辑器转为只读。</summary>
    public virtual bool IsAvailable => true;

    /// <summary>
    /// 编辑对象能使用的设置项；为 null 表示全部（分组、计划根、方案）。叶子只能使用其能力绑定的设置项。
    /// </summary>
    public abstract IReadOnlySet<string>? AllowedKeys { get; }

    /// <summary>
    /// 每个类别影响的任务数说明；为空表示不显示。类别 ID 不在其中时视为没有任务使用。
    /// </summary>
    public abstract IReadOnlyDictionary<string, string>? CategoryUsage { get; }

    /// <summary>编辑对象自身的设置覆盖（节点自身设置或方案内容）。</summary>
    public abstract JObject OwnSettings { get; }

    /// <summary>读取每个设置项的有效值、来源文本与来源种类。</summary>
    public abstract IReadOnlyDictionary<string, PuloniaSharedSettingEffectiveValue> GetEffective();

    /// <summary>读取恢复继承后每个设置项的值。</summary>
    public abstract IReadOnlyDictionary<string, JToken> GetInherited();

    /// <summary>设置项沿设置链的取值说明，用于悬停查看；不显示继承时为空。</summary>
    public abstract string GetHistoryText(string key);

    /// <summary>覆盖一个设置项。</summary>
    public abstract void Set(string key, JToken value);

    /// <summary>恢复一个设置项的继承（方案中为移除该项）。</summary>
    public abstract void Reset(string key);

    /// <summary>恢复全部设置项的继承。</summary>
    public abstract void ResetAll();

    /// <summary>编辑对象的内容在外部发生变化（撤销、其他窗口修改、方案刷新）。</summary>
    public event EventHandler? Changed;

    /// <summary>通知编辑器重新读取。</summary>
    protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>解除对外部事件的订阅。</summary>
    public virtual void Dispose()
    {
    }
}
