using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public class AutoDomainTask
{
    private readonly AutoPickAssets _autoPickAssets = new();

    private AutoDomainParam _taskParam;

    private readonly PostMessageSimulator _simulator;

    public AutoDomainTask(AutoDomainParam taskParam)
    {
        _taskParam = taskParam;
        _simulator = AutoFightContext.Instance().Simulator;
    }

    public async void Start()
    {
        try
        {
            Init();
            var combatScenes = new CombatScenes().InitializeTeam(GetContentFromDispatcher());

            // 1. 走到钥匙处启动
            await WalkToStartDomain();

            // 2. 执行战斗（战斗线程、视角线程、检测战斗完成线程）
            await StartFight(combatScenes);

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
            TaskSettingsPageViewModel.SetSwitchAutoDomainButtonText(false);
            Logger.LogInformation("→ {Text}", "自动秘境结束");
        }
    }

    private void Init()
    {
        Logger.LogInformation("→ {Text} 设置总次数：{Cnt}", "自动秘境，启动！", _taskParam.DomainRoundNum);
        SystemControl.ActivateWindow();
        TaskTriggerDispatcher.Instance().SetOnlyCaptureMode(true);
    }

    /// <summary>
    /// 走到钥匙处启动
    /// </summary>
    private async Task WalkToStartDomain()
    {
        await Task.Run(() =>
        {
            _simulator.KeyDown(VK.VK_W);
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
                _simulator.KeyUp(VK.VK_W);
            }
        });
    }

    private Task StartFight(CombatScenes combatScenes)
    {
        CancellationTokenSource cts = new();
        combatScenes.BeforeTask(cts);
        // 战斗操作
        var combatTask = new Task(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                // 钟离
                combatScenes.Avatars[2].Switch();
                combatScenes.Avatars[3].Walk("s", 200);
                combatScenes.Avatars[2].UseSkill(hold: true);
                combatScenes.Avatars[1].UseBurst();
                combatScenes.Avatars[3].Walk("w", 150);

                // 4号位
                combatScenes.Avatars[3].Switch();
                combatScenes.Avatars[3].UseSkill();
                combatScenes.Avatars[3].UseBurst();

                // 夜兰
                combatScenes.Avatars[1].Switch();
                combatScenes.Avatars[1].UseSkill();
                combatScenes.Avatars[1].UseBurst();

                // 宵宫
                combatScenes.Avatars[0].Switch();
                combatScenes.Avatars[0].UseSkill();
                combatScenes.Avatars[0].Attack(8000);
            }
        }, cts.Token);

        // 视角操作

        // 对局结束检测
        var domainEndTask = DomainEndDetectionTask(cts);

        combatTask.Start();
        domainEndTask.Start();

        return Task.WhenAll(combatTask, domainEndTask);
    }

    /// <summary>
    /// 对局结束检测
    /// </summary>
    private Task DomainEndDetectionTask(CancellationTokenSource cts)
    {
        return new Task(() =>
        {
            while (true)
            {
                if (IsDomainEnd())
                {
                    cts.Cancel();
                    break;
                }

                Sleep(1000);
            }
        });
    }

    private bool IsDomainEnd()
    {
        var content = GetContentFromDispatcher();
        var endTipsRect = content.CaptureRectArea.Crop(AutoFightContext.Instance().FightAssets.EndTipsRect);
        var result = OcrFactory.Paddle.OcrResult(endTipsRect.SrcGreyMat);
        var rect = result.FindRectByText("自动退出");
        if (rect == Rect.Empty)
        {
            return false;
        }

        Logger.LogInformation("检测到秘境结束提示，结束秘境");
        return true;
    }
}