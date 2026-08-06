using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Common.Party;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 角色特化动作分派（按动作名+角色名决定是否使用特化逻辑）
/// </summary>
public static class AvatarSpecialAction
{
    /// <summary>
    /// 资源缩放比例
    /// </summary>
    private static double AssetScale => TaskContext.Instance().SystemInfo.AssetScale;

    /// <summary>
    /// 木偶（桑多涅）红温状态评分阈值（固定 0.5）。
    /// </summary>
    private const double OverheatThreshold = 0.5;

    /// <summary>
    /// 桑多涅特化叠加层目标框共享画笔（避免每帧新建 Pen 导致 GDI+ 句柄抖动）
    /// </summary>
    private static readonly System.Drawing.Pen _targetPen = new(System.Drawing.Color.LimeGreen, 2);

    /// <summary>
    /// 木偶（桑多涅）红温状态特征模型（硬编码自训练工具导出的 JSON）。
    /// </summary>
    private static readonly FeatureScorerExportData _overheatModel = new()
    {
        Features =
        {
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1095, Y = 519, W = 1, H = 1,
                IsCircular = true, Range = 360, RefVal = 301.808, Weight = 0.7914,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0007, 0.0019, 0.0051, 0.0138, 0.0366, 0.0937, 0.2193, 0.433, 0.6749, 0.8494, 0.9388, 0.9766, 0.9913, 0.9968]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1095, Y = 518, W = 1, H = 1,
                IsCircular = true, Range = 360, RefVal = 300.5802, Weight = 0.789,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0023, 0.0062, 0.0166, 0.0439, 0.1109, 0.2532, 0.4796, 0.7147, 0.872, 0.9487, 0.9805, 0.9927, 0.9973]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1095, Y = 517, W = 1, H = 1,
                IsCircular = true, Range = 360, RefVal = 297.9216, Weight = 0.7738,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0029, 0.0079, 0.0213, 0.0558, 0.1384, 0.3038, 0.5426, 0.7633, 0.8976, 0.9597, 0.9848, 0.9944]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1096, Y = 513, W = 1, H = 4,
                IsCircular = false, Range = 1, Weight = 0.5461,
                RefHist = [0.0705, 0.0023, 0, 0, 0, 0.0018, 0.0739, 0.8516],
                ProbTable = [0, 0, 0.0001, 0.0002, 0.0005, 0.0015, 0.004, 0.0108, 0.0289, 0.0747, 0.18, 0.3737, 0.6186, 0.8151, 0.923, 0.9702, 0.9888, 0.9959, 0.9985, 0.9994, 0.9998]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1097, Y = 516, W = 1, H = 4,
                IsCircular = false, Range = 1, Weight = 0.5088,
                RefHist = [0.1062, 0.0046, 0, 0, 0, 0, 0.0201, 0.8691],
                ProbTable = [0, 0, 0.0001, 0.0004, 0.001, 0.0026, 0.0071, 0.0192, 0.0504, 0.1262, 0.2819, 0.5162, 0.7436, 0.8874, 0.9554, 0.9831, 0.9937, 0.9977, 0.9991, 0.9997, 0.9999]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "H", X = 1090, Y = 552, W = 4, H = 1,
                IsCircular = false, Range = 1, Weight = 0.4793,
                RefHist = [0, 0, 0.0191, 0.9213, 0.0576, 0.002, 0, 0],
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0021, 0.0058, 0.0156, 0.0414, 0.1051, 0.2419, 0.4645, 0.7022, 0.865, 0.9457, 0.9793, 0.9923, 0.9972]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1105, Y = 564, W = 2, H = 3,
                IsCircular = true, Range = 360, RefVal = 349.1209, Weight = 0.7477,
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0007, 0.0018, 0.0049, 0.0133, 0.0353, 0.0905, 0.2129, 0.4237, 0.6665, 0.8446, 0.9366, 0.9757]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1095, Y = 572, W = 1, H = 4,
                IsCircular = false, Range = 1, Weight = 0.5165,
                RefHist = [0.9278, 0.0164, 0, 0, 0.0052, 0, 0.0121, 0.0384],
                ProbTable = [0, 0.0001, 0.0002, 0.0004, 0.0011, 0.003, 0.0082, 0.0221, 0.0578, 0.143, 0.3121, 0.5522, 0.7702, 0.9011, 0.9612, 0.9854, 0.9946, 0.998, 0.9993, 0.9997, 0.9999]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1105, Y = 572, W = 5, H = 4,
                IsCircular = true, Range = 360, RefVal = 351.1534, Weight = 0.7542,
                ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0029, 0.0079, 0.0212, 0.0556, 0.138, 0.3032, 0.5419, 0.7628, 0.8973, 0.9596, 0.9848]
            },
        }
    };

    /// <summary>
    /// 判断当前木偶是否处于红温状态（特征评分 ≥ 阈值）。
    /// 评分异常时降级返回 false，不中断战斗。
    /// </summary>
    private static bool IsOverheated(ImageRegion capture)
    {
        try
        {
            return ImageFeatureScorer.Score(_overheatModel, capture.SrcMat) >= OverheatThreshold;
        }
        catch (Exception e)
        {
            Logger.LogWarning("红温状态评分异常: {Message}", e.Message);
            return false;
        }
    }

    /// <summary>
    /// 特化规则：(动作, 角色) → 参数条件（null=无条件，仅检查动作+角色即生效）
    /// 不在此字典中的组合直接跳过，走通用逻辑。
    /// </summary>
    private static readonly Dictionary<(string Action, string Character), Func<object, bool>?> SpecializedRules = new()
    {
        [("UseSkill", "纳西妲")]   = args => args is ActionArgs { Hold: true },
        [("UseSkill", "坎蒂丝")]   = args => args is ActionArgs { Hold: true },
        [("Charge",   "那维莱特")] = null,
        [("Charge",   "恰斯卡")]   = null,
        [("Charge",   "桑多涅")]   = null,
    };

    /// <summary>
    /// 根据动作和角色名分派特化逻辑。
    /// 如果当前角色有对应的特化实现，则执行该特化逻辑并返回 true（调用方应跳过通用逻辑）；
    /// 否则返回 false，由调用方执行通用逻辑。
    /// </summary>
    /// <param name="action">动作名（如 "UseSkill"、"Charge"）</param>
    /// <param name="character">角色名（如 "纳西妲"）</param>
    /// <param name="args">动作参数对象（如 UseSkillArgs、ChargeArgs）</param>
    /// <returns>true 表示已由特化逻辑处理，false 表示无特化逻辑</returns>
    public static bool ExecuteSpecializedAction(Avatar avatar, string action, string character, object args)
    {
        // 不在特化规则中 → 提前退出
        if (!SpecializedRules.TryGetValue((action, character), out var condition)) return false;

        // 参数条件存在且不满足 → 提前退出
        if (condition != null && !condition(args)) return false;

        switch (action)
        {
            case "UseSkill":
                return ExecuteUseSkillSpecialized(avatar, character);
            case "Charge":
                return ExecuteChargeSpecialized(avatar, character, ((ActionArgs)args).Ms);
            default:
                return false;
        }
    }

    /// <summary>
    /// UseSkill 特化分派
    /// </summary>
    private static bool ExecuteUseSkillSpecialized(Avatar avatar, string character)
    {
        switch (character)
        {
            // 纳西妲长按 E：按下后向右移动鼠标
            case "纳西妲":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                    Sleep(300, avatar.Ct);
                    for (int j = 0; j < 10; j++)
                    {
                        Simulation.SendInput.Mouse.MoveMouseBy(1000, 0);
                        Sleep(50);
                    }

                    Sleep(300);
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                    return true;
                }
            }
            // 坎蒂丝长按 E：固定等待 3 秒
            case "坎蒂丝":
            {
                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                Thread.Sleep(3000);
                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Charge 重击特化分派
    /// </summary>
    private static bool ExecuteChargeSpecialized(Avatar avatar, string character, int ms)
    {
        switch (character)
        {
            // 那维莱特：按住普攻循环向右旋转
            case "那维莱特":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    var dpi = TaskContext.Instance().DpiScale;
                    Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);
                    try
                    {
                        while (ms >= 0)
                        {
                            if (avatar.Ct is { IsCancellationRequested: true })
                            {
                                return true;
                            }

                            Simulation.SendInput.Mouse.MoveMouseBy((int)(1000 * dpi), 0);
                            ms -= 50;
                            Sleep(50);
                        }
                    }
                    finally
                    {
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
                    }
                }
                return true;
            }
            // 恰斯卡：按住普攻分段变速旋转
            case "恰斯卡":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    var dpi = TaskContext.Instance().DpiScale;
                    Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);
                    try
                    {
                        int tick = -4;
                        while (ms >= 0)
                        {
                            if (avatar.Ct is { IsCancellationRequested: true })
                            {
                                return true;
                            }

                            const double lowspeed = 0.7, highspeed = 50;
                            double rateX, rateY;
                            if (tick < 3)
                            {
                                rateX = highspeed;
                                rateY = highspeed * 0.23;
                            }
                            else if (tick < 40)
                            {
                                rateX = lowspeed * 0.7;
                                rateY = 0;
                            }
                            else if (tick < 43)
                            {
                                rateX = highspeed;
                                rateY = highspeed * 0.4;
                            }
                            else if (tick < 70)
                            {
                                rateX = lowspeed * 0.9;
                                rateY = 0;
                            }
                            else if (tick < 73)
                            {
                                rateX = highspeed;
                                rateY = highspeed;
                            }
                            else
                            {
                                rateX = lowspeed;
                                rateY = 0;
                            }

                            Simulation.SendInput.Mouse.MoveMouseBy((int)(rateX * 50 * dpi), (int)(rateY * 50 * dpi));
                            tick = (tick + 1) % 100;
                            Sleep(25);
                            ms -= 25;
                        }

                        return true;
                    }
                    finally
                    {
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
                    }
                }
            }
            // 桑多涅：按住普攻 + 截图寻的血条/伤害数字追踪
            case "桑多涅":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    var dpi = TaskContext.Instance().DpiScale;
                    var visConfig = AvatarRecognition.GetVisualRecognitionConfig();
                    var frameIntervalMs = visConfig.TargetingDetectionInterval;
                    var drawResults = visConfig.DrawRecognitionResults;
                    var lockLostWaitTime = visConfig.LockLostWaitTime;

                    Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);

                    DateTime? lastSeenTargetTime = null;
                    var startTime = DateTime.UtcNow;
                    var maxDurationMs = ms;
                    int overheatCount = 0;  // 红温连续命中计数

                    try
                    {
                        while (!avatar.Ct.IsCancellationRequested && (DateTime.UtcNow - startTime).TotalMilliseconds < maxDurationMs)
                        {
                            using (var capture = CaptureToRectArea())
                            {
                                // 距重击开始超过 3 秒后开始检测红温，连续命中 3 次（1/3 → 2/3 → 3/3）才提前退出
                                if ((DateTime.UtcNow - startTime).TotalSeconds >= 3)
                                {
                                    if (IsOverheated(capture))
                                    {
                                        overheatCount++;
                                        if (overheatCount >= 3)
                                        {
                                            Logger.LogInformation("桑多涅重击特化：连续 3 次检测到红温状态，提前退出");
                                            break;
                                        }

                                        Logger.LogInformation("桑多涅重击特化：检测到红温状态 {OverheatCount}/3", overheatCount);
                                    }
                                    else
                                    {
                                        overheatCount = 0;
                                    }
                                }

                                int preAimX = (int)(capture.Width * 0.5);
                                int preAimY = (int)(capture.Height * (480.0 / 1080.0));

                                var bars = AvatarRecognition.FindBloodBars(capture);
                                var valid = bars.Where(b => b.x > (int)(200 * AssetScale)).ToList();

                                var drawList = new System.Collections.Generic.List<View.Drawable.RectDrawable>();

                                bool hasLegendaryBar = valid.Any(b => AvatarRecognition.IsLegendaryBar(b.x, b.y));

                                if (valid.Count > 0 && !hasLegendaryBar)
                                {
                                    lastSeenTargetTime = DateTime.UtcNow;
                                    var nearest = valid.OrderBy(b => Math.Abs((b.x + b.width / 2) - preAimX) + Math.Abs((b.y + b.height / 2) - preAimY)).First();
                                    //Logger.LogInformation("追踪血条: 裁剪坐标({X},{Y}) 大小({W}×{H})", nearest.x, nearest.y, nearest.width, nearest.height);
                                    var offsetX = (nearest.x + nearest.width / 2) - preAimX;
                                    var offsetY = (nearest.y + nearest.height / 2) - preAimY;
                                    Simulation.SendInput.Mouse.MoveMouseBy((int)(offsetX * 0.35 * dpi), (int)(offsetY * 0.25 * dpi));

                                    if (drawResults)
                                    {
                                        foreach (var b in valid)
                                        {
                                            var rect = new OpenCvSharp.Rect(b.x, b.y, b.width, b.height);
                                            if (b.x == nearest.x && b.y == nearest.y && b.width == nearest.width && b.height == nearest.height)
                                                drawList.Add(capture.ToRectDrawable(rect, "target", _targetPen));
                                            else
                                                drawList.Add(capture.ToRectDrawable(rect, "blood"));
                                        }
                                    }
                                }
                                else
                                {
                                    var damageResult = AvatarRecognition.FindDamageNumber(capture);
                                    if (damageResult.HasValue)
                                    {
                                        var (dcx, dcy, _, dx, dy, dw, dh) = damageResult.Value;
                                        lastSeenTargetTime = DateTime.UtcNow;
                                        var offsetX = dcx - preAimX;
                                        var offsetY = dcy - preAimY;
                                        Simulation.SendInput.Mouse.MoveMouseBy((int)(offsetX * 0.35 * dpi), (int)(offsetY * 0.25 * dpi));
                                        if (drawResults)
                                        {
                                            drawList.Add(capture.ToRectDrawable(
                                                new OpenCvSharp.Rect(dx, dy, dw, dh),
                                                "damage_target",
                                                _targetPen));
                                        }
                                    }

                                    if (!damageResult.HasValue)
                                    {

                                        if (!hasLegendaryBar && (DateTime.UtcNow - (lastSeenTargetTime ?? startTime)).TotalSeconds >= 1.5)
                                        {
                                            Logger.LogInformation("桑多涅重击特化：超过1.5秒未找到目标，提前退出");
                                            View.Drawable.VisionContext.Instance().DrawContent.PutOrRemoveRectList("SandroneBloodBars", drawList);
                                            break;
                                        }

                                        if (!lastSeenTargetTime.HasValue || (DateTime.UtcNow - lastSeenTargetTime.Value).TotalSeconds >= lockLostWaitTime)
                                        {
                                            Simulation.SendInput.Mouse.MoveMouseBy((int)(1000 * dpi), 0);
                                        }
                                    }
                                }

                                View.Drawable.VisionContext.Instance().DrawContent.PutOrRemoveRectList("SandroneBloodBars", drawList);
                            }

                            Sleep(frameIntervalMs);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    finally
                    {
                        View.Drawable.VisionContext.Instance().DrawContent.RemoveRect("SandroneBloodBars");
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
                    }
                }

                return true;
            }
            default:
                return false;
        }
    }
}

/// <summary>
/// 特化动作参数（由动作类型决定哪些字段生效）
/// </summary>
/// <param name="Hold">UseSkill 是否长按</param>
/// <param name="Ms">Charge 持续时间（毫秒）</param>
public sealed record ActionArgs(bool Hold = false, int Ms = 0);
