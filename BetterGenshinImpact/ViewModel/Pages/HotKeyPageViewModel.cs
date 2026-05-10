using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Macro;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickBuy;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Vanara.PInvoke;
using HotKeySettingModel = BetterGenshinImpact.Model.HotKeySettingModel;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HotKeyPageViewModel : ObservableObject, IViewModel
{
    private readonly ILogger<HotKeyPageViewModel> _logger;
    private readonly TaskSettingsPageViewModel _taskSettingsPageViewModel;
    private readonly Dictionary<string, HotKey> _acceptedHotKeys = [];
    private readonly HashSet<string> _rollingBackHotKeyProperties = [];
    public AllConfig Config { get; set; }

    [ObservableProperty]
    private ObservableCollection<HotKeySettingModel> _hotKeySettingModels = [];

    public HotKeyPageViewModel(IConfigService configService, ILogger<HotKeyPageViewModel> logger, TaskSettingsPageViewModel taskSettingsPageViewModel)
    {
        _logger = logger;
        _taskSettingsPageViewModel = taskSettingsPageViewModel;
        // 获取配置
        Config = configService.Get();

        // 构建快捷键配置列表
        BuildHotKeySettingModelList();

        var list = GetAllNonDirectoryHotkey(HotKeySettingModels);
        foreach (var hotKeyConfig in list)
        {
            _acceptedHotKeys[hotKeyConfig.ConfigPropertyName] = hotKeyConfig.HotKey;
            hotKeyConfig.RegisterHotKey();
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
                    model.RegisterHotKey();
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

        var message = $"{model.FunctionName}使用{hotKeyId.ToName()}键会与原神键位冲突：\n\n"
                      + string.Join("\n", conflictLines)
                      + "\n\n会导致游戏内动作和 BetterGI 功能同时触发，建议更换为其他按键。是否继续使用？";
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (model.HotKey != newHotKey)
            {
                return;
            }

            var result = ThemedMessageBox.Warning(message, "快捷键冲突提醒", MessageBoxButton.OKCancel, MessageBoxResult.Cancel);
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
            nameof(KeyBindingsConfig.MoveForward) => "向前移动",
            nameof(KeyBindingsConfig.MoveBackward) => "向后移动",
            nameof(KeyBindingsConfig.MoveLeft) => "向左移动",
            nameof(KeyBindingsConfig.MoveRight) => "向右移动",
            nameof(KeyBindingsConfig.SwitchToWalkOrRun) => "切换走/跑；特定操作模式下向下移动",
            nameof(KeyBindingsConfig.NormalAttack) => "普通攻击",
            nameof(KeyBindingsConfig.ElementalSkill) => "元素战技",
            nameof(KeyBindingsConfig.ElementalBurst) => "元素爆发",
            nameof(KeyBindingsConfig.SprintKeyboard) => "冲刺（键盘）",
            nameof(KeyBindingsConfig.SprintMouse) => "冲刺（鼠标）",
            nameof(KeyBindingsConfig.SwitchAimingMode) => "切换瞄准模式",
            nameof(KeyBindingsConfig.Jump) => "跳跃；特定操作模式下向上移动",
            nameof(KeyBindingsConfig.Drop) => "落下",
            nameof(KeyBindingsConfig.PickUpOrInteract) => "拾取/交互（自动拾取由AutoPick模块管理）",
            nameof(KeyBindingsConfig.QuickUseGadget) => "快捷使用小道具",
            nameof(KeyBindingsConfig.InteractionInSomeMode) => "特定玩法内交互操作",
            nameof(KeyBindingsConfig.QuestNavigation) => "开启任务追踪",
            nameof(KeyBindingsConfig.AbandonChallenge) => "中断挑战",
            nameof(KeyBindingsConfig.SwitchMember1) => "切换小队角色1",
            nameof(KeyBindingsConfig.SwitchMember2) => "切换小队角色2",
            nameof(KeyBindingsConfig.SwitchMember3) => "切换小队角色3",
            nameof(KeyBindingsConfig.SwitchMember4) => "切换小队角色4",
            nameof(KeyBindingsConfig.SwitchMember5) => "切换小队角色5",
            nameof(KeyBindingsConfig.ShortcutWheel) => "呼出快捷轮盘",
            nameof(KeyBindingsConfig.OpenInventory) => "打开背包",
            nameof(KeyBindingsConfig.OpenCharacterScreen) => "打开角色界面",
            nameof(KeyBindingsConfig.OpenMap) => "打开地图",
            nameof(KeyBindingsConfig.OpenPaimonMenu) => "打开派蒙界面",
            nameof(KeyBindingsConfig.OpenAdventurerHandbook) => "打开冒险之证界面",
            nameof(KeyBindingsConfig.OpenCoOpScreen) => "打开多人游戏界面",
            nameof(KeyBindingsConfig.OpenWishScreen) => "打开祈愿界面",
            nameof(KeyBindingsConfig.OpenBattlePassScreen) => "打开纪行界面",
            nameof(KeyBindingsConfig.OpenTheEventsMenu) => "打开活动面板",
            nameof(KeyBindingsConfig.OpenTheSettingsMenu) => "打开玩法系统界面（尘歌壶内猫尾酒馆内）",
            nameof(KeyBindingsConfig.OpenTheFurnishingScreen) => "打开摆设界面（尘歌壶内）",
            nameof(KeyBindingsConfig.OpenStellarReunion) => "打开星之归还（条件符合期间生效）",
            nameof(KeyBindingsConfig.OpenQuestMenu) => "开关任务菜单",
            nameof(KeyBindingsConfig.OpenNotificationDetails) => "打开通知详情",
            nameof(KeyBindingsConfig.OpenChatScreen) => "打开聊天界面",
            nameof(KeyBindingsConfig.OpenSpecialEnvironmentInformation) => "打开特殊环境说明",
            nameof(KeyBindingsConfig.CheckTutorialDetails) => "查看教程详情",
            nameof(KeyBindingsConfig.ElementalSight) => "长按打开元素视野",
            nameof(KeyBindingsConfig.ShowCursor) => "呼出鼠标",
            nameof(KeyBindingsConfig.OpenPartySetupScreen) => "打开队伍配置界面",
            nameof(KeyBindingsConfig.OpenFriendsScreen) => "打开好友界面",
            nameof(KeyBindingsConfig.HideUI) => "隐藏主界面",
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
                CancellationContext.Instance.ManualCancel();
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
            (_, _) => { TaskTriggerDispatcher.Instance().TakeScreenshot(); }
        );
        systemDirectory.Children.Add(takeScreenshotHotKeySettingModel);

        systemDirectory.Children.Add(new HotKeySettingModel(
            "日志与状态窗口展示开关",
            nameof(Config.HotKeyConfig.LogBoxDisplayHotkey),
            Config.HotKeyConfig.LogBoxDisplayHotkey,
            Config.HotKeyConfig.LogBoxDisplayHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.MaskWindowConfig.ShowLogBox = !TaskContext.Instance().Config.MaskWindowConfig.ShowLogBox;
                // 与状态窗口同步
                TaskContext.Instance().Config.MaskWindowConfig.ShowStatus = TaskContext.Instance().Config.MaskWindowConfig.ShowLogBox;
            }
        ));

        var autoPickEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动拾取开关",
            nameof(Config.HotKeyConfig.AutoPickEnabledHotkey),
            Config.HotKeyConfig.AutoPickEnabledHotkey,
            Config.HotKeyConfig.AutoPickEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoPickConfig.Enabled = !TaskContext.Instance().Config.AutoPickConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动拾取", ToChinese(TaskContext.Instance().Config.AutoPickConfig.Enabled));
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
                TaskContext.Instance().Config.AutoSkipConfig.Enabled = !TaskContext.Instance().Config.AutoSkipConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动剧情", ToChinese(TaskContext.Instance().Config.AutoSkipConfig.Enabled));
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
                TaskContext.Instance().Config.AutoSkipConfig.AutoHangoutEventEnabled = !TaskContext.Instance().Config.AutoSkipConfig.AutoHangoutEventEnabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动邀约", ToChinese(TaskContext.Instance().Config.AutoSkipConfig.AutoHangoutEventEnabled));
            }
        ));

        var autoFishingEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动钓鱼开关",
            nameof(Config.HotKeyConfig.AutoFishingEnabledHotkey),
            Config.HotKeyConfig.AutoFishingEnabledHotkey,
            Config.HotKeyConfig.AutoFishingEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoFishingConfig.Enabled = !TaskContext.Instance().Config.AutoFishingConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动钓鱼", ToChinese(TaskContext.Instance().Config.AutoFishingConfig.Enabled));
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
                TaskContext.Instance().Config.QuickTeleportConfig.Enabled = !TaskContext.Instance().Config.QuickTeleportConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "快速传送", ToChinese(TaskContext.Instance().Config.QuickTeleportConfig.Enabled));
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
                TaskContext.Instance().Config.SkillCdConfig.Enabled = !TaskContext.Instance().Config.SkillCdConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "冷却提示", ToChinese(TaskContext.Instance().Config.SkillCdConfig.Enabled));
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
                TaskContext.Instance().Config.MapMaskConfig.Enabled = !TaskContext.Instance().Config.MapMaskConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "地图遮罩", ToChinese(TaskContext.Instance().Config.MapMaskConfig.Enabled));
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
            (_, _) => { QuickEnhanceArtifactMacro.Done(); },
            true
        );
        macroDirectory.Children.Add(enhanceArtifactHotKeySettingModel);

        macroDirectory.Children.Add(new HotKeySettingModel(
            "按下快速购买商店物品",
            nameof(Config.HotKeyConfig.QuickBuyHotkey),
            Config.HotKeyConfig.QuickBuyHotkey,
            Config.HotKeyConfig.QuickBuyHotkeyType,
            (_, _) => { QuickBuyTask.Done(); },
            true
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            "按下快速进出尘歌壶",
            nameof(Config.HotKeyConfig.QuickSereniteaPotHotkey),
            Config.HotKeyConfig.QuickSereniteaPotHotkey,
            Config.HotKeyConfig.QuickSereniteaPotHotkeyType,
            (_, _) => { QuickSereniteaPotTask.Done(); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止一条龙",
            nameof(Config.HotKeyConfig.OnedragonHotkey),
            Config.HotKeyConfig.OnedragonHotkey,
            Config.HotKeyConfig.OnedragonHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SOneDragonFlowCommand); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动七圣召唤",
            nameof(Config.HotKeyConfig.AutoGeniusInvokationHotkey),
            Config.HotKeyConfig.AutoGeniusInvokationHotkey,
            Config.HotKeyConfig.AutoGeniusInvokationHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoGeniusInvokationCommand); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动伐木",
            nameof(Config.HotKeyConfig.AutoWoodHotkey),
            Config.HotKeyConfig.AutoWoodHotkey,
            Config.HotKeyConfig.AutoWoodHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoWoodCommand); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动战斗",
            nameof(Config.HotKeyConfig.AutoFightHotkey),
            Config.HotKeyConfig.AutoFightHotkey,
            Config.HotKeyConfig.AutoFightHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoFightCommand); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动秘境",
            nameof(Config.HotKeyConfig.AutoDomainHotkey),
            Config.HotKeyConfig.AutoDomainHotkey,
            Config.HotKeyConfig.AutoDomainHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoDomainCommand); }
        ));
        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动音游",
            nameof(Config.HotKeyConfig.AutoMusicGameHotkey),
            Config.HotKeyConfig.AutoMusicGameHotkey,
            Config.HotKeyConfig.AutoMusicGameHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoMusicGameCommand); }
        ));
        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动钓鱼",
            nameof(Config.HotKeyConfig.AutoFishingGameHotkey),
            Config.HotKeyConfig.AutoFishingGameHotkey,
            Config.HotKeyConfig.AutoFishingGameHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoFishingCommand); }
        ));
        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动烹饪",
            nameof(Config.HotKeyConfig.AutoCookGameHotkey),
            Config.HotKeyConfig.AutoCookGameHotkey,
            Config.HotKeyConfig.AutoCookGameHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoCookCommand); }
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            "快捷点击原神内确认按钮",
            nameof(Config.HotKeyConfig.ClickGenshinConfirmButtonHotkey),
            Config.HotKeyConfig.ClickGenshinConfirmButtonHotkey,
            Config.HotKeyConfig.ClickGenshinConfirmButtonHotkeyType,
            (_, _) =>
            {
                if (Bv.ClickConfirmButton(TaskControl.CaptureToRectArea()))
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
                if (Bv.ClickCancelButton(TaskControl.CaptureToRectArea()))
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
            OnKeyDownAction = (_, _) => { OneKeyFightTask.Instance.KeyDown(); },
            OnKeyUpAction = (_, _) => { OneKeyFightTask.Instance.KeyUp(); }
        });

        devDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止键鼠录制",
            nameof(Config.HotKeyConfig.KeyMouseMacroRecordHotkey),
            Config.HotKeyConfig.KeyMouseMacroRecordHotkey,
            Config.HotKeyConfig.KeyMouseMacroRecordHotkeyType, async (_, _) =>
            {
                var vm = App.GetService<KeyMouseRecordPageViewModel>();
                if (vm == null)
                {
                    _logger.LogError("无法找到 KeyMouseRecordPageViewModel 单例对象！");
                    return;
                }

                if (GlobalKeyMouseRecord.Instance.Status == KeyMouseRecorderStatus.Stop)
                {
                    Thread.Sleep(300); // 防止录进快捷键进去
                    await vm.OnStartRecord();
                }
                else
                {
                    vm.OnStopRecord();
                }
            }
        ));

        devDirectory.Children.Add(new HotKeySettingModel(
            "（开发）获取当前大地图中心点位置",
            nameof(Config.HotKeyConfig.RecBigMapPosHotkey),
            Config.HotKeyConfig.RecBigMapPosHotkey,
            Config.HotKeyConfig.RecBigMapPosHotkeyType,
            (_, _) =>
            {
                var p = new TpTask(CancellationToken.None).GetPositionFromBigMap(MapTypes.Teyvat.ToString());
                _logger.LogInformation("大地图位置：{Position}", p);
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
                    Task.Run(() => { pathRecorder.Start(); });
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
                if (pathRecording)
                {
                    Task.Run(() => { pathRecorder.AddWaypoint(); });

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
                    Task.Run(async () => { await new AutoArtifactSalvageTask(new AutoArtifactSalvageTaskParam(star: 4, null, null, null, null)).Start(new CancellationToken()); });

                }
            ));
            debugDirectory.Children.Add(new HotKeySettingModel(
                "（测试）测试2",
                nameof(Config.HotKeyConfig.Test2Hotkey),
                Config.HotKeyConfig.Test2Hotkey,
                Config.HotKeyConfig.Test2HotkeyType,
                (_, _) =>
                {
                    SetTimeTask setTimeTask = new SetTimeTask();
                    Task.Run(async () => { await setTimeTask.Start(12, 05, new CancellationToken()); });

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
                    //        .FireAndForget(async () => await new PathExecutor(CancellationContext.Instance.Cts).Pathing(pathRecorder._pathingTask));
                    // }
                }
            ));
        }
    }

    private void SwitchSoloTask(IAsyncRelayCommand asyncRelayCommand)
    {
        if (asyncRelayCommand.IsRunning)
        {
            CancellationContext.Instance.Cancel();
        }
        else
        {
            asyncRelayCommand.Execute(null);
        }
    }

    private string ToChinese(bool enabled)
    {
        return enabled.ToChinese();
    }
}
