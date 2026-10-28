using System;
using System.Collections.Generic;
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
/// 共享设置编辑器：左侧类别导航、搜索与筛选，右侧逐项显示有效值、来源和恢复继承；修改立即写入编辑对象。
/// 同一编辑器用于任务树节点（显示继承、可选方案）和设置方案草稿（不显示继承）。
/// </summary>
public partial class PuloniaSharedSettingsEditorViewModel : ObservableObject, IDisposable
{
    /// <summary>编辑对象。</summary>
    private readonly PuloniaSharedSettingsTarget _target;

    /// <summary>编辑会话；为空时不提供方案选择与保存。</summary>
    private readonly PuloniaPlanEditingSession? _session;

    /// <summary>窗口入口；为空时不提供路线 CD 与方案管理窗口。</summary>
    private readonly IPuloniaTaskSettingsDialogs? _dialogs;

    /// <summary>全部行，按目录顺序。</summary>
    private readonly List<PuloniaSharedSettingRowViewModel> _rows = [];

    /// <summary>最近一次刷新得到的有效值，用于判定显示与启用条件。</summary>
    private IReadOnlyDictionary<string, PuloniaSharedSettingEffectiveValue> _effective =
        new Dictionary<string, PuloniaSharedSettingEffectiveValue>();

    /// <summary>正在刷新，期间的方案下拉变化不回写。</summary>
    private bool _isRefreshing;

    /// <summary>编辑对象，供宿主窗口读取标题等信息。</summary>
    public PuloniaSharedSettingsTarget Target => _target;

    /// <summary>标题。</summary>
    public string Title => _target.Title;

    /// <summary>标题下的说明。</summary>
    public string Description => _target.Description;

    /// <summary>是否显示继承来源与“与继承值相同”等提示。</summary>
    public bool ShowsInheritance => _target.ShowsInheritance;

    /// <summary>是否提供方案选择。</summary>
    public bool CanSelectProfile => _target.SupportsProfile && _session is not null;

    /// <summary>编辑对象仍存在、可以修改。</summary>
    public bool IsEditable => _target.IsAvailable;

    /// <summary>左侧类别。</summary>
    public ObservableCollection<PuloniaSharedSettingCategoryViewModel> Categories { get; } = [];

    /// <summary>当前类别。</summary>
    [ObservableProperty] private PuloniaSharedSettingCategoryViewModel? _selectedCategory;

    /// <summary>搜索文字；不为空时跨类别显示命中项。</summary>
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>只显示编辑对象自身覆盖的项。</summary>
    [ObservableProperty] private bool _showOnlyOverridden;

    /// <summary>显示高级设置项；已覆盖的高级项始终显示。</summary>
    [ObservableProperty] private bool _showAdvanced;

    /// <summary>右侧当前显示的行。</summary>
    public ObservableCollection<PuloniaSharedSettingRowViewModel> VisibleRows { get; } = [];

    /// <summary>右侧标题。</summary>
    [ObservableProperty] private string _listHeader = string.Empty;

    /// <summary>右侧标题下的说明（影响任务数或未使用提示）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasListHint))]
    private string? _listHint;

    /// <summary>是否显示右侧说明。</summary>
    public bool HasListHint => !string.IsNullOrEmpty(ListHint);

    /// <summary>没有可显示的行。</summary>
    [ObservableProperty] private bool _isEmpty;

    /// <summary>没有可显示的行时的说明。</summary>
    [ObservableProperty] private string _emptyText = string.Empty;

    /// <summary>底部的覆盖统计。</summary>
    [ObservableProperty] private string _footerText = string.Empty;

    /// <summary>最近一次操作结果或错误。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    /// <summary>是否显示操作结果。</summary>
    public bool HasMessage => !string.IsNullOrEmpty(Message);

    /// <summary>可选择的方案。</summary>
    public ObservableCollection<PuloniaSettingsProfileOptionViewModel> ProfileOptions { get; } = [];

    /// <summary>当前方案。</summary>
    [ObservableProperty] private PuloniaSettingsProfileOptionViewModel? _selectedProfileOption;

    /// <summary>方案缺失等提示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfileWarning))]
    private string? _profileWarning;

    /// <summary>是否显示方案提示。</summary>
    public bool HasProfileWarning => !string.IsNullOrEmpty(ProfileWarning);

    /// <summary>“只看修改”筛选的文字。</summary>
    public string OnlyOverriddenLabel => ShowsInheritance ? "只看本节点修改" : "只看已设置";

    /// <summary>批量恢复按钮的文字。</summary>
    public string ResetAllText => ShowsInheritance ? "全部恢复继承" : "清空方案";

    /// <summary>当前挂了可编辑的方案。</summary>
    public bool CanEditProfile => SelectedProfileOption?.Id is { } id && _session?.Profiles.Any(item => item.Id == id) == true;

    /// <summary>
    /// 建立编辑器；节点模式需要会话以选择和保存方案、显示路线 CD 状态，窗口入口用于路线 CD 和方案管理。
    /// </summary>
    public PuloniaSharedSettingsEditorViewModel(PuloniaSharedSettingsTarget target,
        PuloniaPlanEditingSession? session = null, IPuloniaTaskSettingsDialogs? dialogs = null)
    {
        _target = target;
        _session = session;
        _dialogs = dialogs;

        // 1. 建立行与类别；叶子只显示其能力使用的设置项
        var allowed = target.AllowedKeys;
        foreach (var category in PuloniaSettingCatalog.Categories.OrderBy(item => item.Order))
        {
            var rows = PuloniaSettingCatalog.Definitions
                .Where(definition => definition.Category == category.Id && (allowed is null || allowed.Contains(definition.Key)))
                .OrderBy(definition => definition.Order)
                .Select(definition => new PuloniaSharedSettingRowViewModel(this, definition, category.DisplayName))
                .ToList();
            if (rows.Count == 0)
                continue;
            _rows.AddRange(rows);
            Categories.Add(new PuloniaSharedSettingCategoryViewModel(category, rows));
        }

        // 2. 读取当前值后默认选中第一个有覆盖的类别，其次第一个有任务使用的类别
        Refresh();
        _selectedCategory = Categories.FirstOrDefault(item => item.HasOverrides)
                            ?? Categories.FirstOrDefault(item => !item.IsUnused)
                            ?? Categories.FirstOrDefault();
        UpdateVisibleRows();
        _target.Changed += OnTargetChanged;
    }

    /// <summary>行的输入写入编辑对象；失败时提示并恢复为模型中的值。</summary>
    internal void Commit(PuloniaSharedSettingRowViewModel row, JToken value)
    {
        if (!IsEditable)
            return;
        try
        {
            _target.Set(row.Key, value);
            Message = null;
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or InvalidOperationException or ArgumentException)
        {
            Message = $"“{row.DisplayName}”未生效：{ex.Message}";
            Refresh();
        }
    }

    /// <summary>恢复一项的继承。</summary>
    internal void Reset(PuloniaSharedSettingRowViewModel row)
    {
        if (!IsEditable)
            return;
        try { _target.Reset(row.Key); }
        catch (InvalidOperationException ex) { Message = ex.Message; }
    }

    /// <summary>打开路线 CD 表单；确认后整体写入这一项，选择恢复继承时移除覆盖。叶子节点另显示本账号状态并可清除记录。</summary>
    internal void EditCollectionCooldown(PuloniaSharedSettingRowViewModel row)
    {
        if (_dialogs is null || !IsEditable)
            return;
        try
        {
            PuloniaCollectionCooldownViewModel draft;
            var isLeaf = false;
            if (_target is PuloniaNodeSharedSettingsTarget node)
            {
                draft = node.Document.CreateCollectionCooldownDraft(node.Node);
                isLeaf = node.Node.IsLeaf;
                // 任务计划中的叶子：显示当前登录账号下的 CD 状态、清除入口与跨计划规则差异
                _session?.AttachCooldownStatus(draft, node.Node);
            }
            else
            {
                draft = new PuloniaCollectionCooldownViewModel();
                draft.Load(row.LoadedValue, row.SourceText);
            }
            var original = draft.BuildValue();
            if (_dialogs.EditCollectionCooldown(draft, isLeaf) is not { } result)
                return;
            if (result.RestoreInheritance)
                _target.Reset(row.Key);
            else if (!JToken.DeepEquals(original, draft.BuildValue()))
                _target.Set(row.Key, draft.BuildValue());
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or FormatException or ArgumentException or OverflowException
                                       or InvalidOperationException)
        {
            Message = "路线 CD 未生效：" + ex.Message;
        }
    }

    /// <summary>恢复全部设置项的继承。</summary>
    [RelayCommand]
    private void ResetAll()
    {
        if (!IsEditable)
            return;
        try
        {
            _target.ResetAll();
            Message = ShowsInheritance ? "已恢复全部继承，可以撤销。" : "已清空方案中的全部设置。";
        }
        catch (InvalidOperationException ex) { Message = ex.Message; }
    }

    /// <summary>移除与继承值相同的覆盖。</summary>
    [RelayCommand]
    private void RemoveRedundant()
    {
        if (_target is not PuloniaNodeSharedSettingsTarget node || !IsEditable)
            return;
        try
        {
            var count = node.RemoveRedundant();
            Message = count == 0 ? "没有与继承值相同的覆盖。" : $"已移除 {count} 项与继承值相同的覆盖，可以撤销。";
        }
        catch (InvalidOperationException ex) { Message = ex.Message; }
    }

    /// <summary>清空搜索文字。</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>
    /// 把本节点的设置存为新方案；原方案的内容一并带上，选择改用后有效值不变。
    /// </summary>
    [RelayCommand]
    private async Task SaveAsProfileAsync()
    {
        if (_target is not PuloniaNodeSharedSettingsTarget node || _session is null || !IsEditable)
            return;
        var current = node.ProfileId is { } currentId ? _session.Profiles.FirstOrDefault(item => item.Id == currentId) : null;
        var settings = (JObject?)current?.Settings.DeepClone() ?? new JObject();
        foreach (var property in node.OwnSettings.Properties())
            settings[property.Name] = property.Value.DeepClone();
        if (settings.Count == 0)
        {
            Message = "本节点还没有设置过任何项，没有可保存的内容。";
            return;
        }
        var name = PromptDialog.Prompt("请输入方案名称；方案可在任意计划的任意节点上使用。", "另存为设置方案",
            node.Node.IsRoot ? node.Document.Name : node.Node.Name).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        try
        {
            var saved = await _session.SaveProfileAsync(new PuloniaSettingsProfile { Name = name, Settings = settings });
            var replace = await ThemedMessageBox.QuestionAsync(
                $"已保存方案「{saved.Name}」（{saved.Settings.Count} 项）。\n\n是否让本节点改用这个方案，并移除本节点上的 {node.OwnSettings.Count} 项覆盖？有效值不变，可以撤销。",
                "另存为设置方案");
            if (replace == MessageBoxResult.Yes)
                node.Document.UseProfileInsteadOfSettings(node.Node, saved.Id);
            Message = $"已保存方案「{saved.Name}」。";
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or InvalidOperationException or System.IO.IOException)
        {
            Message = "保存方案失败：" + ex.Message;
        }
    }

    /// <summary>打开方案管理并定位到当前方案。</summary>
    [RelayCommand]
    private void EditProfile() => OpenProfileManager(SelectedProfileOption?.Id);

    /// <summary>打开方案管理。</summary>
    [RelayCommand]
    private void ManageProfiles() => OpenProfileManager(null);

    /// <summary>打开方案管理窗口；关闭后重新读取。</summary>
    private void OpenProfileManager(string? profileId)
    {
        if (_session is null || _dialogs is null)
            return;
        _dialogs.ManageProfiles(_session, profileId);
        Refresh();
    }

    /// <summary>方案下拉框变化时立即写入节点；刷新期间的赋值不回写。</summary>
    partial void OnSelectedProfileOptionChanged(PuloniaSettingsProfileOptionViewModel? value)
    {
        OnPropertyChanged(nameof(CanEditProfile));
        if (_isRefreshing || _target is not PuloniaNodeSharedSettingsTarget node || !IsEditable)
            return;
        try { node.SelectProfile(value?.Id); }
        catch (InvalidOperationException ex) { Message = ex.Message; }
    }

    /// <summary>切换类别后更新右侧。</summary>
    partial void OnSelectedCategoryChanged(PuloniaSharedSettingCategoryViewModel? value) => UpdateVisibleRows();

    /// <summary>搜索文字变化后更新右侧。</summary>
    partial void OnSearchTextChanged(string value) => UpdateVisibleRows();

    /// <summary>筛选变化后更新右侧。</summary>
    partial void OnShowOnlyOverriddenChanged(bool value) => UpdateVisibleRows();

    /// <summary>筛选变化后更新右侧。</summary>
    partial void OnShowAdvancedChanged(bool value) => UpdateVisibleRows();

    /// <summary>编辑对象在外部变化后重新读取。</summary>
    private void OnTargetChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>
    /// 重新读取全部有效值、来源、启用条件、提示与统计，再更新右侧可见行。
    /// </summary>
    private void Refresh()
    {
        _isRefreshing = true;
        try
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(IsEditable));
            if (!IsEditable)
                Message = "此节点已被删除或撤销，设置不能再修改。";

            // 1. 行：值、来源、是否覆盖、与继承值是否相同、启用条件
            _effective = _target.GetEffective();
            var inherited = _target.GetInherited();
            var own = _target.OwnSettings;
            foreach (var row in _rows)
            {
                var effective = _effective[row.Key];
                var overridden = own.ContainsKey(row.Key);
                row.Load(effective.Value, effective.Source, effective.Kind, overridden,
                    overridden && inherited.TryGetValue(row.Key, out var inheritedValue) && JToken.DeepEquals(own[row.Key], inheritedValue),
                    ShowsInheritance ? _target.GetHistoryText(row.Key) : string.Empty);
                var enabled = PuloniaSettingCatalog.Evaluate(row.Definition.EnabledWhen, ValueOf);
                row.IsEditable = IsEditable && enabled;
                row.DisabledReason = enabled ? null : row.Definition.DisabledReason;
            }
            ApplyWarnings();

            // 2. 类别：覆盖数与影响任务数；分组下没有任务使用的类别置灰
            var usage = _target.CategoryUsage;
            foreach (var category in Categories)
            {
                category.OverrideCount = category.Rows.Count(row => row.IsOverridden);
                category.UsageText = usage?.GetValueOrDefault(category.Category.Id);
                category.IsUnused = usage is not null && !usage.ContainsKey(category.Category.Id);
            }
            FooterText = ShowsInheritance
                ? own.Count == 0 ? "本节点没有覆盖，全部沿用上层设置。" : $"本节点覆盖 {own.Count} 项。"
                : own.Count == 0 ? "方案中还没有设置任何项。" : $"方案包含 {own.Count} 项设置。";

            // 3. 方案选项
            if (CanSelectProfile && _target is PuloniaNodeSharedSettingsTarget node)
                RefreshProfileOptions(node);
        }
        finally
        {
            _isRefreshing = false;
        }
        UpdateVisibleRows();
    }

    /// <summary>重建方案选项；节点引用的方案不存在时追加警示项，不自动清除选择。</summary>
    private void RefreshProfileOptions(PuloniaNodeSharedSettingsTarget node)
    {
        var options = new List<PuloniaSettingsProfileOptionViewModel> { new(null, "不使用方案") };
        options.AddRange(_session!.Profiles.Select(profile => new PuloniaSettingsProfileOptionViewModel(profile.Id,
            $"{profile.Name}（{profile.Settings.Count} 项）")));
        ProfileWarning = null;
        if (node.ProfileId is { } id && options.All(option => option.Id != id))
        {
            options.Add(new PuloniaSettingsProfileOptionViewModel(id, "⚠ 方案已不存在：" + id));
            ProfileWarning = "此节点使用的设置方案已不存在，运行前请重新选择；不会自动清除这项选择。";
        }
        if (!ProfileOptions.Select(option => option).SequenceEqual(options))
        {
            ProfileOptions.Clear();
            foreach (var option in options)
                ProfileOptions.Add(option);
        }
        SelectedProfileOption = ProfileOptions.FirstOrDefault(option => option.Id == node.ProfileId) ?? ProfileOptions[0];
        OnPropertyChanged(nameof(CanEditProfile));
    }

    /// <summary>
    /// 跨项约束提示，说明运行时会怎样修正；不阻止输入。
    /// </summary>
    private void ApplyWarnings()
    {
        foreach (var row in _rows)
            row.Warning = null;
        var distance = ValueOf("hurry.distance")?.Value<double?>();
        if (distance is not null)
        {
            if (Find("hurry.approach_stop_distance") is { } approach && approach.LoadedValue.Value<double?>() > distance)
                approach.Warning = $"大于赶路启用距离，运行时按 {distance} 米执行。";
            if (Find("hurry.mwk_jump_fly_distance") is { } jump && jump.LoadedValue.Value<double?>() <= distance)
                jump.Warning = $"需大于赶路启用距离，运行时自动调整为 {distance + 1} 米。";
        }
        if (ValueOf("fight_end.rotate_find_enemy_enabled")?.Value<bool>() == true
            && ValueOf("fight_end.skip_check_when_enemy_visible")?.Value<bool>() == true)
        {
            const string text = "与另一项互斥，同时开启时运行中以“敌人可见时跳过结束检查”为准。";
            if (Find("fight_end.rotate_find_enemy_enabled") is { } rotate) rotate.Warning = text;
            if (Find("fight_end.skip_check_when_enemy_visible") is { } skip) skip.Warning = text;
        }

        PuloniaSharedSettingRowViewModel? Find(string key) => _rows.FirstOrDefault(row => row.Key == key);
    }

    /// <summary>
    /// 计算右侧可见行：搜索时跨类别，否则只看当前类别；按显示条件、高级与“只看覆盖”筛选。
    /// 集合内容未变化时不重建，避免打断正在输入的控件。
    /// </summary>
    private void UpdateVisibleRows()
    {
        var search = SearchText.Trim();
        var searching = search.Length > 0;
        var candidates = searching ? (IEnumerable<PuloniaSharedSettingRowViewModel>)_rows : SelectedCategory?.Rows ?? [];
        var visible = candidates.Where(row => IsRowVisible(row, searching, search)).ToList();

        foreach (var category in Categories)
            category.MatchCount = searching ? category.Rows.Count(row => IsRowVisible(row, true, search)) : 0;
        if (!VisibleRows.SequenceEqual(visible))
        {
            VisibleRows.Clear();
            foreach (var row in visible)
                VisibleRows.Add(row);
        }

        ListHeader = searching ? $"搜索“{search}”" : SelectedCategory?.DisplayName ?? string.Empty;
        ListHint = searching ? null
            : SelectedCategory is { IsUnused: true } ? "本组下目前没有使用这些设置的任务；设置后对以后加入的任务生效。"
            : SelectedCategory?.UsageText;
        IsEmpty = visible.Count == 0;
        EmptyText = searching ? "没有匹配的设置。"
            : ShowOnlyOverridden ? (ShowsInheritance ? "本类别没有本节点修改的项。" : "本类别没有设置过的项。")
            : "本类别的设置项都在“显示高级设置”中，或当前条件下不需要设置。";
    }

    /// <summary>判断一行是否显示；已覆盖的项总是显示，便于查看和恢复。</summary>
    private bool IsRowVisible(PuloniaSharedSettingRowViewModel row, bool searching, string search)
    {
        if (ShowOnlyOverridden && !row.IsOverridden)
            return false;
        if (searching && !(row.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                           || row.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
                           || row.Key.Contains(search, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (row.IsOverridden)
            return true;
        if (row.IsAdvanced && !ShowAdvanced && !searching)
            return false;
        return PuloniaSettingCatalog.Evaluate(row.Definition.VisibleWhen, ValueOf);
    }

    /// <summary>取得设置项的当前有效值。</summary>
    private JToken? ValueOf(string key) => _effective.TryGetValue(key, out var item) ? item.Value : null;

    /// <summary>解除编辑对象事件并释放编辑对象。</summary>
    public void Dispose()
    {
        _target.Changed -= OnTargetChanged;
        _target.Dispose();
    }
}
