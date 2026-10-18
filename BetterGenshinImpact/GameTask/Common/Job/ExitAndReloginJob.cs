using BetterGenshinImpact.Core.Input;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 退出至登录页面并重新登录
/// </summary>
public class ExitAndReloginJob
{
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 第三方渠道（B 服）登录辅助
    /// </summary>
    private readonly Login3rdParty _login3rdParty;

    /// <summary>
    /// 创建退出重登任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    public ExitAndReloginJob(GameRuntime runtime)
    {
        _runtime = runtime;
        _login3rdParty = new Login3rdParty(runtime);
    }

    private RecognitionObject GetAutoWoodRecognitionObject(string objectName)
    {
        return RecognitionAssets.Get("AutoWood", objectName, _runtime.SystemInfo);
    }

    public async Task Start(CancellationToken ct)
    {
        Logger.LogInformation("退出至登录页面");
        SystemControl.FocusWindow(_runtime.Window.Handle);

        // 等待菜单界面出现
        await NewRetry.WaitForElementAppear(
            _runtime,
            GetAutoWoodRecognitionObject("MenuBag"),
            () => InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE),
            ct,
            10,
            1200
        );

        // 点击退出按钮并等待确认弹窗出现
        await NewRetry.WaitForElementAppear(
            _runtime,
            GetAutoWoodRecognitionObject("Confirm"),
            () => _runtime.GameRegionClick((size, scale) => (50 * scale, size.Height - 50 * scale)),
            ct,
            5,
            800
        );

        // 点击确认退出并等待确认弹窗消失
        await NewRetry.WaitForElementDisappear(
            _runtime,
            GetAutoWoodRecognitionObject("Confirm"),
            screen =>
            {
                // 接收当前截图作为参数
                screen.Find(RecognitionAssets.Get("AutoWood", "Confirm", screen), ra =>
                {
                    ra.Click();
                    ra.Dispose();
                });
            },
            ct,
            5,
            1000
        );
        await _runtime.Delay(1000, ct);

        //============== 重新登录流程 ==============
        Logger.LogInformation("点击登录");
        _login3rdParty.RefreshAvailabled();
        if (_login3rdParty is { Type: Login3rdParty.The3rdPartyType.Bilibili, IsAvailabled: true })
        {
            // await Delay(1, ct);
            Thread.Sleep(100);
            _login3rdParty.Login(ct);
            Logger.LogInformation("退出重登启用 B 服模式");
        }

        // 等待进入游戏按钮出现并点击
        var enterGameAppear = await NewRetry.WaitForElementAppear(
            _runtime,
            GetAutoWoodRecognitionObject("EnterGame"),
            () => { },
            ct,
            120,
            1000
        );
        if (!enterGameAppear)
        {
            throw new RetryException("未检测进入游戏界面");
        }

        // 点击进入游戏按钮直到它消失
        var waitForEnterGameRoDisappear = await NewRetry.WaitForElementDisappear(
            _runtime,
            GetAutoWoodRecognitionObject("EnterGame"),
            () => _runtime.GameRegion1080PPosClick(955, 666),
            ct,
            120,
            1000
        );
        if (!waitForEnterGameRoDisappear)
        {
            throw new RetryException("未检测到进入游戏按钮消失, 可能未点击成功");
        }


        // 等待主界面出现
        var mainUiFound = await NewRetry.WaitForElementAppear(
            _runtime,
            ElementRecognition.Get("PaimonMenu", _runtime.SystemInfo),
            () => { },
            ct,
            120,
            1000
        );

        if (mainUiFound)
        {
            Logger.LogInformation("退出重新登录结束！");
        }
        else
        {
            Logger.LogWarning("未检测到主界面，登录可能未完成");
        }

        await _runtime.Delay(500, ct);
    }
}
