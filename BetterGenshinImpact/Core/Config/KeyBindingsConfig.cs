using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Windows.Input;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.Core.Config;

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
    private KeyId _moveForward = KeyId.W;

    /// <summary>
    /// 向后移动
    /// </summary>
    [ObservableProperty]
    private KeyId _moveBackward = KeyId.S;

    /// <summary>
    /// 向左移动
    /// </summary>
    [ObservableProperty]
    private KeyId _moveLeft = KeyId.A;

    /// <summary>
    /// 向右移动
    /// </summary>
    [ObservableProperty]
    private KeyId _moveRight = KeyId.D;

    /// <summary>
    /// 切换走/跑；特定操作模式下向下移动
    /// </summary>
    [ObservableProperty]
    private KeyId _switchToWalkOrRun = KeyId.LeftCtrl;

    /// <summary>
    /// 普通攻击
    /// </summary>
    [ObservableProperty]
    private KeyId _normalAttack = KeyId.MouseLeftButton;

    /// <summary>
    /// 元素战技
    /// </summary>
    [ObservableProperty]
    private KeyId _elementalSkill = KeyId.E;

    /// <summary>
    /// 元素爆发
    /// </summary>
    [ObservableProperty]
    private KeyId _elementalBurst = KeyId.Q;

    /// <summary>
    /// 冲刺（键盘）
    /// </summary>
    [ObservableProperty]
    private KeyId _sprintKeyboard = KeyId.LeftShift;

    /// <summary>
    /// 冲刺（鼠标）
    /// </summary>
    [ObservableProperty]
    private KeyId _sprintMouse = KeyId.MouseRightButton;

    /// <summary>
    /// 切换瞄准模式
    /// </summary>
    [ObservableProperty]
    private KeyId _switchAimingMode = KeyId.R;

    /// <summary>
    /// 跳跃；特定操作模式下向上移动
    /// </summary>
    [ObservableProperty]
    private KeyId _jump = KeyId.Space;

    /// <summary>
    /// 落下
    /// </summary>
    [ObservableProperty]
    private KeyId _drop = KeyId.X;

    /// <summary>
    /// 拾取/交互（自动拾取由AutoPick模块管理）
    /// </summary>
    [ObservableProperty]
    private KeyId _pickUpOrInteract = KeyId.F;

    /// <summary>
    /// 快捷使用小道具
    /// </summary>
    [ObservableProperty]
    private KeyId _quickUseGadget = KeyId.Z;

    /// <summary>
    /// 特定玩法内交互操作
    /// </summary>
    [ObservableProperty]
    private KeyId _interactionInSomeMode = KeyId.T;

    /// <summary>
    /// 开启任务追踪
    /// </summary>
    [ObservableProperty]
    private KeyId _questNavigation = KeyId.V;

    /// <summary>
    /// 中断挑战
    /// </summary>
    [ObservableProperty]
    private KeyId _abandonChallenge = KeyId.P;

    /// <summary>
    /// 切换小队角色1
    /// </summary>
    [ObservableProperty]
    private KeyId _switchMember1 = KeyId.D1;

    /// <summary>
    /// 切换小队角色2
    /// </summary>
    [ObservableProperty]
    private KeyId _switchMember2 = KeyId.D2;

    /// <summary>
    /// 切换小队角色3
    /// </summary>
    [ObservableProperty]
    private KeyId _switchMember3 = KeyId.D3;

    /// <summary>
    /// 切换小队角色4
    /// </summary>
    [ObservableProperty]
    private KeyId _switchMember4 = KeyId.D4;

    /// <summary>
    /// 切换小队角色5
    /// </summary>
    [ObservableProperty]
    private KeyId _switchMember5 = KeyId.D5;

    /// <summary>
    /// 呼出快捷轮盘
    /// </summary>
    [ObservableProperty]
    private KeyId _shortcutWheel = KeyId.Tab;

    #endregion

    #region Menus（菜单）

    /// <summary>
    /// 打开背包
    /// </summary>
    [ObservableProperty]
    private KeyId _openInventory = KeyId.B;

    /// <summary>
    /// 打开角色界面
    /// </summary>
    [ObservableProperty]
    private KeyId _openCharacterScreen = KeyId.C;

    /// <summary>
    /// 打开地图
    /// </summary>
    [ObservableProperty]
    private KeyId _openMap = KeyId.M;

    /// <summary>
    /// 打开派蒙界面
    /// </summary>
    [ObservableProperty]
    private KeyId _openPaimonMenu = KeyId.Escape;

    /// <summary>
    /// 打开冒险之证界面
    /// </summary>
    [ObservableProperty]
    private KeyId _openAdventurerHandbook = KeyId.F1;

    /// <summary>
    /// 打开多人游戏界面
    /// </summary>
    [ObservableProperty]
    private KeyId _openCoOpScreen = KeyId.F2;

    /// <summary>
    /// 打开祈愿界面
    /// </summary>
    [ObservableProperty]
    private KeyId _openWishScreen = KeyId.F3;

    /// <summary>
    /// 打开纪行界面
    /// </summary>
    [ObservableProperty]
    private KeyId _openBattlePassScreen = KeyId.F4;

    /// <summary>
    /// 打开活动面板
    /// </summary>
    [ObservableProperty]
    private KeyId _openTheEventsMenu = KeyId.F5;

    /// <summary>
    /// 打开玩法系统界面（尘歌壶内猫尾酒馆内）
    /// </summary>
    [ObservableProperty]
    private KeyId _openTheSettingsMenu = KeyId.F6;

    /// <summary>
    /// 打开摆设界面（尘歌壶内）
    /// </summary>
    [ObservableProperty]
    private KeyId _openTheFurnishingScreen = KeyId.F7;

    /// <summary>
    /// 打开星之归还（条件符合期间生效）
    /// </summary>
    [ObservableProperty]
    private KeyId _openStellarReunion = KeyId.F8;

    /// <summary>
    /// 开关任务菜单
    /// </summary>
    [ObservableProperty]
    private KeyId _openQuestMenu = KeyId.J;

    /// <summary>
    /// 打开通知详情
    /// </summary>
    [ObservableProperty]
    private KeyId _openNotificationDetails = KeyId.Y;

    /// <summary>
    /// 打开聊天界面
    /// </summary>
    [ObservableProperty]
    private KeyId _openChatScreen = KeyId.Enter;

    /// <summary>
    /// 打开特殊环境说明
    /// </summary>
    [ObservableProperty]
    private KeyId _openSpecialEnvironmentInformation = KeyId.U;

    /// <summary>
    /// 查看教程详情
    /// </summary>
    [ObservableProperty]
    private KeyId _checkTutorialDetails = KeyId.G;

    /// <summary>
    /// 长按打开元素视野
    /// </summary>
    [ObservableProperty]
    private KeyId _elementalSight = KeyId.MouseMiddleButton;

    /// <summary>
    /// 呼出鼠标
    /// </summary>
    [ObservableProperty]
    private KeyId _showCursor = KeyId.LeftAlt;

    /// <summary>
    /// 打开队伍配置界面
    /// </summary>
    [ObservableProperty]
    private KeyId _openPartySetupScreen = KeyId.L;

    /// <summary>
    /// 打开好友界面
    /// </summary>
    [ObservableProperty]
    private KeyId _openFriendsScreen = KeyId.O;

    /// <summary>
    /// 隐藏主界面
    /// </summary>
    [ObservableProperty]
    private KeyId _hideUI = KeyId.Slash;

    #endregion

}

public static class KeyIdConverter
{

    /// <summary>
    /// 将KeyId转换为字符串（可在后续支持多语言），按键名称的显示尽量与原神UI一致
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static string ToName(this KeyId value)
    {
        return ToChineseName(value);
    }

    private static string ToChineseName(KeyId value) 
    {
        return value switch
        {
            // 需要单独翻译的按键
            KeyId.None => "<未指定>",
            KeyId.Unknown => "<未知>",
            KeyId.MouseLeftButton => "鼠标左键",
            KeyId.MouseRightButton => "鼠标右键",
            KeyId.MouseMiddleButton => "鼠标中键",
            KeyId.MouseSideButton1 => "鼠标侧键1",
            KeyId.MouseSideButton2 => "鼠标侧键2",
            KeyId.Apps => "菜单键",
            // 无需单独翻译的部分
            _ => EnglishKeyNameToChinese(value),
        };
    }

    private static string EnglishKeyNameToChinese(KeyId value)
    {
        var engName = ToEnglishName(value);
        if (engName.StartsWith("Left ") || engName.StartsWith("Right "))
        {
            return engName.Replace("Left ", "左").Replace("Right ", "右");
        }
        return engName;
    }

    private static string ToEnglishName(KeyId value)
    {
        return value switch
        {
            // 需要转换的部分
            KeyId.None => "<None>",
            KeyId.Unknown => "<Unknown>",
            KeyId.MouseLeftButton => "Mouse LButton",
            KeyId.MouseRightButton => "Mouse RButton",
            KeyId.MouseMiddleButton => "Mouse MButton",
            KeyId.MouseSideButton1 => "Mouse XButton1",
            KeyId.MouseSideButton2 => "Mouse XButton2",
            KeyId.Escape => "Esc",
            KeyId.PageUp => "Page Up",
            KeyId.PageDown => "Page Down",
            KeyId.CapsLock => "Caps Lock",
            KeyId.ScrollLock => "Scroll Lock",
            KeyId.LeftShift => "Left Shift",
            KeyId.RightShift => "Right Shift",
            KeyId.LeftCtrl => "Left Crtl",
            KeyId.RightCtrl => "Right Ctrl",
            KeyId.LeftAlt => "Left Alt",
            KeyId.RightAlt => "Right Alt",
            KeyId.LeftWin => "Left Win",
            KeyId.RightWin => "Right Win",
            KeyId.Apps => "Menu",
            KeyId.Left => "←",
            KeyId.Up => "↑",
            KeyId.Right => "→",
            KeyId.Down => "↓",
            KeyId.D0 => "0",
            KeyId.D1 => "1",
            KeyId.D2 => "2",
            KeyId.D3 => "3",
            KeyId.D4 => "4",
            KeyId.D5 => "5",
            KeyId.D6 => "6",
            KeyId.D7 => "7",
            KeyId.D8 => "8",
            KeyId.D9 => "9",
            KeyId.Apostrophe => "'",
            KeyId.Comma => ",",
            KeyId.Minus => "-",
            KeyId.Equal => "=",
            KeyId.Period => ".",
            KeyId.Slash => "/",
            KeyId.Backslash => @"\",
            KeyId.Semicolon => ";",
            KeyId.LeftSquareBracket => "[",
            KeyId.RightSquareBracket => "]",
            KeyId.Tilde => "`",
            KeyId.NumLock => "Num Lock",
            KeyId.NumPad0 => "Num 0",
            KeyId.NumPad1 => "Num 1",
            KeyId.NumPad2 => "Num 2",
            KeyId.NumPad3 => "Num 3",
            KeyId.NumPad4 => "Num 4",
            KeyId.NumPad5 => "Num 5",
            KeyId.NumPad6 => "Num 6",
            KeyId.NumPad7 => "Num 7",
            KeyId.NumPad8 => "Num 8",
            KeyId.NumPad9 => "Num 9",
            KeyId.Decimal => "Num .",
            KeyId.Divide => "Num /",
            KeyId.Multiply => "Num *",
            KeyId.Subtract => "Num -",
            KeyId.Add => "Num +",
            KeyId.NumEnter => "Num Enter",
            // 默认使用枚举名
            _ => value.ToString(),
        };
    }

    /// <summary>
    /// 将KeyId转换为VK
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static VK ToVK(this KeyId value)
    {
        return value switch
        {
            // 这两个值在VK中没有，抛异常
            KeyId.None => throw new ArgumentOutOfRangeException(nameof(value), "未指定按键，无法转换为VK。"),
            KeyId.Unknown => throw new ArgumentOutOfRangeException(nameof(value), "未知按键，无法转换为VK。"),
            // 剩下的值相同，直接转
            _ => (VK)value,
        };
    }

    /// <summary>
    /// 将KeyId转换为System.Windows.Input.Key
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static Key ToInputKey(this KeyId value)
    {
        // 部分按键名称相同，使用名称转换
        try
        {
            return Enum.Parse<Key>(value.ToString());
        }
        catch
        {
            // 其他键使用查表法转换
            return value switch
            {
                KeyId.LeftWin => Key.LWin,
                KeyId.RightWin => Key.RWin,
                KeyId.Apostrophe => Key.Oem7,
                KeyId.Comma => Key.OemComma,
                KeyId.Minus => Key.OemMinus,
                KeyId.Equal => Key.OemPlus,
                KeyId.Period => Key.OemPeriod,
                KeyId.Slash => Key.Oem2,
                KeyId.Semicolon => Key.Oem1,
                KeyId.LeftSquareBracket => Key.Oem4,
                KeyId.Backslash => Key.Oem5,
                KeyId.RightSquareBracket => Key.Oem6,
                KeyId.Tilde => Key.Oem3,
                KeyId.Enter => Key.Enter,
                KeyId.ScrollLock => Key.Scroll,
                KeyId.PageUp => Key.Prior,
                KeyId.PageDown => Key.Next,
                KeyId.Backspace => Key.Back,
                KeyId.CapsLock => Key.Capital,
                // None、Unknown和鼠标按键抛异常
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            };
        }
    }

    /// <summary>
    /// 将KeyId转换为MouseButton
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static MouseButton ToMouseButton(this KeyId value)
    {
        return value switch
        {
            KeyId.MouseLeftButton => MouseButton.Left,
            KeyId.MouseRightButton => MouseButton.Right,
            KeyId.MouseMiddleButton => MouseButton.Middle,
            KeyId.MouseSideButton1 => MouseButton.XButton1,
            KeyId.MouseSideButton2 => MouseButton.XButton2,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "键盘按键请使用ToInputKey方法"),
        };
    }


    /// <summary>
    /// 将VK转换为KeyId
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static KeyId FromVK(VK value)
    {
        // 尝试通过VK的值获取KeyId的枚举名。若成功，表示对应的VK在KeyId支持的范围内，直接转换；否则返回Unknown
        return string.IsNullOrEmpty(Enum.GetName(typeof(KeyId), value)) ? KeyId.Unknown : (KeyId)value;
    }

    /// <summary>
    /// 将System.Windows.Input.Key转换为KeyId
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static KeyId FromInputKey(Key value)
    {
        // 部分按键名称相同，使用名称转换
        try
        {
            return Enum.Parse<KeyId>(value.ToString());
        }
        catch
        {
            // 其他键使用查表法转换
            return value switch
            {
                Key.LWin => KeyId.LeftWin,
                Key.RWin => KeyId.RightWin,
                Key.Oem7 => KeyId.Apostrophe,
                Key.OemComma => KeyId.Comma,
                Key.OemMinus => KeyId.Minus,
                Key.OemPlus => KeyId.Equal,
                Key.OemPeriod => KeyId.Period,
                Key.Oem2 => KeyId.Slash,
                Key.Oem1 => KeyId.Semicolon,
                Key.Oem4 => KeyId.LeftSquareBracket,
                Key.Oem5 => KeyId.Backslash,
                Key.Oem6 => KeyId.RightSquareBracket,
                Key.Oem3 => KeyId.Tilde,
                Key.Enter => KeyId.Enter,
                Key.Scroll => KeyId.ScrollLock,
                Key.Prior => KeyId.PageUp,
                Key.Next => KeyId.PageDown,
                Key.Back => KeyId.Backspace,
                Key.Capital => KeyId.CapsLock,
                // 支持列表外的值返回Unknown
                _ => KeyId.Unknown,
            };
        }
    }

    /// <summary>
    /// 将MouseButton转换为KeyId
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static KeyId FromMouseButton(MouseButton value)
    {
        return value switch
        {
            MouseButton.Left => KeyId.MouseLeftButton,
            MouseButton.Right => KeyId.MouseRightButton,
            MouseButton.Middle => KeyId.MouseMiddleButton,
            MouseButton.XButton1 => KeyId.MouseSideButton1,
            MouseButton.XButton2 => KeyId.MouseSideButton2,
            _ => KeyId.Unknown,
        };
    }

}

/// <summary>
/// 用于与VK/Windows.Input.Key解耦，值与VK对应，但是仅包含美式键盘(104键，不包括媒体控制等)和鼠标中的按键
/// </summary>
public enum KeyId
{
    /// <summary>无</summary>
    None = 0x00,

    /// <summary>未知按键</summary>
    Unknown = 0xFF,

    #region 鼠标按键

    /// <summary>鼠标左键</summary>
    MouseLeftButton = 0x01,

    /// <summary>鼠标右键</summary>
    MouseRightButton = 0x02,

    /// <summary>鼠标中键（滚轮）</summary>
    MouseMiddleButton = 0x04,

    /// <summary>鼠标侧键1（后退）</summary>
    MouseSideButton1 = 0x05,

    /// <summary>鼠标侧键2（前进）</summary>
    MouseSideButton2 = 0x06,

    #endregion

    #region F键区

    /// <summary>F1</summary>
    F1 = 0x70,

    /// <summary>F2</summary>
    F2 = 0x71,

    /// <summary>F3</summary>
    F3 = 0x72,

    /// <summary>F4</summary>
    F4 = 0x73,

    /// <summary>F5</summary>
    F5 = 0x74,

    /// <summary>F6</summary>
    F6 = 0x75,

    /// <summary>F7</summary>
    F7 = 0x76,

    /// <summary>F8</summary>
    F8 = 0x77,

    /// <summary>F9</summary>
    F9 = 0x78,

    /// <summary>F10</summary>
    F10 = 0x79,

    /// <summary>F11</summary>
    F11 = 0x7A,

    /// <summary>F12</summary>
    F12 = 0x7B,

    #endregion

    #region 控制&功能键

    /// <summary>Esc</summary>
    Escape = 0x1B,

    /// <summary>PrintScreen</summary>
    PrintScreen = 0x2C,

    /// <summary>ScrollLock</summary>
    ScrollLock = 0x91,

    /// <summary>Pause</summary>
    Pause = 0x13,

    /// <summary>Insert</summary>
    Insert = 0x2D,

    /// <summary>Delete</summary>
    Delete = 0x2E,

    /// <summary>Home</summary>
    Home = 0x24,

    /// <summary>End</summary>
    End = 0x23,

    /// <summary>Page Up</summary>
    PageUp = 0x21,

    /// <summary>Page Down</summary>
    PageDown = 0x22,

    /// <summary>Backspace退格</summary>
    Backspace = 0x08,

    /// <summary>Tab</summary>
    Tab = 0x09,

    /// <summary>Caps Lock大写锁定</summary>
    CapsLock = 0x14,

    /// <summary>Enter回车</summary>
    Enter = 0x0D,

    ///// <summary>Shift</summary>
    //Shift = 0x10,

    /// <summary>左Shift</summary>
    LeftShift = 0xA0,

    /// <summary>右Shift</summary>
    RightShift = 0xA1,

    ///// <summary>Ctrl</summary>
    //Ctrl = 0x11,

    /// <summary>左Ctrl</summary>
    LeftCtrl = 0xA2,

    /// <summary>右Ctrl</summary>
    RightCtrl = 0xA3,

    ///// <summary>Alt</summary>
    //Alt = 0x12,

    /// <summary>左Alt</summary>
    LeftAlt = 0xA4,

    /// <summary>右Alt</summary>
    RightAlt = 0xA5,

    /// <summary>左Win键 (Microsoft Natural Keyboard)</summary>
    LeftWin = 0x5B,

    /// <summary>右Win键 (Microsoft Natural Keyboard)</summary>
    RightWin = 0x5C,

    /// <summary>菜单键 (Microsoft Natural Keyboard)</summary>
    Apps = 0x5D,

    /// <summary>Space空格键</summary>
    Space = 0x20,

    #endregion

    #region 方向键

    /// <summary>方向键 ←</summary>
    Left = 0x25,

    /// <summary>方向键 ↑</summary>
    Up = 0x26,

    /// <summary>方向键 →</summary>
    Right = 0x27,

    /// <summary>方向键 ↓</summary>
    Down = 0x28,

    #endregion

    #region 字母区 - 字母

    /// <summary>A</summary>
    A = 0x41,

    /// <summary>B</summary>
    B = 0x42,

    /// <summary>C</summary>
    C = 0x43,

    /// <summary>D</summary>
    D = 0x44,

    /// <summary>E</summary>
    E = 0x45,

    /// <summary>F</summary>
    F = 0x46,

    /// <summary>G</summary>
    G = 0x47,

    /// <summary>H</summary>
    H = 0x48,

    /// <summary>I</summary>
    I = 0x49,

    /// <summary>J</summary>
    J = 0x4A,

    /// <summary>K</summary>
    K = 0x4B,

    /// <summary>L</summary>
    L = 0x4C,

    /// <summary>M</summary>
    M = 0x4D,

    /// <summary>N</summary>
    N = 0x4E,

    /// <summary>O</summary>
    O = 0x4F,

    /// <summary>P</summary>
    P = 0x50,

    /// <summary>Q</summary>
    Q = 0x51,

    /// <summary>R</summary>
    R = 0x52,

    /// <summary>S</summary>
    S = 0x53,

    /// <summary>T</summary>
    T = 0x54,

    /// <summary>U</summary>
    U = 0x55,

    /// <summary>V</summary>
    V = 0x56,

    /// <summary>W</summary>
    W = 0x57,

    /// <summary>X</summary>
    X = 0x58,

    /// <summary>Y</summary>
    Y = 0x59,

    /// <summary>Z</summary>
    Z = 0x5A,

    #endregion

    #region 字母区 - 数字

    /// <summary>0</summary>
    D0 = 0x30,

    /// <summary>1</summary>
    D1 = 0x31,

    /// <summary>2</summary>
    D2 = 0x32,

    /// <summary>3</summary>
    D3 = 0x33,

    /// <summary>4</summary>
    D4 = 0x34,

    /// <summary>5</summary>
    D5 = 0x35,

    /// <summary>6</summary>
    D6 = 0x36,

    /// <summary>7</summary>
    D7 = 0x37,

    /// <summary>8</summary>
    D8 = 0x38,

    /// <summary>9</summary>
    D9 = 0x39,

    #endregion

    #region 字母区 - 符号

    /// <summary>引号 '</summary>
    Apostrophe = 0xDE,

    /// <summary>逗号 ,</summary>
    Comma = 0xBC,

    /// <summary>连接符 -</summary>
    Minus = 0xBD,

    /// <summary>等于号 =</summary>
    Equal = 0xBB,

    /// <summary>句号 .</summary>
    Period = 0xBE,

    /// <summary>斜杠 /</summary>
    Slash = 0xBF,

    /// <summary>反斜杠 \</summary>
    Backslash = 0xE2,

    /// <summary>分号 ;</summary>
    Semicolon = 0xBA,

    /// <summary>左方括号 [</summary>
    LeftSquareBracket = 0xDB,

    /// <summary>右方括号 ]</summary>
    RightSquareBracket = 0xDD,

    /// <summary>波浪号 `</summary>
    Tilde = 0xC0,

    #endregion

    #region 小键盘区

    /// <summary>Num Lock</summary>
    NumLock = 0x90,

    /// <summary>Num 0</summary>
    NumPad0 = 0x60,

    /// <summary>Num 1</summary>
    NumPad1 = 0x61,

    /// <summary>Num 2</summary>
    NumPad2 = 0x62,

    /// <summary>Num 3</summary>
    NumPad3 = 0x63,

    /// <summary>Num 4</summary>
    NumPad4 = 0x64,

    /// <summary>Num 5</summary>
    NumPad5 = 0x65,

    /// <summary>Num 6</summary>
    NumPad6 = 0x66,

    /// <summary>Num 7</summary>
    NumPad7 = 0x67,

    /// <summary>Num 8</summary>
    NumPad8 = 0x68,

    /// <summary>Num 9</summary>
    NumPad9 = 0x69,

    /// <summary>Num .</summary>
    Decimal = 0x6E,

    /// <summary>Num /</summary>
    Divide = 0x6F,

    /// <summary>Num *</summary>
    Multiply = 0x6A,

    /// <summary>Num -</summary>
    Subtract = 0x6D,

    /// <summary>Num +</summary>
    Add = 0x6B,

    /// <summary>Num Enter</summary>
    NumEnter = 0x0E,

    #endregion

}
