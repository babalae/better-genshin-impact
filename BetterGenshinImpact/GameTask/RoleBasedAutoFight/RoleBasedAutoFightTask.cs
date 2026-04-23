using static BetterGenshinImpact.GameTask.Common.TaskControl;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.RoleBasedAutoFight;

public class RoleBasedAutoFightTask : ISoloTask
{
    public string Name => "智能定位战斗";

    private readonly RoleBasedAutoFightParam _taskParam;
    private CancellationToken _ct;

    public RoleBasedAutoFightTask(RoleBasedAutoFightParam taskParam)
    {
        _taskParam = taskParam;
    }

    public async Task Start(CancellationToken ct)
    {
        _ct = ct;
        TaskControl.Logger.LogInformation("启动基于定位的智能自动战斗...");

        var combatScenes = GetCombatScenesWithRetry();
        var cts2 = new CancellationTokenSource();
        ct.Register(cts2.Cancel);

        combatScenes.BeforeTask(cts2.Token);
        
        var strategy = new RoleBasedFightStrategy(_taskParam);
        
        TimeSpan fightTimeout = TimeSpan.FromSeconds(_taskParam.Timeout);
        Stopwatch timeoutStopwatch = Stopwatch.StartNew();

        var fightTask = Task.Run(async () =>
        {
            try
            {
                while (!cts2.Token.IsCancellationRequested)
                {
                    if (timeoutStopwatch.Elapsed > fightTimeout)
                    {
                        TaskControl.Logger.LogWarning("智能战斗超时退出。");
                        break;
                    }

                    await strategy.OnTickAsync(combatScenes, cts2.Token);
                    await Task.Delay(100, cts2.Token); // 降低 CPU 占用
                }
            }
            catch (TaskCanceledException)
            {
                // Ignored
            }
            catch (Exception ex)
            {
                TaskControl.Logger.LogError(ex, "智能战斗策略执行异常");
            }
        }, cts2.Token);

        await fightTask;
        combatScenes.AfterTask();
        TaskControl.Logger.LogInformation("智能自动战斗结束。");
    }

    private CombatScenes GetCombatScenesWithRetry()
    {
        const int maxRetries = 5;
        var retryDelayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
            if (combatScenes.CheckTeamInitialized())
            {
                return combatScenes;
            }

            if (attempt < maxRetries)
            {
                Thread.Sleep(retryDelayMs);
            }
        }
        throw new Exception("识别队伍角色失败（已重试 5 次）");
    }
}
