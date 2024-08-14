using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using System.Windows.Forms;
using Vanara.PInvoke;
using Microsoft.Extensions.Logging;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.Core.Simulator;
using OpenCvSharp;
using BetterGenshinImpact.Core.Recognition.OCR;

namespace BetterGenshinImpact.GameTask.AutoPathing;
public class PathExecutor
{
    public static async Task Pathing(PathingTask task, CancellationTokenSource cts, bool withDelay = true)
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            MessageBox.Show("请先在启动页，启动截图器再使用本功能");
            return;
        }

        SystemControl.ActivateWindow();

        if (withDelay)
        {
            for (var i = 3; i >= 1; i--)
            {
                TaskControl.Logger.LogInformation("{Sec}秒后开始寻路...", i);
                await Task.Delay(1000, cts.Token);
            }

            TaskControl.Logger.LogInformation("开始寻路");
        }

        if (task.Waypoints.Count == 0)
        {
            TaskControl.Logger.LogWarning("没有路径点，寻路结束");
            return;
        }

        if (task.Waypoints.First().WaypointType != WaypointType.Teleport)
        {
            TaskControl.Logger.LogWarning("第一个路径点不是传送点，将不会进行传送");
        }

        // 这里应该判断一下自动拾取是否处于工作状态，但好像没有什么方便的读取办法

        foreach (var waypoint in task.Waypoints)
        {
            if (waypoint.WaypointType == WaypointType.Teleport)
            {
                TaskControl.Logger.LogInformation("正在传送到{x},{y}", waypoint.X, waypoint.Y);
                await new TpTask(cts).Tp(waypoint.X, waypoint.Y);
                continue;
            }

            // waypoint.WaypointType == WaypointType.Path 或者 WaypointType.Target
            // Path不用走得很近，Target需要接近，但都需要先移动到对应位置

            await MoveTo(waypoint);

            if (waypoint.WaypointType == WaypointType.Target)
            {
                await MoveCloseTo(waypoint);
            }

        }
    }

    internal static async Task MoveTo(Waypoint waypoint)
    {
        var position = await Task.Run(Navigation.GetPosition);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, Navigation.GetPosition());
        TaskControl.Logger.LogInformation("粗略接近路径点，当前位置({x1},{y1})，目标位置({x2},{y2})", position.X, position.Y, waypoint.X, waypoint.Y);
        await WaitUntilRotatedTo(targetOrientation, 10);
        var startTime = DateTime.UtcNow;
        var lastPositionRecord = DateTime.UtcNow;
        var fastMode = false;
        var prevPositions = new List<Point2f>();
        // 按下w，一直走
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        while (true)
        {
            var now = DateTime.UtcNow;
            if ((now - startTime).TotalSeconds > 30)
            {
                TaskControl.Logger.LogWarning("执行超时，跳过路径点");
                break;
            }
            position = await Task.Run(Navigation.GetPosition);
            var distance = Navigation.GetDistance(waypoint, position);
            if (distance < 4)
            {
                TaskControl.Logger.LogInformation("到达路径点附近");
                break;
            }
            if (distance > 500)
            {
                TaskControl.Logger.LogWarning("距离过远，跳过路径点");
                break;
            }
            if ((now - lastPositionRecord).TotalMilliseconds > 1000)
            {
                lastPositionRecord = now;
                prevPositions.Add(position);
                if (prevPositions.Count > 8)
                {
                    var delta = prevPositions[-1] - prevPositions[-8];
                    if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 3)
                    {
                        TaskControl.Logger.LogWarning("疑似卡死，尝试脱离并跳过路径点");
                        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
                        await Task.Delay(500);
                        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_S);
                        await Task.Delay(500);
                        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_A);
                        await Task.Delay(500);
                        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_D);
                        await Task.Delay(500);
                        return;
                    }
                }
            }
            // 旋转视角
            targetOrientation = Navigation.GetTargetOrientation(waypoint, position);
            RotateTo(targetOrientation);
            // 根据指定方式进行移动
            if (waypoint.MoveType == MoveType.Fly)
            {
                // TODO:一直起跳直到打开风之翼
                if (!IsFlying())
                {
                    Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                    await Task.Delay(1000);
                }
                continue;
            }
            if (IsFlying())
            {
                Simulation.SendInput.Mouse.LeftButtonClick();
                await Task.Delay(1000);
                continue;
            }
            if (waypoint.MoveType == MoveType.Jump)
            {
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                await Task.Delay(1000);
                continue;
            }
            // 跑步或者游泳
            if (distance > 20 != fastMode)// 距离大于20时可以使用疾跑/自由泳
            {
                if (fastMode)
                {
                    Simulation.SendInput.Mouse.RightButtonUp();
                }
                else
                {
                    Simulation.SendInput.Mouse.RightButtonDown();
                }
                fastMode = !fastMode;
            }
            await Task.Delay(100);
        }
        // 抬起w键
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
    }

    internal static async Task MoveCloseTo(Waypoint waypoint)
    {
        var position = await Task.Run(Navigation.GetPosition);
        var targetOrientation = Navigation.GetTargetOrientation(waypoint, Navigation.GetPosition());
        TaskControl.Logger.LogInformation("精确接近路径点，当前位置({x1},{y1})，目标位置({x2},{y2})", position.X, position.Y, waypoint.X, waypoint.Y);
        if (waypoint.MoveType == MoveType.Fly && IsFlying())
        {
            //下落攻击接近目的地
            Simulation.SendInput.Mouse.LeftButtonClick();
            await Task.Delay(1000);
        }
        await WaitUntilRotatedTo(targetOrientation, 2);
        var wPressed = false;
        var stepsTaken = 0;
        while (true)
        {
            stepsTaken++;
            if (stepsTaken > 8)
            {
                TaskControl.Logger.LogWarning("精确接近超时");
                break;
            }
            position = await Task.Run(Navigation.GetPosition);
            if (Navigation.GetDistance(waypoint, position) < 2)
            {
                TaskControl.Logger.LogInformation("已到达路径点");
                break;
            }
            RotateTo(targetOrientation); //不再改变视角
            if (waypoint.MoveType == MoveType.Walk)
            {
                // 小碎步接近
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_W);
                await Task.Delay(500);
                continue;
            }
            if (!wPressed)
            {
                Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
            }
            await Task.Delay(500);
        }
        if (wPressed)
        {
            Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
        }
    }

    internal static int RotateTo(int targetOrientation)
    {
        var cao = CameraOrientation.Compute(TaskControl.CaptureToRectArea().SrcGreyMat);
        var diff = (cao - targetOrientation + 180) % 360 - 180;
        if (diff == 0)
        {
            return diff;
        }
        Simulation.SendInput.Mouse.MoveMouseBy(30 * diff + 10 * diff > 0 ? 1 : -1, 0);
        return diff;
    }

    internal static async Task WaitUntilRotatedTo(int targetOrientation, int maxDiff)
    {
        int count = 0;
        while (Math.Abs(RotateTo(targetOrientation)) > maxDiff && count < 50)
        {
            await Task.Delay(50);
            count++;
        }
    }

    internal static bool IsFlying()
    {
        var greyMat = TaskControl.CaptureToRectArea().SrcGreyMat;
        greyMat = new Mat(greyMat, new Rect(1809, 1025, 61, 28));
        var text = OcrFactory.Paddle.OcrWithoutDetector(greyMat);
        return text.ToLower() == "space";
    }

}

