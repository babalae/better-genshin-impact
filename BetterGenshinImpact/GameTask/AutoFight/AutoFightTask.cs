using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight;

public class AutoFightTask : ISoloTask
{
    private readonly AutoFightParam _taskParam;

    private readonly CombatScriptBag _combatScriptBag;

    private CancellationTokenSource? _cts;

    public AutoFightTask(AutoFightParam taskParam)
    {
        _taskParam = taskParam;
        _combatScriptBag = CombatScriptParser.ReadAndParse(_taskParam.CombatStrategyPath);
    }

    public Task Start()
    {
        _cts = CancellationContext.Instance.Cts;

        LogScreenResolution();
        var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
        if (!combatScenes.CheckTeamInitialized())
        {
            throw new Exception("识别队伍角色失败");
        }
        var combatCommands = _combatScriptBag.FindCombatScript(combatScenes.Avatars);

        combatScenes.BeforeTask(_cts);

        // 战斗操作
        try
        {
            while (!_cts.Token.IsCancellationRequested)
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

        return Task.CompletedTask;
    }

    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
        if (gameScreenSize.Width * 9 != gameScreenSize.Height * 16)
        {
            Logger.LogWarning("游戏窗口分辨率不是 16:9 ！当前分辨率为 {Width}x{Height} , 非 16:9 分辨率的游戏可能无法正常使用自动战斗功能 !", gameScreenSize.Width, gameScreenSize.Height);
        }
    }
}
