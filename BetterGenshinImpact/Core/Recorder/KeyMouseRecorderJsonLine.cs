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
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Recorder;

public class KeyMouseRecorderJsonLine
{
    public KeyMouseScriptInfo Info { get; set; }

    private readonly Channel<MacroEvent> _macroEventsChannel = Channel.CreateUnbounded<MacroEvent>();
    private readonly Channel<MacroEvent> _mouseMoveToMacroEventsChannel = Channel.CreateUnbounded<MacroEvent>();
    private readonly Channel<MacroEvent> _mouseMoveByMacroEventsChannel = Channel.CreateUnbounded<MacroEvent>();
    private readonly Task _consumerTask;

    public uint StartTick { get; set; } = Kernel32.GetTickCount();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public KeyMouseRecorderJsonLine(string folderName)
    {
        var path = Global.Absolute($@"User\KeyMouseScript\{folderName}\");

        DateTime startTime = DateTime.UtcNow;
        var bootTime = EnvironmentUtil.LastBootUpTime();
        if (bootTime == null)
        {
            TaskControl.Logger.LogWarning("无法获取系统启动时间");
        }
        else
        {
            startTime = bootTime.Value + TimeSpan.FromMilliseconds(StartTick);
        }

        var rect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        Info = new KeyMouseScriptInfo
        {
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            RecordDpi = TaskContext.Instance().DpiScale,
            StartTime = $"{startTime:yyyy-MM-dd HH:mm:ss:ffff}",
            StartTimeUnixTimestamp = (startTime - new DateTime(1970, 1, 1)).TotalNanoseconds.ToString("F0"),
            SysParams =  RecordContext.Instance.SysParams
        };
        var infoJson = JsonSerializer.Serialize(Info, JsonOptions);
        File.WriteAllText(Path.Combine(path, $"systemInfo.json"), infoJson);

        _consumerTask = Task.Run(async () => await ConsumeEventsAsync(path));
    }

    private async Task ConsumeEventsAsync(string path)
    {
        var tasks = new List<Task>
        {
            ConsumeChannelAsync(_macroEventsChannel, Path.Combine(path, "macroEvents.jsonl")),
            ConsumeChannelAsync(_mouseMoveToMacroEventsChannel, Path.Combine(path, "mouseMoveToMacroEvents.jsonl")),
            ConsumeChannelAsync(_mouseMoveByMacroEventsChannel, Path.Combine(path, "mouseMoveByMacroEvents.jsonl"))
        };

        await Task.WhenAll(tasks);
    }

    private async Task ConsumeChannelAsync(Channel<MacroEvent> channel, string filePath)
    {
        await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream);

        await foreach (var macroEvent in channel.Reader.ReadAllAsync())
        {
            var json = JsonSerializer.Serialize(macroEvent, JsonOptions);
            await writer.WriteLineAsync(json);
            await writer.FlushAsync();
        }
    }

    private void AddEvent(Channel<MacroEvent> channel, MacroEvent macroEvent)
    {
        channel.Writer.TryWrite(macroEvent);
    }

    public void KeyDown(KeyEventArgsExt e)
    {
        var time = e.Timestamp - StartTick;
        AddEvent(_macroEventsChannel, new MacroEvent
        {
            Type = MacroEventType.KeyDown,
            KeyCode = e.KeyValue,
            Time = time
        });
    }

    public void KeyUp(KeyEventArgsExt e)
    {
        var time = e.Timestamp - StartTick;
        AddEvent(_macroEventsChannel, new MacroEvent
        {
            Type = MacroEventType.KeyUp,
            KeyCode = e.KeyValue,
            Time = time
        });
    }

    public void MouseDown(MouseEventExtArgs e)
    {
        var time = e.Timestamp - StartTick;
        AddEvent(_macroEventsChannel, new MacroEvent
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
        AddEvent(_macroEventsChannel, new MacroEvent
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
        AddEvent(_mouseMoveToMacroEventsChannel, mEvent);
        if (save)
        {
            AddEvent(_macroEventsChannel, mEvent);
        }
    }

    public void MouseWheel(MouseEventExtArgs e)
    {
        var time = e.Timestamp - StartTick;
        AddEvent(_macroEventsChannel, new MacroEvent
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
            Time = tick - 5 - StartTick
        };
        AddEvent(_mouseMoveByMacroEventsChannel, mEvent);
        if (save)
        {
            AddEvent(_macroEventsChannel, mEvent);
        }
    }
}