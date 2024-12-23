﻿using BetterGenshinImpact.Core.Recorder.Model;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Fischless.WindowsInput;
using Vanara.PInvoke;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Core.Recorder;

public class KeyMouseMacroPlayerJsonLine
{
    public static async Task PlayMacro(string path, CancellationToken ct, bool withDelay = true)
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            Toast.Warning("请先在启动页，启动截图器再使用本功能");
            return;
        }

        var file = new FileInfo(path);
        var infoJson = await File.ReadAllTextAsync(file.FullName, ct);
        var info = JsonSerializer.Deserialize<KeyMouseScriptInfo>(infoJson, KeyMouseRecorder.JsonOptions) ?? throw new Exception("Failed to deserialize macro info");

        var macroEventsLines = await File.ReadAllTextAsync(Path.Combine(file.Directory!.FullName, "macroEvents.jsonl"), ct);
        List<MacroEvent> macroEvents = new();
        foreach (var line in macroEventsLines.Split("\n"))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            macroEvents.Add(JsonSerializer.Deserialize<MacroEvent>(line, KeyMouseRecorder.JsonOptions) ?? throw new Exception("Failed to deserialize macro event"));
        }
        // MacroEvents 需要以实际时间进行排序
        macroEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
        // 删除为负数的时间
        macroEvents.RemoveAll(m => m.Time < 0);

        var script = new KeyMouseScript
        {
            Info = info,
            MacroEvents = macroEvents
        };
        script.Adapt(TaskContext.Instance().SystemInfo.CaptureAreaRect, TaskContext.Instance().DpiScale);
        script.Merge();
        SystemControl.ActivateWindow();

        if (withDelay)
        {
            for (var i = 2; i >= 1; i--)
            {
                TaskControl.Logger.LogInformation("{Sec}秒后进行重放...", i);
                await Task.Delay(1000, ct);
            }

            TaskControl.Logger.LogInformation("开始重放");
        }

        await PlayMacro(script.MacroEvents, ct);
    }

    public static async Task PlayMacro(List<MacroEvent> macroEvents, CancellationToken ct)
    {
        WorkingArea = PrimaryScreen.WorkingArea;
        var startTime = DateTime.UtcNow;
        foreach (var e in macroEvents)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var timeToWait = (int)(e.Time - (DateTime.UtcNow - startTime).TotalMilliseconds);
            if (timeToWait < 0)
            {
                TaskControl.Logger.LogWarning("无法原速重放事件{Event}，落后{TimeToWait}ms", e.Type.ToString(), -timeToWait);
            }
            else
            {
                await Task.Delay(timeToWait, ct);
            }

            switch (e.Type)
            {
                case MacroEventType.KeyDown:
                    var vkDown = (User32.VK)e.KeyCode!;
                    if (InputBuilder.IsExtendedKey(vkDown))
                    {
                        Simulation.SendInput.Keyboard.KeyDown(false, vkDown);
                    }
                    else
                    {
                        Simulation.SendInput.Keyboard.KeyDown(vkDown);
                    }

                    break;
                case MacroEventType.KeyUp:

                    var vkUp = (User32.VK)e.KeyCode!;
                    if (InputBuilder.IsExtendedKey(vkUp))
                    {
                        Simulation.SendInput.Keyboard.KeyUp(false, vkUp);
                    }
                    else
                    {
                        Simulation.SendInput.Keyboard.KeyUp(vkUp);
                    }

                    break;

                case MacroEventType.MouseDown:
                    var buttonMouseDown = Enum.Parse<MouseButtons>(e.MouseButton!);
                    var xMouseDown = ToVirtualDesktopX(e.MouseX);
                    var yMouseDown = ToVirtualDesktopY(e.MouseY);
                    switch (buttonMouseDown)
                    {
                        case MouseButtons.Left:
                            Simulation.SendInput.Mouse.MoveMouseTo(xMouseDown, yMouseDown).LeftButtonDown();
                            break;

                        case MouseButtons.Right:
                            Simulation.SendInput.Mouse.MoveMouseTo(xMouseDown, yMouseDown).RightButtonDown();
                            break;

                        case MouseButtons.Middle:
                            Simulation.SendInput.Mouse.MoveMouseTo(xMouseDown, yMouseDown).MiddleButtonDown();
                            break;

                        case MouseButtons.None:
                            break;

                        case MouseButtons.XButton1:
                            break;

                        case MouseButtons.XButton2:
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    break;

                case MacroEventType.MouseUp:
                    var buttonMouseUp = Enum.Parse<MouseButtons>(e.MouseButton!);
                    var xMouseUp = ToVirtualDesktopX(e.MouseX);
                    var yMouseUp = ToVirtualDesktopY(e.MouseY);
                    switch (buttonMouseUp)
                    {
                        case MouseButtons.Left:
                            Simulation.SendInput.Mouse.MoveMouseTo(xMouseUp, yMouseUp).LeftButtonUp();
                            break;

                        case MouseButtons.Right:
                            Simulation.SendInput.Mouse.MoveMouseTo(xMouseUp, yMouseUp).RightButtonUp();
                            break;

                        case MouseButtons.Middle:
                            Simulation.SendInput.Mouse.MoveMouseTo(xMouseUp, yMouseUp).MiddleButtonUp();
                            break;

                        case MouseButtons.None:
                            break;

                        case MouseButtons.XButton1:
                            break;

                        case MouseButtons.XButton2:
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    break;

                case MacroEventType.MouseMoveTo:
                    Simulation.SendInput.Mouse.MoveMouseTo(ToVirtualDesktopX(e.MouseX), ToVirtualDesktopY(e.MouseY));
                    break;

                case MacroEventType.MouseWheel:
                    var num = (int)(e.MouseY / 120.0);
                    if (num != 0)
                    {
                        // 不支持多次的场景，但是不会出现这种情况
                        Simulation.SendInput.Mouse.VerticalScroll(num);
                    }

                    break;

                case MacroEventType.MouseMoveBy:
                    if (e.CameraOrientation != null)
                    {
                        var cao = CameraOrientation.Compute(TaskControl.CaptureToRectArea().SrcMat);
                        var diff = ((int)Math.Round(cao) - (int)e.CameraOrientation + 180) % 360 - 180;
                        diff += diff < -180 ? 360 : 0;
                        //过滤一下特别大的角度偏差
                        if (diff != 0 && diff < 8 && diff > -8)
                        {
                            TaskControl.Logger.LogWarning("视角重放偏差{diff}°，尝试修正", diff);
                            e.MouseX -= diff;
                        }
                    }

                    Simulation.SendInput.Mouse.MoveMouseBy(e.MouseX, e.MouseY);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public static Size WorkingArea;

    public static double ToVirtualDesktopX(int x)
    {
        return x * 65535 * 1d / WorkingArea.Width;
    }

    public static double ToVirtualDesktopY(int y)
    {
        return y * 65535 * 1d / WorkingArea.Height;
    }
}