using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 一个子树中启用的地图追踪路线的路线 CD 汇总；目录引用不展开，只计入账本中仍在 CD 的记录。
/// </summary>
/// <param name="Total">已知的路线数（不含目录引用中的文件）。</param>
/// <param name="Skipped">本次会因 CD 跳过的路线数（CD 中或一次性已完成）。</param>
/// <param name="Unmatched">刷新规则未识别的路线数。</param>
/// <param name="EarliestNext">跳过的路线中最早的下次可执行时间。</param>
/// <param name="HasDirectory">是否包含目录引用，总数因此不完整。</param>
internal sealed record PuloniaCooldownSummary(int Total, int Skipped, int Unmatched, DateTimeOffset? EarliestNext, bool HasDirectory)
{
    /// <summary>空汇总。</summary>
    public static PuloniaCooldownSummary Empty { get; } = new(0, 0, 0, null, false);

    /// <summary>合并两个子树的汇总。</summary>
    public static PuloniaCooldownSummary operator +(PuloniaCooldownSummary left, PuloniaCooldownSummary right)
        => new(left.Total + right.Total, left.Skipped + right.Skipped, left.Unmatched + right.Unmatched,
            left.EarliestNext is null ? right.EarliestNext
            : right.EarliestNext is null ? left.EarliestNext
            : left.EarliestNext < right.EarliestNext ? left.EarliestNext : right.EarliestNext,
            left.HasDirectory || right.HasDirectory);
}

/// <summary>
/// 一个目录引用在路线 CD 账本中的位置：资源键前缀与是否包含子目录，以及该组有效的路线 CD 设置。
/// </summary>
/// <param name="EffectKeyPrefix">目录下文件资源键的公共前缀，以 <c>/</c> 结尾。</param>
/// <param name="Recursive">是否包含子目录中的文件。</param>
/// <param name="Setting">目录引用组处有效的路线 CD 设置。</param>
internal sealed record PuloniaCooldownDirectory(string EffectKeyPrefix, bool Recursive, JToken? Setting);

/// <summary>
/// 路线 CD 是一项对象值的共享设置：草稿读取与整体覆盖复用文档撤销和自动保存边界，表单窗口由设置页打开；
/// 这里还提供任务树标记所需的叶子规则与目录引用位置，规则解析与运行准备使用同一个 Resolve。
/// </summary>
public partial class PuloniaTaskPlanDocumentViewModel
{
    /// <summary>任务页签顶部的路线 CD 摘要，例如“路线 CD：8 项 CD 中 · 3 项未识别”；计划没有地图追踪路线时为空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCooldownSummary))]
    private string? _cooldownSummaryText;

    /// <summary>摘要的悬停说明。</summary>
    [ObservableProperty]
    private string? _cooldownSummaryToolTip;

    /// <summary>是否显示路线 CD 摘要。</summary>
    public bool HasCooldownSummary => !string.IsNullOrEmpty(CooldownSummaryText);

    /// <summary>由编辑会话写入整份计划的汇总。</summary>
    /// <param name="text">摘要文字；为空时隐藏。</param>
    /// <param name="toolTip">悬停说明。</param>
    internal void SetCooldownSummary(string? text, string? toolTip)
    {
        CooldownSummaryText = text;
        CooldownSummaryToolTip = toolTip;
    }

    /// <summary>从节点处的有效共享设置创建独立草稿；目录引用组预览只解释该组规则，普通分组附带下属路线的识别分布。</summary>
    internal PuloniaCollectionCooldownViewModel CreateCollectionCooldownDraft(PuloniaTaskNodeViewModel node)
    {
        var resolution = ResolveNodeSettings(node);
        var key = PuloniaSettingCatalog.CollectionCooldownKey;
        var draft = new PuloniaCollectionCooldownViewModel();
        draft.Load(resolution.Values[key], DescribeSettingSource(resolution.Sources[key], node));
        var isDirectory = node.Model.Source?.Kind == "directory";
        draft.SetResource(ResolveCooldownPath(node.Model, isDirectory),
            node.Model.Resource ?? node.Model.Source?.Resource, isDirectory);
        if (isDirectory)
            draft.SetDistribution("目录引用在运行前按固定版本展开，每个文件按自己的目录分别识别。");
        else if (!node.IsLeaf)
            draft.SetDistribution(DescribeCooldownDistribution(node));
        return draft;
    }

    /// <summary>节点处有效的路线 CD 设置值。</summary>
    internal JToken? GetEffectiveCooldownSetting(PuloniaTaskNodeViewModel node)
        => ResolveNodeSettings(node).Values.TryGetValue(PuloniaSettingCatalog.CollectionCooldownKey, out var value) ? value : null;

    /// <summary>
    /// 地图追踪叶子按当前有效设置的规则；不是地图追踪、路径含变量或设置无效时为空，界面不显示标记。
    /// </summary>
    internal PuloniaCollectionCooldownPolicy? ResolveCooldownPolicy(PuloniaTaskNodeViewModel node)
    {
        if (node.Model.TaskType != "pathing" || node.Model.Source is not null)
            return null;
        if (node.Model.Resource is null && ResolveCooldownPath(node.Model, false) is null)
            return null;
        try
        {
            return PuloniaCollectionCooldown.Resolve(ResolveCooldownPath(node.Model, false), node.Model.Resource,
                new JObject { [PuloniaCollectionCooldown.ParameterName] = GetEffectiveCooldownSetting(node)?.DeepClone() });
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>地图追踪目录引用在账本中的位置；不是地图追踪目录引用或路径无法解析时为空。</summary>
    internal PuloniaCooldownDirectory? GetCooldownDirectory(PuloniaTaskNodeViewModel node)
    {
        if (node.Model.Source is not { Kind: "directory", TaskType: "pathing" } source)
            return null;
        string identity;
        if (source.Resource is { } resource)
            identity = PuloniaCollectionCooldown.GetFileIdentity(null, resource);
        else if (ResolveCooldownPath(node.Model, true) is { } path)
            identity = PuloniaCollectionCooldown.GetFileIdentity(path, null);
        else
            return null;
        return new PuloniaCooldownDirectory(PuloniaCollectionCooldown.EffectKeyPrefix + identity.TrimEnd('/') + "/",
            source.Recursive, GetEffectiveCooldownSetting(node));
    }

    /// <summary>
    /// 按运行准备的规则把本地路径解析为绝对路径：叶子相对能力资源目录，目录引用相对程序目录；含路径变量时无法预测，返回空。
    /// </summary>
    private string? ResolveCooldownPath(PuloniaTask task, bool isDirectory)
    {
        var path = isDirectory ? task.Source?.Path : task.Path;
        if (string.IsNullOrWhiteSpace(path) || path.Contains('{') || path.Contains('}'))
            return null;
        try
        {
            var baseDirectory = isDirectory ? null : GetDefinition(task)?.ResourceBaseDirectory;
            return Path.GetFullPath(path, Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>普通分组下启用的地图追踪路线按识别结果分布，最多列出 6 类。</summary>
    private string DescribeCooldownDistribution(PuloniaTaskNodeViewModel group)
    {
        var policies = Flatten(group).Where(item => !ReferenceEquals(item, group) && item.IsEffectivelyEnabled)
            .Select(ResolveCooldownPolicy).OfType<PuloniaCollectionCooldownPolicy>().ToArray();
        if (policies.Length == 0)
            return "此分组下没有直接添加的地图追踪路线；目录引用和计划引用中的路线各自识别。";
        var groups = policies.GroupBy(policy => policy.MaterialName ?? "未识别")
            .OrderByDescending(item => item.Count()).ToArray();
        var text = string.Join("、", groups.Take(6).Select(item => $"{item.Key} {item.Count()}"));
        return $"下属 {policies.Length} 条路线：{text}{(groups.Length > 6 ? " 等" : "")}。";
    }
}
