using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Linq;

namespace BetterGenshinImpact.Genshin.Settings;

internal sealed class OverrideControllerSettings
{
    public KeyboardMap? KeyboardMap;

    public OverrideControllerSettings(MainJson data)
    {
        if (data.OverrideControllerMapKeyList == null
         || data.OverrideControllerMapValueList == null
         || data.OverrideControllerMapKeyList.Length != data.OverrideControllerMapValueList.Length)
        {
            return;
        }

        for (int i = default; i < data.OverrideControllerMapKeyList.Length; i++)
        {
            string? key = data.OverrideControllerMapKeyList[i];

            if (key == "OverrideControllerMap__00000000-0000-0000-0000-000000000000__0")
            {
                string xmlRaw = data.OverrideControllerMapValueList[i];
                XDocument xmlDoc = XDocument.Parse(xmlRaw);

                if (xmlDoc?.Root?.Name.LocalName == "KeyboardMap")
                {
                    KeyboardMap = new(xmlRaw);
                }

                // Only detect the KeyboardMap
                break;
            }
        }
    }
}

internal sealed class KeyboardMap
{
    private readonly int? sourceMapId = null;
    private readonly int? categoryId = null;
    private readonly int? layoutId = null;
    private readonly string? name = null;
    private readonly string? hardwareGuid = null;
    private readonly bool? enabled = null;

    public int? SourceMapId => sourceMapId;
    public int? CategoryId => categoryId;
    public int? LayoutId => layoutId;
    public string? Name => name;
    public string? HardwareGuid => hardwareGuid;
    public bool? Enabled => enabled;
    public List<ActionElementMap> ActionElementMap { get; private set; } = [];

    public KeyboardMap(string? xmlRaw)
    {
        if (string.IsNullOrWhiteSpace(xmlRaw))
        {
            return;
        }

        try
        {
            XElement xml = XElement.Parse(xmlRaw);
            sourceMapId = (int?)xml?.ParseItem<int>(nameof(sourceMapId));
            categoryId = (int?)xml?.ParseItem<int>(nameof(categoryId));
            layoutId = (int?)xml?.ParseItem<int>(nameof(layoutId));
            name = (string?)xml?.ParseItem<string>(nameof(name));
            hardwareGuid = (string?)xml?.ParseItem<string>(nameof(hardwareGuid));
            enabled = (bool?)xml?.ParseItem<bool>(nameof(enabled));

            foreach (XElement mapItem in xml?.ParseList(nameof(ActionElementMap)) ?? [])
            {
                ActionElementMap item = new(mapItem.ToString());
                ActionElementMap.Add(item);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }
}

internal sealed class ActionElementMap
{
    private readonly int? actionCategoryId = null;
    private readonly int? actionId = null;
    private readonly int? elementType = null;
    private readonly int? elementIdentifierId = null;
    private readonly int? axisRange = null;
    private readonly int? invert = null;
    private readonly int? axisContribution = null;
    private readonly int? keyboardKeyCode = null;
    private readonly int? modifierKey1 = null;
    private readonly int? modifierKey2 = null;
    private readonly int? modifierKey3 = null;
    private readonly bool? enabled = null;

    public int? ActionCategoryId => actionCategoryId;
    public ActionId? ActionId => (ActionId?)actionId;
    public int? ElementType => elementType;
    public ElementIdentifierId? ElementIdentifierId => (ElementIdentifierId?)elementIdentifierId;
    public int? AxisRange => axisRange;
    public int? Invert => invert;
    public int? AxisContribution => axisContribution;
    public Keys? KeyboardKeyCode => (Keys?)keyboardKeyCode;
    public bool? Enabled => enabled;

    public bool IsCtrl { get; set; }
    public bool IsShift { get; set; }
    public bool IsAlt { get; set; }

    public ActionElementMap(string? xmlRaw)
    {
        if (string.IsNullOrWhiteSpace(xmlRaw))
        {
            return;
        }

        try
        {
            XElement xml = XElement.Parse(xmlRaw);
            actionCategoryId = (int?)xml?.ParseItem<int>(nameof(actionCategoryId));
            actionId = (int?)xml?.ParseItem<int>(nameof(actionId));
            elementType = (int?)xml?.ParseItem<int>(nameof(elementType));
            elementIdentifierId = (int?)xml?.ParseItem<int>(nameof(elementIdentifierId));
            axisRange = (int?)xml?.ParseItem<int>(nameof(axisRange));
            invert = (int?)xml?.ParseItem<int>(nameof(invert));
            axisContribution = (int?)xml?.ParseItem<int>(nameof(axisContribution));
            keyboardKeyCode = (int?)xml?.ParseItem<int>(nameof(keyboardKeyCode));
            modifierKey1 = (int?)xml?.ParseItem<int>(nameof(modifierKey1));
            modifierKey2 = (int?)xml?.ParseItem<int>(nameof(modifierKey2));
            modifierKey3 = (int?)xml?.ParseItem<int>(nameof(modifierKey3));
            enabled = (bool?)xml?.ParseItem<bool>(nameof(enabled));

            foreach (int? mod in new List<int?> { modifierKey1, modifierKey2, modifierKey3 })
            {
                switch (mod)
                {
                    case 1:
                        IsCtrl = true;
                        break;

                    case 2:
                        IsAlt = true;
                        break;

                    case 3:
                        IsShift = true;
                        break;

                    case null:
                    default:
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }

    public override string ToString()
        => $"{ActionId} : {KeyboardKeyCode} : {ElementIdentifierId}" +
           $" - {(IsCtrl ? "Ctrl" : string.Empty)}" +
           $" {(IsShift ? "Shift" : string.Empty)}" +
           $" {(IsAlt ? "Alt" : string.Empty)}";
}

file static class XmlParseExtension
{
    public static dynamic? ParseItem<T>([In] this XElement xml, [In] string keyName)
    {
        XElement el = xml.Descendants()
                         .Where(el => el.Name.LocalName == keyName)
                         .First();

        if (typeof(T) == typeof(int))
        {
            if (int.TryParse(el.Value, out int result))
            {
                return result;
            }
        }
        else if (typeof(T) == typeof(bool))
        {
            if (bool.TryParse(el.Value, out bool result))
            {
                return result;
            }
        }
        else if (typeof(T) == typeof(string))
        {
            return el.Value;
        }
        return null!;
    }

    public static IEnumerable<XElement> ParseList([In] this XElement xml, [In] string keyName)
    {
        return xml.Descendants().Where(el => el.Name.LocalName == keyName);
    }
}

internal enum ActionId
{
    SideMovement = 0,
    ForwardMovement = 1,
    Map = 2,
    CharacterList = 3,
    Inventory = 4,
    MenuGamepad = 5,
    QuestList = 6,
    Char1 = 7,
    Char2 = 8,
    Char3 = 9,
    AdventurerHandbook = 10,
    Jump = 15,
    Attack = 16,
    ElementalSkill = 17,
    QuestTransport = 18,
    ItemInfo = 19,
    ElementalBurst = 20,
    Sprint = 21,
    UseTalk = 22,
    MoveForwardShow = 23,
    MoveLeftShow = 24,
    MoveRightShow = 25,
    MoveBackShow = 26,
    AimMode = 27,
    AimButton = 28,
    Move = 29,
    CameraHorizontalView = 30,
    CameraVerticalView = 31,
    View = 32,
    QuickWheel = 33,
    TopMenuLeft = 34,
    TopMenuRight = 35,
    MenuSelect = 40,
    AuxiliaryAction = 41,
    MenuBack = 42,
    DPadUp = 43,
    DPadRight = 44,
    DPadDown = 45,
    DPadLeft = 46,
    MenuUp = 47,
    MenuRight = 48,
    MenuDown = 49,
    MenuLeft = 50,
    Less = 51,
    More = 52,
    CancelClimb = 53,
    CameraViewDistance = 54,
    WalkRun = 55,
    ShowCursor = 56,
    ElementalSight = 57,
    DebugMenu = 58,
    RightStickUp = 59,
    RightStickRight = 60,
    RightStickDown = 61,
    RightStickLeft = 62,
    SecondaryAction = 63,
    RightStickVertical = 64,
    CoOp = 65,
    Char4 = 66,
    LockItemMark = 67,
    RightStickHorizontal = 68,
    LeftStickHorizontal = 69,
    LeftStickVertical = 70,
    MenuRight2 = 71,
    MenuDown2 = 72,
    Wish = 73,
    Chat = 75,
    NotificationDetails = 76,
    EnvironmentInfo = 77,
    Char5 = 78,
    AdventurerHandbook2 = 79,
    Navigation = 80,
    Tutorial = 81,
    Events = 82,
    BattlePass = 83,
    AbandonChallenge = 84,
    AbandonChallengeGamepad = 85,
    PhotoHideUI = 86,
    Gadget = 87,
    InteractionSomeModes = 88,
    ExtraUp = 89,
    ExtraDown = 90,
    ExtraLeft = 91,
    ExtraRight = 92,
    MusicLeftUp = 94,
    MusicLeftRight = 95,
    MusicLeftDown = 96,
    MusicLeftLeft = 97,
    MusicRightUp = 98,
    MusicRightRight = 99,
    MusicRightDown = 100,
    MusicRightLeft = 101,
    MusicNote11 = 102,
    MusicNote12 = 103,
    MusicNote13 = 104,
    MusicNote14 = 105,
    MusicNote15 = 106,
    MusicNote16 = 107,
    MusicNote17 = 108,
    MusicNote21 = 109,
    MusicNote22 = 110,
    MusicNote23 = 111,
    MusicNote24 = 112,
    MusicNote25 = 113,
    MusicNote26 = 114,
    MusicNote27 = 115,
    MusicNote31 = 116,
    MusicNote32 = 117,
    MusicNote33 = 118,
    MusicNote34 = 119,
    MusicNote35 = 120,
    MusicNote36 = 121,
    MusicNote37 = 122,
    F1 = 124,
    F2 = 125,
    F3 = 126,
    Return = 127,
    LeftStickMove = 128,
    PotTasks = 129,
    PotEdit = 130,
    PartySetup = 131,
    Friends = 132,
    PotObjectTurnUp = 133,
    PotObjectTurnDown = 134,
    PotObjectTurnLeft = 135,
    PotObjectTurnRight = 136,
    RightStickMove2 = 137,
    LMB = 138,
    RMB = 139,
    CreateCustomSuite = 141,
}

internal enum ElementIdentifierId
{
    None = 0,
    Backspace = 55,
    Tab = 56,
    Clear = 57,
    Enter = 58,
    Pause = 59,
    ESC = 60,
    Space = 54,
    Apostrophe = 66,
    Equal = 78,
    Comma = 71,
    Minus = 72,
    Period = 73,
    Slash = 74,
    D0 = 27,
    D1 = 28,
    D2 = 29,
    D3 = 30,
    D4 = 31,
    D5 = 32,
    D6 = 33,
    D7 = 34,
    D8 = 35,
    D9 = 36,
    Semicolon = 76,
    LeftSquareBracket = 82,
    Backslash = 83,
    RightSquareBracket = 84,
    Tilde = 87,
    A = 1,
    B = 2,
    C = 3,
    D = 4,
    E = 5,
    F = 6,
    G = 7,
    H = 8,
    I = 9,
    J = 10,
    K = 11,
    L = 12,
    M = 13,
    N = 14,
    O = 15,
    P = 16,
    Q = 17,
    R = 18,
    S = 19,
    T = 20,
    U = 21,
    V = 22,
    W = 23,
    X = 24,
    Y = 25,
    Z = 26,
    Delete = 88,
    Numpad0 = 37,
    Numpad1 = 38,
    Numpad2 = 39,
    Numpad3 = 40,
    Numpad4 = 41,
    Numpad5 = 42,
    Numpad6 = 43,
    Numpad7 = 44,
    Numpad8 = 45,
    Numpad9 = 46,
    NumpadDot = 47,
    NumpadSlash = 48,
    NumpadAsterisk = 49,
    NumpadMinus = 50,
    NumpadPlus = 51,
    ArrowUp = 89,
    ArrowDown = 90,
    ArrowRight = 91,
    ArrowLeft = 92,
    Insert = 93,
    Home = 94,
    End = 95,
    PageUp = 96,
    PageDown = 97,
    F1 = 98,
    F2 = 99,
    F3 = 100,
    F4 = 101,
    F5 = 102,
    F6 = 103,
    F7 = 104,
    F8 = 105,
    F9 = 106,
    F10 = 107,
    F11 = 108,
    F12 = 109,
    F13 = 110,
    F14 = 111,
    F15 = 112,
    NumLock = 113,
    CapsLock = 114,
    ScrollLock = 115,
    RightShift = 116,
    LeftShift = 117,
    RightCtrl = 118,
    LeftCtrl = 119,
    RightAlt = 120,
    LeftAlt = 121,
    RightWin = 125,
    LeftWin = 124,
    Help = 127,
    Print = 128,
}
