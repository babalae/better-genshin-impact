using BetterGenshinImpact.Core.Simulator;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.RoleBasedAutoFight;

public class RoleBasedEnemySeeker
{
    private static readonly Scalar BloodLower = new Scalar(255, 90, 90);

    /// <summary>
    /// 检测敌人距离，如果过远则自动冲刺追击
    /// </summary>
    public static async Task CheckAndChaseEnemyAsync(CancellationToken ct)
    {
        try
        {
            using var image = CaptureToRectArea();
            if (image == null) return;

            // 截取上半部分屏幕检测血条
            using var crop = image.DeriveCrop(0, 0, image.Width * 1500 / 1920, image.Height * 900 / 1080);
            using Mat mask = OpenCvCommonHelper.Threshold(crop.SrcMat, BloodLower);

            using Mat labels = new Mat();
            using Mat stats = new Mat();
            using Mat centroids = new Mat();

            int numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
                connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

            if (numLabels > 1)
            {
                using Mat firstRow = stats.Row(1);
                int[] statsArray;
                if (firstRow.GetArray(out statsArray))
                {
                    int height = statsArray[3];
                    // height < 6 往往意味着怪物距离较远
                    if (height > 0 && height < 6)
                    {
                        TaskControl.Logger.LogInformation($"[自动追怪] 识别到怪物较远 (血条高度: {height})，尝试拉近距离");
                        Simulation.SendInput.Keyboard.KeyDown(Vanara.PInvoke.User32.VK.VK_W);
                        Simulation.SendInput.Keyboard.KeyPress(Vanara.PInvoke.User32.VK.VK_SHIFT);
                        await Task.Delay(500, ct); // 冲刺一小段
                        Simulation.SendInput.Keyboard.KeyUp(Vanara.PInvoke.User32.VK.VK_W);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogDebug(ex, "追怪检测异常");
        }
    }
}
