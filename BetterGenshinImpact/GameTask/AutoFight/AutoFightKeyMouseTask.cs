using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Recorder.Model;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>循环回放键鼠宏，在每轮结束后检测阵亡和战斗结束。</summary>
public sealed class AutoFightKeyMouseTask
    : ISoloTask<AutoFightParam, NoData>
{
    /// <summary>本次战斗的运行环境。</summary>
    private readonly GameRuntime _runtime;
    /// <summary>配置根。</summary>
    private readonly AllConfig _config;

    /// <summary>创建键鼠录制战斗任务。</summary>
    public AutoFightKeyMouseTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
    }

    /// <summary>运行键鼠战斗；外部取消和重试异常向上传递，内部超时正常结束。</summary>
    public async Task<SoloTaskResult<NoData>> RunAsync(AutoFightParam param, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // 坐标只适配一次；每轮回放使用同一份未被回放器改写的事件。
        KeyMouseScript script;
        await using (var stream = File.OpenRead(param.CombatStrategyPath))
        {
            script = await JsonSerializer.DeserializeAsync<KeyMouseScript>(stream, KeyMouseRecorder.JsonOptions, ct)
                     ?? throw new InvalidDataException("键鼠录制文件内容为空");
        }
        if (script.MacroEvents is not { Count: > 0 })
            return SoloTaskResult.Skipped("empty_macro", "键鼠录制中没有事件");
        script.Adapt(_runtime.SystemInfo.CaptureAreaRect, _runtime.SystemInfo.DpiScale);

        using var fightCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fightCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(0, param.Timeout)));
        // 旋转寻敌和并发索敌会改变录制视角或争用输入，此实现不启动这些能力。
        var finishConfig = new AutoFightTask.TaskFightFinishDetectConfig(param)
        {
            RotateFindEnemyEnabled = false,
        };
        ExperienceDetector? experienceDetector = null;
        var timedOut = false;
        try
        {
            AutoFightTask.FightStartTime = DateTime.Now;
            AutoFightTask.ResetSkipCheckCounter();
            AvatarRecognition.ClearLegendaryBarTracker();
            AvatarRecognition.SetCurrentAutoFightParam(param);
            AutoFightTask.FightStatusFlag = true;
            if (param.KazuhaPickupEnabled && param.ExpBasedPickupEnabled)
            {
                using var capture = _runtime.CaptureToRectArea();
                experienceDetector = new ExperienceDetector(_runtime,
                    AutoFightAssets.Get(capture).ExperienceRecognitionObjects, ct);
                experienceDetector.Start();
            }

            try
            {
                while (true)
                {
                    await KeyMouseMacroPlayer.PlayMacro(_runtime, script.MacroEvents, fightCts.Token);
                    InputHub.ReleaseAll();
                    ct.ThrowIfCancellationRequested();
                    fightCts.Token.ThrowIfCancellationRequested();

                    // 输入释放后等待游戏与截图更新，再识别阵亡、游泳等需要重试的状态。
                    await _runtime.Delay(100, fightCts.Token);
                    using (var capture = _runtime.CaptureToRectArea())
                        Avatar.ThrowWhenDefeated(_runtime, _config, capture, fightCts.Token);
                    if (param.FightFinishDetectEnabled && await AutoFightTask.CheckFightFinish(
                            _runtime, finishConfig, fightCts.Token, finishConfig.DelayTime, finishConfig.DetectDelayTime))
                        break;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && fightCts.IsCancellationRequested)
            {
                timedOut = true;
            }
            finally
            {
                fightCts.Cancel();
                InputHub.ReleaseAll();
                AutoFightTask.FightStatusFlag = false;
                AvatarRecognition.ClearCurrentAutoFightParam();
            }

            ct.ThrowIfCancellationRequested();
            // 与文本策略一致，经验值没有通过时仍执行扫描拾取。
            var useAvatarPickup = param.KazuhaPickupEnabled;
            if (experienceDetector != null)
            {
                for (var waitMs = 0; !experienceDetector.HasDetectedExperience && waitMs < 1100; waitMs += 100)
                    await _runtime.Delay(100, ct);
                await experienceDetector.StopAsync();
                useAvatarPickup &= experienceDetector.HasDetectedExperience;
            }
            if (useAvatarPickup)
            {
                // 宏不需要识别队伍；仅在战后角色拾取需要队伍时才创建并释放场景。
                using var scenes = CombatScenes.GetCombatScenesWithRetry(_runtime, _config);
                scenes.BeforeTask(ct);
                await new FightPickupHelper(_runtime, _config, param).RunAsync(scenes, timedOut, "", ct);
            }
            else if (param.PickDropsAfterFightEnabled)
            {
                await new ScanPickTask(_runtime, _config).Start(ct, param.PickDropsAfterFightSeconds);
            }
            ct.ThrowIfCancellationRequested();
            return SoloTaskResult.Unverified("战斗结束");
        }
        finally
        {
            // 初始化、回放和拾取的任意异常均不能留下按键或共享战斗状态。
            fightCts.Cancel();
            InputHub.ReleaseAll();
            AutoFightTask.FightStatusFlag = false;
            AvatarRecognition.ClearCurrentAutoFightParam();
            if (experienceDetector != null)
            {
                await experienceDetector.StopAsync();
                experienceDetector.Dispose();
            }
        }
    }
}
