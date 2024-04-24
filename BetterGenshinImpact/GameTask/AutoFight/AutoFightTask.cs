using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight;

public class AutoFightTask
{
    private readonly AutoFightParam _taskParam;

    private readonly CombatScriptBag _combatScriptBag;

    public AutoFightTask(AutoFightParam taskParam)
    {
        _taskParam = taskParam;
        _combatScriptBag = CombatScriptParser.ReadAndParse(_taskParam.CombatStrategyPath);
    }

    public async void Start()
    {
        var hasLock = false;
        try
        {
            hasLock = await TaskSemaphore.WaitAsync(0);
            if (!hasLock)
            {
                Logger.LogError("启动自动战斗功能失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
                return;
            }

            Init();
            var combatScenes = new CombatScenes().InitializeTeam(GetContentFromDispatcher());
            if (!combatScenes.CheckTeamInitialized())
            {
                throw new Exception("识别队伍角色失败");
            }
            var combatCommands = _combatScriptBag.FindCombatScript(combatScenes.Avatars);

            combatScenes.BeforeTask(_taskParam.Cts);

            // 战斗操作
            await Task.Run(() =>
            {
                try
                {
                    while (!_taskParam.Cts.Token.IsCancellationRequested)
                    {
                        // 通用化战斗策略
                        foreach (var command in combatCommands)
                        {
                            command.Execute(combatScenes);
                        }
                    }
                }
                catch (NormalEndException)
                {
                    Logger.LogInformation("战斗操作结束");
                }
                catch (Exception e)
                {
                    Logger.LogWarning(e.Message);
                    throw;
                }
            });
        }
        catch (NormalEndException)
        {
            Logger.LogInformation("手动中断自动秘境");
        }
        catch (Exception e)
        {
            Logger.LogError(e.Message);
            Logger.LogDebug(e.StackTrace);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.OnlyTrigger);
            TaskSettingsPageViewModel.SetSwitchAutoFightButtonText(false);
            Logger.LogInformation("→ {Text}", "自动战斗结束");

            if (hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }

    private void Init()
    {
        LogScreenResolution();
        Logger.LogInformation("→ {Text}", "自动战斗，启动！");
        SystemControl.ActivateWindow();
        TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.CacheCaptureWithTrigger);
        Sleep(TaskContext.Instance().Config.TriggerInterval * 5, _taskParam.Cts); // 等待缓存图像
    }

    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
        if (gameScreenSize.Width != 1920 || gameScreenSize.Height != 1080)
        {
            Logger.LogWarning("游戏窗口分辨率不是 1920x1080 ！当前分辨率为 {Width}x{Height} , 非 1920x1080 分辨率的游戏可能无法正常使用自动战斗功能 !", gameScreenSize.Width, gameScreenSize.Height);
        }
    }
}
