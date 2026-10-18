using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Macro;

/// <summary>
/// 一键强化圣遗物
/// </summary>
public class QuickEnhanceArtifactMacro
{
    /// <summary>
    /// 配置根。静态方法没有创建方可以传参，暂时读配置根（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigService.Config!;

    /// <summary>
    /// 一键强化热键入口。运行环境是否存在由热键调用方判断
    /// </summary>
    /// <param name="runtime">热键触发时的运行环境，点击坐标按它的捕获区域换算</param>
    public static void Done(GameRuntime runtime)
    {
        // SystemControl.ActivateWindow(TaskContext.Instance().GameHandle);

        var config = Config.MacroConfig;

        // 快捷放入 1760x770
        runtime.GameRegion1080PPosClick(1760, 770);
        Thread.Sleep(100);
        // 强化 1760x1020
        runtime.GameRegion1080PPosClick(1760, 1020);
        Thread.Sleep(100 + config.EnhanceWaitDelay);
        // 详情菜单 150x150
        runtime.GameRegion1080PPosClick(150, 150);
        Thread.Sleep(100);
        // 强化菜单 150x220
        runtime.GameRegion1080PPosClick(150, 220);
        Thread.Sleep(100);
        // 移动回快捷放入 #30
        runtime.GameRegion1080PPosMove(1760, 770);
    }
}
