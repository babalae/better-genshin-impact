using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.KeyMacro;
using BetterGenshinImpact.GameTask.KeyMacro.Script;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.KeyMacro;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages.KeyMacro;

/// <summary>
/// 一个按键宏的编辑：名称、触发设置、步骤视图与脚本视图。
/// 脚本文本是唯一的保存形式，步骤视图是它的解析结果；在步骤视图里编辑会把步骤写回脚本
/// </summary>
public partial class KeyMacroItemViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// 撤销记录的最大条数
    /// </summary>
    private const int MaxUndoSteps = 50;

    /// <summary>
    /// 在此时间内的连续步骤编辑合并为一条撤销记录（例如连续输入备注）
    /// </summary>
    private static readonly TimeSpan UndoMergeWindow = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// 脚本视图停止输入后多久重新解析
    /// </summary>
    private static readonly TimeSpan ParseDelay = TimeSpan.FromMilliseconds(300);

    private readonly KeyMacroStore _store;
    private readonly KeyMacroHotkeyHost _host;

    /// <summary>
    /// 配置根：总开关、上次使用的编辑视图、原神键位
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 脚本视图输入后的解析防抖
    /// </summary>
    private readonly DispatcherTimer _parseTimer;

    /// <summary>
    /// 撤销记录：步骤编辑前的脚本
    /// </summary>
    private readonly List<string> _undo = [];

    /// <summary>
    /// 重做记录
    /// </summary>
    private readonly List<string> _redo = [];

    /// <summary>
    /// 上次记录撤销的时间
    /// </summary>
    private DateTime _lastUndoRecord = DateTime.MinValue;

    /// <summary>
    /// 正在按解析结果重建步骤，此时的集合变化不写回脚本
    /// </summary>
    private bool _rebuildingSteps;

    /// <summary>
    /// 已安排写回脚本，避免拖动排序等一次操作产生的多次集合变化重复写回
    /// </summary>
    private bool _writePending;

    /// <summary>
    /// 正在由本对象修改宏定义的脚本，忽略随之而来的属性变化通知
    /// </summary>
    private bool _settingScript;

    /// <summary>
    /// 编辑器请求序号
    /// </summary>
    private int _requestId;

    /// <summary>
    /// 编辑的宏
    /// </summary>
    public KeyMacroDefinition Definition { get; }

    /// <summary>
    /// 名称输入框的内容，失去焦点时提交重命名
    /// </summary>
    [ObservableProperty]
    private string _editingName;

    /// <summary>
    /// 宏是否正在运行
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// 快捷键下方的状态说明
    /// </summary>
    [ObservableProperty]
    private string _hotkeyStatus = string.Empty;

    /// <summary>
    /// 快捷键状态是否为错误（未生效）
    /// </summary>
    [ObservableProperty]
    private bool _hotkeyStatusIsError;

    /// <summary>
    /// 快捷键状态是否为提醒（未设置、与游戏键位冲突、开关关闭）
    /// </summary>
    [ObservableProperty]
    private bool _hotkeyStatusIsWarning;

    /// <summary>
    /// 当前脚本的解析结果
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Diagnostics), nameof(HasErrors), nameof(IsStepEditable), nameof(SummaryText),
        nameof(StatusText), nameof(StepsReadOnlyMessage), nameof(HasDiagnostics), nameof(ShowEmptyStepsHint))]
    private KeyMacroDocument _document = KeyMacroDocument.Empty;

    /// <summary>
    /// 步骤视图中选中的步骤
    /// </summary>
    [ObservableProperty]
    private KeyMacroStepViewModel? _selectedStep;

    /// <summary>
    /// 对脚本编辑器的最新请求
    /// </summary>
    [ObservableProperty]
    private KeyMacroEditorRequest? _editorRequest;

    /// <summary>
    /// 脚本视图工具栏「插入按键」捕获到的按键名，插入后清空
    /// </summary>
    [ObservableProperty]
    private string _insertKeyName = string.Empty;

    /// <summary>
    /// 步骤视图中的步骤
    /// </summary>
    public ObservableCollection<KeyMacroStepViewModel> Steps { get; } = [];

    /// <summary>
    /// 语法速查列表
    /// </summary>
    public IReadOnlyList<KeyMacroCommandInfo> CommandHelp => KeyMacroCommandCatalog.All;

    /// <summary>
    /// 创建宏的编辑对象
    /// </summary>
    public KeyMacroItemViewModel(KeyMacroDefinition definition, KeyMacroStore store, KeyMacroHotkeyHost host, AllConfig config)
    {
        Definition = definition;
        _store = store;
        _host = host;
        _config = config;
        _editingName = definition.Name;
        _parseTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ParseDelay };
        _parseTimer.Tick += (_, _) =>
        {
            _parseTimer.Stop();
            Reparse(true);
        };
        Definition.PropertyChanged += OnDefinitionPropertyChanged;
        Steps.CollectionChanged += OnStepsCollectionChanged;
        Reparse(true);
        UpdateStatus();
    }

    #region 名称与列表展示

    /// <summary>
    /// 宏名称
    /// </summary>
    public string Name => Definition.Name;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled
    {
        get => Definition.Enabled;
        set => Definition.Enabled = value;
    }

    /// <summary>
    /// 列表中显示的快捷键
    /// </summary>
    public string ListHotkeyText => string.IsNullOrEmpty(Definition.Hotkey) ? "未设置快捷键" : Definition.Hotkey;

    /// <summary>
    /// 触发方式名称
    /// </summary>
    public string ModeName => Definition.TriggerMode switch
    {
        KeyMacroTriggerMode.Hold => "按住循环",
        KeyMacroTriggerMode.Toggle => "切换循环",
        _ => "单次"
    };

    /// <summary>
    /// 名称输入框失去焦点时提交重命名，失败时提示并恢复原名称
    /// </summary>
    partial void OnEditingNameChanged(string value)
    {
        if (value == Definition.Name)
        {
            return;
        }

        if (!_store.TryRename(Definition, value, out var error))
        {
            Toast.Warning(error ?? "重命名失败");
            EditingName = Definition.Name;
        }
    }

    #endregion

    #region 触发设置

    /// <summary>
    /// 快捷键，绑定到 HotKeyTextBox
    /// </summary>
    public HotKey HotKey
    {
        get
        {
            try
            {
                return HotKey.FromString(Definition.Hotkey);
            }
            catch (ArgumentException)
            {
                return HotKey.None;
            }
        }
        set
        {
            if (Definition.EffectiveHotkeyType == HotKeyTypeEnum.KeyboardMonitor && value.Modifiers != ModifierKeys.None)
            {
                Toast.Warning("键鼠监听不支持组合键，请改用单键，或把类型切换为全局热键");
                OnPropertyChanged();
                return;
            }

            Definition.Hotkey = value.IsEmpty ? string.Empty : value.ToString();
        }
    }

    /// <summary>
    /// 热键类型的中文名，HotKeyTextBox 按它决定捕获规则
    /// </summary>
    public string HotKeyTypeName => Definition.EffectiveHotkeyType.ToChineseName();

    /// <summary>
    /// 热键类型按钮上显示的名称
    /// </summary>
    public string LocalizedHotKeyTypeName => Definition.EffectiveHotkeyType.ToLocalizedName();

    /// <summary>
    /// 是否可以切换热键类型。按住循环需要松开事件，只能使用键鼠监听
    /// </summary>
    public bool CanSwitchHotKeyType => Definition.TriggerMode != KeyMacroTriggerMode.Hold;

    /// <summary>
    /// 触发方式：单次
    /// </summary>
    public bool IsOnceMode
    {
        get => Definition.TriggerMode == KeyMacroTriggerMode.Once;
        set
        {
            if (value)
            {
                SetTriggerMode(KeyMacroTriggerMode.Once);
            }
        }
    }

    /// <summary>
    /// 触发方式：按住循环
    /// </summary>
    public bool IsHoldMode
    {
        get => Definition.TriggerMode == KeyMacroTriggerMode.Hold;
        set
        {
            if (value)
            {
                SetTriggerMode(KeyMacroTriggerMode.Hold);
            }
        }
    }

    /// <summary>
    /// 触发方式：切换循环
    /// </summary>
    public bool IsToggleMode
    {
        get => Definition.TriggerMode == KeyMacroTriggerMode.Toggle;
        set
        {
            if (value)
            {
                SetTriggerMode(KeyMacroTriggerMode.Toggle);
            }
        }
    }

    /// <summary>
    /// 当前触发方式的说明
    /// </summary>
    public string ModeDescription => Definition.TriggerMode switch
    {
        KeyMacroTriggerMode.Hold => "按住时重复执行；松开立即终止，即使本轮没有执行完，并松开宏里按下的键。只能使用键鼠监听",
        KeyMacroTriggerMode.Toggle => "按一下开始循环；再按一下立即终止，即使本轮没有执行完，并松开宏里按下的键",
        _ => "按一下执行一遍；执行中再按会被忽略"
    };

    /// <summary>
    /// 切换热键类型。与快捷键页一致，切换后清空键值，需要重新设置
    /// </summary>
    [RelayCommand]
    private void SwitchHotKeyType()
    {
        if (!CanSwitchHotKeyType)
        {
            return;
        }

        Definition.HotkeyType = Definition.HotkeyType == HotKeyTypeEnum.GlobalRegister
            ? HotKeyTypeEnum.KeyboardMonitor
            : HotKeyTypeEnum.GlobalRegister;
        Definition.Hotkey = string.Empty;
    }

    /// <summary>
    /// 设置触发方式。切换到按住循环时热键类型改为键鼠监听，组合键不再可用，一并清空
    /// </summary>
    private void SetTriggerMode(KeyMacroTriggerMode mode)
    {
        if (Definition.TriggerMode == mode)
        {
            return;
        }

        if (mode == KeyMacroTriggerMode.Hold && Definition.HotkeyType == HotKeyTypeEnum.GlobalRegister)
        {
            Definition.HotkeyType = HotKeyTypeEnum.KeyboardMonitor;
            if (HotKey.Modifiers != ModifierKeys.None)
            {
                Definition.Hotkey = string.Empty;
                Toast.Information("按住循环只能使用键鼠监听，原来的组合键已清空，请重新设置单键或鼠标侧键");
            }
        }

        Definition.TriggerMode = mode;
    }

    /// <summary>
    /// 按宿主的注册结果更新运行状态和快捷键说明
    /// </summary>
    public void UpdateStatus()
    {
        IsRunning = _host.IsRunning(Definition);
        var error = _host.GetError(Definition);
        string status;
        var isError = false;
        var isWarning = false;
        if (!_config.KeyMacroConfig.Enabled)
        {
            status = "按键宏总开关已关闭";
            isWarning = true;
        }
        else if (!Definition.Enabled)
        {
            status = "此宏已停用，打开右上角的开关后生效";
            isWarning = true;
        }
        else if (string.IsNullOrEmpty(Definition.Hotkey))
        {
            status = "还没有设置快捷键：点击输入框后按下要使用的键";
            isWarning = true;
        }
        else if (error != null)
        {
            status = "快捷键未生效：" + error;
            isError = true;
        }
        else
        {
            var conflicts = Definition.EffectiveHotkeyType == HotKeyTypeEnum.KeyboardMonitor
                ? HotKeyPageViewModel.GetGameKeyBindingConflictLines(HotKey, _config.KeyBindingsConfig)
                : [];
            if (conflicts.Count > 0)
            {
                status = "该键在原神中也有绑定，按下时游戏也会响应：" + string.Join("、", conflicts.Select(c => c.TrimStart('-', ' ')));
                isWarning = true;
            }
            else if (Definition.EffectiveHotkeyType == HotKeyTypeEnum.KeyboardMonitor)
            {
                status = "已生效。键鼠监听需要先在启动页启动截图器，只在游戏位于前台时响应，按键会同时传给游戏";
            }
            else
            {
                status = "已生效。全局热键不会把按键传给游戏，只在游戏位于前台时执行";
            }
        }

        HotkeyStatus = status;
        HotkeyStatusIsError = isError;
        HotkeyStatusIsWarning = isWarning;
    }

    #endregion

    #region 脚本与解析

    /// <summary>
    /// 宏脚本，绑定到脚本视图
    /// </summary>
    public string Script
    {
        get => Definition.Script;
        set
        {
            if (Definition.Script == value)
            {
                return;
            }

            SetDefinitionScript(value);
            // 脚本视图的输入防抖后解析，再按结果重建步骤
            _parseTimer.Stop();
            _parseTimer.Start();
        }
    }

    /// <summary>
    /// 全部诊断
    /// </summary>
    public IReadOnlyList<KeyMacroDiagnostic> Diagnostics => Document.Diagnostics;

    /// <summary>
    /// 是否有诊断需要显示
    /// </summary>
    public bool HasDiagnostics => Document.Diagnostics.Count > 0;

    /// <summary>
    /// 脚本是否有错误
    /// </summary>
    public bool HasErrors => Document.HasError;

    /// <summary>
    /// 步骤视图是否可编辑。脚本有错误时只读，避免写回时丢掉无法解析的内容
    /// </summary>
    public bool IsStepEditable => !Document.HasError;

    /// <summary>
    /// 步骤视图是否显示“还没有步骤”的提示
    /// </summary>
    public bool ShowEmptyStepsHint => Steps.Count == 0 && !Document.HasError;

    /// <summary>
    /// 步骤视图只读时的提示
    /// </summary>
    public string StepsReadOnlyMessage
    {
        get
        {
            var first = Document.Diagnostics.FirstOrDefault(d => d.IsError);
            return first == null ? string.Empty : $"脚本第 {first.Line} 行有错误，请在脚本视图修正，修正前步骤视图为只读";
        }
    }

    /// <summary>
    /// 指令数量和每轮时长
    /// </summary>
    public string SummaryText =>
        $"{Document.Commands.Count} 条指令 · 每轮约 {KeyMacroScriptWriter.FormatSeconds(Document.TotalWaitMs)} 秒";

    /// <summary>
    /// 状态栏文字
    /// </summary>
    public string StatusText
    {
        get
        {
            if (Document.HasError)
            {
                return $"有 {Document.ErrorCount} 处错误，执行时会跳过出错的指令";
            }

            var warnings = Document.Diagnostics.Count(d => !d.IsError);
            return warnings > 0 ? $"语法正确，有 {warnings} 条提醒" : "语法正确";
        }
    }

    /// <summary>
    /// 编辑的视图：0 为步骤视图，1 为脚本视图，记住上次的选择
    /// </summary>
    public int SelectedTabIndex
    {
        get => _config.KeyMacroConfig.PreferScriptView ? 1 : 0;
        set
        {
            _config.KeyMacroConfig.PreferScriptView = value == 1;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 修改宏定义的脚本，忽略随之而来的属性变化通知
    /// </summary>
    private void SetDefinitionScript(string script)
    {
        _settingScript = true;
        try
        {
            Definition.Script = script;
        }
        finally
        {
            _settingScript = false;
        }

        OnPropertyChanged(nameof(Script));
    }

    /// <summary>
    /// 重新解析脚本
    /// </summary>
    /// <param name="rebuildSteps">没有错误时是否按解析结果重建步骤视图</param>
    private void Reparse(bool rebuildSteps)
    {
        Document = KeyMacroParser.Parse(Definition.Script, KeyMacroRunner.CreateParseOptions(Definition.TriggerMode, HotKey));
        if (rebuildSteps && !Document.HasError)
        {
            RebuildSteps();
        }
    }

    /// <summary>
    /// 按解析结果重建步骤视图，尽量保留选中的位置
    /// </summary>
    private void RebuildSteps()
    {
        var selectedIndex = SelectedStep == null ? -1 : Steps.IndexOf(SelectedStep);
        _rebuildingSteps = true;
        try
        {
            foreach (var step in Steps)
            {
                step.Changed -= OnStepChanged;
            }

            Steps.Clear();
            foreach (var step in Document.Steps)
            {
                var vm = KeyMacroStepViewModel.FromStep(step);
                vm.Changed += OnStepChanged;
                Steps.Add(vm);
            }

            RenumberSteps();
        }
        finally
        {
            _rebuildingSteps = false;
        }

        SelectedStep = selectedIndex >= 0 && selectedIndex < Steps.Count ? Steps[selectedIndex] : null;
    }

    /// <summary>
    /// 宏定义变化：外部修改了文件，或本页修改了触发设置
    /// </summary>
    private void OnDefinitionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(KeyMacroDefinition.Name):
                EditingName = Definition.Name;
                OnPropertyChanged(nameof(Name));
                break;
            case nameof(KeyMacroDefinition.Enabled):
                OnPropertyChanged(nameof(Enabled));
                break;
            case nameof(KeyMacroDefinition.Script):
                if (!_settingScript)
                {
                    // 文件被外部修改：立即解析并重建步骤
                    OnPropertyChanged(nameof(Script));
                    Reparse(true);
                }

                return;
            case nameof(KeyMacroDefinition.Hotkey):
            case nameof(KeyMacroDefinition.HotkeyType):
            case nameof(KeyMacroDefinition.TriggerMode):
                OnPropertyChanged(nameof(HotKey));
                OnPropertyChanged(nameof(HotKeyTypeName));
                OnPropertyChanged(nameof(LocalizedHotKeyTypeName));
                OnPropertyChanged(nameof(CanSwitchHotKeyType));
                OnPropertyChanged(nameof(IsOnceMode));
                OnPropertyChanged(nameof(IsHoldMode));
                OnPropertyChanged(nameof(IsToggleMode));
                OnPropertyChanged(nameof(ModeDescription));
                OnPropertyChanged(nameof(ModeName));
                OnPropertyChanged(nameof(ListHotkeyText));
                // 自身快捷键和循环等待的检查依赖这些设置
                Reparse(false);
                break;
        }

        UpdateStatus();
    }

    #endregion

    #region 步骤编辑

    /// <summary>
    /// 在选中步骤之后插入新步骤，没有选中时追加到末尾
    /// </summary>
    [RelayCommand]
    private void AddStep(KeyMacroCommandKind kind)
    {
        if (!IsStepEditable)
        {
            return;
        }

        var step = KeyMacroStepViewModel.Create(kind);
        var index = SelectedStep == null ? Steps.Count : Steps.IndexOf(SelectedStep) + 1;
        InsertStep(index, step);
    }

    /// <summary>
    /// 复制步骤，插入到它的后面
    /// </summary>
    [RelayCommand]
    private void DuplicateStep(KeyMacroStepViewModel? step)
    {
        step ??= SelectedStep;
        if (!IsStepEditable || step == null)
        {
            return;
        }

        InsertStep(Steps.IndexOf(step) + 1, step.Clone());
    }

    /// <summary>
    /// 删除步骤，选中相邻的步骤
    /// </summary>
    [RelayCommand]
    private void DeleteStep(KeyMacroStepViewModel? step)
    {
        step ??= SelectedStep;
        if (!IsStepEditable || step == null)
        {
            return;
        }

        var index = Steps.IndexOf(step);
        if (index < 0)
        {
            return;
        }

        step.Changed -= OnStepChanged;
        Steps.RemoveAt(index);
        SelectedStep = Steps.Count == 0 ? null : Steps[Math.Min(index, Steps.Count - 1)];
    }

    /// <summary>
    /// 上移步骤
    /// </summary>
    [RelayCommand]
    private void MoveStepUp(KeyMacroStepViewModel? step)
    {
        MoveStep(step, -1);
    }

    /// <summary>
    /// 下移步骤
    /// </summary>
    [RelayCommand]
    private void MoveStepDown(KeyMacroStepViewModel? step)
    {
        MoveStep(step, 1);
    }

    /// <summary>
    /// 撤销上一次步骤编辑
    /// </summary>
    [RelayCommand]
    private void Undo()
    {
        RestoreHistory(_undo, _redo);
    }

    /// <summary>
    /// 重做
    /// </summary>
    [RelayCommand]
    private void Redo()
    {
        RestoreHistory(_redo, _undo);
    }

    /// <summary>
    /// 插入步骤并选中
    /// </summary>
    private void InsertStep(int index, KeyMacroStepViewModel step)
    {
        step.Changed += OnStepChanged;
        Steps.Insert(Math.Clamp(index, 0, Steps.Count), step);
        SelectedStep = step;
    }

    /// <summary>
    /// 按偏移移动步骤
    /// </summary>
    private void MoveStep(KeyMacroStepViewModel? step, int offset)
    {
        step ??= SelectedStep;
        if (!IsStepEditable || step == null)
        {
            return;
        }

        var index = Steps.IndexOf(step);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Steps.Count)
        {
            return;
        }

        Steps.Move(index, target);
        SelectedStep = step;
    }

    /// <summary>
    /// 从一个历史栈恢复脚本，当前脚本压入另一个栈
    /// </summary>
    private void RestoreHistory(List<string> from, List<string> to)
    {
        if (from.Count == 0)
        {
            return;
        }

        var script = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(Definition.Script);
        _lastUndoRecord = DateTime.MinValue;
        SetDefinitionScript(script);
        Reparse(true);
    }

    /// <summary>
    /// 某个步骤的内容变化
    /// </summary>
    private void OnStepChanged(object? sender, EventArgs e)
    {
        OnStepsEdited();
    }

    /// <summary>
    /// 步骤增删、排序（包括拖动排序）
    /// </summary>
    private void OnStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ShowEmptyStepsHint));
        if (_rebuildingSteps)
        {
            return;
        }

        // 拖动排序产生的新对象（复制时）也要订阅
        foreach (var added in e.NewItems?.OfType<KeyMacroStepViewModel>() ?? [])
        {
            added.Changed -= OnStepChanged;
            added.Changed += OnStepChanged;
        }

        OnStepsEdited();
    }

    /// <summary>
    /// 步骤被编辑：记录撤销，安排在本次操作结束后写回脚本
    /// </summary>
    private void OnStepsEdited()
    {
        if (_rebuildingSteps)
        {
            return;
        }

        // 写回尚未执行，此时的脚本就是编辑前的内容
        var now = DateTime.UtcNow;
        if (now - _lastUndoRecord > UndoMergeWindow)
        {
            _undo.Add(Definition.Script);
            if (_undo.Count > MaxUndoSteps)
            {
                _undo.RemoveAt(0);
            }

            _redo.Clear();
        }

        _lastUndoRecord = now;
        if (_writePending)
        {
            return;
        }

        _writePending = true;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(WriteStepsToScript));
    }

    /// <summary>
    /// 把步骤写回脚本并立即解析（不重建步骤，保留界面状态）
    /// </summary>
    private void WriteStepsToScript()
    {
        _writePending = false;
        RenumberSteps();
        var script = KeyMacroScriptWriter.Write(Steps.Select(s => s.ToStep()));
        if (script != Definition.Script)
        {
            SetDefinitionScript(script);
        }

        Reparse(false);
    }

    /// <summary>
    /// 更新步骤序号
    /// </summary>
    private void RenumberSteps()
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].Index = i + 1;
        }
    }

    #endregion

    #region 脚本视图

    /// <summary>
    /// 「插入按键」捕获到按键后，在光标处插入 keypress(键名)
    /// </summary>
    partial void OnInsertKeyNameChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        RequestInsert($"keypress({value})");
        InsertKeyName = string.Empty;
    }

    /// <summary>
    /// 语法速查：在光标处插入示例
    /// </summary>
    [RelayCommand]
    private void InsertExample(KeyMacroCommandInfo? info)
    {
        if (info != null)
        {
            RequestInsert(info.Example);
        }
    }

    /// <summary>
    /// 跳到诊断所在的位置，并切换到脚本视图
    /// </summary>
    [RelayCommand]
    private void JumpToDiagnostic(KeyMacroDiagnostic? diagnostic)
    {
        if (diagnostic == null || diagnostic.Line <= 0)
        {
            return;
        }

        SelectedTabIndex = 1;
        EditorRequest = new KeyMacroEditorRequest(++_requestId, KeyMacroEditorRequestKind.Jump, Line: diagnostic.Line,
            Column: diagnostic.Column, Length: diagnostic.Length);
    }

    /// <summary>
    /// 查看诊断：有错误时跳到第一处错误，只有提醒时切换到脚本视图查看问题列表
    /// </summary>
    [RelayCommand]
    private void ShowDiagnostics()
    {
        var firstError = Document.Diagnostics.FirstOrDefault(d => d.IsError);
        if (firstError is { Line: > 0 })
        {
            JumpToDiagnostic(firstError);
            return;
        }

        SelectedTabIndex = 1;
    }

    /// <summary>
    /// 请求编辑器在光标处插入文字
    /// </summary>
    private void RequestInsert(string text)
    {
        EditorRequest = new KeyMacroEditorRequest(++_requestId, KeyMacroEditorRequestKind.Insert, text);
    }

    #endregion

    /// <inheritdoc />
    public void Dispose()
    {
        _parseTimer.Stop();
        Definition.PropertyChanged -= OnDefinitionPropertyChanged;
        Steps.CollectionChanged -= OnStepsCollectionChanged;
        foreach (var step in Steps)
        {
            step.Changed -= OnStepChanged;
        }
    }
}
