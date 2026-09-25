using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Compunet.YoloSharp;
using Compunet.YoloSharp.Data;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 队伍内的角色
/// </summary>
public class Avatar
{
    /// <summary>
    /// 配置文件中的角色信息
    /// </summary>
    public readonly CombatAvatar CombatAvatar;

    /// <summary>
    /// 角色名称 中文
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 队伍内序号
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 最近一次OCR识别出的CD到期时间
    /// 是原始 E 技能（编号 "01"）的 CD 记录，
    /// </summary>
    private DateTime OcrSkillCd { get; set; }

    /// <summary>
    /// 手动配置的技能CD，有它就不使用OCR,小于0为自动
    /// </summary>
    public double ManualSkillCd { get; set; }

    /// <summary>
    /// 最近一次使用元素战技的时间
    /// </summary>
    public DateTime LastSkillTime { get; set; }

    /// <summary>
    /// 元素爆发是否就绪
    /// </summary>
    public bool IsBurstReady { get; set; }

    /// <summary>
    /// 名字所在矩形位置
    /// </summary>
    public Rect NameRect { get; set; }

    /// <summary>
    /// 名字右边的编号位置
    /// </summary>
    public Rect IndexRect { get; set; }

    /// <summary>
    /// 任务取消令牌
    /// </summary>
    public CancellationToken Ct { get; set; }

    /// <summary>
    /// 战斗场景
    /// </summary>
    public CombatScenes CombatScenes { get; set; }

    /// <summary>
    /// 脱困方向数组（前/后/左/右）
    /// </summary>
    private static readonly GIActions[] UnstuckDirections =
    {
        GIActions.MoveForward,
        GIActions.MoveBackward,
        GIActions.MoveLeft,
        GIActions.MoveRight
    };

    private static readonly Random UnstuckRandom = new();

    private static readonly Lazy<BgiYoloPredictor> QBurstClassifierLazy = new(() =>
        App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiQClassify));

    private static readonly Lazy<BgiYoloPredictor> ESkillClassifierLazy = new(() =>
        App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiEClassify));

    public Avatar(CombatScenes combatScenes, string name, int index, Rect nameRect, double manualSkillCd = -1)
    {
        CombatScenes = combatScenes;
        Name = name;
        Index = index;
        NameRect = nameRect;
        CombatAvatar = DefaultAutoFightConfig.CombatAvatarMap[name];
        ManualSkillCd = manualSkillCd;
        AutoFightTask.FightStatusFlag = false;
    }


    /// <summary>
    /// 是否存在角色被击败
    /// 通过判断确认按钮
    /// </summary>
    /// <param name="region"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public static void ThrowWhenDefeated(ImageRegion region, CancellationToken ct)
    {
        if (Bv.IsInRevivePrompt(region))
        {
            Logger.LogWarning("检测到复苏界面，存在角色被击败，前往七天神像复活");
            // 先打开地图
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE); // NOTE: 此处按下Esc是为了关闭复苏界面，无需改键
            Sleep(600, ct);
            TpForRecover(ct, new RetryException("检测到复苏界面，存在角色被击败，前往七天神像复活"));
        }
        else if (AutoFightParam.SwimmingEnabled && AutoFightTask.FightStatusFlag && SwimmingConfirm(region))
        {
            if (AutoFightTask.FightWaypoint is not null)
            {
                // 二次确认：延迟 800ms 后重新截屏，避免同帧误判
                Sleep(800, ct);
                using var ra = CaptureToRectArea();
                if (!SwimmingConfirm(ra))
                {
                    return;
                }

                Logger.LogInformation("游泳检测：尝试回到战斗地点");

                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    // 保存原始 MoveMode，用于 finally 还原
                    var originalMoveMode = AutoFightTask.FightWaypoint.MoveMode;
                    // 链接外部取消令牌，确保外部取消时能及时响应；using 确保自动 Dispose
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                    try
                    {
                        var pathExecutor = new PathExecutor(cts.Token);

                        // FaceTo 朝向战斗点，超时 2 秒
                        cts.CancelAfter(2000);
                        pathExecutor.FaceTo(AutoFightTask.FightWaypoint).GetAwaiter().GetResult();

                        // 重置超时，MoveTo 超时 15 秒
                        cts.CancelAfter(15000);
                        // 使用 Climb 模式：MoveTo 内部对 Climb 模式跳过卡死脱困检测，避免水中 TrapEscaper 死循环
                        AutoFightTask.FightWaypoint.MoveMode = MoveModeEnum.Climb.Code;
                        Simulation.SendInput.Mouse.RightButtonDown();
                        pathExecutor.MoveTo(AutoFightTask.FightWaypoint).GetAwaiter().GetResult();
                        Logger.LogInformation("游泳检测：移动结束");
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.LogWarning("游泳检测：回到战斗地点超时");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "游泳检测：回到战斗地点异常");
                    }
                    finally
                    {
                        // 确保所有资源和状态在任何路径都被正确清理
                        cts.Cancel(); // 终止 PathExecutor 内部截屏循环
                        AutoFightTask.FightWaypoint.MoveMode = originalMoveMode;
                        AutoFightTask.FightWaypoint = null;
                        Simulation.SendInput.Mouse.RightButtonUp();
                        Simulation.ReleaseAllKey();
                    }
                }

                using var bitmap2 = CaptureToRectArea();
                if (!SwimmingConfirm(bitmap2))
                {
                    Logger.LogInformation("游泳检测：游泳脱困成功");
                    return;
                }

                Logger.LogWarning("游泳检测：回到战斗地点失败");
            }

            Logger.LogWarning("战斗过程检测到游泳，前往七天神像重试");
            TpForRecover(ct, new RetryException("战斗过程检测到游泳，前往七天神像重试"));
        }
    }

    /// <summary>
    /// 游泳检测（色块连通性检测）
    /// 游泳时右下角会出现鼠标图标，带有黄色色块，不受改按键影响
    /// </summary>
    private static bool SwimmingConfirm(Region region)
    {
        var imageRegion = region.ToImageRegion();
        using var cropped = imageRegion.DeriveCrop(1819, 1025, 9, 11);
        using var mask = OpenCvCommonHelper.Threshold(cropped.SrcMat, new Scalar(242, 223, 39), new Scalar(255, 233, 44));
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();

        var numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
            connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

        return numLabels > 1;
    }

    /// <summary>
    /// tp 到七天神像恢复
    /// </summary>
    /// <param name="ct"></param>
    /// <param name="retryException"></param>
    /// <exception cref="RetryException"></exception>
    public static void TpForRecover(CancellationToken ct, RetryException retryException)
    {
        try
        {
            // tp 到七天神像复活。保留等待取消能力，同时避免 Wait 将原异常包装为 AggregateException。
            new TpTask(ct).TpToStatueOfTheSeven().WaitAsync(ct).GetAwaiter().GetResult();
            Logger.LogInformation("血量恢复完成。【设置】-【七天神像设置】可以修改回血相关配置。");
        }
        catch (NormalEndException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 一旦进入恢复流程，就不能返回原战斗循环；否则会从七天神像向旧战斗点执行回点。
            Logger.LogWarning(ex, "前往七天神像恢复时发生异常，将重试当前任务");
        }

        throw retryException;
    }

    /// <summary>
    /// 切换到本角色
    /// 切换cd是1秒，如果切换失败，会尝试再次切换，最多尝试5次
    /// </summary>
    public void Switch()
    {
        var context = new AvatarActiveCheckContext();
        for (var i = 0; i < 30; i++)
        {
            if (Ct is { IsCancellationRequested: true })
            {
                return;
            }

            using var region = CaptureToRectArea();
            ThrowWhenDefeated(region, Ct);

            // 切换成功
            if (CombatScenes.GetActiveAvatarIndex(region, context) == Index)
            {
                return;
            }

            SimulateSwitchAction(Index);
            // Debug.WriteLine($"切换到{Index}号位");
            // Cv2.ImWrite($"log/切换.png", region.SrcMat);

            // 第10次重试时，战斗状态下执行脱困动作
            if (i == 10 && AutoFightTask.FightStatusFlag)
            {
                PerformUnstuckAction(Ct);
            }

            Sleep(250, Ct);
        }
    }

    /// <summary>
    /// 尝试切换到本角色
    /// </summary>
    /// <param name="tryTimes"></param>
    /// <param name="needLog"></param>
    /// <returns></returns>
    public bool TrySwitch(int tryTimes = 4)
    {
        var context = new AvatarActiveCheckContext();
        var shouldResendSwitchAction = CombatScenes.UpdateTrySwitchTarget(Index);
        for (var i = 0; i < tryTimes; i++)
        {
            if (Ct is { IsCancellationRequested: true })
            {
                return false;
            }

            using var region = CaptureToRectArea();
            ThrowWhenDefeated(region, Ct);

            if (CombatScenes.GetActiveAvatarIndex(region, context) == Index)
            {
                // 目标变化且本次尚未实际发送切换按键时补发，避免识别假阳性。
                if (shouldResendSwitchAction)
                {
                    SimulateSwitchAction(Index);
                }
                return true;
            }
            else
            {
                if (i == tryTimes - 1 && tryTimes == 4) //默认状态，没有特意设置重试次数的情况下，最后一次重试失败才输出日志
                {
                    Logger.LogWarning("切换角色失败，最后一次尝试，当前角色编号:{CurrentIndex}，期望角色编号:{ExpectedIndex}", CombatScenes.GetActiveAvatarIndex(region, context), Index);
                }
                else
                {
                    // 特意需要脱困情形下，会设置重试次数激活脱困检测，第10次重试时(2.5秒切换失败，超过角色的大招动画时间)，如在盾奶位功能中次数会到第十次。
                    if (i == 9 && AutoFightTask.FightStatusFlag)
                    {
                        PerformUnstuckAction(Ct);
                    }
                }
            }

            SimulateSwitchAction(Index);
            shouldResendSwitchAction = false;

            Sleep(250, Ct);
        }

        Logger.LogWarning("切换角色失败:{Name}", Name);

        return false;
    }

    private void SimulateSwitchAction(int index)
    {
        Simulation.SendInput.SimulateAction(GIActions.Drop); //反正会重试就不等落地了
        switch (index)
        {
            case 1:
                Simulation.SendInput.SimulateAction(GIActions.SwitchMember1);
                break;
            case 2:
                Simulation.SendInput.SimulateAction(GIActions.SwitchMember2);
                break;
            case 3:
                Simulation.SendInput.SimulateAction(GIActions.SwitchMember3);
                break;
            case 4:
                Simulation.SendInput.SimulateAction(GIActions.SwitchMember4);
                break;
            case 5:
                Simulation.SendInput.SimulateAction(GIActions.SwitchMember5);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// 战斗中切换角色卡住时的脱困动作：跳跃 → 随机方向移动+切换 → 攻击 → 释放按键
    /// </summary>
    private void PerformUnstuckAction(CancellationToken ct)
    {
        var direction = UnstuckDirections[UnstuckRandom.Next(4)];
        Logger.LogWarning("切换角色卡住，执行脱困（方向：{Dir}）", direction);

        Simulation.SendInput.SimulateAction(GIActions.Jump);
        Sleep(200, ct);
        Simulation.SendInput.SimulateAction(direction, KeyType.KeyDown);
        SimulateSwitchAction(Index);
        Sleep(1000, ct);
        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
        Simulation.ReleaseAllKey();
    }

    /// <summary>
    /// 切换到本角色
    /// 切换cd是1秒，如果切换失败，会尝试再次切换，最多尝试5次
    /// </summary>
    public void SwitchWithoutCts()
    {
        var context = new AvatarActiveCheckContext();
        for (var i = 0; i < 10; i++)
        {
            using var region = CaptureToRectArea();
            ThrowWhenDefeated(region, Ct);

            if (CombatScenes.GetActiveAvatarIndex(region, context) == Index)
            {
                return;
            }

            SimulateSwitchAction(Index);

            Sleep(250);
        }
    }

    /// <summary>
    /// 是否出战状态
    /// </summary>
    /// <returns></returns>
    public bool IsActive(ImageRegion region)
    {
        if (IndexRect == default)
        {
            throw new Exception("IndexRect为空");
        }
        else
        {
            var white = IsIndexRectWhite(region, IndexRect);
            return !white;
        }
    }

    private bool IsIndexRectWhite(ImageRegion region, Rect rect)
    {
        // 剪裁出IndexRect区域
        using var indexRa = region.DeriveCrop(rect);
        using var mat = indexRa.CacheGreyMat;
        var count = OpenCvCommonHelper.CountGrayMatColor(mat, 251, 255);
        if (count * 1.0 / (mat.Width * mat.Height) > 0.5)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 是否出战状态
    /// </summary>
    /// <returns></returns>
    [Obsolete]
    public bool IsActiveNoIndexRect(ImageRegion region)
    {
        // 通过寻找右侧人物编号来判断是否出战
        if (IndexRect == default)
        {
            var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
            // 剪裁出队伍区域
            var teamRa = region.DeriveCrop(AutoFightAssets.Get(region).TeamRect);
            var blockX = NameRect.X + NameRect.Width * 2 - 10;
            var block = teamRa.DeriveCrop(new Rect(blockX, NameRect.Y, teamRa.Width - blockX, NameRect.Height * 2));
            // Cv2.ImWrite($"block_{Name}.png", block.SrcMat);
            // 取白色区域
            var bMat = OpenCvCommonHelper.Threshold(block.SrcMat, new Scalar(255, 255, 255), new Scalar(255, 255, 255));
            // Cv2.ImWrite($"block_b_{Name}.png", bMat);
            // 矩形识别
            Cv2.FindContours(bMat, out var contours, out _, RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);
            if (contours.Length > 0)
            {
                var boxes = contours.Select(Cv2.BoundingRect)
                    .Where(w => w.Width >= 20 * assetScale && w.Height >= 18 * assetScale)
                    .OrderByDescending(w => w.Width).ToList();
                if (boxes.Count is not 0)
                {
                    IndexRect = boxes.First();
                    return false;
                }
            }
        }
        else
        {
            // 剪裁出IndexRect区域
            var teamRa = region.DeriveCrop(AutoFightAssets.Get(region).TeamRect);
            var blockX = NameRect.X + NameRect.Width * 2 - 10;
            var indexBlock = teamRa.DeriveCrop(new Rect(blockX + IndexRect.X, NameRect.Y + IndexRect.Y, IndexRect.Width,
                IndexRect.Height));
            // Cv2.ImWrite($"indexBlock_{Name}.png", indexBlock.SrcMat);
            var count = OpenCvCommonHelper.CountGrayMatColor(indexBlock.CacheGreyMat, 255);
            if (count * 1.0 / (IndexRect.Width * IndexRect.Height) > 0.5)
            {
                return false;
            }
        }

        Logger.LogInformation("{Name} 当前出战", Name);
        return true;
    }

    /// <summary>
    /// 普通攻击
    /// </summary>
    /// <param name="ms">攻击时长，建议是200的倍数</param>
    public void Attack(int ms = 0)
    {
        while (ms >= 0)
        {
            if (Ct is { IsCancellationRequested: true })
            {
                return;
            }

            Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
            ms -= 200;
            Sleep(200, Ct);
        }
    }

    /// <summary>
    /// 使用元素战技 E
    /// </summary>
    public void UseSkill(bool hold = false)
    {
        if (AvatarSpecialAction.ExecuteSpecializedAction(this, "UseSkill", Name, new ActionArgs(Hold: hold))) return;

        if (Ct is { IsCancellationRequested: true })
        {
            return;
        }

        if (hold)
        {
            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.Hold);
        }
        else
        {
            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
        }

        // 0.2 秒内循环检测（分类器优先、确认冷却后才 OCR），直到识别到 CD 或超时
        var recordedCd = 0d;
        var deadline = DateTime.UtcNow.AddMilliseconds(200);
        while (recordedCd <= 0 && DateTime.UtcNow < deadline)
        {
            Sleep(35, Ct);

            using (var region = CaptureToRectArea())
            {
                var cd = AfterUseSkill(region);
                recordedCd = ESkillCdTracker.Record(Name, cd);
            }
        }

        // 慢速设备兜底：轮询中的检测可能跨过截止时间（如开始于0.035s、结束时已0.25s），CD 数字恰好在其后才出现，超时后再补检一次
        if (recordedCd <= 0)
        {
            using (var region = CaptureToRectArea())
            {
                var cd = AfterUseSkill(region);
                recordedCd = ESkillCdTracker.Record(Name, cd);
            }
        }

        if (recordedCd <= 0)
        {
            recordedCd = ESkillCdTracker.ApplyFallback(Name);
        }

        if (recordedCd > 0)
        {
            Logger.LogInformation(hold ? "{Name} 长按元素战技，cd:{Cd} 秒" : "{Name} 点按元素战技，cd:{Cd} 秒", Name,
                Math.Round(recordedCd, 2));
        }
    }

    /// <summary>
    /// 使用完元素战技的回调,注意,不会在这里检测是不是需要跑七天神像 <br/>
    /// UseSkill 方法内会调用，如果没有使用UseSkill但是释放了技能之后记得调用一下这个方法
    /// </summary>
    /// <returns>当前技能CD</returns>
    public double AfterUseSkill(ImageRegion? givenRegion = null)
    {
        LastSkillTime = DateTime.UtcNow;
        if (ManualSkillCd > 0)
        {
            return GetSkillCdSeconds();
        }

        // 调用方传入的截图由调用方负责释放，方法只释放自己创建的
        // 公开契约：0 表示未记录到 CD（就绪或未读到数字），供调用方重试/兜底
        if (givenRegion != null)
        {
            return ReadSkillCdFromScreenshot(givenRegion).Cd ?? 0;
        }

        using var region = CaptureToRectArea();
        return ReadSkillCdFromScreenshot(region).Cd ?? 0;
    }

    /// <summary>
    /// 从截图中判定 E 技能状态并读取剩余 CD：先用 <see cref="IsESkillReadyByClassify"/> 分类判定，
    /// 仅在明确判定 Cooldown 时才 OCR 读取具体剩余秒数；就绪返回 0；
    /// 未知（置信度不足/角色不匹配）时不 OCR，避免在不确定截图归属时误读并污染记录。
    /// 注意 Cooldown 状态下 OCR 可能读不到数字（<see cref="Cd"/> 为 <c>null</c>），调用方须以 State 为准。
    /// </summary>
    private (SkillCdState State, double? Cd) ReadSkillCdFromScreenshot(ImageRegion imageRegion, bool onlyCode01Ready = true)
    {
        var (State, Code) = IsESkillReadyByClassify(imageRegion, onlyCode01Ready);
        if (State == SkillCdState.Ready)
        {
            // 只有原始 E（编号 "01"）的 Ready 才清零 OcrSkillCd
            // 特殊状态技能（02/03...）的 Ready 不清零
            if (string.Equals(Code, "01", StringComparison.OrdinalIgnoreCase))
            {
                OcrSkillCd = DateTime.UtcNow;
            }
            return (SkillCdState.Ready, 0);
        }

        // 仅在分类器明确判定 Cooldown 时才 OCR 读具体秒数
        // Unknown（置信度不足/角色不匹配）时截图归属存疑，不 OCR 避免误读污染记录
        if (State == SkillCdState.Cooldown)
        {
            var cd = ReadSkillCdByOcr(imageRegion);
            if (cd > 0)
            {
                ESkillCdTracker.Record(Name, cd.Value);
            }
            return (State, cd);
        }

        // Unknown
        return (State, null);
    }

    /// <summary>
    /// 根据Ocr识别元素战技是否正在CD中
    /// 右下 267x132
    /// 77x77
    /// </summary>
    /// <returns>识别到的剩余 CD 秒数；未读到数字时为 <c>null</c>（读到有效 CD 时会记入 <see cref="OcrSkillCd"/>）</returns>
    private double? ReadSkillCdByOcr(ImageRegion imageRegion)
    {
        using var eRa = imageRegion.DeriveCrop(AutoFightAssets.Get(imageRegion).ECooldownRect);
        using var eRaWhite = OpenCvCommonHelper.InRangeHsv(eRa.SrcMat, new Scalar(0, 0, 235), new Scalar(0, 25, 255));
        var text = OcrFactory.Paddle.OcrWithoutDetector(eRaWhite);
        var cd = StringUtils.TryParseDouble(text);
        if (cd is > 0 && cd <= CombatAvatar.SkillCd)
        {
            OcrSkillCd = DateTime.UtcNow.AddSeconds(cd);
            return cd;
        }

        return null;
    }


    /// <summary>
    /// 使用元素爆发 Q
    /// Q释放等待 2s 超时认为没有Q技能
    /// </summary>
    public void UseBurst()
    {
        using (AvatarRecognition.BeginExclusiveOperation())
        {
            // CD 中立即返回，其余场景尝试释放
            using var region1 = CaptureToRectArea();
            if (IsBurstReadyByClassify(region1) != BurstReadyState.Ready)
            {
                // Logger.LogInformation("Q在CD，跳过");
                return;
            }

            for (var i = 0; i < 10; i++)
            {
                if (Ct is { IsCancellationRequested: true })
                {
                    return;
                }

                // Logger.LogInformation("释放Q");
                Simulation.SendInput.SimulateAction(GIActions.ElementalBurst);
                Sleep(200, Ct);

                using var region = CaptureToRectArea();
                ThrowWhenDefeated(region, Ct);

                if (!PartyAvatarSideIndexHelper.HasAnyIndexRect(region))
                {
                    // 找不到角色编号块意味者技能释放成功
                    Sleep(1500, Ct);
                    return;
                }
                else
                {
                    // 找到编号块判断是否进入了CD，四星角色没有大招动画
                    if (IsBurstReadyByClassify(region) != BurstReadyState.Ready)
                    {
                        // Logger.LogInformation("释放Q后检查到CD");
                        Sleep(1500, Ct);
                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 通过 ONNX 分类器判断当前场上角色的Q爆发是否就绪（仅对场上角色有效）
    /// </summary>
    internal static BurstReadyState IsBurstReadyByClassify(ImageRegion imageRegion)
    {
        using var qRa = imageRegion.DeriveCrop(AutoFightAssets.Get(imageRegion).QRectForClassify);
        var result = QBurstClassifierLazy.Value.Predictor.Classify(qRa.CacheImage);
        var topClass = result.GetTopClass();
        var topClassName = topClass.Name.Name;
        // Logger.LogInformation("Q技能冷却分类：{ClassName}，置信度：{Confidence:F2}", topClassName, topClass.Confidence);

        // 置信度不足时，直接返回未知，避免误判导致漏放/乱放
        if (topClass.Confidence <= 0.7)
        {
            // Logger.LogInformation("Q技能冷却分类置信度不足：{Confidence:F2}，类别：{ClassName}", topClass.Confidence, topClassName);
            return BurstReadyState.Unknown;
        }

        if (topClassName.Contains("cd 1", StringComparison.OrdinalIgnoreCase))
        {
            return BurstReadyState.Cooldown;
        }

        if (topClassName.Contains("energy 1 cd 0", StringComparison.OrdinalIgnoreCase))
        {
            return BurstReadyState.Ready;
        }

        return BurstReadyState.Unknown;
    }

    /// <summary>
    /// 通过 ONNX 分类器判断当前场上角色的E技能（元素战技）是否就绪（仅对场上角色有效）
    /// </summary>
    /// <param name="onlyCode01Ready">
    /// 编号段（第 3 段）策略：
    /// <list type="bullet">
    /// <item><c>true</c>（默认）：仅 "01" 才视为就绪，其他编号（E 技能开启后的特殊状态图标）保守视为冷却中。</item>
    /// <item><c>false</c>：任意编号都参与就绪判定，不因编号挡掉 Ready。</item>
    /// </list>
    /// </param>
    public (SkillCdState State, string? Code) IsESkillReadyByClassify(ImageRegion imageRegion, bool onlyCode01Ready = true)
    {
        using var eRa = imageRegion.DeriveCrop(AutoFightAssets.Get(imageRegion).ERectForClassify);
        var result = ESkillClassifierLazy.Value.Predictor.Classify(eRa.CacheImage);
        var topClass = result.GetTopClass();
        var topClassName = topClass.Name.Name;
        // Logger.LogInformation("E技能就绪分类：{ClassName}，置信度：{Confidence:F2}", topClassName, topClass.Confidence);

        (SkillCdState State, string? Code) classifyResult;

        // 置信度不足时，直接返回未知，避免误判导致漏放/乱放
        if (topClass.Confidence <= 0.7)
        {
            // Logger.LogInformation("E技能就绪分类置信度不足：{Confidence:F2}，类别：{ClassName}", topClass.Confidence, topClassName);
            classifyResult = (SkillCdState.Unknown, null);
        }
        else
        {
            // e_classify_sim 模型实际输出类别名格式: "<前缀> <角色名> <编号> <状态>"，
            // 实测样本: "S Arlecchino 01 nocd" (confidence 1.0) 表示无冷却/就绪。
            // 第 2 段为角色英文名，与当前 Avatar 的 CombatAvatar.NameEn 做不区分大小写比对；
            // 不匹配说明分类结果不属于本角色（模型未覆盖该角色或截图与当前 Avatar 错位），返回未知避免误判。
            var parts = topClassName.Split(' ');
            if (parts.Length < 4 ||
                !string.Equals(parts[1], CombatAvatar.NameEn, StringComparison.OrdinalIgnoreCase))
            {
                classifyResult = (SkillCdState.Unknown, null);
            }
            else
            {
                // 编号段（第 3 段）必须为 "01" 才视为就绪；其他编号对应 E 技能开启后的特殊状态图标，
                // 保守视为冷却中，避免在该状态下误判为就绪而错放技能。
                if (onlyCode01Ready &&
                    !string.Equals(parts[2], "01", StringComparison.OrdinalIgnoreCase))
                {
                    classifyResult = (SkillCdState.Cooldown, parts[2]);
                }
                else if (topClassName.Contains("nocd", StringComparison.OrdinalIgnoreCase))
                {
                    classifyResult = (SkillCdState.Ready, parts[2]);
                }
                // 冷却状态实测样本: "S Arlecchino 01 cd"
                // 顺序重要：nocd 含 cd 子串，必须先判断 nocd 再判断 cd。
                else if (topClassName.Contains("cd", StringComparison.OrdinalIgnoreCase))
                {
                    classifyResult = (SkillCdState.Cooldown, parts[2]);
                }
                else
                {
                    classifyResult = (SkillCdState.Unknown, parts[2]);
                }
            }
        }

        DrawESkillClassifyResult(imageRegion, classifyResult.State, classifyResult.Code);
        return classifyResult;
    }

    /// <summary>
    /// 在 E 技能图标上方绘制 <see cref="IsESkillReadyByClassify"/> 的识别结果（就绪/冷却/未知 + 编号）。
    /// 仅在遮罩窗口存在且开启"显示识别结果"时可见（MaskWindow 渲染时统一过滤）。
    /// 遮罩窗口未初始化（如单元测试环境）时直接跳过，不影响调用方。
    /// </summary>
    private void DrawESkillClassifyResult(ImageRegion imageRegion, SkillCdState state, string? code)
    {
        if (View.MaskWindow.InstanceNullable() == null)
        {
            return;
        }

        var eRect = AutoFightAssets.Get(imageRegion).ERectForClassify;
        var stateText = state switch
        {
            SkillCdState.Ready => "就绪",
            SkillCdState.Cooldown => "冷却",
            _ => "未知",
        };

        if (!string.IsNullOrEmpty(code))
        {
            stateText += $"({code})";
        }

        View.Drawable.VisionContext.Instance().DrawContent.PutOrRemoveTextList("ESkillClassify",
            [new View.Drawable.TextDrawable(stateText, new System.Windows.Point(eRect.X, eRect.Y - 24))]);
    }

    // /// <summary>
    // /// 元素爆发是否正在CD中
    // /// 右下 157x165
    // /// 110x110
    // /// </summary>
    // public double GetBurstCurrentCd(CaptureContent content)
    // {
    //     var qRa = content.CaptureRectArea.Crop(AutoFightAssets.Get(content.CaptureRectArea).QRect);
    //     var text = OcrFactory.Paddle.Ocr(qRa.SrcGreyMat);
    //     return StringUtils.TryParseDouble(text);
    // }

    /// <summary>
    /// 冲刺
    /// </summary>
    public void Dash(int ms = 0)
    {
        if (Ct is { IsCancellationRequested: true })
        {
            return;
        }

        if (ms == 0)
        {
            ms = 200;
        }

        Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyDown);
        Sleep(ms); // 冲刺不能被cts取消
        Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
    }

    public void Walk(string key, int ms)
    {
        if (Ct is { IsCancellationRequested: true })
        {
            return;
        }

        User32.VK vk = User32.VK.VK_NONAME;
        if (key == "w")
        {
            vk = GIActions.MoveForward.ToActionKey().ToVK();
        }
        else if (key == "s")
        {
            vk = GIActions.MoveBackward.ToActionKey().ToVK();
        }
        else if (key == "a")
        {
            vk = GIActions.MoveLeft.ToActionKey().ToVK();
        }
        else if (key == "d")
        {
            vk = GIActions.MoveRight.ToActionKey().ToVK();
        }

        if (vk == User32.VK.VK_NONAME)
        {
            return;
        }

        Simulation.SendInput.Keyboard.KeyDown(vk);
        Sleep(ms); // 行走不能被cts取消
        Simulation.SendInput.Keyboard.KeyUp(vk);
    }

    /// <summary>
    /// 移动摄像机
    /// </summary>
    /// <param name="pixelDeltaX">负数是左移，正数是右移</param>
    /// <param name="pixelDeltaY"></param>
    public void MoveCamera(int pixelDeltaX, int pixelDeltaY)
    {
        Simulation.SendInput.Mouse.MoveMouseBy(pixelDeltaX, pixelDeltaY);
    }

    /// <summary>
    /// 等待
    /// </summary>
    /// <param name="ms"></param>
    public void Wait(int ms)
    {
        Sleep(ms); // 由于存在宏操作，等待不应被cts取消
    }

    /// <summary>
    /// 等待完成
    /// </summary>
    public void Ready()
    {
        Sleep(10, Ct);

        for (int i = 0; i < 20; i++)
        {
            if (Ct is { IsCancellationRequested: true })
            {
                return;
            }

            using var region = CaptureToRectArea();
            // 等待角色编号块出现
            if (PartyAvatarSideIndexHelper.HasAnyIndexRect(region))
            {
                region.Dispose();
                return;
            }

            Sleep(150, Ct);
        }
    }

    /// <summary>
    ///
    /// 根据cd推算E技能是否好了
    /// </summary>
    /// <param name="skillCd">强制指定技能CD</param>
    /// <param name="printLog">log是否输出</param>
    /// <returns>是否好了</returns>
    public bool IsSkillReady(bool printLog = false)
    {
        var cd = GetSkillCdSeconds();
        if (cd > 0)
        {
            if (printLog)
            {
                Logger.LogInformation("{Name}的E技能未准备好,CD还有{Seconds}秒", Name, Math.Round(cd, 2));
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// 纯 OCR 视角的 E 技能三态：只依据 OCR 记录（<see cref="OcrSkillCd"/>）与最近一次使用时间
    /// （<see cref="LastSkillTime"/>）的相对新旧判断记录可信度。
    /// </summary>
    private SkillCdState GetSkillCdStateFromRecord()
    {
        // OCR 记录晚于最近一次使用 → 记录可信
        if (OcrSkillCd > LastSkillTime)
        {
            return DateTime.UtcNow > OcrSkillCd ? SkillCdState.Ready : SkillCdState.Cooldown;
        }

        // 从未使用过（默认时间）→ 就绪；否则记录是过期的（用过但没读到 CD）
        return LastSkillTime == default ? SkillCdState.Ready : SkillCdState.Unknown;
    }

    /// <summary>
    /// 获取 E 技能的综合三态冷却状态（就绪 / 冷却中 / 未知）。
    /// 优先级：<see cref="ManualSkillCd"/> 手动配置 → 场上角色走视觉判定（<see cref="IsESkillReadyByClassify"/>）→ 复用截图跑 OCR 读 CD → <see cref="GetSkillCdStateFromRecord"/> OCR 视角推算。
    /// 视觉判定仅对场上角色有效；后台角色或视觉返回 Unknown 时降级到 OCR 推算。
    /// </summary>
    /// <param name="onlyCode01Ready">
    /// 透传给 <see cref="IsESkillReadyByClassify"/>：
    /// <c>true</c>（默认）仅原始 E（编号 "01"）就绪才返回 Ready，特殊状态技能（02/03...）视为 Cooldown；
    /// <c>false</c> 时特殊状态技能就绪也返回 Ready，但清零 <see cref="OcrSkillCd"/> 仍只对编号 "01" 生效（特殊状态技能 Ready 不污染原始 E 的 OCR 视角记录）。
    /// </param>
    public SkillCdState GetSkillCdState(bool onlyCode01Ready = true)
    {
        // 手动配置：直接按上次释放时间 + 手动 CD 判断
        if (ManualSkillCd > 0)
        {
            var dif = DateTime.UtcNow - LastSkillTime;
            return ManualSkillCd > dif.TotalSeconds ? SkillCdState.Cooldown : SkillCdState.Ready;
        }

        // 场上角色走视觉判定优先：用 ONNX 分类器判 E 技能状态，置信度足够时优先返回
        using (var region = CaptureToRectArea())
        {
            var context = new AvatarActiveCheckContext();
            if (CombatScenes.GetActiveAvatarIndex(region, context) == Index)
            {
                var (state, _) = ReadSkillCdFromScreenshot(region, onlyCode01Ready);
                if (state != SkillCdState.Unknown)
                {
                    return state;
                }

                // state == Unknown → 落到 OCR 记录推算
            }
            // 后台角色 → 走 OCR 记录推算
        }

        return GetSkillCdStateFromRecord();
    }

    /// <summary>
    /// 复位 E 技能的使用记录（<see cref="OcrSkillCd"/> 与 <see cref="LastSkillTime"/>）
    /// </summary>
    public void ResetSkillCdRecord()
    {
        OcrSkillCd = default;
        LastSkillTime = default;
    }

    /// <summary>
    /// 计算上一次使用技能到现在还剩下多长时间的cd
    /// </summary>
    /// <returns></returns>
    public double GetSkillCdSeconds()
    {
        switch (ManualSkillCd)
        {
            case < 0:
                {
                    var now = DateTime.UtcNow;
                    // 若未经过OCR的技能释放,上次时间加上最长的技能时间
                    var maxCd = Math.Max(CombatAvatar.SkillHoldCd, CombatAvatar.SkillCd);
                    var target =
                        LastSkillTime >= OcrSkillCd
                            ? LastSkillTime.AddSeconds(Math.Max(CombatAvatar.SkillHoldCd, CombatAvatar.SkillCd))
                            : OcrSkillCd;
                    var result = now > target ? 0d : (target - now).TotalSeconds;
                    if (!(result > maxCd)) return result;
                    Logger.LogWarning("{Name}的当前技能CD大于其最大技能CD{MaxCd}。如果你没有调整系统时间的话，这是一个bug。", Name, maxCd);
                    return maxCd;
                }
            case > 0:
                {
                    // 用户设置，所以直接通过上次释放技能的时间计算
                    var dif = DateTime.UtcNow - LastSkillTime;
                    if (ManualSkillCd > dif.TotalSeconds)
                    {
                        return ManualSkillCd - dif.TotalSeconds;
                    }

                    break;
                }
        }

        return 0;
    }

    /// <summary>
    /// 计算剩余技能 CD 的可信版本：与 <see cref="GetSkillCdState"/> 同源，只依据
    /// <see cref="ManualSkillCd"/> 手动配置与 <see cref="OcrSkillCd"/> OCR 记录，
    /// 不使用 <see cref="CombatAvatar.SkillCd"/> 做推算（对 CD 从持续时间结束后才起算的角色不准）。
    /// 返回值三态：&gt;0 冷却中剩余秒数；0 确定就绪；null 未知（用过但没读到 CD）。
    /// </summary>
    public double? GetSkillCdSecondsV2()
    {
        if (ManualSkillCd > 0)
        {
            // 用户设置，直接通过上次释放技能的时间计算；手动配置不存在未知态
            var dif = DateTime.UtcNow - LastSkillTime;
            return ManualSkillCd > dif.TotalSeconds ? ManualSkillCd - dif.TotalSeconds : 0;
        }

        // OCR 记录可信判定与 GetOcrSkillCdState 一致：只认晚于最近一次使用时间的记录
        if (OcrSkillCd > LastSkillTime)
        {
            var remaining = (OcrSkillCd - DateTime.UtcNow).TotalSeconds;
            return remaining > 0 ? remaining : 0;
        }

        // 从未使用过 → 就绪；用过但记录过期/缺失 → 未知
        return LastSkillTime == default ? 0 : null;
    }

    /// <summary>
    /// 等待技能CD
    /// </summary>
    /// <param name="ct">CancellationToken</param>
    public async Task WaitSkillCd(CancellationToken ct = default)
    {
        // 获取CD时间
        if (IsSkillReady())
        {
            return;
        }

        var s = GetSkillCdSeconds() + 0.2;
        Logger.LogInformation("{Name}的E技能CD未结束，等待{Seconds}秒", Name, Math.Round(s, 2));
        await Delay((int)Math.Ceiling(s * 1000), ct);
    }

    /// <summary>
    /// 跳跃
    /// </summary>
    public void Jump()
    {
        Simulation.SendInput.SimulateAction(GIActions.Jump);
    }

    /// <summary>
    /// 重击
    /// </summary>
    public void Charge(int ms = 0)
    {
        // 默认重击持续 1 秒；必须在特化分派前归一化，否则特化 handler 收到 ms=0 会异常
        if (ms == 0)
        {
            ms = 1000;
        }

        if (AvatarSpecialAction.ExecuteSpecializedAction(this, "Charge", Name, new ActionArgs(Ms: ms))) return;

        Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);
        Sleep(ms);
        Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
    }

    public void MouseDown(string key = "left")
    {
        key = key.ToLower();
        if (key == "left")
        {
            Simulation.SendInput.Mouse.LeftButtonDown();
        }
        else if (key == "right")
        {
            Simulation.SendInput.Mouse.RightButtonDown();
        }
        else if (key == "middle")
        {
            Simulation.SendInput.Mouse.MiddleButtonDown();
        }
    }

    public void MouseUp(string key = "left")
    {
        key = key.ToLower();
        if (key == "left")
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
        }
        else if (key == "right")
        {
            Simulation.SendInput.Mouse.RightButtonUp();
        }
        else if (key == "middle")
        {
            Simulation.SendInput.Mouse.MiddleButtonUp();
        }
    }

    public void Click(string key = "left")
    {
        key = key.ToLower();
        if (key == "left")
        {
            Simulation.SendInput.Mouse.LeftButtonClick();
        }
        else if (key == "right")
        {
            Simulation.SendInput.Mouse.RightButtonClick();
        }
        else if (key == "middle")
        {
            Simulation.SendInput.Mouse.MiddleButtonClick();
        }
    }

    public void MoveBy(int x, int y)
    {
        using (AvatarRecognition.BeginExclusiveOperation())
        {
            GlobalMethod.MoveMouseBy(x, y);
        }
    }

    public void Scroll(int scrollAmountInClicks)
    {
        Simulation.SendInput.Mouse.VerticalScroll(scrollAmountInClicks);
    }

    public void KeyDown(string key)
    {
        var vk = KeyBindingsSettingsPageViewModel.MappingKey(User32Helper.ToVk(key));
        switch (key)
        {
            case "VK_LBUTTON":
                Simulation.SendInput.Mouse.LeftButtonDown();
                break;
            case "VK_RBUTTON":
                Simulation.SendInput.Mouse.RightButtonDown();
                break;
            case "VK_MBUTTON":
                Simulation.SendInput.Mouse.MiddleButtonDown();
                break;
            case "VK_XBUTTON1":
                Simulation.SendInput.Mouse.XButtonDown(0x0001);
                break;
            case "VK_XBUTTON2":
                Simulation.SendInput.Mouse.XButtonDown(0x0001);
                break;
            default:
                Simulation.SendInput.Keyboard.KeyDown(vk);
                break;
        }
    }

    public void KeyUp(string key)
    {
        var vk = KeyBindingsSettingsPageViewModel.MappingKey(User32Helper.ToVk(key));
        switch (key)
        {
            case "VK_LBUTTON":
                Simulation.SendInput.Mouse.LeftButtonUp();
                break;
            case "VK_RBUTTON":
                Simulation.SendInput.Mouse.RightButtonUp();
                break;
            case "VK_MBUTTON":
                Simulation.SendInput.Mouse.MiddleButtonUp();
                break;
            case "VK_XBUTTON1":
                Simulation.SendInput.Mouse.XButtonUp(0x0001);
                break;
            case "VK_XBUTTON2":
                Simulation.SendInput.Mouse.XButtonUp(0x0001);
                break;
            default:
                Simulation.SendInput.Keyboard.KeyUp(vk);
                break;
        }
    }

    public void KeyPress(string key)
    {
        var vk = KeyBindingsSettingsPageViewModel.MappingKey(User32Helper.ToVk(key));
        switch (key)
        {
            case "VK_LBUTTON":
                Simulation.SendInput.Mouse.LeftButtonClick();
                break;
            case "VK_RBUTTON":
                Simulation.SendInput.Mouse.RightButtonClick();
                break;
            case "VK_MBUTTON":
                Simulation.SendInput.Mouse.MiddleButtonClick();
                break;
            case "VK_XBUTTON1":
                Simulation.SendInput.Mouse.XButtonClick(0x0001);
                break;
            case "VK_XBUTTON2":
                Simulation.SendInput.Mouse.XButtonClick(0x0001);
                break;
            default:
                Simulation.SendInput.Keyboard.KeyPress(vk);
                break;
        }
    }

    /// <summary>
    /// 从配置字符串中查找角色cd
    /// 仅有角色名时返回 -1 ,没找到角色返回null
    /// </summary>
    /// <param name="avatarName">角色名</param>
    /// <param name="input">序列</param>
    /// <returns></returns>
    public static double? ParseActionSchedulerByCd(string avatarName, string input)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(avatarName))
            return null;

        var searchIndex = input.Length - 1;

        while (true)
        {
            // 逆向查找角色名最后一次出现的位置
            var foundIndex = input.LastIndexOf(avatarName, searchIndex, StringComparison.Ordinal);
            if (foundIndex == -1) return null;

            // 验证前向边界（分号或字符串起点）
            var startValid = foundIndex == 0 ||
                             input[foundIndex - 1] == ';';

            // 验证后向边界（逗号或分号/字符串终点）
            var endValid = foundIndex + avatarName.Length == input.Length ||
                           input[foundIndex + avatarName.Length] == ',' ||
                           input[foundIndex + avatarName.Length] == ';';

            if (startValid && endValid)
            {
                var valueStart = foundIndex + avatarName.Length;
                // 处理逗号后的数值部分
                if (valueStart >= input.Length || input[valueStart] != ',') return -1;
                var valueEnd = input.IndexOf(';', valueStart);
                if (valueEnd == -1) valueEnd = input.Length;

                if (double.TryParse(input.AsSpan(valueStart + 1, valueEnd - valueStart - 1),
                        out var result))
                {
                    return result;
                }

                // 存在角色名但没有数值的情况
                return -1;
            }

            // 更新搜索范围继续查找
            searchIndex = foundIndex - 1;
            if (searchIndex < 0) break;
        }

        return null;
    }
}
