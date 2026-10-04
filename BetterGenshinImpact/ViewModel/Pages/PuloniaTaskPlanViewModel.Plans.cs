using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 任务计划列表的整份复制和删除，独立于树节点的复制、删除命令。
/// </summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>
    /// 列表操作始终以显式右键目标为准，忙碌或已移除的文档不可操作。
    /// </summary>
    private bool CanManagePlan(PuloniaTaskPlanDocumentViewModel? document)
        => !IsBusy && document is not null && Documents.Contains(document)
           && !document.IsDeleted && !_deletingDocuments.Contains(document);

    /// <summary>
    /// 复制目标计划的当前草稿，以唯一名称加入列表并立即保存为独立计划。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanManagePlan))]
    private async Task CopyPlanAsync(PuloniaTaskPlanDocumentViewModel? document)
    {
        if (!CanManagePlan(document) || document is null)
            return;
        IsBusy = true;
        try
        {
            var plan = PuloniaTaskJson.CopyPlan(document.Plan);
            var baseName = document.Name + " - 副本";
            var name = baseName;
            var suffix = 1;
            while (Documents.Any(item => item.Name == name))
                name = baseName + " " + ++suffix;
            if (plan.RootTask.Name == plan.Name)
                plan.RootTask.Name = name;
            plan.Name = name;

            var copy = new PuloniaTaskPlanDocumentViewModel(plan, _presets, _clipboard, isNew: true);
            AddDocument(copy);
            // 副本紧邻来源计划，便于连续复制后比较编辑内容。
            Documents.Move(Documents.IndexOf(copy), Documents.IndexOf(document) + 1);
            SelectedDocument = copy;
            if (await SaveDocumentAsync(copy))
                StatusMessage = $"已复制为“{name}”。副本的定时和热键触发器默认禁用，可在触发方式中启用。";
        }
        catch (Exception ex)
        {
            StatusMessage = "复制计划失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法复制任务计划");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 确认后等待当前保存结束，再删除计划文件并移除编辑会话；失败时保留草稿。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanManagePlan))]
    private async Task DeletePlanAsync(PuloniaTaskPlanDocumentViewModel? document)
    {
        if (!CanManagePlan(document) || document is null)
            return;
        var answer = await ThemedMessageBox.ShowAsync(
            $"确定删除任务计划“{document.Name}”及其全部任务和触发配置？此操作无法撤销。\n"
            + "已有执行历史会保留；已提交的手动运行仍使用原快照继续执行。",
            "删除任务计划", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes || !CanManagePlan(document))
            return;

        IsBusy = true;
        _deletingDocuments.Add(document);
        CancelPendingAutoSave(document);
        var saveGate = GetSaveGate(document);
        await saveGate.WaitAsync();
        try
        {
            // 除磁盘检查外也检查未保存草稿，避免删除后其他文档的自动保存失败。
            var reference = Documents.FirstOrDefault(other => !ReferenceEquals(other, document)
                && EnumeratePlanReferenceIds(other.Plan.RootTask).Contains(document.Id));
            if (reference is not null)
                throw new InvalidOperationException($"计划“{reference.Name}”仍引用此计划，请先移除引用后再删除。");
            await _store.DeletePlanAsync(document.Id, document.Revision);

            // 只有文件删除成功才解除事件订阅，旧异步操作通过删除标记阻止再次保存。
            document.IsDeleted = true;
            document.Changed -= OnDocumentChanged;
            document.ContentChanged -= OnDocumentContentChanged;
            var wasSelected = ReferenceEquals(SelectedDocument, document);
            var index = Documents.IndexOf(document);
            Documents.Remove(document);
            if (wasSelected)
                SelectedDocument = Documents.Count == 0 ? null : Documents[Math.Min(index, Documents.Count - 1)];
            RefreshReferencePlans();
            UpdateResourceSummaries(GetResourceDocuments());
            StatusMessage = $"已删除任务计划“{document.Name}”，执行历史已保留。";
            await PersistPlanOrderAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "删除计划失败，当前草稿已保留：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法删除任务计划");
        }
        finally
        {
            ReleaseSaveGate(document, saveGate);
            _deletingDocuments.Remove(document);
            if (!document.IsDeleted)
                ScheduleAutoSave(document);
            IsBusy = false;
        }
    }
}
