using BetterGenshinImpact.Core.Recorder.Model;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map;
using Gma.System.MouseKeyHook;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

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
        // MacroEvents 需要以实际时间进行排序
        MacroEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
        // 删除为负数的时间
        MacroEvents.RemoveAll(m => m.Time < 0);
        
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
        if (currentMerge != null)
        {
            mergedMacroEvents.Add(currentMerge);
        }
        KeyMouseScript keyMouseScript = new()
        {
            MacroEvents = mergedMacroEvents,
            Info = new KeyMouseScriptInfo
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                RecordDpi = TaskContext.Instance().DpiScale
            }
        };
        return JsonSerializer.Serialize(keyMouseScript, JsonOptions);
    }

    public void KeyDown(KeyEventArgs e, DateTime time)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.KeyDown,
            KeyCode = e.KeyValue,
            Time = (time - StartTime).TotalMilliseconds
        });
    }

    public void KeyUp(KeyEventArgs e, DateTime time)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.KeyUp,
            KeyCode = e.KeyValue,
            Time = (time - StartTime).TotalMilliseconds
        });
    }

    public void MouseDown(MouseEventExtArgs e, DateTime time)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseDown,
            MouseX = e.X,
            MouseY = e.Y,
            MouseButton = e.Button.ToString(),
            Time = (time - StartTime).TotalMilliseconds
        });
    }

    public void MouseUp(MouseEventExtArgs e, DateTime time)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseUp,
            MouseX = e.X,
            MouseY = e.Y,
            MouseButton = e.Button.ToString(),
            Time = (time - StartTime).TotalMilliseconds
        });
    }

    public void MouseMoveTo(MouseEventExtArgs e, DateTime time)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseMoveTo,
            MouseX = e.X,
            MouseY = e.Y,
            Time = (time - StartTime).TotalMilliseconds
        });
    }
    
    public void MouseWheel(MouseEventExtArgs e, DateTime time)
    {
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseWheel,
            MouseY = e.Delta, // 120 的倍率
            Time = (time - StartTime).TotalMilliseconds
        });
    }

    public void MouseMoveBy(RelativeMouseMoveEventArgs e)
    {
        
        int? cao = null;
        if (TaskContext.Instance().Config.RecordConfig.IsRecordCameraOrientation)
        {
            var now = DateTime.UtcNow;
            if ((now - LastOrientationDetection).TotalMilliseconds > 100.0)
            {
                LastOrientationDetection = now;
                using var capture = TaskControl.CaptureToRectArea();
                cao = (int)Math.Round(CameraOrientation.Compute(capture.SrcMat));
            }
        }
        
        MacroEvents.Add(new MacroEvent
        {
            Type = MacroEventType.MouseMoveBy,
            MouseX = e.DeltaX,
            MouseY = e.DeltaY,
            Time = (e.Timestamp - StartTime).TotalMilliseconds,
            CameraOrientation = cao,
        });
    }
}
