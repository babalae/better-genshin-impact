using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>计划页的触发配置、草稿保存与实时反馈。</summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>提供运行时状态的唯一调度宿主。</summary>
    private readonly PuloniaTaskTriggerHost? _triggerHost;
    /// <summary>列表最后显示的内容签名，树编辑不覆盖尚未保存的触发器草稿。</summary>
    private string _triggerListSignature = "";
    /// <summary>当前计划的触发器列表。</summary>
    public ObservableCollection<PuloniaTaskTriggerItemViewModel> Triggers { get; } = [];
    /// <summary>选中的已保存触发器，新增草稿没有对应列表项。</summary>
    [ObservableProperty] private PuloniaTaskTriggerItemViewModel? _selectedTrigger;
    /// <summary>当前可修改的独立草稿。</summary>
    [ObservableProperty] private PuloniaTaskTriggerEditorViewModel? _triggerEditor;
    /// <summary>程序内调度、热键注册和错误反馈。</summary>
    [ObservableProperty] private string _triggerStatus = "触发配置保存到本地；仅主实例负责自动执行。";
    /// <summary>计划页当前页签，触发器可直达其最近执行记录。</summary>
    [ObservableProperty] private int _selectedPlanTabIndex;

    /// <summary>切换计划或撤销触发配置后刷新列表，保留仍存在的选择。</summary>
    private void RefreshTriggers(bool force = false)
    {
        var signature = SelectedDocument?.Id + "/" + PuloniaTaskJson.Write(SelectedDocument?.Plan.Triggers);
        if (!force && signature == _triggerListSignature) return;
        _triggerListSignature = signature;
        var id = force ? null : SelectedTrigger?.Trigger.Id;
        SelectedTrigger = null;
        TriggerEditor = null;
        Triggers.Clear();
        if (SelectedDocument is not null)
            foreach (var trigger in SelectedDocument.Plan.Triggers) Triggers.Add(new PuloniaTaskTriggerItemViewModel(trigger));
        SelectedTrigger = Triggers.FirstOrDefault(item => item.Trigger.Id == id) ?? Triggers.FirstOrDefault();
        UpdateTriggerStatus();
    }

    /// <summary>列表选择建立草稿，不直接回写自动保存文档。</summary>
    partial void OnSelectedTriggerChanged(PuloniaTaskTriggerItemViewModel? value)
        => TriggerEditor = value is not null && SelectedDocument is not null
            ? new PuloniaTaskTriggerEditorViewModel(value.Trigger, SelectedDocument.Plan) : null;

    /// <summary>在 UI 线程合并最新的平台状态和当前计划游标。</summary>
    private void UpdateTriggerStatus()
    {
        TriggerStatus = _triggerHost?.Status ?? "调度宿主不可用。";
        foreach (var item in Triggers)
            item.Update(_triggerHost?.States.FirstOrDefault(state => state.PlanId == SelectedDocument?.Id && state.TriggerId == item.Trigger.Id));
    }

    /// <summary>后台通知只安排 UI 工作，不从计时器线程修改可观察集合。</summary>
    private void OnTriggerHostChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
            dispatcher.BeginInvoke(new Action(UpdateTriggerStatus));
    }

    /// <summary>复用原执行记录浏览器，定位最近一次触发提交，而不是打开另一套历史。</summary>
    [RelayCommand]
    private async Task ViewTriggerHistoryAsync()
    {
        var id = _triggerHost?.States.FirstOrDefault(item => item.PlanId == SelectedDocument?.Id
            && item.TriggerId == SelectedTrigger?.Trigger.Id)?.LastRequestId;
        History.PlanFilterId = SelectedDocument?.Id;
        History.OnlyCurrentPlan = true;
        await History.RefreshAsync(id);
        SelectedPlanTabIndex = 1;
    }

    /// <summary>新增默认禁用的每日 4 点日程草稿。</summary>
    [RelayCommand]
    private void AddScheduleTrigger()
    {
        if (SelectedDocument is null) return;
        SelectedTrigger = null;
        TriggerEditor = new PuloniaTaskTriggerEditorViewModel(new PuloniaTaskTrigger(), SelectedDocument.Plan);
    }

    /// <summary>新增默认禁用的全局热键草稿，仅在程序运行时生效。</summary>
    [RelayCommand]
    private void AddHotkeyTrigger()
    {
        if (SelectedDocument is null) return;
        SelectedTrigger = null;
        TriggerEditor = new PuloniaTaskTriggerEditorViewModel(new PuloniaTaskTrigger
        { Name = "快捷运行", Kind = PuloniaTaskTriggerKind.Hotkey }, SelectedDocument.Plan);
    }

    /// <summary>校验草稿、纳入撤销并立即保存；只有耐久保存成功后宿主才读取新配置。</summary>
    [RelayCommand]
    private async Task SaveTriggerAsync()
    {
        var document = SelectedDocument;
        var editor = TriggerEditor;
        if (document is null || editor is null || IsBusy || !editor.IsValid) return;
        try
        {
            var trigger = editor.CreateTrigger();
            if (trigger.BusyPolicy == PuloniaTaskBusyPolicy.StopCurrent && trigger.Enabled)
            {
                var answer = await ThemedMessageBox.ShowAsync("该触发器到点会停止当前任务，并在清理完成后执行新请求。确认启用此策略？",
                    "停止当前任务策略", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
            }
            IsBusy = true;
            // 先校验候选整个计划，避免目标被树编辑删除后将无效草稿装入文档。
            var candidate = PuloniaTaskJson.ClonePlan(document.Plan);
            candidate.Triggers.RemoveAll(item => item.Id == trigger.Id);
            candidate.Triggers.Add(trigger);
            PuloniaTaskValidator.ValidatePlan(candidate);
            document.ApplyMutation(() =>
            {
                document.Plan.Triggers.RemoveAll(item => item.Id == trigger.Id);
                document.Plan.Triggers.Add(trigger);
            }, document.SelectedNode);
            if (await SaveDocumentAsync(document))
            {
                RefreshTriggers();
                SelectedTrigger = Triggers.FirstOrDefault(item => item.Trigger.Id == trigger.Id);
                StatusMessage = "触发器已保存到本地；调度器将在下次检查接入。";
            }
        }
        catch (Exception ex) { await ThemedMessageBox.ErrorAsync(ex.Message, "触发配置无效"); }
        finally { IsBusy = false; }
    }

    /// <summary>删除需明确确认；排队中的旧触发请求开始前会再次检查并取消。</summary>
    [RelayCommand]
    private async Task DeleteTriggerAsync()
    {
        var document = SelectedDocument;
        var trigger = SelectedTrigger?.Trigger;
        if (document is null || trigger is null || IsBusy) return;
        var answer = await ThemedMessageBox.ShowAsync($"删除触发器“{trigger.Name}”？已保存的执行历史不会删除。",
            "删除触发器", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        document.ApplyMutation(() => document.Plan.Triggers.RemoveAll(item => item.Id == trigger.Id), document.SelectedNode);
        await SaveDocumentAsync(document);
        RefreshTriggers();
    }

    /// <summary>放弃当前触发器草稿，恢复已保存配置或关闭新增表单。</summary>
    [RelayCommand]
    private void CancelTriggerEdit()
        => TriggerEditor = SelectedTrigger is not null && SelectedDocument is not null
            ? new PuloniaTaskTriggerEditorViewModel(SelectedTrigger.Trigger, SelectedDocument.Plan) : null;
}
