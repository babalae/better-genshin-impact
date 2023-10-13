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
}

public partial record struct HotKey
{
    public static HotKey None { get; } = new();
}