using System;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>向独立一条龙界面开放同一编辑会话的原子操作，不改变任务计划页面。</summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>次级窗口的保存归属，共用主会话的串行门和删除检查。</summary>
    private PuloniaTaskPlanViewModel? _saveOwner;
    /// <summary>次级触发窗口的记录展示回调；普通任务计划仍使用原页签。</summary>
    private Func<Task>? _showScopedTriggerHistory;

    /// <summary>为触发窗口建立独立选择上下文，文档对象和保存门仍由主会话拥有。</summary>
    internal PuloniaTaskPlanViewModel CreateOneDragonTriggerEditor(PuloniaTaskPlanDocumentViewModel document, Func<Task> showHistory)
    {
        var editor = new PuloniaTaskPlanViewModel(_store, _clipboard, _taskService, _resourceCatalog, History, _triggerHost)
        { _saveOwner = this, _presets = _presets, _isInitialized = true, _showScopedTriggerHistory = showHistory };
        // 不重复订阅文档编辑事件：自动保存只由主会话负责。
        foreach (var item in Documents) editor.Documents.Add(item);
        foreach (var item in OneDragonDocuments) editor.OneDragonDocuments.Add(item);
        editor.SelectedDocument = document;
        return editor;
    }

    /// <summary>关闭次级窗口时解除调度事件，避免保存完毕后残留界面订阅。</summary>
    internal void CloseOneDragonTriggerEditor()
    {
        if (_triggerHost is not null) _triggerHost.Changed -= OnTriggerHostChanged;
    }

    /// <summary>创建一份独立的一条龙计划，八个默认节点各自拥有稳定 ID。</summary>
    internal PuloniaTaskPlanDocumentViewModel CreateOneDragonConfiguration(string name)
    {
        var document = new PuloniaTaskPlanDocumentViewModel(PuloniaOneDragonDefaults.CreatePlan(name), _presets, _clipboard, isNew: true);
        AddDocument(document);
        ScheduleAutoSave(document);
        return document;
    }

    /// <summary>在明确节点位置写入创建窗口确认的完整任务；一条龙始终插入根清单。</summary>
    internal void InsertOneDragonTask(PuloniaTaskPlanDocumentViewModel document, PuloniaTaskNodeViewModel? selected, PuloniaTask task)
    {
        if (!OneDragonDocuments.Contains(document) || document.IsDeleted) return;
        var root = document.RootNode;
        var index = selected is not null && ReferenceEquals(selected.Parent, root)
            ? root.Children.IndexOf(selected) + 1 : root.Children.Count;
        document.InsertNode(task, root, index);
        StatusMessage = $"已添加“{task.Name}”。";
    }

    /// <summary>引用普通计划前保存草稿并检查循环；目标计划保持原菜单归属。</summary>
    internal async Task AddOneDragonReferenceAsync(PuloniaTaskPlanDocumentViewModel document,
        PuloniaTaskNodeViewModel? selected, PuloniaTaskPlanDocumentViewModel target)
    {
        if (!Documents.Contains(target) || WouldCreatePlanReferenceCycle(document, target))
            throw new InvalidOperationException("此计划不可引用，或引用关系会形成循环。");
        if (!await SaveDocumentAsync(target)) return;
        InsertOneDragonTask(document, selected, new PuloniaTask
        {
            Name = target.Name, TaskType = "group",
            Source = new PuloniaTaskSource { Kind = "plan", PlanId = target.Id }
        });
    }

    /// <summary>导入确认后逐份原子保存并加入当前会话；部分失败保留已完成计划并明确反馈。</summary>
    internal async Task ImportPreparedAsync(PuloniaTaskImportResult result)
    {
        foreach (var preset in result.Presets) await _store.SavePresetAsync(preset);
        _presets = await _store.ListPresetsAsync();
        foreach (var document in AllDocuments) document.SetPresets(_presets);
        foreach (var plan in result.Plans)
        {
            var baseName = plan.Name; var suffix = 2;
            while (OneDragonDocuments.Any(d => d.Name == plan.Name)) plan.Name = baseName + $"（导入 {suffix++}）";
            // 归属来自导入入口，不以名称、任务类型或引用目标推断。
            plan.Purpose = PuloniaTaskPlanPurpose.OneDragon;
            var saved = await _store.SavePlanAsync(plan);
            AddDocument(new PuloniaTaskPlanDocumentViewModel(saved, _presets, _clipboard, isNew: false));
        }
        await PersistPlanOrderAsync();
        await RefreshResourceVersionsAsync();
    }
}
