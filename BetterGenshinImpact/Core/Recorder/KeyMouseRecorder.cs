using BetterGenshinImpact.Core.Recorder.Model;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map;
using Gma.System.MouseKeyHook;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace BetterGenshinImpact.Core.Recorder;

public class KeyMouseRecorder
{
    public List<MacroEvent> MacroEvents { get; } = [];

    public List<MacroEvent> MouseMoveToMacroEvents { get; } = [];
    public List<MacroEvent> MouseMoveByMacroEvents { get; } = [];

    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    public DateTime LastOrientationDetection { get; set; } = DateTime.UtcNow;

    public double MergedEventTimeMax { get; set; } = 20.0;

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
        var rect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        KeyMouseScript keyMouseScript = new()
        {
            MacroEvents = MacroEvents,
            MouseMoveByMacroEvents = MouseMoveByMacroEvents,
            MouseMoveToMacroEvents = MouseMoveToMacroEvents,
            Info = new KeyMouseScriptInfo
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                RecordDpi = TaskContext.Instance().DpiScale,
                StartTime = $"{StartTime:yyyy-MM-dd HH:mm:ss:ffff}",
                StartTimeUnixTimestamp = (StartTime - new DateTime(1970, 1, 1)).TotalNanoseconds.ToString("F0")
            }
        };
        return JsonSerializer.Serialize(keyMouseScript, JsonOptions);
    }

    public void KeyDown(KeyEventArgs e)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.KeyDown,
            KeyCode = e.KeyValue,
            Time = (DateTime.UtcNow - StartTime).TotalMilliseconds
        });
    }

    public void KeyUp(KeyEventArgs e)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.KeyUp,
            KeyCode = e.KeyValue,
            Time = (DateTime.UtcNow - StartTime).TotalMilliseconds
        });
    }

    public void MouseDown(MouseEventExtArgs e)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseDown,
            MouseX = e.X,
            MouseY = e.Y,
            MouseButton = e.Button.ToString(),
            Time = (DateTime.UtcNow - StartTime).TotalMilliseconds
        });
    }

    public void MouseUp(MouseEventExtArgs e)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseUp,
            MouseX = e.X,
            MouseY = e.Y,
            MouseButton = e.Button.ToString(),
            Time = (DateTime.UtcNow - StartTime).TotalMilliseconds
        });
    }

    public void MouseMoveTo(MouseEventExtArgs e, bool save = false)
    {
        var mEvent = new MacroEvent
        {
            Type = MacroEventType.MouseMoveTo,
            MouseX = e.X,
            MouseY = e.Y,
            Time = (DateTime.UtcNow - StartTime).TotalMilliseconds
        };
        MouseMoveToMacroEvents.Add(mEvent);
        if (save)
        {
            MacroEvents.Add(mEvent);
        }
    }

    public void MouseWheel(MouseEventExtArgs e)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseWheel,
            MouseY = e.Delta, // 120 的倍率
            Time = (DateTime.UtcNow - StartTime).TotalMilliseconds
        });
    }

    public void MouseMoveBy(MouseState state, bool save = false)
    {
        var now = DateTime.UtcNow;

        var mEvent = new MacroEvent
        {
            Type = MacroEventType.MouseMoveBy,
            MouseX = state.X,
            MouseY = state.Y,
            Time = (now - StartTime).TotalMilliseconds,
        };
        MouseMoveByMacroEvents.Add(mEvent);
        if (save)
        {
            MacroEvents.Add(mEvent);
        }
    }
}