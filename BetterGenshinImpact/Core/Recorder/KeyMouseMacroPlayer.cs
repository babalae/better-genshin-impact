using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recorder.Model;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Vanara.PInvoke;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Core.Recorder;

public class KeyMouseMacroPlayer
{
    public static async Task PlayMacro(string macro, CancellationToken ct, bool withDelay = true)
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            Toast.Warning("请先在启动页，启动截图器再使用本功能");
            return;
        }

        var script = JsonSerializer.Deserialize<KeyMouseScript>(macro, KeyMouseRecorder.JsonOptions) ?? throw new Exception("Failed to deserialize macro");
        script.Adapt(TaskContext.Instance().SystemInfo.CaptureAreaRect, TaskContext.Instance().DpiScale);
        SystemControl.ActivateWindow();

        if (withDelay)
        {
            for (var i = 3; i >= 1; i--)
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

            var timeToWait = e.Time - (DateTime.UtcNow - startTime).TotalMilliseconds;
            if (timeToWait < -1)
            {
                TaskControl.Logger.LogDebug("无法原速重放事件{Event}，落后{TimeToWait}ms", e.Type.ToString(), (-timeToWait).ToString("F0"));
            }
            else
            {
                await Task.Delay(TimeSpan.FromMilliseconds(timeToWait), ct);
            }

            // 每个事件都从 InputHub 取当前通道，不缓存：回放期间截图器停止时后端会被替换。
            // 扩展键标志由通道按真实键盘判断（Win32 见 ExtendedKeys），网页版由 WebSdk 后端映射
            var input = InputHub.Foreground;
            switch (e.Type)
            {
                case MacroEventType.KeyDown:
                    input.Keyboard.KeyDown((User32.VK)e.KeyCode!);
                    break;

                case MacroEventType.KeyUp:
                    input.Keyboard.KeyUp((User32.VK)e.KeyCode!);
                    break;

                case MacroEventType.MouseDown:
                    var buttonMouseDown = Enum.Parse<MouseButtons>(e.MouseButton!);
                    var xMouseDown = ToVirtualDesktopX(e.MouseX);
                    var yMouseDown = ToVirtualDesktopY(e.MouseY);
                    switch (buttonMouseDown)
                    {
                        case MouseButtons.Left:
                            input.Mouse.MoveMouseTo(xMouseDown, yMouseDown).LeftButtonDown();
                            break;

                        case MouseButtons.Right:
                            input.Mouse.MoveMouseTo(xMouseDown, yMouseDown).RightButtonDown();
                            break;

                        case MouseButtons.Middle:
                            input.Mouse.MoveMouseTo(xMouseDown, yMouseDown).MiddleButtonDown();
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
                            input.Mouse.MoveMouseTo(xMouseUp, yMouseUp).LeftButtonUp();
                            break;

                        case MouseButtons.Right:
                            input.Mouse.MoveMouseTo(xMouseUp, yMouseUp).RightButtonUp();
                            break;

                        case MouseButtons.Middle:
                            input.Mouse.MoveMouseTo(xMouseUp, yMouseUp).MiddleButtonUp();
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
                    input.Mouse.MoveMouseTo(ToVirtualDesktopX(e.MouseX), ToVirtualDesktopY(e.MouseY));
                    break;

                case MacroEventType.MouseWheel:
                    var num = (int)(e.MouseY / 120.0);
                    if (num != 0)
                    {
                        // 不支持多次的场景，但是不会出现这种情况
                        input.Mouse.VerticalScroll(num);
                    }

                    break;

                case MacroEventType.MouseMoveBy:
                    if (e.CameraOrientation != null)
                    {
                        using var capture = TaskControl.CaptureToRectArea();
                        var cao = CameraOrientation.Compute(capture.SrcMat);
                        var diff = ((int)Math.Round(cao) - (int)e.CameraOrientation + 180) % 360 - 180;
                        diff += diff < -180 ? 360 : 0;
                        //过滤一下特别大的角度偏差
                        if (diff != 0 && diff < 8 && diff > -8)
                        {
                            TaskControl.Logger.LogWarning("视角重放偏差{diff}°，尝试修正", diff);
                            e.MouseX -= diff;
                        }
                    }

                    input.Mouse.MoveMouseBy(e.MouseX, e.MouseY);
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
