using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Service.Notification;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model.Enum;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;

/// <summary>
/// 对局
/// </summary>
public class Duel
{
    private readonly ILogger<Duel> _logger = App.GetLogger<Duel>();

    /// <summary>
    /// 本次对局使用的运行环境：分辨率检查、画面信息与遮罩清理都只使用它
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根，由创建方传入
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 本次对局的游戏操控对象，由 <see cref="AutoGeniusInvokationTask"/> 每次运行时创建
    /// </summary>
    private readonly GeniusInvokationControl _control;

    /// <summary>
    /// 创建一次对局
    /// </summary>
    /// <param name="runtime">本次对局使用的运行环境</param>
    /// <param name="config">配置根</param>
    /// <param name="control">本次对局的游戏操控对象</param>
    public Duel(GameRuntime runtime, AllConfig config, GeniusInvokationControl control)
    {
        _runtime = runtime;
        _config = config;
        _control = control;
    }

    public Character CurrentCharacter { get; set; } = default!;
    public Character[] Characters { get; set; } = new Character[4];

    /// <summary>
    /// 行动指令队列
    /// </summary>
    public List<ActionCommand> ActionCommandQueue { get; set; } = [];

    /// <summary>
    /// 当前回合数
    /// </summary>
    public int RoundNum { get; set; } = 1;

    /// <summary>
    /// 角色牌位置
    /// </summary>
    public List<Rect> CharacterCardRects { get; set; } = default!;

    /// <summary>
    /// 手牌数量
    /// </summary>
    public int CurrentCardCount { get; set; } = 0;

    /// <summary>
    /// 骰子数量
    /// </summary>
    public int CurrentDiceCount { get; set; } = 0;

    private int _keqingECount = 0;

    /// <summary>
    /// 对局因业务原因提前结束时的结果（对战结束、指令执行完毕、没有可切换角色）；未记录时为 null
    /// </summary>
    public SoloTaskResult? EndResult { get; private set; }

    public async Task RunAsync(CancellationToken ct)
    {
        await Task.Run(() => { Run(ct); }, ct);
    }

    public void Run(CancellationToken ct)
    {
        LogScreenResolution();
        try
        {
            Notify.Event(NotificationEvent.TcgStart).Success("自动七圣召唤启动");
            
            
            _control.Init(ct);

            // 对局准备 选择初始手牌
            _control.CommonDuelPrepare();

            // 获取角色区域
            try
            {
                CharacterCardRects = NewRetry.Do(() => _control.GetCharacterRects(), TimeSpan.FromSeconds(1.5), 3);
            }
            catch
            {
                // ignored
            }

            if (CharacterCardRects is not { Count: 3 })
            {
                CharacterCardRects = [];
                var defaultCharacterCardRects = _config.AutoGeniusInvokationConfig.DefaultCharacterCardRects;
                var assetScale = _runtime.SystemInfo.AssetScale;
                for (var i = 0; i < defaultCharacterCardRects.Count; i++)
                {
                    CharacterCardRects.Add(defaultCharacterCardRects[i].Multiply(assetScale));
                }

                _logger.LogInformation("获取角色区域失败，使用默认区域");
            }

            for (var i = 1; i < 4; i++)
            {
                Characters[i].Area = CharacterCardRects[i - 1];
            }

            // 出战角色
            CurrentCharacter = ActionCommandQueue[0].Character;
            CurrentCharacter.ChooseFirst(_control);

            // 开始执行回合
            while (true)
            {
                _logger.LogInformation("--------------第{RoundNum}回合--------------", RoundNum);
                ClearCharacterStatus(); // 清空单回合的异常状态
                if (RoundNum == 1)
                {
                    CurrentCardCount = 5;
                }
                else
                {
                    CurrentCardCount += 2;
                }

                CurrentDiceCount = 8;

                // 预计算本回合内的所有可能的元素 // 并调整识别骰子素材的顺序
                var elementSet = PredictionDiceType();

                // 0 投骰子
                _control.ReRollDice([.. elementSet]);

                // 等待到我的回合 // 投骰子动画时间是不确定的  // 可能是对方先手
                _control.WaitForMyTurn(this, 1000);

                // 开始执行行动
                while (true)
                {
                    // 没骰子了就结束行动
                    _logger.LogInformation("行动开始,当前骰子数[{CurrentDiceCount}],当前手牌数[{CurrentCardCount}]", CurrentDiceCount, CurrentCardCount);
                    if (CurrentDiceCount <= 0)
                    {
                        _logger.LogInformation("骰子已经用完");
                        _control.Sleep(2000);
                        break;
                    }

                    // 每次行动前都要检查当前角色
                    CurrentCharacter = _control.WhichCharacterActiveWithRetry(this);

                    // 行动前重新确认骰子数量
                    var diceCountFromOcr = _control.GetDiceCountByOcr();
                    if (diceCountFromOcr != -10)
                    {
                        var diceDiff = Math.Abs(CurrentDiceCount - diceCountFromOcr);
                        if (diceDiff is > 0 and <= 4)
                        {
                            _logger.LogInformation("可能存在场地牌影响了骰子数[{CurrentDiceCount}] -> [{DiceCountFromOcr}]", CurrentDiceCount, diceCountFromOcr);
                            CurrentDiceCount = diceCountFromOcr;
                        }
                        else if (diceDiff > 4)
                        {
                            _logger.LogWarning(" OCR识别到的骰子数[{DiceCountFromOcr}]和计算得出的骰子数[{CurrentDiceCount}]差距较大，舍弃结果", diceCountFromOcr, CurrentDiceCount);
                        }
                    }

                    var alreadyExecutedActionIndex = new List<int>();
                    var alreadyExecutedActionCommand = new List<ActionCommand>();
                    var i = 0;
                    for (i = 0; i < ActionCommandQueue.Count; i++)
                    {
                        var actionCommand = ActionCommandQueue[i];
                        // 指令中的角色未被打败、角色有异常状态 跳过指令
                        if (actionCommand.Character.IsDefeated || actionCommand.Character.StatusList?.Count > 0)
                        {
                            continue;
                        }

                        // 当前出战角色身上存在异常状态的情况下不执行本角色的指令
                        if (CurrentCharacter.StatusList?.Count > 0 &&
                            actionCommand.Character.Index == CurrentCharacter.Index)
                        {
                            continue;
                        }

                        // 1. 判断切人
                        if (CurrentCharacter.Index != actionCommand.Character.Index)
                        {
                            if (CurrentDiceCount >= 1)
                            {
                                actionCommand.SwitchLater(_control);
                                CurrentDiceCount--;
                                alreadyExecutedActionIndex.Add(-actionCommand.Character.Index); // 标记为已执行
                                var switchAction = new ActionCommand
                                {
                                    Character = CurrentCharacter,
                                    Action = ActionEnum.SwitchLater,
                                    TargetIndex = actionCommand.Character.Index
                                };
                                alreadyExecutedActionCommand.Add(switchAction);
                                _logger.LogInformation("→指令执行完成：{Action}", switchAction);
                                break;
                            }
                            else
                            {
                                _logger.LogInformation("骰子不足以进行下一步：切换角色 {CharacterIndex}", actionCommand.Character.Index);
                                break;
                            }
                        }

                        // 2. 判断使用技能
                        if (actionCommand.GetAllDiceUseCount() + actionCommand.DiceDelta > CurrentDiceCount)// 判断条件加上 DiceDelta 骰子变化
                        {
                            _logger.LogInformation("骰子不足以进行下一步：{Action}", actionCommand);
                            break;
                        }
                        else
                        {
                            bool useSkillRes = actionCommand.UseSkill(_control, this);
                            if (useSkillRes)
                            {
                                int realCost = actionCommand.GetAllDiceUseCount() + actionCommand.DiceDelta;
                                realCost = Math.Max(realCost, 0);
                                CurrentDiceCount -= realCost;
                                alreadyExecutedActionIndex.Add(i);
                                alreadyExecutedActionCommand.Add(actionCommand);
                                _logger.LogInformation("→指令执行完成：{Action}", actionCommand);
                                // 刻晴的E加手牌
                                if (actionCommand.Character.Name == "刻晴" && actionCommand.TargetIndex == 2)
                                {
                                    _keqingECount++;
                                    if (_keqingECount % 2 == 0)
                                    {
                                        CurrentCardCount -= 1;
                                    }
                                    else
                                    {
                                        CurrentCardCount += 1;
                                    }
                                }
                            }
                            else
                            {
                                _logger.LogWarning("→指令执行失败(可能是手牌不够)：{Action}", actionCommand);
                                _control.Sleep(1000);
                                _control.ClickGameWindowCenter();
                            }

                            break;
                        }
                    }

                    if (alreadyExecutedActionIndex.Count != 0)
                    {
                        foreach (var index in alreadyExecutedActionIndex)
                        {
                            if (index >= 0)
                            {
                                ActionCommandQueue.RemoveAt(index);
                            }
                        }

                        alreadyExecutedActionIndex.Clear();
                        // 等待对方行动完成 （开大的时候等待时间久一点）
                        var sleepTime = ComputeWaitForMyTurnTime(alreadyExecutedActionCommand);
                        _control.WaitForMyTurn(this, sleepTime);
                        alreadyExecutedActionCommand.Clear();
                    }
                    else
                    {
                        // 如果没有任何指令可以执行 则跳出循环
                        // TODO 也有可能是角色死亡/所有角色被冻结导致没有指令可以执行
                        //if (i >= ActionCommandQueue.Count)
                        //{
                        //    throw new DuelEndException("策略中所有指令已经执行完毕，结束自动打牌");
                        //}
                        _control.Sleep(2000);
                        break;
                    }

                    if (ActionCommandQueue.Count == 0)
                    {
                        throw new SoloTaskAbortException(SoloTaskResult.Success("策略中所有指令已经执行完毕，结束自动打牌"));
                    }
                }

                // 回合结束
                _control.Sleep(1000);
                _logger.LogInformation("我方点击回合结束");
                _control.RoundEnd();

                // 等待对方行动+回合结算
                _control.WaitOpponentAction(this);

                _runtime.MaskWindowDrawingBoard.ClearAll();
                RoundNum++;
            }
        }
        catch (TaskCanceledException ex)
        {
            throw;
        }
        catch (NormalEndException ex)
        {
            _logger.LogInformation("对局结束");
            // 业务原因的提前结束携带明确结果，记录下来交给任务返回；其余正常结束保持原来的吞没语义
            if (ex is SoloTaskAbortException abort)
            {
                EndResult = abort.Result;
            }
            // throw;
        }
        catch (System.Exception ex)
        {
            if (_config.DetailedErrorLogs)
            {
                _logger.LogError(ex.StackTrace);
            }
            throw;
        }
        
        Notify.Event(NotificationEvent.TcgEnd).Success("自动七圣召唤结束");
    }

    private HashSet<ElementalType> PredictionDiceType()
    {
        var actionUseDiceSum = 0;
        var elementSet = new HashSet<ElementalType>
        {
            ElementalType.Omni
        };
        for (var i = 0; i < ActionCommandQueue.Count; i++)
        {
            var actionCommand = ActionCommandQueue[i];

            // 角色未被打败的情况下才能执行
            if (actionCommand.Character.IsDefeated)
            {
                continue;
            }

            // 通过骰子数量判断是否可以执行

            // 1. 判断切人
            if (i > 0 && actionCommand.Character.Index != ActionCommandQueue[i - 1].Character.Index)
            {
                actionUseDiceSum++;
                if (actionUseDiceSum > CurrentDiceCount)
                {
                    break;
                }
                else
                {
                    // elementSet.Add(actionCommand.GetDiceUseElementType());
                    //executeActionIndex.Add(-actionCommand.Character.Index);
                }
            }

            // 2. 判断使用技能
            actionUseDiceSum += Math.Max(actionCommand.GetAllDiceUseCount() + actionCommand.DiceDelta, 0);
            if (actionUseDiceSum > CurrentDiceCount)
            {
                break;
            }
            else
            {
                elementSet.Add(actionCommand.GetDiceUseElementType());
                //executeActionIndex.Add(i);
            }
        }

        // 调整元素骰子识别素材顺序
        _control.SortActionPhaseDiceMats(elementSet);

        return elementSet;
    }

    public void ClearCharacterStatus()
    {
        foreach (var character in Characters)
        {
            character?.StatusList?.Clear();
        }
    }

    /// <summary>
    /// 根据前面执行的命令计算等待时间
    /// 大招等待15秒
    /// 快速切换等待3秒
    /// </summary>
    /// <param name="alreadyExecutedActionCommand"></param>
    /// <returns></returns>
    private int ComputeWaitForMyTurnTime(List<ActionCommand> alreadyExecutedActionCommand)
    {
        foreach (var command in alreadyExecutedActionCommand)
        {
            if (command.Action == ActionEnum.UseSkill && command.TargetIndex == 1)
            {
                return 15000;
            }

            // 莫娜切换等待3秒
            if (command.Character.Name == "莫娜" && command.Action == ActionEnum.SwitchLater)
            {
                return 3000;
            }
        }

        return 10000;
    }

    /// <summary>
    /// 获取角色切换顺序
    /// </summary>
    /// <returns></returns>
    public List<int> GetCharacterSwitchOrder()
    {
        List<int> orderList = [];
        for (var i = 0; i < ActionCommandQueue.Count; i++)
        {
            if (!orderList.Contains(ActionCommandQueue[i].Character.Index))
            {
                orderList.Add(ActionCommandQueue[i].Character.Index);
            }
        }

        return orderList;
    }

    ///// <summary>
    ///// 获取角色存活数量
    ///// </summary>
    ///// <returns></returns>
    //public int GetCharacterAliveNum()
    //{
    //    int num = 0;
    //    foreach (var character in Characters)
    //    {
    //        if (character != null && !character.IsDefeated)
    //        {
    //            num++;
    //        }
    //    }

    //    return num;
    //}

    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(_runtime.Window.Handle);
        if (gameScreenSize.Width != 1920 || gameScreenSize.Height != 1080)
        {
            _logger.LogWarning("游戏窗口分辨率不是 1920x1080 ！当前分辨率为 {Width}x{Height} , 非 1920x1080 分辨率的游戏可能无法正常使用自动七圣召唤 !", gameScreenSize.Width, gameScreenSize.Height);
            throw new System.Exception("游戏窗口分辨率不是 1920x1080");
        }
    }
}
