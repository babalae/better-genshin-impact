using System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoCook;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoMusicGame;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Macro;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.OneDragon;
using BetterGenshinImpact.GameTask.QuickBuy;
using BetterGenshinImpact.GameTask.QuickClaimReward;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Fischless.HotkeyCapture;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Vanara.PInvoke;
using HotKeySettingModel = BetterGenshinImpact.Model.HotKeySettingModel;
using Toast = Wpf.Ui.Violeta.Controls.Toast;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HotKeyPageViewModel : ObservableObject, IViewModel
{
    private readonly ILogger<HotKeyPageViewModel> _logger;
    private readonly TaskSettingsPageViewModel _taskSettingsPageViewModel;
    private readonly RecognitionTemplateEditorService _recognitionTemplateEditorService;

    /// <summary>
    /// 游戏运行环境服务。热键触发时读取 <see cref="GameRuntimeService.Current"/>，不缓存
    /// </summary>
    private readonly GameRuntimeService _runtimeService;

    /// <summary>
    /// 游戏截图保存服务，截图热键使用
    /// </summary>
    private readonly GameScreenshotService _screenshots;

    /// <summary>
    /// "截图器未启动"提示的最小间隔（毫秒），长按类热键会反复回调
    /// </summary>
    private const long NoRuntimeToastIntervalMs = 2000;

    /// <summary>
    /// 上一次提示"截图器未启动"的时间（<see cref="Environment.TickCount64"/>），初始值保证首次立即提示
    /// </summary>
    private long _lastNoRuntimeToastTick = -NoRuntimeToastIntervalMs;

    /// <summary>
    /// 把热键产生的外部停止信号转发给当前运行。
    /// </summary>
    private readonly TaskStopService _taskStopService;

    /// <summary>
    /// 悬浮小窗服务，“悬浮小窗”分组的快捷键调用它
    /// </summary>
    private readonly IGuideWindowService _guideWindowService;

    /// <summary>
    /// “悬浮小窗”分组，悬浮小窗设置页直接绑定其中的快捷键对象，与快捷键页共用同一份注册
    /// </summary>
    public HotKeySettingModel GuideHotKeyDirectory { get; private set; } = null!;

    private readonly Dictionary<string, HotKey> _acceptedHotKeys = [];
    private readonly HashSet<string> _rollingBackHotKeyProperties = [];
    public AllConfig Config { get; set; }

    [ObservableProperty]
    private ObservableCollection<HotKeySettingModel> _hotKeySettingModels = [];

    /// <summary>
    /// 快捷键页的筛选关键字，按功能名称或分组名称匹配，忽略大小写；为空时显示全部
    /// </summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>
    /// 当前筛选关键字没有匹配任何功能，页面据此显示空结果提示
    /// </summary>
    [ObservableProperty]
    private bool _isFilterResultEmpty;

    /// <summary>
    /// 网页版实例不响应任何热键（全局热键与键鼠监听都不注册）：
    /// 同一 Windows Session 中全局热键只能被先启动的实例注册，键鼠监听也会与 Primary 重复响应。
    /// 网页版实例虽然显示主界面，但热键页只展示共享配置，不应在当前进程重复注册。
    /// </summary>
    public bool IsHotKeyEnabled { get; } = !InstanceBootstrap.Current.Context.IsWebView;

    public HotKeyPageViewModel(
        IConfigService configService,
        ILogger<HotKeyPageViewModel> logger,
        TaskSettingsPageViewModel taskSettingsPageViewModel,
        RecognitionTemplateEditorService recognitionTemplateEditorService,
        GameRuntimeService runtimeService,
        GameScreenshotService screenshots,
        TaskStopService taskStopService,
        IGuideWindowService guideWindowService)
    {
        _logger = logger;
        _guideWindowService = guideWindowService;
        _taskSettingsPageViewModel = taskSettingsPageViewModel;
        _recognitionTemplateEditorService = recognitionTemplateEditorService;
        _runtimeService = runtimeService;
        _screenshots = screenshots;
        _taskStopService = taskStopService;
        // 获取配置
        Config = configService.Get();

        // 构建快捷键配置列表
        BuildHotKeySettingModelList();

        var list = GetAllNonDirectoryHotkey(HotKeySettingModels);
        foreach (var hotKeyConfig in list)
        {
            _acceptedHotKeys[hotKeyConfig.ConfigPropertyName] = hotKeyConfig.HotKey;
            if (IsHotKeyEnabled)
            {
                hotKeyConfig.RegisterHotKey();
            }
            hotKeyConfig.PropertyChanged += (sender, e) =>
            {
                if (sender is HotKeySettingModel model)
                {
                    // 反射更新配置

                    // 更新快捷键
                    if (e.PropertyName == "HotKey")
                    {
                        Debug.WriteLine($"{model.FunctionName} 快捷键变更为 {model.HotKey}");
                        if (_rollingBackHotKeyProperties.Remove(model.ConfigPropertyName))
                        {
                            UpdateHotKeyConfig(model);
                        }
                        else
                        {
                            var newHotKey = model.HotKey;
                            var previousHotKey = _acceptedHotKeys.GetValueOrDefault(model.ConfigPropertyName, HotKey.None);
                            UpdateHotKeyConfig(model);
                            _acceptedHotKeys[model.ConfigPropertyName] = newHotKey;
                            ShowGameKeyBindingConflictWarning(model, newHotKey, previousHotKey);
                        }
                    }

                    // 更新快捷键类型
                    if (e.PropertyName == "HotKeyType")
                    {
                        Debug.WriteLine($"{model.FunctionName} 快捷键类型变更为 {model.HotKeyType.ToChineseName()}");
                        model.HotKey = HotKey.None;
                        var pi = Config.HotKeyConfig.GetType().GetProperty(model.ConfigPropertyName + "Type", BindingFlags.Public | BindingFlags.Instance);
                        if (null != pi && pi.CanWrite)
                        {
                            pi.SetValue(Config.HotKeyConfig, model.HotKeyType.ToString(), null);
                        }
                    }

                    RemoveDuplicateHotKey(model);
                    model.UnRegisterHotKey();
                    if (IsHotKeyEnabled)
                    {
                        model.RegisterHotKey();
                    }
                }
            };
        }
    }

    private void ShowGameKeyBindingConflictWarning(HotKeySettingModel model, HotKey newHotKey, HotKey previousHotKey)
    {
        if (!TryConvertToGameKeyId(model.HotKey, out var hotKeyId))
        {
            return;
        }

        var conflictLines = GetGameKeyBindingConflictLines(hotKeyId);
        if (conflictLines.Count == 0)
        {
            return;
        }

        var message = string.Format(
                          I18nService.Instance.Translate("{0}使用{1}键会与原神键位冲突："),
                          model.LocalizedFunctionName,
                          hotKeyId.ToName())
                      + "\n\n"
                      + string.Join("\n", conflictLines)
                      + "\n\n"
                      + I18nService.Instance.Translate("会导致游戏内动作和 BetterGI 功能同时触发，建议更换为其他按键。是否继续使用？");
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (model.HotKey != newHotKey)
            {
                return;
            }

            var result = ThemedMessageBox.Warning(
                message,
                I18nService.Instance.Translate("快捷键冲突提醒"),
                MessageBoxButton.OKCancel,
                MessageBoxResult.Cancel);
            if (result == MessageBoxResult.Cancel && model.HotKey == newHotKey)
            {
                _acceptedHotKeys[model.ConfigPropertyName] = previousHotKey;
                _rollingBackHotKeyProperties.Add(model.ConfigPropertyName);
                model.HotKey = previousHotKey;
            }
        });
    }

    private static bool TryConvertToGameKeyId(HotKey hotKey, out KeyId keyId)
    {
        keyId = KeyId.Unknown;

        if (hotKey.IsEmpty)
        {
            return false;
        }

        if (hotKey.MouseButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            keyId = KeyIdConverter.FromMouseButton(hotKey.MouseButton);
            return keyId is not KeyId.Unknown and not KeyId.None;
        }

        if (hotKey.Modifiers != ModifierKeys.None || hotKey.Key == Key.None)
        {
            return false;
        }

        keyId = KeyIdConverter.FromInputKey(hotKey.Key);
        return keyId is not KeyId.Unknown and not KeyId.None;
    }

    private List<string> GetGameKeyBindingConflictLines(KeyId hotKeyId)
    {
        var lines = new List<string>();
        foreach (var pi in typeof(KeyBindingsConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (pi.PropertyType != typeof(KeyId))
            {
                continue;
            }

            if (pi.GetValue(Config.KeyBindingsConfig) is KeyId gameKeyId && gameKeyId == hotKeyId)
            {
                lines.Add($"- {GetGameKeyBindingActionName(pi.Name)}（{gameKeyId.ToName()}）");
            }
        }

        return lines;
    }

    private static string GetGameKeyBindingActionName(string propertyName)
    {
        return propertyName switch
        {
            nameof(KeyBindingsConfig.MoveForward) => I18nService.Instance.Translate("向前移动"),
            nameof(KeyBindingsConfig.MoveBackward) => I18nService.Instance.Translate("向后移动"),
            nameof(KeyBindingsConfig.MoveLeft) => I18nService.Instance.Translate("向左移动"),
            nameof(KeyBindingsConfig.MoveRight) => I18nService.Instance.Translate("向右移动"),
            nameof(KeyBindingsConfig.SwitchToWalkOrRun) => I18nService.Instance.Translate("切换走/跑；特定操作模式下向下移动"),
            nameof(KeyBindingsConfig.NormalAttack) => I18nService.Instance.Translate("普通攻击"),
            nameof(KeyBindingsConfig.ElementalSkill) => I18nService.Instance.Translate("元素战技"),
            nameof(KeyBindingsConfig.ElementalBurst) => I18nService.Instance.Translate("元素爆发"),
            nameof(KeyBindingsConfig.SprintKeyboard) => I18nService.Instance.Translate("冲刺（键盘）"),
            nameof(KeyBindingsConfig.SprintMouse) => I18nService.Instance.Translate("冲刺（鼠标）"),
            nameof(KeyBindingsConfig.SwitchAimingMode) => I18nService.Instance.Translate("切换瞄准模式"),
            nameof(KeyBindingsConfig.Jump) => I18nService.Instance.Translate("跳跃；特定操作模式下向上移动"),
            nameof(KeyBindingsConfig.Drop) => I18nService.Instance.Translate("落下"),
            nameof(KeyBindingsConfig.PickUpOrInteract) => I18nService.Instance.Translate("拾取/交互（自动拾取由AutoPick模块管理）"),
            nameof(KeyBindingsConfig.QuickUseGadget) => I18nService.Instance.Translate("快捷使用小道具"),
            nameof(KeyBindingsConfig.InteractionInSomeMode) => I18nService.Instance.Translate("特定玩法内交互操作"),
            nameof(KeyBindingsConfig.QuestNavigation) => I18nService.Instance.Translate("开启任务追踪"),
            nameof(KeyBindingsConfig.AbandonChallenge) => I18nService.Instance.Translate("中断挑战"),
            nameof(KeyBindingsConfig.SwitchMember1) => I18nService.Instance.Translate("切换小队角色1"),
            nameof(KeyBindingsConfig.SwitchMember2) => I18nService.Instance.Translate("切换小队角色2"),
            nameof(KeyBindingsConfig.SwitchMember3) => I18nService.Instance.Translate("切换小队角色3"),
            nameof(KeyBindingsConfig.SwitchMember4) => I18nService.Instance.Translate("切换小队角色4"),
            nameof(KeyBindingsConfig.SwitchMember5) => I18nService.Instance.Translate("切换小队角色5"),
            nameof(KeyBindingsConfig.ShortcutWheel) => I18nService.Instance.Translate("呼出快捷轮盘"),
            nameof(KeyBindingsConfig.OpenInventory) => I18nService.Instance.Translate("打开背包"),
            nameof(KeyBindingsConfig.OpenCharacterScreen) => I18nService.Instance.Translate("打开角色界面"),
            nameof(KeyBindingsConfig.OpenMap) => I18nService.Instance.Translate("打开地图"),
            nameof(KeyBindingsConfig.OpenPaimonMenu) => I18nService.Instance.Translate("打开派蒙界面"),
            nameof(KeyBindingsConfig.OpenAdventurerHandbook) => I18nService.Instance.Translate("打开冒险之证界面"),
            nameof(KeyBindingsConfig.OpenCoOpScreen) => I18nService.Instance.Translate("打开多人游戏界面"),
            nameof(KeyBindingsConfig.OpenWishScreen) => I18nService.Instance.Translate("打开祈愿界面"),
            nameof(KeyBindingsConfig.OpenBattlePassScreen) => I18nService.Instance.Translate("打开纪行界面"),
            nameof(KeyBindingsConfig.OpenTheEventsMenu) => I18nService.Instance.Translate("打开活动面板"),
            nameof(KeyBindingsConfig.OpenTheSettingsMenu) => I18nService.Instance.Translate("打开玩法系统界面（尘歌壶内猫尾酒馆内）"),
            nameof(KeyBindingsConfig.OpenTheFurnishingScreen) => I18nService.Instance.Translate("打开摆设界面（尘歌壶内）"),
            nameof(KeyBindingsConfig.OpenStellarReunion) => I18nService.Instance.Translate("打开星之归还（条件符合期间生效）"),
            nameof(KeyBindingsConfig.OpenQuestMenu) => I18nService.Instance.Translate("开关任务菜单"),
            nameof(KeyBindingsConfig.OpenNotificationDetails) => I18nService.Instance.Translate("打开通知详情"),
            nameof(KeyBindingsConfig.OpenChatScreen) => I18nService.Instance.Translate("打开聊天界面"),
            nameof(KeyBindingsConfig.OpenSpecialEnvironmentInformation) => I18nService.Instance.Translate("打开特殊环境说明"),
            nameof(KeyBindingsConfig.CheckTutorialDetails) => I18nService.Instance.Translate("查看教程详情"),
            nameof(KeyBindingsConfig.ElementalSight) => I18nService.Instance.Translate("长按打开元素视野"),
            nameof(KeyBindingsConfig.ShowCursor) => I18nService.Instance.Translate("呼出鼠标"),
            nameof(KeyBindingsConfig.OpenPartySetupScreen) => I18nService.Instance.Translate("打开队伍配置界面"),
            nameof(KeyBindingsConfig.OpenFriendsScreen) => I18nService.Instance.Translate("打开好友界面"),
            nameof(KeyBindingsConfig.HideUI) => I18nService.Instance.Translate("隐藏主界面"),
            _ => propertyName
        };
    }

    private void UpdateHotKeyConfig(HotKeySettingModel model)
    {
        var pi = Config.HotKeyConfig.GetType().GetProperty(model.ConfigPropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (null != pi && pi.CanWrite)
        {
            pi.SetValue(Config.HotKeyConfig, model.HotKey.IsEmpty ? "" : model.HotKey.ToString(), null);
        }
    }

    /// <summary>
    /// 移除重复的快捷键配置
    /// </summary>
    /// <param name="current"></param>
    private void RemoveDuplicateHotKey(HotKeySettingModel current)
    {
        if (current.HotKey.IsEmpty)
        {
            return;
        }

        var list = GetAllNonDirectoryHotkey(HotKeySettingModels);
        foreach (var hotKeySettingModel in list)
        {
            if (hotKeySettingModel.HotKey.IsEmpty)
            {
                continue;
            }

            if (hotKeySettingModel.ConfigPropertyName != current.ConfigPropertyName && hotKeySettingModel.HotKey == current.HotKey)
            {
                hotKeySettingModel.HotKey = HotKey.None;
            }
        }
    }

    /// <summary>
    /// 筛选关键字变化时重新计算各行的显示状态
    /// </summary>
    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter(value);
    }

    /// <summary>
    /// 按关键字设置每个节点的显示状态：分组名称匹配时显示整组，功能名称匹配时显示该项及其所在分组。
    /// 只修改显示状态，不增删集合，热键注册和悬浮小窗设置页都不受影响。
    /// 切换界面语言后不会自动重新筛选，修改关键字时按新语言重新计算
    /// </summary>
    /// <param name="keyword">用户输入的关键字</param>
    private void ApplyFilter(string? keyword)
    {
        var trimmedKeyword = keyword?.Trim() ?? string.Empty;
        var anyVisible = false;
        foreach (var model in HotKeySettingModels)
        {
            // 用 | 而不是 ||，保证每个顶层节点都会被计算
            anyVisible |= ApplyFilter(model, trimmedKeyword, false);
        }

        IsFilterResultEmpty = !anyVisible;
    }

    /// <summary>
    /// 递归计算节点及其子节点的显示状态
    /// </summary>
    /// <param name="model">当前节点</param>
    /// <param name="keyword">已去除首尾空白的关键字，空字符串表示不筛选</param>
    /// <param name="ancestorMatched">上级分组名称已匹配，此时整组显示</param>
    /// <returns>当前节点是否显示</returns>
    private static bool ApplyFilter(HotKeySettingModel model, string keyword, bool ancestorMatched)
    {
        var matched = ancestorMatched || keyword.Length == 0 || IsFilterMatch(model, keyword);
        var anyChildVisible = false;
        foreach (var child in model.Children)
        {
            anyChildVisible |= ApplyFilter(child, keyword, matched);
        }

        var visible = matched || anyChildVisible;
        model.IsFilterVisible = visible;

        // 有关键字时展开含显示项的分组，避免匹配项藏在用户折叠的分组里；清空关键字时保留当前的展开状态
        if (keyword.Length > 0 && visible && model.IsDirectory)
        {
            model.IsExpanded = true;
        }

        return visible;
    }

    /// <summary>
    /// 当前界面语言的名称或中文原名包含关键字即为匹配，忽略大小写
    /// </summary>
    private static bool IsFilterMatch(HotKeySettingModel model, string keyword)
    {
        return model.LocalizedFunctionName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
               || model.FunctionName.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    public static List<HotKeySettingModel> GetAllNonDirectoryHotkey(IEnumerable<HotKeySettingModel> modelList)
    {
        var list = new List<HotKeySettingModel>();
        foreach (var hotKeySettingModel in modelList)
        {
            if (!hotKeySettingModel.IsDirectory)
            {
                list.Add(hotKeySettingModel);
            }

            list.AddRange(GetAllNonDirectoryChildren(hotKeySettingModel));
        }

        return list;
    }

    public static List<HotKeySettingModel> GetAllNonDirectoryChildren(HotKeySettingModel model)
    {
        var result = new List<HotKeySettingModel>();

        if (model.Children.Count == 0)
        {
            return result;
        }

        foreach (var child in model.Children)
        {
            if (!child.IsDirectory)
            {
                result.Add(child);
            }

            // 递归调用以获取子节点中的非目录对象
            result.AddRange(GetAllNonDirectoryChildren(child));
        }

        return result;
    }

    private void BuildHotKeySettingModelList()
    {
        // 一级目录/快捷键
        var bgiEnabledHotKeySettingModel = new HotKeySettingModel(
            "启动停止 BetterGI",
            nameof(Config.HotKeyConfig.BgiEnabledHotkey),
            Config.HotKeyConfig.BgiEnabledHotkey,
            Config.HotKeyConfig.BgiEnabledHotkeyType,
            (_, _) => { WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "SwitchTriggerStatus", "", "")); }
        );
        HotKeySettingModels.Add(bgiEnabledHotKeySettingModel);

        var systemDirectory = new HotKeySettingModel(
            "系统控制"
        );
        HotKeySettingModels.Add(systemDirectory);

        var timerDirectory = new HotKeySettingModel(
            "实时任务"
        );
        HotKeySettingModels.Add(timerDirectory);

        var soloTaskDirectory = new HotKeySettingModel(
            "独立任务"
        );
        HotKeySettingModels.Add(soloTaskDirectory);

        var macroDirectory = new HotKeySettingModel(
            "操控辅助"
        );
        HotKeySettingModels.Add(macroDirectory);

        var guideDirectory = new HotKeySettingModel(
            "悬浮小窗"
        );
        HotKeySettingModels.Add(guideDirectory);
        GuideHotKeyDirectory = guideDirectory;
        BuildGuideHotKeys(guideDirectory);

        var devDirectory = new HotKeySettingModel(
            "开发者"
        );
        HotKeySettingModels.Add(devDirectory);

        // 二级快捷键
        systemDirectory.Children.Add(new HotKeySettingModel(
            "停止当前脚本/独立任务",
            nameof(Config.HotKeyConfig.CancelTaskHotkey),
            Config.HotKeyConfig.CancelTaskHotkey,
            Config.HotKeyConfig.CancelTaskHotkeyType,
            (_, _) =>
            {
                _logger.LogInformation("检测到您配置的停止快捷键{Key}按下，停止当前执行任务", Config.HotKeyConfig.CancelTaskHotkey);
                _taskStopService.StopAll(TaskStopReason.UserRequested);
            }
        ));
        systemDirectory.Children.Add(new HotKeySettingModel(
            "暂停当前脚本/独立任务",
            nameof(Config.HotKeyConfig.SuspendHotkey),
            Config.HotKeyConfig.SuspendHotkey,
            Config.HotKeyConfig.SuspendHotkeyType,
            (_, _) => { RunnerContext.Instance.IsSuspend = !RunnerContext.Instance.IsSuspend; }
        ));
        var takeScreenshotHotKeySettingModel = new HotKeySettingModel(
            "游戏截图",
            nameof(Config.HotKeyConfig.TakeScreenshotHotkey),
            Config.HotKeyConfig.TakeScreenshotHotkey,
            Config.HotKeyConfig.TakeScreenshotHotkeyType,
            (_, _) =>
            {
                if (_runtimeService.Current is { } runtime)
                {
                    _screenshots.SaveCurrentFrame(runtime);
                }
                else
                {
                    // 截图器未启动时没有可截取的画面，沿用原来的提示
                    _logger.LogInformation("截图失败，未获取到图像");
                }
            }
        );
        systemDirectory.Children.Add(takeScreenshotHotKeySettingModel);

        systemDirectory.Children.Add(new HotKeySettingModel(
            "日志与状态窗口展示开关",
            nameof(Config.HotKeyConfig.LogBoxDisplayHotkey),
            Config.HotKeyConfig.LogBoxDisplayHotkey,
            Config.HotKeyConfig.LogBoxDisplayHotkeyType,
            (_, _) =>
            {
                Config.MaskWindowConfig.ShowLogBox = !Config.MaskWindowConfig.ShowLogBox;
                // 与状态窗口同步
                Config.MaskWindowConfig.ShowStatus = Config.MaskWindowConfig.ShowLogBox;
            }
        ));

        systemDirectory.Children.Add(new HotKeySettingModel(
            "遮罩指标栏展示开关",
            nameof(Config.HotKeyConfig.OverlayMetricsDisplayHotkey),
            Config.HotKeyConfig.OverlayMetricsDisplayHotkey,
            Config.HotKeyConfig.OverlayMetricsDisplayHotkeyType,
            (_, _) =>
            {
                Config.MaskWindowConfig.ShowOverlayMetrics = !Config.MaskWindowConfig.ShowOverlayMetrics;
            }
        ));

        var autoPickEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动拾取开关",
            nameof(Config.HotKeyConfig.AutoPickEnabledHotkey),
            Config.HotKeyConfig.AutoPickEnabledHotkey,
            Config.HotKeyConfig.AutoPickEnabledHotkeyType,
            (_, _) =>
            {
                Config.AutoPickConfig.Enabled = !Config.AutoPickConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动拾取", ToChinese(Config.AutoPickConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(autoPickEnabledHotKeySettingModel);

        var autoSkipEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动剧情开关",
            nameof(Config.HotKeyConfig.AutoSkipEnabledHotkey),
            Config.HotKeyConfig.AutoSkipEnabledHotkey,
            Config.HotKeyConfig.AutoSkipEnabledHotkeyType,
            (_, _) =>
            {
                Config.AutoSkipConfig.Enabled = !Config.AutoSkipConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动剧情", ToChinese(Config.AutoSkipConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(autoSkipEnabledHotKeySettingModel);

        timerDirectory.Children.Add(new HotKeySettingModel(
            "自动邀约开关",
            nameof(Config.HotKeyConfig.AutoSkipHangoutEnabledHotkey),
            Config.HotKeyConfig.AutoSkipHangoutEnabledHotkey,
            Config.HotKeyConfig.AutoSkipHangoutEnabledHotkeyType,
            (_, _) =>
            {
                Config.AutoSkipConfig.AutoHangoutEventEnabled = !Config.AutoSkipConfig.AutoHangoutEventEnabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动邀约", ToChinese(Config.AutoSkipConfig.AutoHangoutEventEnabled));
            }
        ));

        var autoFishingEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动钓鱼开关",
            nameof(Config.HotKeyConfig.AutoFishingEnabledHotkey),
            Config.HotKeyConfig.AutoFishingEnabledHotkey,
            Config.HotKeyConfig.AutoFishingEnabledHotkeyType,
            (_, _) =>
            {
                Config.AutoFishingConfig.Enabled = !Config.AutoFishingConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动钓鱼", ToChinese(Config.AutoFishingConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(autoFishingEnabledHotKeySettingModel);

        var quickTeleportEnabledHotKeySettingModel = new HotKeySettingModel(
            "快速传送开关",
            nameof(Config.HotKeyConfig.QuickTeleportEnabledHotkey),
            Config.HotKeyConfig.QuickTeleportEnabledHotkey,
            Config.HotKeyConfig.QuickTeleportEnabledHotkeyType,
            (_, _) =>
            {
                Config.QuickTeleportConfig.Enabled = !Config.QuickTeleportConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "快速传送", ToChinese(Config.QuickTeleportConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(quickTeleportEnabledHotKeySettingModel);
        
        var skillCdEnabledHotKeySettingModel = new HotKeySettingModel(
            "冷却提示开关",
            nameof(Config.HotKeyConfig.SkillCdEnabledHotkey),
            Config.HotKeyConfig.SkillCdEnabledHotkey,
            Config.HotKeyConfig.SkillCdEnabledHotkeyType,
            (_, _) =>
            {
                Config.SkillCdConfig.Enabled = !Config.SkillCdConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "冷却提示", ToChinese(Config.SkillCdConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(skillCdEnabledHotKeySettingModel);

        var quickTeleportTickHotKeySettingModel = new HotKeySettingModel(
            "手动触发快速传送触发快捷键（按住起效）",
            nameof(Config.HotKeyConfig.QuickTeleportTickHotkey),
            Config.HotKeyConfig.QuickTeleportTickHotkey,
            Config.HotKeyConfig.QuickTeleportTickHotkeyType,
            (_, _) => { Thread.Sleep(100); },
            true
        );
        timerDirectory.Children.Add(quickTeleportTickHotKeySettingModel);

        var mapMaskEnabledHotKeySettingModel = new HotKeySettingModel(
            "地图遮罩开关",
            nameof(Config.HotKeyConfig.MapMaskEnabledHotkey),
            Config.HotKeyConfig.MapMaskEnabledHotkey,
            Config.HotKeyConfig.MapMaskEnabledHotkeyType,
            (_, _) =>
            {
                Config.MapMaskConfig.Enabled = !Config.MapMaskConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "地图遮罩", ToChinese(Config.MapMaskConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(mapMaskEnabledHotKeySettingModel);

        var turnAroundHotKeySettingModel = new HotKeySettingModel(
            "长按旋转视角 - 那维莱特转圈",
            nameof(Config.HotKeyConfig.TurnAroundHotkey),
            Config.HotKeyConfig.TurnAroundHotkey,
            Config.HotKeyConfig.TurnAroundHotkeyType,
            (_, _) => { TurnAroundMacro.Done(); },
            true
        );
        macroDirectory.Children.Add(turnAroundHotKeySettingModel);

        var enhanceArtifactHotKeySettingModel = new HotKeySettingModel(
            "按下快速强化圣遗物",
            nameof(Config.HotKeyConfig.EnhanceArtifactHotkey),
            Config.HotKeyConfig.EnhanceArtifactHotkey,
            Config.HotKeyConfig.EnhanceArtifactHotkeyType,
            (_, _) =>
            {
                if (TryGetRuntime(out var runtime))
                {
                    QuickEnhanceArtifactMacro.Done(runtime);
                }
            },
            true
        );
        macroDirectory.Children.Add(enhanceArtifactHotKeySettingModel);

        macroDirectory.Children.Add(new HotKeySettingModel(
            "按下快速购买商店物品",
            nameof(Config.HotKeyConfig.QuickBuyHotkey),
            Config.HotKeyConfig.QuickBuyHotkey,
            Config.HotKeyConfig.QuickBuyHotkeyType,
            (_, _) =>
            {
                if (TryGetRuntime(out var runtime))
                {
                    QuickBuyTask.Done(runtime);
                }
            },
            true
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            "一键领取奖励",
            nameof(Config.HotKeyConfig.OneKeyClaimRewardHotkey),
            Config.HotKeyConfig.OneKeyClaimRewardHotkey,
            Config.HotKeyConfig.OneKeyClaimRewardHotkeyType,
            null,
            true)
        {
            OnKeyDownAction = (_, _) =>
            {
                if (TryGetRuntime(out var runtime))
                {
                    OneKeyClaimRewardTask.Instance.KeyDown(runtime);
                }
            },
            OnKeyUpAction = (_, _) => { OneKeyClaimRewardTask.Instance.KeyUp(); }
        });

        macroDirectory.Children.Add(new HotKeySettingModel(
            "按下快速进出尘歌壶",
            nameof(Config.HotKeyConfig.QuickSereniteaPotHotkey),
            Config.HotKeyConfig.QuickSereniteaPotHotkey,
            Config.HotKeyConfig.QuickSereniteaPotHotkeyType,
            (_, _) =>
            {
                if (TryGetRuntime(out var runtime))
                {
                    QuickSereniteaPotTask.Done(runtime);
                }
            }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止一条龙",
            nameof(Config.HotKeyConfig.OnedragonHotkey),
            Config.HotKeyConfig.OnedragonHotkey,
            Config.HotKeyConfig.OnedragonHotkeyType,
            (_, _) => { ManageSoloTask(OneDragonTask.TaskId); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动七圣召唤",
            nameof(Config.HotKeyConfig.AutoGeniusInvokationHotkey),
            Config.HotKeyConfig.AutoGeniusInvokationHotkey,
            Config.HotKeyConfig.AutoGeniusInvokationHotkeyType,
            (_, _) => { ManageSoloTask(AutoGeniusInvokationTask.TaskId); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动伐木",
            nameof(Config.HotKeyConfig.AutoWoodHotkey),
            Config.HotKeyConfig.AutoWoodHotkey,
            Config.HotKeyConfig.AutoWoodHotkeyType,
            (_, _) => { ManageSoloTask(AutoWoodTask.TaskId); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动战斗",
            nameof(Config.HotKeyConfig.AutoFightHotkey),
            Config.HotKeyConfig.AutoFightHotkey,
            Config.HotKeyConfig.AutoFightHotkeyType,
            (_, _) => { ManageSoloTask(AutoFightEntryTask.TaskId); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动秘境",
            nameof(Config.HotKeyConfig.AutoDomainHotkey),
            Config.HotKeyConfig.AutoDomainHotkey,
            Config.HotKeyConfig.AutoDomainHotkeyType,
            (_, _) => { ManageSoloTask(AutoDomainTask.TaskId); }
        ));
        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动音游",
            nameof(Config.HotKeyConfig.AutoMusicGameHotkey),
            Config.HotKeyConfig.AutoMusicGameHotkey,
            Config.HotKeyConfig.AutoMusicGameHotkeyType,
            (_, _) => { ManageSoloTask(AutoMusicGameTask.TaskId); }
        ));
        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动钓鱼",
            nameof(Config.HotKeyConfig.AutoFishingGameHotkey),
            Config.HotKeyConfig.AutoFishingGameHotkey,
            Config.HotKeyConfig.AutoFishingGameHotkeyType,
            (_, _) => { ManageSoloTask(AutoFishingTask.TaskId); }
        ));
        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动烹饪",
            nameof(Config.HotKeyConfig.AutoCookGameHotkey),
            Config.HotKeyConfig.AutoCookGameHotkey,
            Config.HotKeyConfig.AutoCookGameHotkeyType,
            (_, _) => { ManageSoloTask(AutoCookTask.TaskId); }
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            "快捷点击原神内确认按钮",
            nameof(Config.HotKeyConfig.ClickGenshinConfirmButtonHotkey),
            Config.HotKeyConfig.ClickGenshinConfirmButtonHotkey,
            Config.HotKeyConfig.ClickGenshinConfirmButtonHotkeyType,
            (_, _) =>
            {
                if (!TryGetRuntime(out var runtime))
                {
                    return;
                }

                using var capture = runtime.CaptureToRectArea();
                if (Bv.ClickConfirmButton(capture))
                {
                    TaskControl.Logger.LogInformation("触发快捷点击原神内{Btn}按钮：成功", "确认");
                }
                else
                {
                    TaskControl.Logger.LogInformation("触发快捷点击原神内{Btn}按钮：未找到按钮图片", "确认");
                }
            },
            true
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            "快捷点击原神内取消按钮",
            nameof(Config.HotKeyConfig.ClickGenshinCancelButtonHotkey),
            Config.HotKeyConfig.ClickGenshinCancelButtonHotkey,
            Config.HotKeyConfig.ClickGenshinCancelButtonHotkeyType,
            (_, _) =>
            {
                if (!TryGetRuntime(out var runtime))
                {
                    return;
                }

                using var capture = runtime.CaptureToRectArea();
                if (Bv.ClickCancelButton(capture))
                {
                    TaskControl.Logger.LogInformation("触发快捷点击原神内{Btn}按钮：成功", "取消");
                }
                else
                {
                    TaskControl.Logger.LogInformation("触发快捷点击原神内{Btn}按钮：未找到按钮图片", "取消");
                }
            },
            true
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            "一键战斗宏快捷键",
            nameof(Config.HotKeyConfig.OneKeyFightHotkey),
            Config.HotKeyConfig.OneKeyFightHotkey,
            Config.HotKeyConfig.OneKeyFightHotkeyType,
            null,
            true)
        {
            OnKeyDownAction = (_, _) =>
            {
                // 一键宏未启用时保持原来的静默返回，只有启用时才提示截图器未启动
                if (OneKeyFightTask.IsEnabled() && TryGetRuntime(out var runtime))
                {
                    OneKeyFightTask.Instance.KeyDown(runtime);
                }
            },
            OnKeyUpAction = (_, _) => { OneKeyFightTask.Instance.KeyUp(); }
        });

        devDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止键鼠录制",
            nameof(Config.HotKeyConfig.KeyMouseMacroRecordHotkey),
            Config.HotKeyConfig.KeyMouseMacroRecordHotkey,
            Config.HotKeyConfig.KeyMouseMacroRecordHotkeyType, async (_, _) =>
            {
                var service = App.GetService<KeyMouseRecordService>();
                if (service == null) return;
                if (!service.IsRecording)
                {
                    await Task.Delay(300); // 防止录进快捷键进去
                    await service.StartAsync();
                }
                else
                {
                    service.Stop();
                }
            }
        ));

        devDirectory.Children.Add(new HotKeySettingModel(
            "（开发）模板素材制作",
            nameof(Config.HotKeyConfig.RecognitionTemplateEditorHotkey),
            Config.HotKeyConfig.RecognitionTemplateEditorHotkey,
            Config.HotKeyConfig.RecognitionTemplateEditorHotkeyType,
            (_, _) => { _recognitionTemplateEditorService.OpenAsync().SafeForget(); }
        ));

        devDirectory.Children.Add(new HotKeySettingModel(
            "（开发）获取当前大地图中心点位置",
            nameof(Config.HotKeyConfig.RecBigMapPosHotkey),
            Config.HotKeyConfig.RecBigMapPosHotkey,
            Config.HotKeyConfig.RecBigMapPosHotkeyType,
            (_, _) =>
            {
                if (!TryGetRuntime(out var runtime))
                {
                    return;
                }

                try
                {
                    var p = new TpTask(runtime, Config, runtime.Stopping).GetPositionFromBigMap(MapTypes.Teyvat.ToString());
                    _logger.LogInformation("大地图位置：{Position}", p);
                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                }
            }
        ));

        var pathRecorder = PathRecorder.Instance;
        var pathRecording = false;

        devDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止路径记录器",
            nameof(Config.HotKeyConfig.PathRecorderHotkey),
            Config.HotKeyConfig.PathRecorderHotkey,
            Config.HotKeyConfig.PathRecorderHotkeyType,
            (_, _) =>
            {
                if (pathRecording)
                {
                    pathRecorder.Save();
                }
                else
                {
                    if (!TryGetRuntime(out var runtime))
                    {
                        return;
                    }

                    Task.Run(() => { pathRecorder.Start(runtime); });
                }

                pathRecording = !pathRecording;
            }
        ));

        devDirectory.Children.Add(new HotKeySettingModel(
            "添加路径点",
            nameof(Config.HotKeyConfig.AddWaypointHotkey),
            Config.HotKeyConfig.AddWaypointHotkey,
            Config.HotKeyConfig.AddWaypointHotkeyType,
            (_, _) =>
            {
                if (pathRecording && TryGetRuntime(out var runtime))
                {
                    Task.Run(() => { pathRecorder.AddWaypoint(runtime); });

                }
            }
        ));

        // DEBUG
        if (RuntimeHelper.IsDebug)
        {
            var debugDirectory = new HotKeySettingModel(
                "内部测试"
            );
            HotKeySettingModels.Add(debugDirectory);


            // HotKeySettingModels.Add(new HotKeySettingModel(
            //     "（测试）启动/停止自动追踪",
            //     nameof(Config.HotKeyConfig.AutoTrackHotkey),
            //     Config.HotKeyConfig.AutoTrackHotkey,
            //     Config.HotKeyConfig.AutoTrackHotkeyType,
            //     (_, _) =>
            //     {
            //         // _taskSettingsPageViewModel.OnSwitchAutoTrack();
            //     }
            // ));
            // HotKeySettingModels.Add(new HotKeySettingModel(
            //     "（测试）地图路线录制",
            //     nameof(Config.HotKeyConfig.MapPosRecordHotkey),
            //     Config.HotKeyConfig.MapPosRecordHotkey,
            //     Config.HotKeyConfig.MapPosRecordHotkeyType,
            //     (_, _) =>
            //     {
            //         PathPointRecorder.Instance.Switch();
            //     }));
            // HotKeySettingModels.Add(new HotKeySettingModel(
            //     "（测试）自动寻路",
            //     nameof(Config.HotKeyConfig.AutoTrackPathHotkey),
            //     Config.HotKeyConfig.AutoTrackPathHotkey,
            //     Config.HotKeyConfig.AutoTrackPathHotkeyType,
            //     (_, _) =>
            //     {
            //         // _taskSettingsPageViewModel.OnSwitchAutoTrackPath();
            //     }
            // ));
            debugDirectory.Children.Add(new HotKeySettingModel(
                "（测试）测试",
                nameof(Config.HotKeyConfig.Test1Hotkey),
                Config.HotKeyConfig.Test1Hotkey,
                Config.HotKeyConfig.Test1HotkeyType,
                (_, _) =>
                {
                    if (!TryGetRuntime(out var runtime))
                    {
                        return;
                    }

                    Task.Run(async () =>
                    {
                        try
                        {
                            await new TpTask(runtime, Config, runtime.Stopping).Tp(7001.6416, -569.6846, nameof(MapTypes.Teyvat));
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e.Message);
                        }
                    });

                }
            ));
            debugDirectory.Children.Add(new HotKeySettingModel(
                "（测试）测试2",
                nameof(Config.HotKeyConfig.Test2Hotkey),
                Config.HotKeyConfig.Test2Hotkey,
                Config.HotKeyConfig.Test2HotkeyType,
                (_, _) =>
                {
                    if (!TryGetRuntime(out var runtime))
                    {
                        return;
                    }

                    var myTask = new ChooseFOptionTask(runtime, Config);
                    Task.Run(async () => { await myTask.SingleSelectText("荒坠的圣迹", runtime.Stopping); });

                    // var pName = SystemControl.GetActiveProcessName();
                    // Debug.WriteLine($"当前处于前台的程序：{pName}，原神是否位于前台：{SystemControl.IsGenshinImpactActive()}");
                    // TaskControl.Logger.LogInformation($"当前处于前台的程序：{pName}");
                }
            ));

            debugDirectory.Children.Add(new HotKeySettingModel(
                "（测试）播放内存中的路径",
                nameof(Config.HotKeyConfig.ExecutePathHotkey),
                Config.HotKeyConfig.ExecutePathHotkey,
                Config.HotKeyConfig.ExecutePathHotkeyType,
                (_, _) =>
                {
                    // if (pathRecording)
                    // {
                    //     new TaskRunner(DispatcherTimerOperationEnum.UseCacheImageWithTrigger)
                    //        .FireAndForget(async ct => await new PathExecutor(ct).Pathing(pathRecorder._pathingTask));
                    // }
                }
            ));
        }
    }

    /// <summary>
    /// “悬浮小窗”分组的快捷键与推荐方案（见 Docs/design/guide-window.md 8.1 节）。
    /// 推荐方案只在用户点击“一键填入”时使用，默认不绑定
    /// </summary>
    private static readonly (string Property, HotKey Recommended)[] GuideRecommendedHotKeys =
    [
        (nameof(HotKeyConfig.GuideToggleHotkey), new HotKey(Key.H, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuidePlayPauseHotkey), new HotKey(Key.Space, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideSeekBackwardHotkey), new HotKey(Key.Left, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideSeekForwardHotkey), new HotKey(Key.Right, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideSpeedUpHotkey), new HotKey(Key.Up, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideSpeedDownHotkey), new HotKey(Key.Down, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideSpeedResetHotkey), new HotKey(Key.D0, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideBoostHotkey), new HotKey(Key.None, ModifierKeys.None, MouseButton.XButton2)),
        (nameof(HotKeyConfig.GuideVolumeUpHotkey), new HotKey(Key.PageUp, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideVolumeDownHotkey), new HotKey(Key.PageDown, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideMuteHotkey), new HotKey(Key.M, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuidePrevEpisodeHotkey), new HotKey(Key.OemOpenBrackets, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideNextEpisodeHotkey), new HotKey(Key.OemCloseBrackets, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideOpacityUpHotkey), new HotKey(Key.Up, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)),
        (nameof(HotKeyConfig.GuideOpacityDownHotkey), new HotKey(Key.Down, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)),
        (nameof(HotKeyConfig.GuideClickThroughHotkey), new HotKey(Key.P, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideInteractHotkey), new HotKey(Key.I, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideImmersiveHotkey), new HotKey(Key.F, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideSubtitleHotkey), new HotKey(Key.C, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideOpenClipboardHotkey), new HotKey(Key.V, ModifierKeys.Control | ModifierKeys.Alt)),
        (nameof(HotKeyConfig.GuideRestoreHotkey), new HotKey(Key.F10, ModifierKeys.Control | ModifierKeys.Alt)),
    ];

    /// <summary>
    /// 构建“悬浮小窗”分组。开关类快捷键禁止按住时系统重复触发；快进、倍速、音量允许按住连续调整
    /// </summary>
    private void BuildGuideHotKeys(HotKeySettingModel directory)
    {
        var guide = _guideWindowService;

        void Add(string functionName, string property, Action<object?, KeyPressedEventArgs> action, bool suppressRepeat)
        {
            var value = (string)typeof(HotKeyConfig).GetProperty(property)!.GetValue(Config.HotKeyConfig)!;
            var type = (string)typeof(HotKeyConfig).GetProperty(property + "Type")!.GetValue(Config.HotKeyConfig)!;
            directory.Children.Add(new HotKeySettingModel(functionName, property, value, type, action)
            {
                SuppressRepeat = suppressRepeat,
            });
        }

        Add("显示/隐藏小窗", nameof(HotKeyConfig.GuideToggleHotkey), (_, _) => guide.ToggleVisible(), true);
        Add("播放/暂停", nameof(HotKeyConfig.GuidePlayPauseHotkey), (_, _) => guide.TogglePlay(), true);
        Add("快退", nameof(HotKeyConfig.GuideSeekBackwardHotkey), (_, _) => guide.SeekBy(-1), false);
        Add("快进", nameof(HotKeyConfig.GuideSeekForwardHotkey), (_, _) => guide.SeekBy(1), false);
        Add("倍速增加", nameof(HotKeyConfig.GuideSpeedUpHotkey), (_, _) => guide.ChangeRate(1), false);
        Add("倍速降低", nameof(HotKeyConfig.GuideSpeedDownHotkey), (_, _) => guide.ChangeRate(-1), false);
        Add("倍速重置为 1x", nameof(HotKeyConfig.GuideSpeedResetHotkey), (_, _) => guide.SetRate(1), true);

        // 按住临时加速需要松开事件，只能使用键鼠监听（单键或鼠标侧键）
        directory.Children.Add(new HotKeySettingModel(
            "按住临时加速",
            nameof(HotKeyConfig.GuideBoostHotkey),
            Config.HotKeyConfig.GuideBoostHotkey,
            Config.HotKeyConfig.GuideBoostHotkeyType,
            null,
            true)
        {
            OnKeyDownAction = (_, _) => guide.BeginBoost(),
            OnKeyUpAction = (_, _) => guide.EndBoost(),
        });

        Add("音量增加", nameof(HotKeyConfig.GuideVolumeUpHotkey), (_, _) => guide.ChangeVolume(1), false);
        Add("音量降低", nameof(HotKeyConfig.GuideVolumeDownHotkey), (_, _) => guide.ChangeVolume(-1), false);
        Add("静音", nameof(HotKeyConfig.GuideMuteHotkey), (_, _) => guide.ToggleMute(), true);
        Add("上一集", nameof(HotKeyConfig.GuidePrevEpisodeHotkey), (_, _) => guide.SwitchEpisode(-1), true);
        Add("下一集", nameof(HotKeyConfig.GuideNextEpisodeHotkey), (_, _) => guide.SwitchEpisode(1), true);
        Add("不透明度增加", nameof(HotKeyConfig.GuideOpacityUpHotkey), (_, _) => guide.ChangeOpacity(1), false);
        Add("不透明度降低", nameof(HotKeyConfig.GuideOpacityDownHotkey), (_, _) => guide.ChangeOpacity(-1), false);
        Add("鼠标穿透开关", nameof(HotKeyConfig.GuideClickThroughHotkey), (_, _) => guide.ToggleClickThrough(), true);
        Add("交互模式（临时操作网页）", nameof(HotKeyConfig.GuideInteractHotkey), (_, _) => guide.ToggleInteraction(), true);
        Add("沉浸模式", nameof(HotKeyConfig.GuideImmersiveHotkey), (_, _) => guide.ToggleImmersive(), true);
        Add("字幕浮窗开关", nameof(HotKeyConfig.GuideSubtitleHotkey), (_, _) => guide.ToggleSubtitle(), true);
        Add("用小窗打开剪贴板链接", nameof(HotKeyConfig.GuideOpenClipboardHotkey), (_, _) => guide.OpenFromClipboard(), true);
        Add("紧急恢复小窗", nameof(HotKeyConfig.GuideRestoreHotkey), (_, _) => guide.RestoreEmergency(), true);
    }

    /// <summary>
    /// 一键填入“悬浮小窗”分组的推荐快捷键：只填当前为空的项，跳过已被其他功能占用的组合，不清除用户已有的绑定
    /// </summary>
    /// <returns>填入的数量、因冲突或注册失败跳过的数量</returns>
    public (int Filled, int Skipped) ApplyGuideRecommendedHotKeys()
    {
        var filled = 0;
        var skipped = 0;
        var all = GetAllNonDirectoryHotkey(HotKeySettingModels);
        foreach (var (property, recommended) in GuideRecommendedHotKeys)
        {
            var model = GuideHotKeyDirectory.Children.FirstOrDefault(child => child.ConfigPropertyName == property);
            if (model is null || !model.HotKey.IsEmpty)
            {
                continue;
            }

            // 侧键只能用键鼠监听，组合键只能用全局热键；用户改过类型时不覆盖
            var expectedType = recommended.MouseButton != MouseButton.Left ? HotKeyTypeEnum.KeyboardMonitor : HotKeyTypeEnum.GlobalRegister;
            if (model.HotKeyType != expectedType
                || all.Any(other => !ReferenceEquals(other, model)
                                    && HotKeySettingModel.AreConflicting(other.HotKey, other.HotKeyType, recommended, expectedType)))
            {
                skipped++;
                continue;
            }

            model.HotKey = recommended;
            if (model.HotKey.IsEmpty)
            {
                // 被其他程序占用，注册失败后已被清空
                skipped++;
                continue;
            }

            filled++;
        }

        return (filled, skipped);
    }

    /// <summary>
    /// 读取当前运行环境。截图器未启动时提示用户并返回 false，热键直接放弃本次执行。
    /// 运行环境只在本次热键回调内使用，不缓存
    /// </summary>
    /// <param name="runtime">当前运行环境；返回 false 时为 null</param>
    /// <returns>存在运行环境时返回 true</returns>
    private bool TryGetRuntime([NotNullWhen(true)] out GameRuntime? runtime)
    {
        runtime = _runtimeService.Current;
        if (runtime is not null)
        {
            return true;
        }

        // 长按类热键会在循环里反复回调，限制提示频率，避免刷屏
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastNoRuntimeToastTick) >= NoRuntimeToastIntervalMs)
        {
            Interlocked.Exchange(ref _lastNoRuntimeToastTick, now);
            // 长按类热键在后台线程回调，提示统一切回 UI 线程
            UIDispatcherHelper.BeginInvoke(() => Toast.Warning("请先启动"));
        }

        return false;
    }

    /// <summary>
    /// 独立任务专用快捷键与页面按钮共用同一多实例管理入口。
    /// </summary>
    private void ManageSoloTask(string taskId)
    {
        UIDispatcherHelper.BeginInvoke(() =>
        {
            if (_taskSettingsPageViewModel.ManageSoloTaskCommand.CanExecute(taskId))
            {
                _taskSettingsPageViewModel.ManageSoloTaskCommand.Execute(taskId);
            }
        });
    }

    private string ToChinese(bool enabled)
    {
        return enabled.ToChinese();
    }
}
