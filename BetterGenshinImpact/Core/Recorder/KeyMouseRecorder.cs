using BetterGenshinImpact.Core.Recorder.Model;
using Gma.System.MouseKeyHook;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using SharpDX.DirectInput;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Common;

namespace BetterGenshinImpact.Core.Recorder;

public class KeyMouseRecorder
{
    public List<MacroEvent> MacroEvents { get; } = [];

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
        // 合并鼠标移动事件
        var mergedMacroEvents = new List<MacroEvent>();
        MacroEvent? currentMerge = null;
        foreach (var macroEvent in MacroEvents)
        {
            if (currentMerge == null)
            {
                currentMerge = macroEvent;
                continue;
            }
            if (currentMerge.Type != macroEvent.Type)
            {
                mergedMacroEvents.Add(currentMerge);
                currentMerge = macroEvent;
                continue;
            }
            switch (macroEvent.Type)
            {
                case MacroEventType.MouseMoveTo:
                    // 控制合并时间片段长度
                    if (macroEvent.Time - currentMerge.Time > MergedEventTimeMax)
                    {
                        mergedMacroEvents.Add(currentMerge);
                        currentMerge = macroEvent;
                        break;
                    }
                    // 合并为最后一个事件的位置，避免丢步
                    currentMerge.MouseX = macroEvent.MouseX;
                    currentMerge.MouseY = macroEvent.MouseY;
                    break;

                case MacroEventType.MouseMoveBy:
                    if (macroEvent.Time - currentMerge.Time > MergedEventTimeMax)
                    {
                        mergedMacroEvents.Add(currentMerge);
                        currentMerge = macroEvent;
                        break;
                    }
                    // 相对位移量相加
                    currentMerge.MouseX += macroEvent.MouseX;
                    currentMerge.MouseY += macroEvent.MouseY;
                    if (macroEvent.CameraOrientation != null)
                    {
                        currentMerge.CameraOrientation = macroEvent.CameraOrientation;
                    }
                    break;

                default:
                    mergedMacroEvents.Add(currentMerge);
                    mergedMacroEvents.Add(macroEvent);
                    currentMerge = null;
                    break;
            }
        }
        KeyMouseScript keyMouseScript = new()
        {
            MacroEvents = mergedMacroEvents,
            Info = new KeyMouseScriptInfo
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height
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

    public void MouseMoveTo(MouseEventExtArgs e)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseMoveTo,
            MouseX = e.X,
            MouseY = e.Y,
            Time = (DateTime.UtcNow - StartTime).TotalMilliseconds
        });
    }

    public void MouseMoveBy(MouseState state)
    {
        var now = DateTime.UtcNow;
        int? cao = null;
        if (TaskContext.Instance().Config.RecordConfig.IsRecordCameraOrientation)
        {
            if ((now - LastOrientationDetection).TotalMilliseconds > 100.0)
            {
                LastOrientationDetection = now;
                cao = CameraOrientation.Compute(TaskControl.CaptureToRectArea().SrcGreyMat);
            }
        }
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseMoveBy,
            MouseX = state.X,
            MouseY = state.Y,
            Time = (now - StartTime).TotalMilliseconds,
            CameraOrientation = cao,
        });
    }
}
