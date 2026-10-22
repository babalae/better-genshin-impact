using BetterGenshinImpact.Core.Input;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 返回主界面
/// </summary>
/// <param name="runtime">本次运行使用的运行环境</param>
[SoloTaskInfo(TaskId, "返回主界面", Group = "工具", Order = 430, Scopes = SoloTaskScopes.Plan)]
public class ReturnMainUiTask(GameRuntime runtime) : ISoloTask<NoParam, NoData>
{
    /// <summary>
    /// 任务标识。
    /// </summary>
    public const string TaskId = "return_main_ui";

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime = runtime;

    /// <summary>
    /// 作为独立任务运行：返回主界面后报告成功。
    /// </summary>
    /// <param name="param">空参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>返回主界面流程结束后返回 Success</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(NoParam param, CancellationToken ct)
    {
        await Start(ct);
        return SoloTaskResult.Success("已返回主界面。");
    }

    /// <summary>
    /// 反复按 ESC（必要时点击退出门）直到回到主界面。供其他任务内部组合调用。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task Start(CancellationToken ct)
    {
        using var initialCapture = _runtime.CaptureToRectArea();
        if (Bv.IsInMainUi(initialCapture))
        {
            return;
        }

        for (var i = 0; i < 8; i++)
        {
            InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await _runtime.Delay(900, ct);

            var region = _runtime.CaptureToRectArea();

            try
            {
                var exitDoor = region.Find(ElementRecognition.Get("BtnExitDoor", region));
                if (exitDoor.IsExist())
                {
                    exitDoor.Click();
                    await _runtime.Delay(5000, ct);
                    region.Dispose();
                    region = _runtime.CaptureToRectArea();
                }

                if (Bv.IsInMainUi(region))
                {
                    return;
                }
            }
            finally
            {
                region.Dispose();
            }
        }
        await _runtime.Delay(500, ct);
        InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_RETURN);
        await _runtime.Delay(500, ct);
        InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
    }
}
