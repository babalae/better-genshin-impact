using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 任务计划列表的整份复制和删除，独立于树节点的复制、删除命令；文件操作由编辑会话完成。
/// </summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>
    /// 列表操作始终以显式右键目标为准，忙碌或已移除的文档不可操作。
    /// </summary>
    private bool CanManagePlan(PuloniaTaskPlanDocumentViewModel? document) => Session.CanManagePlan(document);

    /// <summary>
    /// 复制目标计划的当前草稿，以唯一名称加入列表并立即保存为独立计划；普通计划的副本随后被选中。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanManagePlan))]
    private async Task CopyPlanAsync(PuloniaTaskPlanDocumentViewModel? document)
    {
        if (document is null)
            return;
        var copy = await Session.CopyPlanAsync(document);
        if (copy?.Purpose == PuloniaTaskPlanPurpose.General)
            SelectedDocument = copy;
    }

    /// <summary>
    /// 确认后删除计划文件并移出编辑会话；删除的是当前计划时选中相邻计划。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanManagePlan))]
    private async Task DeletePlanAsync(PuloniaTaskPlanDocumentViewModel? document)
    {
        if (document is null)
            return;
        var wasSelected = ReferenceEquals(SelectedDocument, document);
        var index = Documents.IndexOf(document);
        if (!await Session.DeletePlanAsync(document) || !wasSelected)
            return;
        SelectedDocument = Documents.Count == 0 ? null : Documents[Math.Clamp(index, 0, Documents.Count - 1)];
    }
}
