using BetterGenshinImpact.Core.Recorder.Model;
using Gma.System.MouseKeyHook;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using SharpDX.DirectInput;

namespace BetterGenshinImpact.Core.Recorder;

public class KeyMouseRecorder
{
    public List<MacroEvent> MacroEvents { get; } = [];

    public DateTime CurrentTime { get; set; } = DateTime.Now;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJsonMacro()
    {
        return JsonSerializer.Serialize(MacroEvents, JsonOptions);
    }

    public void KeyDown(KeyEventArgs e)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.KeyDown,
            KeyCode = e.KeyValue,
            Time = (DateTime.Now - CurrentTime).TotalMilliseconds
        });
        CurrentTime = DateTime.Now;
    }

    public void KeyUp(KeyEventArgs e)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.KeyUp,
            KeyCode = e.KeyValue,
            Time = (DateTime.Now - CurrentTime).TotalMilliseconds
        });
        CurrentTime = DateTime.Now;
    }

    public void MouseDown(MouseEventExtArgs e)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseDown,
            MouseX = e.X,
            MouseY = e.Y,
            MouseButton = e.Button.ToString(),
            Time = (DateTime.Now - CurrentTime).TotalMilliseconds
        });
        CurrentTime = DateTime.Now;
    }

    public void MouseUp(MouseEventExtArgs e)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseUp,
            MouseX = e.X,
            MouseY = e.Y,
            MouseButton = e.Button.ToString(),
            Time = (DateTime.Now - CurrentTime).TotalMilliseconds
        });
        CurrentTime = DateTime.Now;
    }

    public void MouseMoveTo(MouseEventExtArgs e)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseMoveTo,
            MouseX = e.X,
            MouseY = e.Y,
            Time = (DateTime.Now - CurrentTime).TotalMilliseconds
        });
        CurrentTime = DateTime.Now;
    }

    public void MouseMoveBy(MouseState state)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseMoveBy,
            MouseX = state.X,
            MouseY = state.Y,
            Time = (DateTime.Now - CurrentTime).TotalMilliseconds
        });
        CurrentTime = DateTime.Now;
    }
}
