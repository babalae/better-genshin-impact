using BetterGenshinImpact.Core.Recorder.Model;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map;
using Gma.System.MouseKeyHook;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using BetterGenshinImpact.Helpers;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Recorder;

public class KeyMouseRecorder
{
    public List<MacroEvent> MacroEvents { get; } = [];

    public List<MacroEvent> MouseMoveToMacroEvents { get; } = [];
    public List<MacroEvent> MouseMoveByMacroEvents { get; } = [];

    public uint StartTick { get; set; } = Kernel32.GetTickCount();

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
        // MacroEvents 需要以实际时间进行排序
        MacroEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
        // 删除为负数的时间
        MacroEvents.RemoveAll(m => m.Time < 0);
        
        var startTime = EnvironmentUtil.LastBootUpTime()! + TimeSpan.FromMilliseconds(StartTick);
        
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
                StartTime = $"{startTime:yyyy-MM-dd HH:mm:ss:ffff}",
                StartTimeUnixTimestamp = (startTime - new DateTime(1970, 1, 1)).Value.TotalNanoseconds.ToString("F0")
            }
        };
        return JsonSerializer.Serialize(keyMouseScript, JsonOptions);
    }

    public void KeyDown(KeyEventArgsExt e)
    {
        var time = e.Timestamp - StartTick;
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.KeyDown,
            KeyCode = e.KeyValue,
            Time = time
        });
    }

    public void KeyUp(KeyEventArgsExt e)
    {
        var time = e.Timestamp - StartTick;
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.KeyUp,
            KeyCode = e.KeyValue,
            Time = time
        });
    }

    public void MouseDown(MouseEventExtArgs e)
    {
        var time = e.Timestamp - StartTick;
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseDown,
            MouseX = e.X,
            MouseY = e.Y,
            MouseButton = e.Button.ToString(),
            Time = time
        });
    }

    public void MouseUp(MouseEventExtArgs e)
    {
        var time = e.Timestamp - StartTick;
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseUp,
            MouseX = e.X,
            MouseY = e.Y,
            MouseButton = e.Button.ToString(),
            Time = time
        });
    }

    public void MouseMoveTo(MouseEventExtArgs e, bool save = false)
    {
        var time = e.Timestamp - StartTick;
        var mEvent = new MacroEvent
        {
            Type = MacroEventType.MouseMoveTo,
            MouseX = e.X,
            MouseY = e.Y,
            Time = time
        };
        MouseMoveToMacroEvents.Add(mEvent);
        if (save)
        {
            MacroEvents.Add(mEvent);
        }
    }

    public void MouseWheel(MouseEventExtArgs e)
    {
        var time = e.Timestamp - StartTick;
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseWheel,
            MouseY = e.Delta, // 120 的倍率
            Time = time
        });
    }

    public void MouseMoveBy(MouseState state, uint tick, bool save = false)
    {
        var mEvent = new MacroEvent
        {
            Type = MacroEventType.MouseMoveBy,
            MouseX = state.X,
            MouseY = state.Y,
            Time = tick - 5,
        };
        MouseMoveByMacroEvents.Add(mEvent);
        if (save)
        {
            MacroEvents.Add(mEvent);
        }
    }
}