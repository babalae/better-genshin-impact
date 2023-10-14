using System;
using System.Text;
using System.Windows.Input;

namespace BetterGenshinImpact.Model;

public readonly partial record struct HotKey(Key Key, ModifierKeys Modifiers = ModifierKeys.None)
{
    public override string ToString()
    {
        if (Key == Key.None && Modifiers == ModifierKeys.None)
            return "< None >";

        var buffer = new StringBuilder();

        if (Modifiers.HasFlag(ModifierKeys.Control))
            buffer.Append("Ctrl + ");
        if (Modifiers.HasFlag(ModifierKeys.Shift))
            buffer.Append("Shift + ");
        if (Modifiers.HasFlag(ModifierKeys.Alt))
            buffer.Append("Alt + ");
        if (Modifiers.HasFlag(ModifierKeys.Windows))
            buffer.Append("Win + ");

        buffer.Append(Key);

        return buffer.ToString();
    }

    public bool IsEmpty => Key == Key.None && Modifiers == ModifierKeys.None;

    public static HotKey FromString(string str)
    {
        var key = Key.None;
        var modifiers = ModifierKeys.None;

        if (!string.IsNullOrWhiteSpace(str))
        {
            var parts = str.Split('+');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed == "Ctrl")
                    modifiers |= ModifierKeys.Control;
                else if (trimmed == "Shift")
                    modifiers |= ModifierKeys.Shift;
                else if (trimmed == "Alt")
                    modifiers |= ModifierKeys.Alt;
                else if (trimmed == "Win")
                    modifiers |= ModifierKeys.Windows;
                else
                    key = (Key)Enum.Parse(typeof(Key), trimmed);
            }
        }

        return new HotKey(key, modifiers);
    }
}

public partial record struct HotKey
{
    public static HotKey None { get; } = new();
}