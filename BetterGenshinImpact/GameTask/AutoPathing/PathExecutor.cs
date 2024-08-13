using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using System;
using System.Collections.Generic;
using System.Text;
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

            if (waypoint.WaypointType != WaypointType.Target)
            {
                continue;
            }

            await MoveCloseTo(waypoint);
        }
    }

    internal static async Task MoveTo(Waypoint waypoint)
    {
        var position = await Task.Run(GetPosition);
        var targetOrientation = GetTargetOrientation(waypoint, GetPosition());
        TaskControl.Logger.LogInformation("粗略接近路径点，当前位置({x1},{y1})，目标位置({x2},{y2})", position.X, position.Y, waypoint.X, waypoint.Y);
        await WaitUntilRotatedTo(targetOrientation, 10);
        var startTime = DateTime.UtcNow;
        // 按下w，一直走
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        var fastMode = false;
        while (true)
        {
            if ((DateTime.UtcNow - startTime).TotalSeconds > 30)
            {
                TaskControl.Logger.LogWarning("执行超时，跳过路径点");
                break;
            }
            position = await Task.Run(GetPosition);
            var distance = GetDistance(waypoint, position);
            if (distance < 2)
            {
                TaskControl.Logger.LogInformation("到达路径点附近");
                break;
            }
            if (distance > 500)
            {
                TaskControl.Logger.LogWarning("距离过远，跳过路径点");
                break;
            }
            // 旋转视角
            targetOrientation = GetTargetOrientation(waypoint, position);
            RotateTo(targetOrientation);
            // 根据指定方式进行移动
            if (waypoint.MoveType == MoveType.Fly)
            {
                // TODO:一直起跳直到打开风之翼
                continue;
            }
            if (waypoint.MoveType == MoveType.Jump)
            {
                // TODO:一直按空格
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
            await Task.Delay(50);
        }

    }

    internal static async Task MoveCloseTo(Waypoint waypoint)
    {

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

    internal static Point2f GetPosition()
    {
        var greyMat = TaskControl.CaptureToRectArea().SrcGreyMat;
        greyMat = new Mat(greyMat, new Rect(62, 19, 212, 212));
        return EntireMap.Instance.GetMiniMapPositionByFeatureMatch(greyMat);
    }

    internal static int GetTargetOrientation(Waypoint waypoint, Point2f position)
    {

        var target = MapCoordinate.GameToMain2048(waypoint.X, waypoint.Y);
        double deltaX = target.x - position.X;
        double deltaY = target.y - position.Y;
        double vectorLength = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (vectorLength == 0)
        {
            return 0;
        }
        // 计算向量与x轴之间的夹角（逆时针方向）
        double angle = Math.Acos(deltaX / vectorLength);
        // 如果向量在x轴下方，角度需要调整
        if (deltaY < 0)
        {
            angle = 2 * Math.PI - angle;
        }
        // 将角度转换为顺时针方向
        angle = 2 * Math.PI - angle;
        return (int)(angle * (180.0 / Math.PI));
    }

    internal static double GetDistance(Waypoint waypoint, Point2f position)
    {
        var x1 = waypoint.X;
        var y1 = waypoint.Y;
        var x2 = position.X;
        var y2 = position.Y;
        return Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
    }
}

