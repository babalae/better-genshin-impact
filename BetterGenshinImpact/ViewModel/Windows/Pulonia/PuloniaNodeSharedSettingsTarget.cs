using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 以任务树节点为编辑对象：显示设置链继承，修改写入节点自身的共享设置，经文档撤销与自动保存。
/// </summary>
public sealed class PuloniaNodeSharedSettingsTarget : PuloniaSharedSettingsTarget
{
    /// <summary>节点 ID；撤销会重建树包装，按 ID 找回当前包装。</summary>
    private readonly string _nodeId;

    /// <summary>节点所属文档。</summary>
    public PuloniaTaskPlanDocumentViewModel Document { get; }

    /// <summary>当前节点包装。</summary>
    public PuloniaTaskNodeViewModel Node { get; private set; }

    /// <summary>节点仍在文档中。</summary>
    private bool _isAvailable = true;

    /// <summary>以指定节点建立编辑对象，并订阅文档内容与方案变化。</summary>
    public PuloniaNodeSharedSettingsTarget(PuloniaTaskNodeViewModel node)
    {
        Node = node;
        _nodeId = node.Id;
        Document = node.Document;
        Document.ContentChanged += OnDocumentChanged;
        Document.ProfilesChanged += OnDocumentChanged;
    }

    /// <inheritdoc />
    public override string Title => "共享设置 · " + (Document.IsPlanRoot(Node) ? $"计划「{Document.Name}」"
        : Node.IsLeaf ? $"任务「{Node.Name}」"
        : Node.Model.Source?.Kind == "plan" ? $"引用「{Node.Name}」"
        : $"分组「{Node.Name}」");

    /// <inheritdoc />
    public override string Description => Document.IsPlanRoot(Node)
        ? "计划级设置会传给本计划的全部任务；分组和任务上修改的项优先。"
        : Node.IsLeaf
            ? "只对这个任务生效；未修改的项沿用上层设置。"
            : Node.Model.Source?.Kind == "plan"
                ? "这里的设置会替换被引用计划的计划级设置；被引用计划内部分组和任务上的设置仍然优先。"
                : "这里的设置会传给本组下的全部任务；子组和任务上修改的项优先。";

    /// <inheritdoc />
    public override bool ShowsInheritance => true;

    /// <inheritdoc />
    public override bool SupportsProfile => true;

    /// <inheritdoc />
    public override bool IsAvailable => _isAvailable;

    /// <inheritdoc />
    public override IReadOnlySet<string>? AllowedKeys => Node.IsLeaf
        ? Document.GetSharedSettingUsage(Node).Keys ?? new HashSet<string>(StringComparer.Ordinal)
        : null;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, string>? CategoryUsage
    {
        get
        {
            if (Node.IsLeaf)
                return null;
            var usage = Document.GetSharedSettingUsage(Node);
            return usage.CategoryCounts.ToDictionary(item => item.Key, item =>
            {
                var total = item.Value.Values.Sum();
                var detail = string.Join("、", item.Value.OrderByDescending(pair => pair.Value)
                    .Select(pair => $"{pair.Key} {pair.Value}"));
                return $"影响 {total} 个任务（{detail}）";
            }, StringComparer.Ordinal);
        }
    }

    /// <inheritdoc />
    public override JObject OwnSettings => Node.Model.Settings;

    /// <summary>节点当前所挂方案 ID。</summary>
    public string? ProfileId => Node.Model.ProfileId;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, PuloniaSharedSettingEffectiveValue> GetEffective()
    {
        var resolution = Document.ResolveNodeSettings(Node);
        return resolution.Values.ToDictionary(item => item.Key, item =>
        {
            var source = resolution.Sources[item.Key];
            return new PuloniaSharedSettingEffectiveValue(item.Value, Document.DescribeSettingSource(source, Node), ToKind(source));
        }, StringComparer.Ordinal);
    }

    /// <summary>把设置链来源归为界面的来源种类：本节点自身的修改单独标出，其余节点的修改视为继承。</summary>
    private PuloniaSharedSettingSourceKind ToKind(PuloniaSettingSource source) => source.Kind switch
    {
        PuloniaSettingSourceKind.Default => PuloniaSharedSettingSourceKind.Default,
        PuloniaSettingSourceKind.Profile => PuloniaSharedSettingSourceKind.Profile,
        _ => source.Level?.Node.Id == Node.Id ? PuloniaSharedSettingSourceKind.Own : PuloniaSharedSettingSourceKind.Inherited
    };

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, JToken> GetInherited()
        => Document.ResolveInheritedSettings(Node).Values;

    /// <inheritdoc />
    public override string GetHistoryText(string key)
        => string.Join("\n", Document.GetSettingHistory(Node, key).Select(entry =>
            (entry.IsEffective ? "▶ " : "　 ") + entry.Location + "：" + PuloniaSharedSettingText.Format(key, entry.Value)));

    /// <inheritdoc />
    public override void Set(string key, JToken value) => Document.SetSetting(Node, key, value);

    /// <inheritdoc />
    public override void Reset(string key) => Document.ResetSetting(Node, key);

    /// <inheritdoc />
    public override void ResetAll() => Document.ResetAllSettings(Node);

    /// <summary>移除与继承值相同的覆盖，返回移除数量。</summary>
    public int RemoveRedundant() => Document.RemoveRedundantSettings(Node);

    /// <summary>为节点选择方案；为空表示不使用。</summary>
    public void SelectProfile(string? profileId) => Document.SelectProfile(Node, profileId);

    /// <summary>文档变化后按 ID 找回节点包装；节点被删除时转为不可用。</summary>
    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (!Document.EnumerateNodes().Contains(Node))
        {
            if (Document.EnumerateNodes().FirstOrDefault(item => item.Id == _nodeId) is { } current)
                Node = current;
            else
                _isAvailable = false;
        }
        RaiseChanged();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Document.ContentChanged -= OnDocumentChanged;
        Document.ProfilesChanged -= OnDocumentChanged;
    }
}
