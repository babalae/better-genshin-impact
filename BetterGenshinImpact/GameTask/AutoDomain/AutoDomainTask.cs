using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
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
            Init();
            var combatScenes = new CombatScenes();
            combatScenes.InitializeTeam(GetContentFromDispatcher());
            // 1. 走到钥匙处启动
            // WalkToStartDomain();

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
            TaskTriggerDispatcher.Instance().SetOnlyCaptureMode(false);
            Logger.LogInformation("→ {Text}", "自动秘境结束");
        }
    }

    private void Init()
    {
        Logger.LogInformation("→ {Text} 设置总次数：{Cnt}", "自动秘境，启动！", _taskParam.DomainRoundNum);
        SystemControl.ActivateWindow();
        TaskTriggerDispatcher.Instance().SetOnlyCaptureMode(true);
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
                    var content = GetContentFromDispatcher();
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