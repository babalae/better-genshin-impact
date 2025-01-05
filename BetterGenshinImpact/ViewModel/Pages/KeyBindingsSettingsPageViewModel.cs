using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Wpf.Ui.Controls;
using BetterGenshinImpact.Genshin.Settings;
using CommunityToolkit.Mvvm.Input;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class KeyBindingsSettingsPageViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<KeyBindingsSettingsPageViewModel> _logger;

    /// <summary>
    /// 配置文件
    /// </summary>
    private readonly KeyBindingsConfig _config;

    [ObservableProperty]
    private ObservableCollection<KeyBindingSettingModel> _keyBindingSettingModels = [];

    public void OnNavigatedFrom()
    {
    }

    public void OnNavigatedTo()
    {
    }

    public KeyBindingsSettingsPageViewModel(IConfigService configService, ILogger<KeyBindingsSettingsPageViewModel> logger)
    {
        _logger = logger;

        // 获取本模块的配置文件
        _config = configService.Get().KeyBindingsConfig;

        BuildKeyBindingsList();

        var list = GetAllNonDirectoryKeyBinding(KeyBindingSettingModels);
        foreach (var keyConfig in list)
        {
            keyConfig.PropertyChanged += (sender, e) =>
            {
                if (sender is KeyBindingSettingModel model)
                {
                    // 使用反射更新配置文件
                    if (e.PropertyName == nameof(model.KeyValue))
                    {
                        Debug.WriteLine($"按键绑定 \"{model.ActionName}\" 变更为 {model.KeyValue}");

                        var pi = _config.GetType().GetProperty(model.ConfigPropertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (pi != null && pi.CanWrite)
                        {
                            pi.SetValue(_config, model.KeyValue, null);
                        }
                    }
                }
            };
        }
    }


    public static List<KeyBindingSettingModel> GetAllNonDirectoryKeyBinding(IEnumerable<KeyBindingSettingModel> modelList)
    {
        var list = new List<KeyBindingSettingModel>();
        foreach (var keyBindingSettingModel in modelList)
        {
            if (!keyBindingSettingModel.IsDirectory)
            {
                list.Add(keyBindingSettingModel);
            }

            list.AddRange(GetAllNonDirectoryChildren(keyBindingSettingModel));
        }

        return list;
    }

    public static List<KeyBindingSettingModel> GetAllNonDirectoryChildren(KeyBindingSettingModel model)
    {
        var result = new List<KeyBindingSettingModel>();

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

    /// <summary>
    /// 构造按键绑定列表
    /// </summary>
    private void BuildKeyBindingsList()
    {
        // 动作按键
        var actionDirectory = new KeyBindingSettingModel("动作");

        // 菜单按键
        var menuDirectory = new KeyBindingSettingModel("菜单");

        // 添加动作按键的二级菜单
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "向前移动",
                    nameof(_config.MoveForward),
                    _config.MoveForward
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "向后移动",
                    nameof(_config.MoveBackward),
                    _config.MoveBackward
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "向左移动",
                    nameof(_config.MoveLeft),
                    _config.MoveLeft
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "向右移动",
                    nameof(_config.MoveRight),
                    _config.MoveRight
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换走/跑；特定操作模式下向下移动",
                    nameof(_config.SwitchToWalkOrRun),
                    _config.SwitchToWalkOrRun
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "普通攻击",
                    nameof(_config.NormalAttack),
                    _config.NormalAttack
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "元素战技",
                    nameof(_config.ElementalSkill),
                    _config.ElementalSkill
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "元素爆发",
                    nameof(_config.ElementalBurst),
                    _config.ElementalBurst
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "冲刺（键盘）",
                    nameof(_config.SprintKeyboard),
                    _config.SprintKeyboard
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "冲刺（鼠标）",
                    nameof(_config.SprintMouse),
                    _config.SprintMouse
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换瞄准模式",
                    nameof(_config.SwitchAimingMode),
                    _config.SwitchAimingMode
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "跳跃；特定操作模式下向上移动",
                    nameof(_config.Jump),
                    _config.Jump
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "落下",
                    nameof(_config.Drop),
                    _config.Drop
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "拾取/交互（自动拾取由AutoPick模块管理）",
                    nameof(_config.PickUpOrInteract),
                    _config.PickUpOrInteract
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "快捷使用小道具",
                    nameof(_config.QuickUseGadget),
                    _config.QuickUseGadget
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "特定玩法内交互操作",
                    nameof(_config.InteractionInSomeMode),
                    _config.InteractionInSomeMode
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "开启任务追踪",
                    nameof(_config.QuestNavigation),
                    _config.QuestNavigation
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "中断挑战",
                    nameof(_config.AbandonChallenge),
                    _config.AbandonChallenge
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换小队角色1",
                    nameof(_config.SwitchMember1),
                    _config.SwitchMember1
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换小队角色2",
                    nameof(_config.SwitchMember2),
                    _config.SwitchMember2
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换小队角色3",
                    nameof(_config.SwitchMember3),
                    _config.SwitchMember3
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换小队角色4",
                    nameof(_config.SwitchMember4),
                    _config.SwitchMember4
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换小队角色5",
                    nameof(_config.SwitchMember5),
                    _config.SwitchMember5
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "呼出快捷轮盘",
                    nameof(_config.ShortcutWheel),
                    _config.ShortcutWheel
                ));

        // 添加菜单按键的二级菜单
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开背包",
                    nameof(_config.OpenInventory),
                    _config.OpenInventory
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开角色界面",
                    nameof(_config.OpenCharacterScreen),
                    _config.OpenCharacterScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开地图",
                    nameof(_config.OpenMap),
                    _config.OpenMap
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开派蒙界面",
                    nameof(_config.OpenPaimonMenu),
                    _config.OpenPaimonMenu
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开冒险之证界面",
                    nameof(_config.OpenAdventurerHandbook),
                    _config.OpenAdventurerHandbook
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开多人游戏界面",
                    nameof(_config.OpenCoOpScreen),
                    _config.OpenCoOpScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开祈愿界面",
                    nameof(_config.OpenWishScreen),
                    _config.OpenWishScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开纪行界面",
                    nameof(_config.OpenBattlePassScreen),
                    _config.OpenBattlePassScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开活动面板",
                    nameof(_config.OpenTheEventsMenu),
                    _config.OpenTheEventsMenu
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开玩法系统界面（尘歌壶内猫尾酒馆内）",
                    nameof(_config.OpenTheSettingsMenu),
                    _config.OpenTheSettingsMenu
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开摆设界面（尘歌壶内）",
                    nameof(_config.OpenTheFurnishingScreen),
                    _config.OpenTheFurnishingScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开星之归还（条件符合期间生效）",
                    nameof(_config.OpenStellarReunion),
                    _config.OpenStellarReunion
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "开关任务菜单",
                    nameof(_config.OpenQuestMenu),
                    _config.OpenQuestMenu
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开通知详情",
                    nameof(_config.OpenNotificationDetails),
                    _config.OpenNotificationDetails
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开聊天界面",
                    nameof(_config.OpenChatScreen),
                    _config.OpenChatScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开特殊环境说明",
                    nameof(_config.OpenSpecialEnvironmentInformation),
                    _config.OpenSpecialEnvironmentInformation
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "查看教程详情",
                    nameof(_config.CheckTutorialDetails),
                    _config.CheckTutorialDetails
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "长按打开元素视野",
                    nameof(_config.ElementalSight),
                    _config.ElementalSight
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "呼出鼠标",
                    nameof(_config.ShowCursor),
                    _config.ShowCursor
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开队伍配置界面",
                    nameof(_config.OpenPartySetupScreen),
                    _config.OpenPartySetupScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开好友界面",
                    nameof(_config.OpenFriendsScreen),
                    _config.OpenFriendsScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "隐藏主界面",
                    nameof(_config.HideUI),
                    _config.HideUI
                ));

        KeyBindingSettingModels.Add(actionDirectory);
        KeyBindingSettingModels.Add(menuDirectory);
    }

    /// <summary>
    /// 从注册表中读取按键
    /// </summary>
    [RelayCommand]
    private void FetchFromRegistry()
    {
        // 读取注册表
        SettingsContainer settings = new();
        settings.FromReg();

        var keySetting = settings.OverrideController?.KeyboardMap?.ActionElementMap;
        if (keySetting != null)
        {
            foreach (var item in keySetting)
            {
                if (item.ElementIdentifierId is ElementIdentifierId keyId)
                {
                    // 跳过无法识别的按键
                    if (keyId.ToName() == "Unknown" || keyId.ToName() == "None")
                    {
                        continue;
                    }
                    var key = KeyIdConverter.FromVK(keyId.ToVK());
                    switch (item.ActionId)
                    {
                        case ActionId.MoveForwardShow:
                            _config.MoveForward = key;
                            break;
                        case ActionId.MoveBackShow:
                            _config.MoveBackward = key;
                            break;
                        case ActionId.MoveLeftShow:
                            _config.MoveLeft = key;
                            break;
                        case ActionId.MoveRightShow:
                            _config.MoveRight = key;
                            break;
                        case ActionId.WalkRun:
                            _config.SwitchToWalkOrRun = key;
                            break;
                        case ActionId.Attack:
                            _config.NormalAttack = key;
                            break;
                        case ActionId.ElementalSkill:
                            _config.ElementalSkill = key;
                            break;
                        case ActionId.ElementalBurst:
                            _config.ElementalBurst = key;
                            break;
                        case ActionId.Sprint:
                            if (key >= KeyId.MouseLeftButton && key <= KeyId.MouseSideButton2)
                            {
                                _config.SprintMouse = key;
                            }
                            else
                            {
                                _config.SprintKeyboard = key;
                            }
                            break;
                        case ActionId.AimMode:
                            _config.SwitchAimingMode = key;
                            break;
                        case ActionId.Jump:
                            _config.Jump = key;
                            break;
                        case ActionId.CancelClimb:
                            _config.Drop = key;
                            break;
                        // TODO: 拾取&交互可能不正确
                        case ActionId.UseTalk:
                            _config.PickUpOrInteract = key;
                            break;
                        case ActionId.Gadget:
                            _config.QuickUseGadget = key;
                            break;
                        case ActionId.InteractionSomeModes:
                            _config.InteractionInSomeMode = key;
                            break;
                        case ActionId.Navigation:
                            _config.QuestNavigation = key;
                            break;
                        case ActionId.AbandonChallenge:
                            _config.AbandonChallenge = key;
                            break;
                        case ActionId.Char1:
                            _config.SwitchMember1 = key;
                            break;
                        case ActionId.Char2:
                            _config.SwitchMember2 = key;
                            break;
                        case ActionId.Char3:
                            _config.SwitchMember3 = key;
                            break;
                        case ActionId.Char4:
                            _config.SwitchMember4 = key;
                            break;
                        case ActionId.Char5:
                            _config.SwitchMember5 = key;
                            break;
                        case ActionId.QuickWheel:
                            _config.ShortcutWheel = key;
                            break;
                        case ActionId.Inventory:
                            _config.OpenInventory = key;
                            break;
                        case ActionId.CharacterList:
                            _config.OpenCharacterScreen = key;
                            break;
                        case ActionId.Map:
                            _config.OpenMap = key;
                            break;
                        // TODO: 派蒙界面可能不正确
                        case ActionId.MenuGamepad:
                            _config.OpenPaimonMenu = key;
                            break;
                        case ActionId.AdventurerHandbook:
                            _config.OpenAdventurerHandbook = key;
                            break;
                        case ActionId.CoOp:
                            _config.OpenCoOpScreen = key;
                            break;
                        case ActionId.Wish:
                            _config.OpenWishScreen = key;
                            break;
                        case ActionId.BattlePass:
                            _config.OpenBattlePassScreen = key;
                            break;
                        case ActionId.Events:
                            _config.OpenTheEventsMenu = key;
                            break;
                        case ActionId.PotTasks:
                            _config.OpenTheSettingsMenu = key;
                            break;
                        case ActionId.PotEdit:
                            _config.OpenTheFurnishingScreen = key;
                            break;
                        // TODO: 星之归还未找到
                        case ActionId.QuestList:
                            _config.OpenQuestMenu = key;
                            break;
                        case ActionId.NotificationDetails:
                            _config.OpenNotificationDetails = key;
                            break;
                        case ActionId.Chat:
                            _config.OpenChatScreen = key;
                            break;
                        case ActionId.EnvironmentInfo:
                            _config.OpenSpecialEnvironmentInformation = key;
                            break;
                        case ActionId.Tutorial:
                            _config.CheckTutorialDetails = key;
                            break;
                        case ActionId.ElementalSight:
                            _config.ElementalSight = key;
                            break;
                        case ActionId.ShowCursor:
                            _config.ShowCursor = key;
                            break;
                        case ActionId.PartySetup:
                            _config.OpenPartySetupScreen = key;
                            break;
                        case ActionId.Friends:
                            _config.OpenFriendsScreen = key;
                            break;
                         // TODO: 隐藏主界面未找到
                        default:
                            break;
                    }
                }
            }

            // 重新加载按键绑定列表
            KeyBindingSettingModels.Clear();
            BuildKeyBindingsList();
            var list = GetAllNonDirectoryKeyBinding(KeyBindingSettingModels);
            foreach (var keyConfig in list)
            {
                keyConfig.PropertyChanged += (sender, e) =>
                {
                    if (sender is KeyBindingSettingModel model)
                    {
                        // 使用反射更新配置文件
                        if (e.PropertyName == nameof(model.KeyValue))
                        {
                            Debug.WriteLine($"按键绑定 \"{model.ActionName}\" 变更为 {model.KeyValue}");

                            var pi = _config.GetType().GetProperty(model.ConfigPropertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            if (pi != null && pi.CanWrite)
                            {
                                pi.SetValue(_config, model.KeyValue, null);
                            }
                        }
                    }
                };
            }
        }
    }

}
