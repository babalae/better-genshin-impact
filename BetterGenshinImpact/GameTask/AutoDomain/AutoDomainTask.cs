using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.View.Drawable;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.Helpers;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public class AutoDomainTask
{
    private readonly AutoPickAssets _autoPickAssets = new();

    private AutoDomainParam _taskParam;

    private readonly PostMessageSimulator _postMessage;

    public AutoDomainTask(AutoDomainParam taskParam)
    {
        _taskParam = taskParam;
        _postMessage = Simulation.PostMessage(TaskContext.Instance().GameHandle);
    }

    public void Start()
    {
        try
        {
            Logger.LogInformation("→ {Text} 设置总次数：{Cnt}", "自动秘境，启动！", _taskParam.DomainRoundNum);
            SystemControl.ActivateWindow();
            // 1. 走到钥匙处启动
            WalkToStartDomain();

            // 2. 执行战斗（战斗线程、视角线程、检测战斗完成线程）

            // 3. 旋转视角后寻找石化古树
        }
        catch (Exception e)
        {
            Logger.LogInformation(e.Message);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            TaskTriggerDispatcher.Instance().StartTimer();
        }
    }

    private async void WalkToStartDomain()
    {
        await Task.Run(() =>
        {
            _postMessage.KeyDown(VK.VK_W);
            try
            {
                while (true)
                {
                    var content = CaptureToContent();
                    var fRectArea = content.CaptureRectArea.Find(_autoPickAssets.FRo);
                    if (fRectArea.IsEmpty())
                    {
                        Sleep(100, _taskParam.Cts);
                    }
                    else
                    {
                        Logger.LogInformation("检测到F，启动秘境");
                        Simulation.SendInputEx.Keyboard.KeyPress(VK.VK_F);
                        break;
                    }
                }
            }
            finally
            {
                _postMessage.KeyUp(VK.VK_W);
            }
        });
    }
}