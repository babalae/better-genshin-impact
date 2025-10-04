using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterGenshinImpact.ViewModel.Pages.Component;
using BetterGenshinImpact.Model.Gear.Triggers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Violeta.Controls;
using BetterGenshinImpact.Helpers.Extensions;

namespace BetterGenshinImpact.ViewModel.Windows.GearTask;

/// <summary>
/// 新增触发器对话框 ViewModel
/// </summary>
public partial class AddTriggerDialogViewModel : ObservableObject
{
    private readonly GearTaskStorageService _storageService;
    private readonly ILogger<AddTriggerDialogViewModel> _logger;

    [ObservableProperty]
    private string _triggerName = string.Empty;

    [ObservableProperty]
    private TriggerType _selectedTriggerType = TriggerType.Timed;

    [ObservableProperty]
    private string _cronExpression = "0 0 8 * * ?"; // 默认每天8点

    [ObservableProperty]
    private HotKey? _selectedHotkey;

    [ObservableProperty]
    private HotKeyTypeEnum _hotkeyType = HotKeyTypeEnum.KeyboardMonitor;

    /// <summary>
    /// 快捷键类型显示名称
    /// </summary>
    public string HotkeyTypeName => HotkeyType.ToChineseName();

    [ObservableProperty]
    private string _selectedTaskDefinitionName = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>
    /// 触发器类型选择是否可用
    /// </summary>
    [ObservableProperty]
    private bool _isTriggerTypeSelectionEnabled = true;

    /// <summary>
    /// 可用的触发器类型
    /// </summary>
    public ObservableCollection<EnumItem<TriggerType>> TriggerTypes { get; } = new()
    {
        EnumItem<TriggerType>.Create(TriggerType.Timed),
        EnumItem<TriggerType>.Create(TriggerType.Hotkey)
    };

    /// <summary>
    /// 可用的任务定义列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _availableTaskDefinitions = new();

    /// <summary>
    /// 请求关闭事件
    /// </summary>
    public event EventHandler<bool>? RequestClose;

    /// <summary>
    /// 创建的触发器
    /// </summary>
    public GearTriggerViewModel? CreatedTrigger { get; private set; }

    public AddTriggerDialogViewModel(GearTaskStorageService storageService, ILogger<AddTriggerDialogViewModel> logger)
    {
        _storageService = storageService;
        _logger = logger;
        
        // 生成默认名称
        GenerateDefaultName();
        
        // 加载可用的任务定义
        LoadAvailableTaskDefinitions();
    }

    /// <summary>
    /// 构造函数，用于指定触发器类型
    /// </summary>
    public AddTriggerDialogViewModel(GearTaskStorageService storageService, ILogger<AddTriggerDialogViewModel> logger, TriggerType? predefinedType = null)
    {
        _storageService = storageService;
        _logger = logger;
        
        // 如果指定了预定义类型，则设置并禁用选择
        if (predefinedType.HasValue)
        {
            SelectedTriggerType = predefinedType.Value;
            IsTriggerTypeSelectionEnabled = false;
        }
        
        // 生成默认名称
        GenerateDefaultName();
        
        // 加载可用的任务定义
        LoadAvailableTaskDefinitions();
    }

    /// <summary>
    /// 生成默认触发器名称
    /// </summary>
    private void GenerateDefaultName()
    {
        var typeName = SelectedTriggerType == TriggerType.Timed ? "定时触发器" : "热键触发器";
        TriggerName = $"{typeName} {DateTime.Now:MMdd_HHmm}";
    }

    /// <summary>
    /// 加载可用的任务定义
    /// </summary>
    private async void LoadAvailableTaskDefinitions()
    {
        try
        {
            AvailableTaskDefinitions.Clear();
            
            // 从 GearTaskStorageService 加载所有任务定义
            var taskDefinitions = await _storageService.LoadAllTaskDefinitionsAsync();
            
            // 提取任务定义名称并添加到列表中
            foreach (var taskDefinition in taskDefinitions)
            {
                if (!string.IsNullOrWhiteSpace(taskDefinition.Name))
                {
                    AvailableTaskDefinitions.Add(taskDefinition.Name);
                }
            }
            
            _logger.LogInformation("已加载 {Count} 个可用的任务定义", AvailableTaskDefinitions.Count);
            
            // 如果有任务定义，默认选择第一个
            if (AvailableTaskDefinitions.Count > 0)
            {
                SelectedTaskDefinitionName = AvailableTaskDefinitions[0];
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "加载可用任务定义时发生错误");
            
            // 发生错误时添加一个提示项
            AvailableTaskDefinitions.Clear();
            AvailableTaskDefinitions.Add("(无可用任务定义)");
        }
    }

    /// <summary>
    /// 快捷键类型改变时的处理
    /// </summary>
    partial void OnHotkeyTypeChanged(HotKeyTypeEnum value)
    {
        // 通知UI更新快捷键类型显示名称
        OnPropertyChanged(nameof(HotkeyTypeName));
        
        // 清空当前快捷键设置
        SelectedHotkey = null;
    }

    /// <summary>
    /// 触发器类型改变时的处理
    /// </summary>
    partial void OnSelectedTriggerTypeChanged(TriggerType value)
    {
        GenerateDefaultName();
    }

    /// <summary>
    /// 确认创建触发器
    /// </summary>
    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(TriggerName))
        {
            Toast.Error("请输入触发器名称");
            return;
        }

        if (SelectedTriggerType == TriggerType.Timed && string.IsNullOrWhiteSpace(CronExpression))
        {
            Toast.Error("请输入 Cron 表达式");
            return;
        }

        if (SelectedTriggerType == TriggerType.Hotkey && SelectedHotkey == null)
        {
            Toast.Error("请选择热键");
            return;
        }

        // 创建触发器 ViewModel
        CreatedTrigger = new GearTriggerViewModel(TriggerName, SelectedTriggerType)
        {
            IsEnabled = IsEnabled,
            TaskDefinitionName = SelectedTaskDefinitionName,
            CronExpression = SelectedTriggerType == TriggerType.Timed ? CronExpression : null,
            Hotkey = SelectedTriggerType == TriggerType.Hotkey ? SelectedHotkey : null,
            HotkeyType = SelectedTriggerType == TriggerType.Hotkey ? HotkeyType : HotKeyTypeEnum.KeyboardMonitor
        };

        RequestClose?.Invoke(this, true);
    }

    /// <summary>
    /// 取消创建
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, false);
    }

    /// <summary>
    /// 切换快捷键类型
    /// </summary>
    [RelayCommand]
    private void SwitchHotKeyType()
    {
        HotkeyType = HotkeyType == HotKeyTypeEnum.GlobalRegister ? HotKeyTypeEnum.KeyboardMonitor : HotKeyTypeEnum.GlobalRegister;
    }

    /// <summary>
    /// 选择热键
    /// </summary>
    [RelayCommand]
    private void SelectHotkey()
    {
        // 移除旧的示例代码，现在使用HotKeyTextBox直接设置
        // HotKeyTextBox会直接绑定到SelectedHotkey属性
    }
}