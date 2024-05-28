using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using System.Threading;
using System.Windows;

namespace BetterGenshinImpact.GameTask.Macro;

/// <summary>
/// 一键强化圣遗物
/// </summary>
public class QuickEnhanceArtifactMacro
{
    public static void Done()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            MessageBox.Show("请先启动");
            return;
        }

        // SystemControl.ActivateWindow(TaskContext.Instance().GameHandle);

        var config = TaskContext.Instance().Config.MacroConfig;

        // 快捷放入 1760x770
        GameCaptureRegion.GameRegion1080PPosClick(1760, 770);
        Thread.Sleep(100);
        // 强化 1760x1020
        GameCaptureRegion.GameRegion1080PPosClick(1760, 1020);
        Thread.Sleep(100 + config.EnhanceWaitDelay);
        // 详情菜单 150x150
        GameCaptureRegion.GameRegion1080PPosClick(150, 150);
        Thread.Sleep(100);
        // 强化菜单 150x220
        GameCaptureRegion.GameRegion1080PPosClick(150, 220);
        Thread.Sleep(100);
        // 移动回快捷放入 #30
        GameCaptureRegion.GameRegion1080PPosMove(1760, 770);
    }
}
