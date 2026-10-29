using System.Linq;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// “任务”页签工具栏：按树的当前选中项确定新增位置与单项操作目标，并提供整棵树的展开与折叠。
/// 实际的新增、编辑、复制、删除仍复用右键菜单使用的同一组命令。
/// </summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>
    /// 工具栏“添加”和“粘贴”的目标节点：选中分组时添加到组内末尾，选中任务时添加到其后，未选中时添加到计划末尾。
    /// </summary>
    public PuloniaTaskNodeViewModel? InsertTargetNode => SelectedDocument?.SelectedNode ?? SelectedDocument?.RootNode;

    /// <summary>
    /// 工具栏编辑、共享设置、复制、删除的目标；未选中任务或只选中内部根节点时为空。
    /// </summary>
    public PuloniaTaskNodeViewModel? SelectedTaskNode => SelectedDocument?.SelectedNode is { IsRoot: false } node ? node : null;

    /// <summary>
    /// 当前是否有可供单项操作的选中任务。
    /// </summary>
    public bool HasSelectedTaskNode => SelectedTaskNode is not null;

    /// <summary>
    /// 展开当前计划中的全部分组。
    /// </summary>
    [RelayCommand]
    private void ExpandAllNodes() => SetAllNodesExpanded(true);

    /// <summary>
    /// 折叠当前计划中的全部分组。
    /// </summary>
    [RelayCommand]
    private void CollapseAllNodes() => SetAllNodesExpanded(false);

    /// <summary>
    /// 统一设置当前计划全部分组的展开状态；只改变编辑器界面状态，不修改计划内容，也不进入撤销。
    /// </summary>
    /// <param name="expanded">是否展开。</param>
    private void SetAllNodesExpanded(bool expanded)
    {
        if (SelectedDocument is not { } document)
            return;
        // 先取快照再逐个设置，展开状态的变化不会改动树结构
        foreach (var node in document.EnumerateNodes().Where(node => !node.IsRoot && node.HasChildren).ToList())
            node.IsExpanded = expanded;
    }

    /// <summary>
    /// 树选中项或当前计划变化后，刷新工具栏按钮的作用目标。
    /// </summary>
    private void NotifyToolbarTargets()
    {
        OnPropertyChanged(nameof(InsertTargetNode));
        OnPropertyChanged(nameof(SelectedTaskNode));
        OnPropertyChanged(nameof(HasSelectedTaskNode));
    }
}
