using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 以设置方案草稿为编辑对象：不显示继承，修改只写入草稿，由方案管理窗口显式保存。
/// </summary>
public sealed class PuloniaProfileSharedSettingsTarget : PuloniaSharedSettingsTarget
{
    /// <summary>被编辑的方案草稿（独立副本）。</summary>
    public PuloniaSettingsProfile Draft { get; }

    /// <summary>草稿被修改过。</summary>
    public bool IsDirty { get; private set; }

    /// <summary>草稿内容变化，供方案管理窗口刷新保存状态。</summary>
    public event EventHandler? DraftChanged;

    /// <summary>以方案草稿建立编辑对象。</summary>
    public PuloniaProfileSharedSettingsTarget(PuloniaSettingsProfile draft) => Draft = draft;

    /// <inheritdoc />
    public override string Title => $"设置方案「{Draft.Name}」";

    /// <inheritdoc />
    public override string Description => "方案只保存设置过的项；挂到节点后，这些项从该节点开始向下传播，节点自己修改的项优先。";

    /// <inheritdoc />
    public override bool ShowsInheritance => false;

    /// <inheritdoc />
    public override IReadOnlySet<string>? AllowedKeys => null;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, string>? CategoryUsage => null;

    /// <inheritdoc />
    public override JObject OwnSettings => Draft.Settings;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, PuloniaSharedSettingEffectiveValue> GetEffective()
        => PuloniaSettingCatalog.Definitions.ToDictionary(definition => definition.Key, definition =>
            Draft.Settings.TryGetValue(definition.Key, out var value)
                ? new PuloniaSharedSettingEffectiveValue(value.DeepClone(), "已在方案中设置", PuloniaSharedSettingSourceKind.Own)
                : new PuloniaSharedSettingEffectiveValue(definition.DefaultValue.DeepClone(),
                    "未设置，挂到节点后沿用上层设置", PuloniaSharedSettingSourceKind.Default),
            StringComparer.Ordinal);

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, JToken> GetInherited()
        => PuloniaSettingCatalog.Definitions.ToDictionary(definition => definition.Key,
            definition => definition.DefaultValue.DeepClone(), StringComparer.Ordinal);

    /// <inheritdoc />
    public override string GetHistoryText(string key) => string.Empty;

    /// <inheritdoc />
    public override void Set(string key, JToken value)
    {
        PuloniaTaskValidator.ValidateSettings(new JObject { [key] = value.DeepClone() }, "profile/" + Draft.Id);
        if (Draft.Settings.TryGetValue(key, out var current) && JToken.DeepEquals(current, value))
            return;
        Draft.Settings[key] = value.DeepClone();
        MarkChanged();
    }

    /// <inheritdoc />
    public override void Reset(string key)
    {
        if (Draft.Settings.Remove(key))
            MarkChanged();
    }

    /// <inheritdoc />
    public override void ResetAll()
    {
        if (Draft.Settings.Count == 0)
            return;
        Draft.Settings = new JObject();
        MarkChanged();
    }

    /// <summary>用整份设置替换草稿内容，例如“从全局配置创建”。</summary>
    public void ReplaceAll(JObject settings)
    {
        PuloniaTaskValidator.ValidateSettings(settings, "profile/" + Draft.Id);
        Draft.Settings = (JObject)settings.DeepClone();
        MarkChanged();
    }

    /// <summary>草稿名称改变后刷新标题。</summary>
    public void NotifyRenamed() => MarkChanged();

    /// <summary>标记草稿已修改并通知编辑器与管理窗口。</summary>
    private void MarkChanged()
    {
        IsDirty = true;
        RaiseChanged();
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }
}
