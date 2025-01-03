using CommunityToolkit.Mvvm.ComponentModel;
using Sdcb.PaddleOCR.Models.Online;
using System;
using System.Collections.Generic;
using System.Text;
using Vanara.PInvoke;
using Windows.UI.Notifications;

namespace BetterGenshinImpact.Core.Config
{
    /// <summary>
    /// 原神按键绑定配置
    /// </summary>
    [Serializable]
    public partial class KeyBindingsConfig:ObservableObject
    {

        #region Actions

        /// <summary>
        /// 向前移动
        /// </summary>
        [ObservableProperty]
        private User32.VK _moveForward = User32.VK.VK_W;

        /// <summary>
        /// 向后移动
        /// </summary>
        [ObservableProperty]
        private User32.VK _moveBackward = User32.VK.VK_S;

        /// <summary>
        /// 向左移动
        /// </summary>
        [ObservableProperty]
        private User32.VK _moveLeft = User32.VK.VK_A;

        /// <summary>
        /// 向右移动
        /// </summary>
        [ObservableProperty]
        private User32.VK _moveRight = User32.VK.VK_D;

        /// <summary>
        /// 切换走/跑；特定操作模式下向下移动
        /// </summary>
        [ObservableProperty]
        private User32.VK _switchToWalkOrRun = User32.VK.VK_LCONTROL;

        /// <summary>
        /// 普通攻击
        /// </summary>
        [ObservableProperty]
        private User32.VK _normalAttack = User32.VK.VK_LBUTTON;

        /// <summary>
        /// 元素战技
        /// </summary>
        [ObservableProperty]
        private User32.VK _elementalSkill = User32.VK.VK_E;

        /// <summary>
        /// 元素爆发
        /// </summary>
        [ObservableProperty]
        private User32.VK _elementalBurst = User32.VK.VK_Q;

        /// <summary>
        /// 冲刺（键盘）
        /// </summary>
        [ObservableProperty]
        private User32.VK _sprintKeyboard = User32.VK.VK_LSHIFT;

        /// <summary>
        /// 冲刺（鼠标）
        /// </summary>
        [ObservableProperty]
        private User32.VK _sprintMouse = User32.VK.VK_RBUTTON;

        /// <summary>
        /// 切换瞄准模式
        /// </summary>
        [ObservableProperty]
        private User32.VK _switchAimingMode = User32.VK.VK_R;

        /// <summary>
        /// 跳跃；特定操作模式下向上移动
        /// </summary>
        [ObservableProperty]
        private User32.VK _jump = User32.VK.VK_SPACE;

        /// <summary>
        /// 落下
        /// </summary>
        [ObservableProperty]
        private User32.VK _drop = User32.VK.VK_X;

        /// <summary>
        /// 拾取/交互（自动拾取由AutoPick模块管理）
        /// </summary>
        [ObservableProperty]
        private User32.VK _pickUpOrInteract = User32.VK.VK_F;

        /// <summary>
        /// 快捷使用小道具
        /// </summary>
        [ObservableProperty]
        private User32.VK _quickUseGadget = User32.VK.VK_Z;

        /// <summary>
        /// 特定玩法内交互操作
        /// </summary>
        [ObservableProperty]
        private User32.VK _interaction = User32.VK.VK_T;

        /// <summary>
        /// 开启任务追踪
        /// </summary>
        [ObservableProperty]
        private User32.VK _questNavigation = User32.VK.VK_V;

        /// <summary>
        /// 中断挑战
        /// </summary>
        [ObservableProperty]
        private User32.VK _abandonChallenge = User32.VK.VK_P;

        /// <summary>
        /// 切换小队角色1
        /// </summary>
        [ObservableProperty]
        private User32.VK _switchMember1 = User32.VK.VK_1;

        /// <summary>
        /// 切换小队角色2
        /// </summary>
        [ObservableProperty]
        private User32.VK _switchMember2 = User32.VK.VK_2;

        /// <summary>
        /// 切换小队角色3
        /// </summary>
        [ObservableProperty]
        private User32.VK _switchMember3 = User32.VK.VK_3;

        /// <summary>
        /// 切换小队角色4
        /// </summary>
        [ObservableProperty]
        private User32.VK _switchMember4 = User32.VK.VK_4;

        /// <summary>
        /// 切换小队角色5
        /// </summary>
        [ObservableProperty]
        private User32.VK _switchMember5 = User32.VK.VK_5;

        /// <summary>
        /// 呼出快捷轮盘
        /// </summary>
        [ObservableProperty]
        private User32.VK _shortcutWheel = User32.VK.VK_TAB;

        #endregion

        #region Menus

        /// <summary>
        /// 打开背包
        /// </summary>
        [ObservableProperty]
        private User32.VK _openInventory = User32.VK.VK_B;

        /// <summary>
        /// 打开角色界面
        /// </summary>
        [ObservableProperty]
        private User32.VK _openCharacterScreen = User32.VK.VK_C;

        /// <summary>
        /// 打开地图
        /// </summary>
        [ObservableProperty]
        private User32.VK _openMap = User32.VK.VK_M;

        /// <summary>
        /// 打开派蒙界面
        /// </summary>
        [ObservableProperty]
        private User32.VK _openPaimonMenu = User32.VK.VK_ESCAPE;

        /// <summary>
        /// 打开冒险之证界面
        /// </summary>
        [ObservableProperty]
        private User32.VK _openAdventurerHandbook = User32.VK.VK_F1;

        /// <summary>
        /// 打开多人游戏界面
        /// </summary>
        [ObservableProperty]
        private User32.VK _openCoOpScreen = User32.VK.VK_F2;

        /// <summary>
        /// 打开祈愿界面
        /// </summary>
        [ObservableProperty]
        private User32.VK _openWishScreen = User32.VK.VK_F3;

        /// <summary>
        /// 打开纪行界面
        /// </summary>
        [ObservableProperty]
        private User32.VK _openBattlePassScreen = User32.VK.VK_F4;

        /// <summary>
        /// 打开活动面板
        /// </summary>
        [ObservableProperty]
        private User32.VK _openTheEventsMenu = User32.VK.VK_F5;

        /// <summary>
        /// 打开玩法系统界面（尘歌壶内猫尾酒馆内）
        /// </summary>
        [ObservableProperty]
        private User32.VK _openTheSettingsMenu = User32.VK.VK_F6;

        /// <summary>
        /// 打开摆设界面（尘歌壶内）
        /// </summary>
        [ObservableProperty]
        private User32.VK _openTheFurnishingScreen = User32.VK.VK_F7;

        /// <summary>
        /// 打开星之归还（条件符合期间生效）
        /// </summary>
        [ObservableProperty]
        private User32.VK _openStellarReunion = User32.VK.VK_F8;

        /// <summary>
        /// 开关任务菜单
        /// </summary>
        [ObservableProperty]
        private User32.VK _openQuestMenu = User32.VK.VK_J;

        /// <summary>
        /// 打开通知详情
        /// </summary>
        [ObservableProperty]
        private User32.VK _openNotificationDetails = User32.VK.VK_Y;

        /// <summary>
        /// 打开聊天界面
        /// </summary>
        [ObservableProperty]
        private User32.VK _openChatScreen = User32.VK.VK_RETURN;

        /// <summary>
        /// 打开特殊环境说明
        /// </summary>
        [ObservableProperty]
        private User32.VK _openSpecialEnvironmentInformation = User32.VK.VK_U;

        /// <summary>
        /// 查看教程详情
        /// </summary>
        [ObservableProperty]
        private User32.VK _checkTutorialDetails = User32.VK.VK_G;

        /// <summary>
        /// 长按打开元素视野
        /// </summary>
        [ObservableProperty]
        private User32.VK _elementalSight = User32.VK.VK_MBUTTON;

        /// <summary>
        /// 呼出鼠标
        /// </summary>
        [ObservableProperty]
        private User32.VK _showCursor = User32.VK.VK_LMENU;

        /// <summary>
        /// 打开队伍配置界面
        /// </summary>
        [ObservableProperty]
        private User32.VK _openPartySetupScreen = User32.VK.VK_I;

        /// <summary>
        /// 打开好友界面
        /// </summary>
        [ObservableProperty]
        private User32.VK _openFriendsScreen = User32.VK.VK_O;

        /// <summary>
        /// 隐藏主界面
        /// </summary>
        [ObservableProperty]
        private User32.VK _hideUI = User32.VK.VK_DIVIDE;

        #endregion

    }
}
