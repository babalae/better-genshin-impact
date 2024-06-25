namespace BetterGenshinImpact.Core.Recorder.Model;

public class MacroEvent
{
    public MacroEventType Type { get; set; }
    public int? KeyCode { get; set; }
    public int MouseX { get; set; }
    public int MouseY { get; set; }
    public string? MouseButton { get; set; }
    public double Time { get; set; }
}

public enum MacroEventType
{
    KeyDown,
    KeyUp,
    MouseMoveTo,
    MouseMoveBy,
    MouseDown,
    MouseUp
}
