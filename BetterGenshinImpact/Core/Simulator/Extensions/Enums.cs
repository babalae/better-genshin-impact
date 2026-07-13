using System;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.Core.Simulator.Extensions;

/// <summary>
/// 模拟按键类型
/// </summary>
public enum KeyType
{

    /// <summary>
    /// 单击按键（KeyPress）
    /// </summary>
    KeyPress,

    /// <summary>
    /// 按住按键（KeyDown）
    /// </summary>
    KeyDown,

    /// <summary>
    /// 释放按键（KeyUp）
    /// </summary>
    KeyUp,

    /// <summary>
    /// 长按1s
    /// </summary>
    Hold,

}

/// <summary>
/// 原神按键动作
/// </summary>
public enum GIActions
{
    /// <summary>
    /// 向前移动
    /// </summary>
    MoveForward,

    /// <summary>
    /// 向后移动
    /// </summary>
    MoveBackward,

    /// <summary>
    /// 向左移动
    /// </summary>
    MoveLeft,

    /// <summary>
    /// 向右移动
    /// </summary>
    MoveRight,

    /// <summary>
    /// 切换走/跑；特定操作模式下向下移动
    /// </summary>
    SwitchToWalkOrRun,

    /// <summary>
    /// 普通攻击
    /// </summary>
    NormalAttack,

    /// <summary>
    /// 元素战技
    /// </summary>
    ElementalSkill,

    /// <summary>
    /// 元素爆发
    /// </summary>
    ElementalBurst,

    /// <summary>
    /// 冲刺（键盘）
    /// </summary>
    SprintKeyboard,

    /// <summary>
    /// 冲刺（鼠标）
    /// </summary>
    SprintMouse,

    /// <summary>
    /// 切换瞄准模式
    /// </summary>
    SwitchAimingMode,

    /// <summary>
    /// 跳跃；特定操作模式下向上移动
    /// </summary>
    Jump,

    /// <summary>
    /// 落下
    /// </summary>
    Drop,

    /// <summary>
    /// 拾取/交互（自动拾取由AutoPick模块管理）
    /// </summary>
    PickUpOrInteract,

    /// <summary>
    /// 快捷使用小道具
    /// </summary>
    QuickUseGadget,

    /// <summary>
    /// 特定玩法内交互操作
    /// </summary>
    InteractionInSomeMode,

    /// <summary>
    /// 开启任务追踪
    /// </summary>
    QuestNavigation,

    /// <summary>
    /// 中断挑战
    /// </summary>
    AbandonChallenge,

    /// <summary>
    /// 切换小队角色1
    /// </summary>
    SwitchMember1,

    /// <summary>
    /// 切换小队角色2
    /// </summary>
    SwitchMember2,

    /// <summary>
    /// 切换小队角色3
    /// </summary>
    SwitchMember3,

    /// <summary>
    /// 切换小队角色4
    /// </summary>
    SwitchMember4,

    /// <summary>
    /// 切换小队角色5
    /// </summary>
    SwitchMember5,

    /// <summary>
    /// 呼出快捷轮盘
    /// </summary>
    ShortcutWheel,

    /// <summary>
    /// 打开背包
    /// </summary>
    OpenInventory,

    /// <summary>
    /// 打开角色界面
    /// </summary>
    OpenCharacterScreen,

    /// <summary>
    /// 打开地图
    /// </summary>
    OpenMap,

    /// <summary>
    /// 打开派蒙界面
    /// </summary>
    OpenPaimonMenu,

    /// <summary>
    /// 打开冒险之证界面
    /// </summary>
    OpenAdventurerHandbook,

    /// <summary>
    /// 打开多人游戏界面
    /// </summary>
    OpenCoOpScreen,

    /// <summary>
    /// 打开祈愿界面
    /// </summary>
    OpenWishScreen,

    /// <summary>
    /// 打开纪行界面
    /// </summary>
    OpenBattlePassScreen,

    /// <summary>
    /// 打开活动面板
    /// </summary>
    OpenTheEventsMenu,

    /// <summary>
    /// 打开玩法系统界面（尘歌壶内猫尾酒馆内）
    /// </summary>
    OpenTheSettingsMenu,

    /// <summary>
    /// 打开摆设界面（尘歌壶内）
    /// </summary>
    OpenTheFurnishingScreen,

    /// <summary>
    /// 打开星之归还（条件符合期间生效）
    /// </summary>
    OpenStellarReunion,

    /// <summary>
    /// 开关任务菜单
    /// </summary>
    OpenQuestMenu,

    /// <summary>
    /// 打开通知详情
    /// </summary>
    OpenNotificationDetails,

    /// <summary>
    /// 打开聊天界面
    /// </summary>
    OpenChatScreen,

    /// <summary>
    /// 打开特殊环境说明
    /// </summary>
    OpenSpecialEnvironmentInformation,

    /// <summary>
    /// 查看教程详情
    /// </summary>
    CheckTutorialDetails,

    /// <summary>
    /// 长按打开元素视野
    /// </summary>
    ElementalSight,

    /// <summary>
    /// 呼出鼠标
    /// </summary>
    ShowCursor,

    /// <summary>
    /// 打开队伍配置界面
    /// </summary>
    OpenPartySetupScreen,

    /// <summary>
    /// 打开好友界面
    /// </summary>
    OpenFriendsScreen,

    /// <summary>
    /// 隐藏主界面
    /// </summary>
    HideUI,

}
