using BetterGenshinImpact.Core.Config;
using System.Collections.Generic;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator.Hardware;

internal static class HardwareKeyMapper
{
    private readonly record struct TextKey(int HidCode, bool WithShift);

    private static readonly IReadOnlyDictionary<KeyId, int> KeyIdToHid = new Dictionary<KeyId, int>
    {
        [KeyId.A] = 4,
        [KeyId.B] = 5,
        [KeyId.C] = 6,
        [KeyId.D] = 7,
        [KeyId.E] = 8,
        [KeyId.F] = 9,
        [KeyId.G] = 10,
        [KeyId.H] = 11,
        [KeyId.I] = 12,
        [KeyId.J] = 13,
        [KeyId.K] = 14,
        [KeyId.L] = 15,
        [KeyId.M] = 16,
        [KeyId.N] = 17,
        [KeyId.O] = 18,
        [KeyId.P] = 19,
        [KeyId.Q] = 20,
        [KeyId.R] = 21,
        [KeyId.S] = 22,
        [KeyId.T] = 23,
        [KeyId.U] = 24,
        [KeyId.V] = 25,
        [KeyId.W] = 26,
        [KeyId.X] = 27,
        [KeyId.Y] = 28,
        [KeyId.Z] = 29,
        [KeyId.D1] = 30,
        [KeyId.D2] = 31,
        [KeyId.D3] = 32,
        [KeyId.D4] = 33,
        [KeyId.D5] = 34,
        [KeyId.D6] = 35,
        [KeyId.D7] = 36,
        [KeyId.D8] = 37,
        [KeyId.D9] = 38,
        [KeyId.D0] = 39,
        [KeyId.Enter] = 40,
        [KeyId.Escape] = 41,
        [KeyId.Backspace] = 42,
        [KeyId.Tab] = 43,
        [KeyId.Space] = 44,
        [KeyId.Minus] = 45,
        [KeyId.Equal] = 46,
        [KeyId.LeftSquareBracket] = 47,
        [KeyId.RightSquareBracket] = 48,
        [KeyId.Backslash] = 49,
        [KeyId.Semicolon] = 51,
        [KeyId.Apostrophe] = 52,
        [KeyId.Tilde] = 53,
        [KeyId.Comma] = 54,
        [KeyId.Period] = 55,
        [KeyId.Slash] = 56,
        [KeyId.CapsLock] = 57,
        [KeyId.F1] = 58,
        [KeyId.F2] = 59,
        [KeyId.F3] = 60,
        [KeyId.F4] = 61,
        [KeyId.F5] = 62,
        [KeyId.F6] = 63,
        [KeyId.F7] = 64,
        [KeyId.F8] = 65,
        [KeyId.F9] = 66,
        [KeyId.F10] = 67,
        [KeyId.F11] = 68,
        [KeyId.F12] = 69,
        [KeyId.PrintScreen] = 70,
        [KeyId.ScrollLock] = 71,
        [KeyId.Pause] = 72,
        [KeyId.Insert] = 73,
        [KeyId.Home] = 74,
        [KeyId.PageUp] = 75,
        [KeyId.Delete] = 76,
        [KeyId.End] = 77,
        [KeyId.PageDown] = 78,
        [KeyId.Right] = 79,
        [KeyId.Left] = 80,
        [KeyId.Down] = 81,
        [KeyId.Up] = 82,
        [KeyId.NumLock] = 83,
        [KeyId.Divide] = 84,
        [KeyId.Multiply] = 85,
        [KeyId.Subtract] = 86,
        [KeyId.Add] = 87,
        [KeyId.NumEnter] = 88,
        [KeyId.NumPad1] = 89,
        [KeyId.NumPad2] = 90,
        [KeyId.NumPad3] = 91,
        [KeyId.NumPad4] = 92,
        [KeyId.NumPad5] = 93,
        [KeyId.NumPad6] = 94,
        [KeyId.NumPad7] = 95,
        [KeyId.NumPad8] = 96,
        [KeyId.NumPad9] = 97,
        [KeyId.NumPad0] = 98,
        [KeyId.Decimal] = 99,
        [KeyId.Apps] = 101,
        [KeyId.LeftCtrl] = 224,
        [KeyId.LeftShift] = 225,
        [KeyId.LeftAlt] = 226,
        [KeyId.LeftWin] = 227,
        [KeyId.RightCtrl] = 228,
        [KeyId.RightShift] = 229,
        [KeyId.RightAlt] = 230,
        [KeyId.RightWin] = 231,
    };

    private static readonly IReadOnlyDictionary<char, TextKey> TextCharMap = new Dictionary<char, TextKey>
    {
        ['a'] = new(4, false), ['b'] = new(5, false), ['c'] = new(6, false), ['d'] = new(7, false),
        ['e'] = new(8, false), ['f'] = new(9, false), ['g'] = new(10, false), ['h'] = new(11, false),
        ['i'] = new(12, false), ['j'] = new(13, false), ['k'] = new(14, false), ['l'] = new(15, false),
        ['m'] = new(16, false), ['n'] = new(17, false), ['o'] = new(18, false), ['p'] = new(19, false),
        ['q'] = new(20, false), ['r'] = new(21, false), ['s'] = new(22, false), ['t'] = new(23, false),
        ['u'] = new(24, false), ['v'] = new(25, false), ['w'] = new(26, false), ['x'] = new(27, false),
        ['y'] = new(28, false), ['z'] = new(29, false),
        ['A'] = new(4, true), ['B'] = new(5, true), ['C'] = new(6, true), ['D'] = new(7, true),
        ['E'] = new(8, true), ['F'] = new(9, true), ['G'] = new(10, true), ['H'] = new(11, true),
        ['I'] = new(12, true), ['J'] = new(13, true), ['K'] = new(14, true), ['L'] = new(15, true),
        ['M'] = new(16, true), ['N'] = new(17, true), ['O'] = new(18, true), ['P'] = new(19, true),
        ['Q'] = new(20, true), ['R'] = new(21, true), ['S'] = new(22, true), ['T'] = new(23, true),
        ['U'] = new(24, true), ['V'] = new(25, true), ['W'] = new(26, true), ['X'] = new(27, true),
        ['Y'] = new(28, true), ['Z'] = new(29, true),
        ['1'] = new(30, false), ['2'] = new(31, false), ['3'] = new(32, false), ['4'] = new(33, false),
        ['5'] = new(34, false), ['6'] = new(35, false), ['7'] = new(36, false), ['8'] = new(37, false),
        ['9'] = new(38, false), ['0'] = new(39, false),
        ['!'] = new(30, true), ['@'] = new(31, true), ['#'] = new(32, true), ['$'] = new(33, true),
        ['%'] = new(34, true), ['^'] = new(35, true), ['&'] = new(36, true), ['*'] = new(37, true),
        ['('] = new(38, true), [')'] = new(39, true),
        [' '] = new(44, false), ['-'] = new(45, false), ['_'] = new(45, true), ['='] = new(46, false),
        ['+'] = new(46, true), ['['] = new(47, false), ['{'] = new(47, true), [']'] = new(48, false),
        ['}'] = new(48, true), ['\\'] = new(49, false), ['|'] = new(49, true), [';'] = new(51, false),
        [':'] = new(51, true), ['\''] = new(52, false), ['"'] = new(52, true), ['`'] = new(53, false),
        ['~'] = new(53, true), [','] = new(54, false), ['<'] = new(54, true), ['.'] = new(55, false),
        ['>'] = new(55, true), ['/'] = new(56, false), ['?'] = new(56, true), ['\r'] = new(40, false),
        ['\n'] = new(40, false), ['\t'] = new(43, false),
    };

    public static bool TryGetHidKey(User32.VK keyCode, out int hidCode)
    {
        var keyId = KeyIdConverter.FromVK(keyCode);
        return KeyIdToHid.TryGetValue(keyId, out hidCode);
    }

    public static IEnumerable<(int HidCode, bool WithShift)> EnumerateText(string text)
    {
        foreach (var ch in text)
        {
            if (TextCharMap.TryGetValue(ch, out var key))
            {
                yield return (key.HidCode, key.WithShift);
            }
        }
    }
}
