using CommunityToolkit.Mvvm.ComponentModel;
using System;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.Core.Config
{
    /// <summary>
    /// 原神按键绑定配置
    /// </summary>
    [Serializable]
    public partial class KeyBindingsConfig:ObservableObject
    {

        #region Actions（操作）

        /// <summary>
        /// 向前移动
        /// </summary>
        [ObservableProperty]
        private VK _moveForward = VK.VK_W;

        /// <summary>
        /// 向后移动
        /// </summary>
        [ObservableProperty]
        private VK _moveBackward = VK.VK_S;

        /// <summary>
        /// 向左移动
        /// </summary>
        [ObservableProperty]
        private VK _moveLeft = VK.VK_A;

        /// <summary>
        /// 向右移动
        /// </summary>
        [ObservableProperty]
        private VK _moveRight = VK.VK_D;

        /// <summary>
        /// 切换走/跑；特定操作模式下向下移动
        /// </summary>
        [ObservableProperty]
        private VK _switchToWalkOrRun = VK.VK_LCONTROL;

        /// <summary>
        /// 普通攻击
        /// </summary>
        [ObservableProperty]
        private VK _normalAttack = VK.VK_LBUTTON;

        /// <summary>
        /// 元素战技
        /// </summary>
        [ObservableProperty]
        private VK _elementalSkill = VK.VK_E;

        /// <summary>
        /// 元素爆发
        /// </summary>
        [ObservableProperty]
        private VK _elementalBurst = VK.VK_Q;

        /// <summary>
        /// 冲刺（键盘）
        /// </summary>
        [ObservableProperty]
        private VK _sprintKeyboard = VK.VK_LSHIFT;

        /// <summary>
        /// 冲刺（鼠标）
        /// </summary>
        [ObservableProperty]
        private VK _sprintMouse = VK.VK_RBUTTON;

        /// <summary>
        /// 切换瞄准模式
        /// </summary>
        [ObservableProperty]
        private VK _switchAimingMode = VK.VK_R;

        /// <summary>
        /// 跳跃；特定操作模式下向上移动
        /// </summary>
        [ObservableProperty]
        private VK _jump = VK.VK_SPACE;

        /// <summary>
        /// 落下
        /// </summary>
        [ObservableProperty]
        private VK _drop = VK.VK_X;

        /// <summary>
        /// 拾取/交互（自动拾取由AutoPick模块管理）
        /// </summary>
        [ObservableProperty]
        private VK _pickUpOrInteract = VK.VK_F;

        /// <summary>
        /// 快捷使用小道具
        /// </summary>
        [ObservableProperty]
        private VK _quickUseGadget = VK.VK_Z;

        /// <summary>
        /// 特定玩法内交互操作
        /// </summary>
        [ObservableProperty]
        private VK _interactionInSomeMode = VK.VK_T;

        /// <summary>
        /// 开启任务追踪
        /// </summary>
        [ObservableProperty]
        private VK _questNavigation = VK.VK_V;

        /// <summary>
        /// 中断挑战
        /// </summary>
        [ObservableProperty]
        private VK _abandonChallenge = VK.VK_P;

        /// <summary>
        /// 切换小队角色1
        /// </summary>
        [ObservableProperty]
        private VK _switchMember1 = VK.VK_1;

        /// <summary>
        /// 切换小队角色2
        /// </summary>
        [ObservableProperty]
        private VK _switchMember2 = VK.VK_2;

        /// <summary>
        /// 切换小队角色3
        /// </summary>
        [ObservableProperty]
        private VK _switchMember3 = VK.VK_3;

        /// <summary>
        /// 切换小队角色4
        /// </summary>
        [ObservableProperty]
        private VK _switchMember4 = VK.VK_4;

        /// <summary>
        /// 切换小队角色5
        /// </summary>
        [ObservableProperty]
        private VK _switchMember5 = VK.VK_5;

        /// <summary>
        /// 呼出快捷轮盘
        /// </summary>
        [ObservableProperty]
        private VK _shortcutWheel = VK.VK_TAB;

        #endregion

        #region Menus（菜单）

        /// <summary>
        /// 打开背包
        /// </summary>
        [ObservableProperty]
        private VK _openInventory = VK.VK_B;

        /// <summary>
        /// 打开角色界面
        /// </summary>
        [ObservableProperty]
        private VK _openCharacterScreen = VK.VK_C;

        /// <summary>
        /// 打开地图
        /// </summary>
        [ObservableProperty]
        private VK _openMap = VK.VK_M;

        /// <summary>
        /// 打开派蒙界面
        /// </summary>
        [ObservableProperty]
        private VK _openPaimonMenu = VK.VK_ESCAPE;

        /// <summary>
        /// 打开冒险之证界面
        /// </summary>
        [ObservableProperty]
        private VK _openAdventurerHandbook = VK.VK_F1;

        /// <summary>
        /// 打开多人游戏界面
        /// </summary>
        [ObservableProperty]
        private VK _openCoOpScreen = VK.VK_F2;

        /// <summary>
        /// 打开祈愿界面
        /// </summary>
        [ObservableProperty]
        private VK _openWishScreen = VK.VK_F3;

        /// <summary>
        /// 打开纪行界面
        /// </summary>
        [ObservableProperty]
        private VK _openBattlePassScreen = VK.VK_F4;

        /// <summary>
        /// 打开活动面板
        /// </summary>
        [ObservableProperty]
        private VK _openTheEventsMenu = VK.VK_F5;

        /// <summary>
        /// 打开玩法系统界面（尘歌壶内猫尾酒馆内）
        /// </summary>
        [ObservableProperty]
        private VK _openTheSettingsMenu = VK.VK_F6;

        /// <summary>
        /// 打开摆设界面（尘歌壶内）
        /// </summary>
        [ObservableProperty]
        private VK _openTheFurnishingScreen = VK.VK_F7;

        /// <summary>
        /// 打开星之归还（条件符合期间生效）
        /// </summary>
        [ObservableProperty]
        private VK _openStellarReunion = VK.VK_F8;

        /// <summary>
        /// 开关任务菜单
        /// </summary>
        [ObservableProperty]
        private VK _openQuestMenu = VK.VK_J;

        /// <summary>
        /// 打开通知详情
        /// </summary>
        [ObservableProperty]
        private VK _openNotificationDetails = VK.VK_Y;

        /// <summary>
        /// 打开聊天界面
        /// </summary>
        [ObservableProperty]
        private VK _openChatScreen = VK.VK_RETURN;

        /// <summary>
        /// 打开特殊环境说明
        /// </summary>
        [ObservableProperty]
        private VK _openSpecialEnvironmentInformation = VK.VK_U;

        /// <summary>
        /// 查看教程详情
        /// </summary>
        [ObservableProperty]
        private VK _checkTutorialDetails = VK.VK_G;

        /// <summary>
        /// 长按打开元素视野
        /// </summary>
        [ObservableProperty]
        private VK _elementalSight = VK.VK_MBUTTON;

        /// <summary>
        /// 呼出鼠标
        /// </summary>
        [ObservableProperty]
        private VK _showCursor = VK.VK_LMENU;

        /// <summary>
        /// 打开队伍配置界面
        /// </summary>
        [ObservableProperty]
        private VK _openPartySetupScreen = VK.VK_I;

        /// <summary>
        /// 打开好友界面
        /// </summary>
        [ObservableProperty]
        private VK _openFriendsScreen = VK.VK_O;

        /// <summary>
        /// 隐藏主界面
        /// </summary>
        [ObservableProperty]
        private VK _hideUI = VK.VK_OEM_2;

        #endregion

    }
}
