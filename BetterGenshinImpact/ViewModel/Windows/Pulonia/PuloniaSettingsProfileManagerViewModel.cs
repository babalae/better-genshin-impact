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
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 设置方案管理：左侧方案列表，右侧以方案模式的共享设置编辑器编辑草稿。
/// 方案影响多个计划，修改是草稿并显式保存，保存前列出影响范围；新建、复制、删除立即写入存储。
/// </summary>
public partial class PuloniaSettingsProfileManagerViewModel : ObservableObject, IDisposable
{
    /// <summary>编辑会话，负责方案存储与刷新全部文档。</summary>
    private readonly PuloniaPlanEditingSession _session;

    /// <summary>窗口入口，供编辑器打开路线 CD 表单。</summary>
    private readonly IPuloniaTaskSettingsDialogs _dialogs;

    /// <summary>读取当前全局配置并转换为共享设置；由视图层提供，视图模型不直接读取全局配置。</summary>
    private readonly Func<JObject>? _createFromGlobal;

    /// <summary>当前草稿的编辑对象。</summary>
    private PuloniaProfileSharedSettingsTarget? _draftTarget;

    /// <summary>正在切换选择，期间不询问放弃修改。</summary>
    private bool _isSwitching;

    /// <summary>方案列表。</summary>
    public ObservableCollection<PuloniaSettingsProfileListItemViewModel> Profiles { get; } = [];

    /// <summary>选中的方案。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand), nameof(RenameCommand), nameof(DeleteCommand))]
    private PuloniaSettingsProfileListItemViewModel? _selectedProfile;

    /// <summary>当前草稿的编辑器。</summary>
    [ObservableProperty] private PuloniaSharedSettingsEditorViewModel? _editor;

    /// <summary>草稿有未保存的修改。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DiscardCommand))]
    private bool _isDirty;

    /// <summary>最近一次操作结果。</summary>
    [ObservableProperty] private string? _message;

    /// <summary>是否选中了方案。</summary>
    public bool HasSelection => SelectedProfile is not null;

    /// <summary>是否提供“从全局配置创建”。</summary>
    public bool CanCreateFromGlobal => _createFromGlobal is not null;

    /// <summary>建立方案管理，并选中指定方案（为空时选中第一个）。</summary>
    public PuloniaSettingsProfileManagerViewModel(PuloniaPlanEditingSession session, IPuloniaTaskSettingsDialogs dialogs,
        Func<JObject>? createFromGlobal, string? selectProfileId)
    {
        _session = session;
        _dialogs = dialogs;
        _createFromGlobal = createFromGlobal;
        ReloadList(selectProfileId);
    }

    /// <summary>新建空方案并立即保存。</summary>
    [RelayCommand]
    private Task NewAsync() => CreateAsync("新建设置方案", "新方案", new JObject());

    /// <summary>复制选中方案（含未保存的草稿内容）为新方案。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task DuplicateAsync()
        => _draftTarget is null ? Task.CompletedTask
            : CreateAsync("复制设置方案", _draftTarget.Draft.Name + " 副本", _draftTarget.Draft.Settings);

    /// <summary>按当前全局自动战斗与地图追踪条件配置创建方案，便于把旧的全局设置带到任务计划。</summary>
    [RelayCommand]
    private Task CreateFromGlobalAsync()
        => _createFromGlobal is null ? Task.CompletedTask
            : CreateAsync("从全局配置创建方案", "全局配置", _createFromGlobal());

    /// <summary>修改草稿名称，保存后生效。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Rename()
    {
        if (_draftTarget is null)
            return;
        var name = PromptDialog.Prompt("请输入新的方案名称。", "重命名设置方案", _draftTarget.Draft.Name).Trim();
        if (string.IsNullOrWhiteSpace(name) || name == _draftTarget.Draft.Name)
            return;
        _draftTarget.Draft.Name = name;
        _draftTarget.NotifyRenamed();
        Message = "名称已修改，点击“保存”后生效。";
    }

    /// <summary>删除选中方案；仍被使用时拒绝并列出位置。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (SelectedProfile is not { } item || _session.Profiles.FirstOrDefault(profile => profile.Id == item.Id) is not { } profile)
            return;
        if (await ThemedMessageBox.QuestionAsync($"确定删除方案「{profile.Name}」吗？此操作不能撤销。", "删除设置方案") != MessageBoxResult.Yes)
            return;
        try
        {
            await _session.DeleteProfileAsync(profile);
            IsDirty = false;
            ReloadList(null);
            Message = $"已删除方案「{profile.Name}」。";
        }
        catch (Exception ex) when (ex is InvalidOperationException or PuloniaTaskValidationException or System.IO.IOException)
        {
            await ThemedMessageBox.WarningAsync(ex.Message, "无法删除设置方案");
        }
    }

    /// <summary>保存草稿；被使用时先列出影响范围并确认。</summary>
    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task SaveAsync()
    {
        if (_draftTarget is null)
            return;
        var usages = _session.GetProfileUsages(_draftTarget.Draft.Id);
        if (usages.Count > 0)
        {
            var shown = string.Join("\n", usages.Take(10)) + (usages.Count > 10 ? $"\n… 另有 {usages.Count - 10} 处" : string.Empty);
            if (await ThemedMessageBox.QuestionAsync(
                    $"保存后将影响 {usages.Count} 个节点：\n{shown}\n\n已排队和正在运行的任务不受影响。确定保存吗？",
                    "保存设置方案") != MessageBoxResult.Yes)
                return;
        }
        try
        {
            var saved = await _session.SaveProfileAsync(_draftTarget.Draft);
            IsDirty = false;
            ReloadList(saved.Id);
            Message = $"已保存方案「{saved.Name}」。";
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or InvalidOperationException or System.IO.IOException)
        {
            // 修订冲突或校验失败时保留草稿，用户可以放弃后重新编辑
            Message = "保存失败：" + ex.Message;
        }
    }

    /// <summary>放弃草稿，重新读取已保存的方案。</summary>
    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Discard()
    {
        IsDirty = false;
        LoadDraft(SelectedProfile?.Id);
        Message = "已放弃未保存的修改。";
    }

    /// <summary>
    /// 关闭窗口前确认；有未保存修改时询问是否放弃。
    /// </summary>
    /// <returns>可以关闭时返回 true。</returns>
    public bool ConfirmClose()
        => !IsDirty || ThemedMessageBox.Question("当前方案有未保存的修改，关闭后将丢失。确定关闭吗？", "设置方案",
            MessageBoxButton.YesNo, MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>切换方案前确认是否放弃未保存的修改；不放弃时回到原选择。</summary>
    partial void OnSelectedProfileChanged(PuloniaSettingsProfileListItemViewModel? oldValue,
        PuloniaSettingsProfileListItemViewModel? newValue)
    {
        if (_isSwitching)
            return;
        if (IsDirty && ThemedMessageBox.Question("当前方案有未保存的修改，切换后将丢失。确定切换吗？", "设置方案",
                MessageBoxButton.YesNo, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            // 回到原选择；在当前绑定更新结束后再改，避免列表控件状态错乱
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                _isSwitching = true;
                SelectedProfile = oldValue;
                _isSwitching = false;
            });
            return;
        }
        IsDirty = false;
        LoadDraft(newValue?.Id);
    }

    /// <summary>输入名称后建立并保存新方案，然后选中它。</summary>
    private async Task CreateAsync(string title, string defaultName, JObject settings)
    {
        if (IsDirty && ThemedMessageBox.Question("当前方案有未保存的修改，继续将丢失。确定继续吗？", "设置方案",
                MessageBoxButton.YesNo, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        var name = PromptDialog.Prompt("请输入方案名称。", title, defaultName).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        try
        {
            var saved = await _session.SaveProfileAsync(new PuloniaSettingsProfile { Name = name, Settings = (JObject)settings.DeepClone() });
            IsDirty = false;
            ReloadList(saved.Id);
            Message = $"已创建方案「{saved.Name}」（{saved.Settings.Count} 项）。";
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or InvalidOperationException or System.IO.IOException)
        {
            Message = "创建失败：" + ex.Message;
        }
    }

    /// <summary>重新读取列表并选中指定方案。</summary>
    private void ReloadList(string? selectId)
    {
        _isSwitching = true;
        try
        {
            Profiles.Clear();
            foreach (var profile in _session.Profiles)
                Profiles.Add(new PuloniaSettingsProfileListItemViewModel(profile.Id, profile.Name, profile.Settings.Count,
                    _session.GetProfileUsages(profile.Id).Count));
            SelectedProfile = Profiles.FirstOrDefault(item => item.Id == selectId) ?? Profiles.FirstOrDefault();
        }
        finally
        {
            _isSwitching = false;
        }
        LoadDraft(SelectedProfile?.Id);
    }

    /// <summary>为选中方案建立独立草稿和编辑器。</summary>
    private void LoadDraft(string? profileId)
    {
        if (_draftTarget is not null)
            _draftTarget.DraftChanged -= OnDraftChanged;
        Editor?.Dispose();
        _draftTarget = null;
        Editor = null;
        if (profileId is null || _session.Profiles.FirstOrDefault(profile => profile.Id == profileId) is not { } profile)
            return;
        _draftTarget = new PuloniaProfileSharedSettingsTarget(PuloniaTaskJson.CloneProfile(profile));
        _draftTarget.DraftChanged += OnDraftChanged;
        Editor = new PuloniaSharedSettingsEditorViewModel(_draftTarget, null, _dialogs);
    }

    /// <summary>草稿被修改后标记未保存。</summary>
    private void OnDraftChanged(object? sender, EventArgs e) => IsDirty = true;

    /// <summary>释放编辑器。</summary>
    public void Dispose()
    {
        if (_draftTarget is not null)
            _draftTarget.DraftChanged -= OnDraftChanged;
        Editor?.Dispose();
    }
}
