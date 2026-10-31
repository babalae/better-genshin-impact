using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using OpenCvSharp;
using BetterGenshinImpact.Helpers;
using Vanara;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>文本、JSON 与键鼠战斗共用的战后角色拾取和掉落物扫描。</summary>
public sealed class FightPickupHelper
{
    /// <summary>本次拾取使用的运行环境。</summary>
    private readonly GameRuntime _runtime;
    /// <summary>配置根。</summary>
    private readonly AllConfig _config;
    /// <summary>本次战斗的拾取设置。</summary>
    private readonly AutoFightParam _taskParam;
    /// <summary>素材缩放比例。</summary>
    private readonly double _assetScale;
    /// <summary>换队前返回主界面的任务。</summary>
    private readonly ReturnMainUiTask _returnMainUiTask;
    /// <summary>琴拾取识别的互斥锁。</summary>
    private static readonly object _pickLock = new();

    /// <summary>创建战后拾取辅助器，复用调用方的运行环境和参数。</summary>
    public FightPickupHelper(GameRuntime runtime, AllConfig config, AutoFightParam param)
    {
        _runtime = runtime;
        _config = config;
        _taskParam = param;
        _assetScale = runtime.SystemInfo.AssetScale;
        _returnMainUiTask = new ReturnMainUiTask(runtime);
    }

    /// <summary>执行万叶/琴拾取和扫描；JSON 策略保留读取队伍失败时跳过后续拾取的原有行为。</summary>
    public async Task RunAsync(CombatScenes combatScenes, bool timeOutFlag, string lastFightName,
        CancellationToken ct, bool stopOnPartyReadFailure = false)
    {
        if (_taskParam.KazuhaPickupEnabled)
        {
            // 队伍中存在万叶的时候使用一次长E
            var useCombatScenes = combatScenes; // 新增行
    	    var picker = useCombatScenes.SelectAvatar("枫原万叶") ?? useCombatScenes.SelectAvatar("琴");
            string? oldPartyName = null;
            if (RunnerContext.Instance.PartyName is not null)
            {
                oldPartyName = RunnerContext.Instance.PartyName;
            }
            else if(picker is null && !string.IsNullOrEmpty(_taskParam.KazuhaPartyName))
            {
                Logger.LogWarning("换队拾取：当前队伍名称为空，尝试读取！");
                await _runtime.Delay(1000, ct);
                await _returnMainUiTask.Start(ct);

                for (int attempt = 0; attempt < 6; attempt++)
                {
                    InputHub.Foreground.SimulateAction(GIActions.OpenPartySetupScreen);
                    var enterGameAppear = await NewRetry.WaitForElementAppear(
                        _runtime,
                        ElementRecognition.Get("PartyBtnChooseView", _runtime.SystemInfo),
                        () => { },
                        ct,
                        15,
                        500
                    );
                    if(attempt == 5 && !enterGameAppear)
                    {
                        Logger.LogWarning("换队拾取：读取队伍名称失败，跳过换队拾取步骤");
                        if (stopOnPartyReadFailure) return;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_taskParam.KazuhaPartyName)){
                await _runtime.Delay(1000, ct);

                //等待寻找2秒队伍按钮出现
                var timeWaitStart = 0;
                while(timeWaitStart < 6000)
                {
                    using var ra = _runtime.CaptureToRectArea();
                    var partyViewBtn = ra.Find(ElementRecognition.Get("PartyBtnChooseView", ra));
                    if (partyViewBtn.IsExist())
                    {
                        // OCR 当前队伍名称（无法单字，中间禁止空格）
                    // 读取OCR原始识别文本
                    var rawPartyName = ra.Find(new RecognitionObject
                    {
                        RecognitionType = RecognitionTypes.Ocr,
                        RegionOfInterest = new Rect(partyViewBtn.Right, partyViewBtn.Top, (int)(350 * _assetScale),
                            partyViewBtn.Height)
                    }).Text;

                    // 核心处理逻辑：1.空值兜底 2.去首尾空白 3.移除末尾的“口”字（仅最后一个是口才删）
                    if (string.IsNullOrWhiteSpace(rawPartyName))
                    {
                        oldPartyName = string.Empty;
                    }
                    else
                    {
                        //有概率把编辑图标识别为字符，并且含有空格或换行符，需要过滤
                        var tempName = rawPartyName
                            .Replace("\"", "")        // 移除所有双引号（核心新增，解决日志里的""问题）
                            .Replace("\r\n", "")      // 清理Windows换行符
                            .Replace("\r", "");   // 先清理所有双引号，避免引号干扰后续处理

                            // 核心逻辑：找到第一个换行符(\n)的位置，截断并删除换行+后面所有字符
                            int firstNewLineIndex = tempName.IndexOf('\n');
                            if (firstNewLineIndex != -1) // 存在换行符，截取到换行符前
                            {
                                tempName = tempName.Substring(0, firstNewLineIndex);
                            }

                            // 最后统一去首尾所有空白（空格、制表符、回车符\r等），得到纯净队伍名
                            oldPartyName = tempName.Trim();
                    }

                    // 后续原有逻辑不变
                    Logger.LogInformation("换队拾取：当前队伍名称读取为：{oldPartyName}", oldPartyName);
                    // 加在rawPartyName赋值后，打印原始文本的“原始形态”（转义符会显示）
                    Logger.LogDebug("OCR原始识别文本（含转义）：{rawPartyName}", rawPartyName);
                    RunnerContext.Instance.PartyName = oldPartyName;
                        // await _returnMainUiTask.Start(ct);
                        break;
                    }
                    await _runtime.Delay(200, ct);
                    timeWaitStart += 200;
                }
            }

            var switchPartyFlag = false;
            if (picker == null && !timeOutFlag &&!string.IsNullOrEmpty(_taskParam.KazuhaPartyName) && oldPartyName != _taskParam.KazuhaPartyName)
            {
                try
                {
                    Logger.LogInformation($"切换为拾取队伍：{_taskParam.KazuhaPartyName}");
                    var success = await new SwitchPartyTask(_runtime).Start(_taskParam.KazuhaPartyName, ct);
                    if (success)
                    {
                        Logger.LogInformation($"成功切换队伍为{_taskParam.KazuhaPartyName}");
                        switchPartyFlag = true;
                        RunnerContext.Instance.PartyName = _taskParam.KazuhaPartyName;
                        RunnerContext.Instance.ClearCombatScenes();
                        useCombatScenes = await RunnerContext.Instance.GetCombatScenes(_runtime, _config, ct);
                        picker = useCombatScenes.SelectAvatar("枫原万叶") ?? useCombatScenes.SelectAvatar("琴");
                    }
                }
                catch (Exception e)
                {
                    Logger.LogInformation("切换队伍异常，跳过此步骤！");
                }

            }

            if (picker != null)
            {
                InputHub.ReleaseAll();

                if (picker.Name == "枫原万叶")
                {
                    var time = TimeSpan.FromSeconds(picker.GetSkillCdSeconds());

                    // 如果配置了二次拾取，或者不满足跳过条件（上次是万叶且冷却时间>3秒），则执行拾取
                    bool shouldSkip = lastFightName == picker.Name && time.TotalSeconds > 3;
                    bool forcePickup = _taskParam.QinDoublePickUp;

                    if (forcePickup || !shouldSkip)
                    {
                        Logger.LogInformation("使用 枫原万叶-长E 拾取掉落物");
                        if (picker.TrySwitch(10))
                        {
                            await _runtime.Delay(100, ct);
                            // 等待元素战技 CD 就绪
                            await picker.WaitSkillCd(ct);

                            // 调用统一的辅助方法，模拟万叶长按 E 的输入序列：
                            // 包含释放鼠标左键前摇防卡键 -> E 键 KeyDown -> 延时 800ms -> E 键 KeyUp -> 延时 50ms
                            await _runtime.SimulateHoldElementalSkillAsync(800, ct);

                            // 调用统一的辅助方法，模拟 6 次鼠标左键连续点击：
                            // 配合万叶长 E 的滞空特性执行下落攻击，内部包含 try/finally 以保证取消任务时安全释放左键
                            await _runtime.SimulateMouseLeftClickLoopAsync(6, ct);

                            // 等待下落攻击和聚怪拾取动作彻底结束
                            await _runtime.Delay(1500, ct);
                            // 截图并更新技能最新冷却时间
                            picker.AfterUseSkill();
                        }
                    }
                    else
                    {
                        Logger.LogInformation("距最近一次万叶出招，时间过短，跳过此次万叶拾取！");
                    }
                }
                else if (picker.Name == "琴")
                {
                    Logger.LogInformation("使用 琴-长E 拾取掉落物");

                    var actionsToUse = PickUpCollectHandler.PickUpActions
                        .Where(action => action.StartsWith("琴-长E" + " ", StringComparison.OrdinalIgnoreCase))
                        .Select(action => action.Replace("琴-长E","琴", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    var find = _taskParam.QinDoublePickUp;
                    if (picker.TrySwitch(10))
                    {
                        await _runtime.Delay(100, ct);
                        foreach (var miningActionStr in actionsToUse)
                        {
                            var pickUpAction = CombatScriptParser.ParseContext(miningActionStr);

                            for (int i = 0; i < 2; i++)
                            {
                                await picker.WaitSkillCd(ct);
                                foreach (var command in pickUpAction.CombatCommands)
                                {
                                    command.Execute(useCombatScenes);
                                    //异步执行，防止卡顿
                                    Task.Run(() =>
                                    {
                                        if (Monitor.TryEnter(_pickLock))
                                        {
                                            try
                                            {
                                                if (find)
                                                {
                                                    using (var imagePick = _runtime.CaptureToRectArea())
                                                    {
                                                        if (imagePick.Find(AutoPickAssets.Get(imagePick, _config.AutoPickConfig.PickKey).PickRo).IsExist())
                                                        {
                                                            find = false;
                                                        }
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                Monitor.Exit(_pickLock);
                                            }
                                        }
                                        // 后面没代码了，不用写return？
                                    });
                                }

                                if (!find)
                                {
                                    break;
                                }

                                if (i == 0)
                                {
                                    Logger.LogInformation("自动拾取；尝试再次执行 琴-长E 拾取");
                                    // picker.LastSkillTime = DateTime.Now;不正确
                                    picker.AfterUseSkill();
                                }
                                else
                                {
                                    break;
                                }
                            }

                            InputHub.ReleaseAll();
                        }
                    }
                }
            }
            //切换过队伍的，需要再切回来
            if (switchPartyFlag && !string.IsNullOrEmpty(oldPartyName))
            {
                try
                {
                    Logger.LogInformation($"切换为原队伍：{oldPartyName}");
                    var success = await new SwitchPartyTask(_runtime).Start(oldPartyName, ct);
                    if (success)
                    {
                        Logger.LogInformation($"切换为原队伍{oldPartyName}");
                        switchPartyFlag = true;
                        RunnerContext.Instance.PartyName = oldPartyName;
                        RunnerContext.Instance.ClearCombatScenes();
                        await RunnerContext.Instance.GetCombatScenes(_runtime, _config, ct);

                    }
                }
                catch (Exception e)
                {
                    Logger.LogInformation("恢复原队伍失败，跳过此步骤！");
                }

            }
        }

        if (_taskParam is { PickDropsAfterFightEnabled: true } )
        {
            // 执行扫描掉落物光柱并靠近的功能
            await new ScanPickTask(_runtime, _config).Start(ct, _taskParam.PickDropsAfterFightSeconds);
        }

    }
}
