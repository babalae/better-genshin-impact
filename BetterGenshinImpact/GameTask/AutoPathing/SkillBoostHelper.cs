﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 角色技能加速赶路逻辑（玛薇卡、瓦雷莎、希诺宁、闲云、桑多涅、恰斯卡/伊法、流浪者）
/// </summary>
public partial class PathExecutor
{
    private class HurryOnState
    {
        public int MavikaFlyCount;
        public bool SprintMouseLogo = true;
        public int RunCount;
        public bool IsFlyingMwk;
        public bool PendingApproach = true;
        public bool? RunToDash = false;
        public double DistanceHalf;
        public int MavikaSlopeCount;
        public int ClimbLogo;
        public int RotationStableCount;
        public string? OriginalMoveMode;
        public bool FlyingState;
        public int ChascaFlightCheckCount;
        public int WandererFlightCheckCount;
    }
    // 赶路切换角色黑名单，防止切人后触发夜魂传递
    private static readonly HashSet<string> HurryOnBlacklist = ["玛薇卡", "希诺宁", "瓦雷莎", "茜特菈莉"];

    /// <summary>
    /// 各角色在连续赶路模式下的转向夹角阈值（度）。
    /// 当路径转向角 ≥ 该角色的阈值时，视为急转弯，提前下车步行接近防止冲过头。
    /// 未显式列出的角色使用默认值 120°（几乎只有掉头才下车）。
    /// </summary>
    private static readonly Dictionary<string, double> TurnAngleThresholds = new()
    {
        { "桑多涅", 45 },
        { "恰斯卡", 45 },
        { "伊法", 45 },
        { "流浪者", 45 },
        { "玛薇卡", 60 },
        { "闲云", 120 },
        { "希诺宁", 120 },
    };

    private string _hurryOnAvatar = "";
    private DateTime _lastJumpFlyTime = DateTime.MinValue;
    private bool _jumpFlySafetyPending;
    private DateTime _lastMavikaBoardTime = DateTime.MinValue;
    private DateTime _lastSkillCheckTime = DateTime.MinValue;
    private DateTime _lastLandingTime = DateTime.MinValue;
    /// <summary>
    /// 上一帧识别的体力值，用于跨帧 fallback。初始为满值 240。
    /// </summary>
    private int _lastStamina = 240;
    private int _lastWaypointIndex = -1;
    private readonly List<int> _staminaHistory = new(50);
    private DateTime _lastSandroneSkillTime = DateTime.MinValue;

    /// <summary>
    /// 获取切人步行目标序号：排除赶路角色自身 + 黑名单，取序号最靠前的有效角色。
    /// 若排除后无合法角色，则忽略黑名单再试一次。
    /// 返回 "1"/"2"/"3"/"4"，不会返回 null。
    /// </summary>
    private string GetSwitchToWalkIndex()
    {
        for (var i = 1; i <= 4; i++)
        {
            var avatar = _combatScenes?.SelectAvatar(i);
            if (avatar == null) continue;
            if (avatar.Name == _hurryOnAvatar) continue;
            if (HurryOnBlacklist.Contains(avatar.Name)) continue;
            return i.ToString();
        }

        for (var i = 1; i <= 4; i++)
        {
            var avatar = _combatScenes?.SelectAvatar(i);
            if (avatar == null) continue;
            if (avatar.Name == _hurryOnAvatar) continue;
            return i.ToString();
        }

        var currentIdx = _combatScenes?.SelectAvatar(_hurryOnAvatar)?.Index ?? 1;
        return ((currentIdx % 4) + 1).ToString();
    }

    private async Task SwitchToHurryAvatarAsync(ImageRegion screen2, Avatar avatar, double distance, int num, CancellationToken ct)
    {
        if (Bv.GetMotionStatus(screen2) != MotionStatus.Fly)
        {
            await SwitchAvatar(avatar.Index.ToString());
        }

        if (num % 5 == 0)
        {
            Logger.LogInformation("自动赶路：{t} 赶路...{t2}", avatar.Name, Math.Round(distance));
        }
    }

    /// <summary>
    /// 赶路逻辑：处理角色特化赶路、接近节点检测、防误飞等。
    /// 在主循环的通用移动逻辑之前调用。
    /// </summary>
    /// <returns>true = 跳过本次通用移动逻辑（continue）；false = 继续执行通用移动逻辑</returns>
    private async Task<bool> ExecuteHurryOnAsync(
        WaypointForTrack waypoint,
        WaypointForTrack? nextWaypoint,
        double distance,
        double? nextDistance,
        bool isPoint,
        Avatar? avatar,
        ImageRegion screen2,
        int num,
        HurryOnState state,
        List<string>? disabledAvatars)
    {
        if (avatar == null) return false;

        if (disabledAvatars is { Count: > 0 } && disabledAvatars.Contains(avatar.Name))
            return false;

        if (SwimmingConfirm(screen2))
        {
            return false;
        }

        // 赶路逻辑只在 Run/Dash 路段触发，Fly 路段不处理
        // 终点（nextWaypoint == null）不受此限制，需要进入角色分支执行接近下车
        if (nextWaypoint != null
            && waypoint?.MoveMode != MoveModeEnum.Run.Code
            && waypoint?.MoveMode != MoveModeEnum.Dash.Code)
            return false;

        // Logger.LogInformation("[赶路调试] ExecuteHurryOnAsync: avatar={a}, dist={d}, nextDist={nd}, moveMode={m}, type={t}, num={n}, pending={pa}",
        //     avatar.Name, Math.Round(distance, 1), nextDistance, waypoint?.MoveMode, waypoint?.Type, num, state.PendingApproach);

        double interval = PartyConfig.MwkJumpFlyIntervalSeconds > 0 ? PartyConfig.MwkJumpFlyIntervalSeconds : 1;

        switch (avatar.Name)
        {
            case "玛薇卡":
                if (CurWaypoint.Item1 != _lastWaypointIndex)
                {
                    _jumpFlySafetyPending = false;
                    _lastWaypointIndex = CurWaypoint.Item1;
                }

                if (state.OriginalMoveMode != null)
                {
                    waypoint.MoveMode = state.OriginalMoveMode;
                    state.OriginalMoveMode = null;
                }

                bool boarded = false;

                if (state.PendingApproach)
                {
                    var shouldApproach = ShouldApproach(distance, nextDistance, waypoint, nextWaypoint, avatar.Name);

                    if (shouldApproach)
                    {
                        Simulation.ReleaseAllKey();
                        state.PendingApproach = false;
                        var colorDiff = GetMavikaColorDifference(screen2);
                        if (colorDiff < 15 && Bv.GetMotionStatus(screen2) != MotionStatus.Fly)
                        {
                            if (PartyConfig.SwitchToWalkEnabled)
                            {
                                var nextIdx = GetSwitchToWalkIndex();
                                Logger.LogInformation("自动赶路：{t} 节点接近...-i {t2} {t3} {t4}", PartyConfig.TravelMode, nextIdx, waypoint?.MoveMode, Math.Round(colorDiff));

                                await SwitchAvatar(nextIdx);
                            }
                            else
                            {
                                Logger.LogInformation("自动赶路：玛薇卡接近节点，下车步行");
                                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                            }
                        }
                        return false;
                    }
                }

                if (distance > PartyConfig.Distance)
                {
                    await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);

                    if ((DateTime.UtcNow - _lastMavikaBoardTime).TotalSeconds >= 3
                        && GetMavikaColorDifference(screen2) > 15
                        && await ReadEskillCdAsync("玛薇卡") <= 0)
                    {
                        // Logger.LogInformation("[赶路调试] 玛薇卡 启动摩托: dist={d}, colorDiff={cd}",
                        //     Math.Round(distance, 1), Math.Round(GetMavikaColorDifference(screen2), 1));
                        _lastMavikaBoardTime = DateTime.UtcNow;
                        boarded = true;
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                        await Delay(200, ct);
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                        await Delay(300, ct);
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                        await Delay(700, ct);
                    }
                }

                if (PartyConfig.MwkJumpFlyEnabled && distance > 2 * PartyConfig.Distance && state.RotationStableCount >= 1)
                {
                    if (!(boarded || GetMavikaColorDifference(screen2) <= 15 && await ReadEskillCdAsync("玛薇卡") < 1))
                    {
                        return false;
                    }

                    if ((DateTime.UtcNow - _lastJumpFlyTime).TotalSeconds < interval)
                    {
                        return true;
                    }

                    Logger.LogInformation("自动赶路：玛薇卡跳飞赶路 距离下个节点距离 {d}", Math.Round(distance));
                    await Delay(50, ct);
                    Simulation.SendInput.SimulateAction(GIActions.Jump);
                    await Delay(150, ct);
                    Simulation.SendInput.SimulateAction(GIActions.Jump);
                    await Delay(100, ct);
                    Simulation.SendInput.SimulateAction(GIActions.Jump);
                    await Delay(10, ct);
                    Simulation.SendInput.SimulateAction(GIActions.Jump);
                    await Delay(150, ct);
                    _lastJumpFlyTime = DateTime.UtcNow;
                    _jumpFlySafetyPending = true;

                    using var jumpCheckRegion = CaptureToRectArea();
                    if (Bv.GetMotionStatus(jumpCheckRegion) == MotionStatus.Fly)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                        await Delay(300, ct);
                        for (int i = 0; i < 5; i++)
                        {
                            using var retryRegion = CaptureToRectArea();
                            if (Bv.GetMotionStatus(retryRegion) == MotionStatus.Fly)
                            {
                                Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                                await Delay(300, ct);
                            }
                            else break;
                        }
                        return false;
                    }

                    if (SpaceAtSecondPlaceExist(state))
                    {
                        Simulation.SendInput.SimulateAction(GIActions.Jump);
                    }

                    return true;
                }

                // 安全降落：同路段最后一次跳飞后，间隔已过仍可能在空中 → 普攻防摔伤
                if (_lastJumpFlyTime != DateTime.MinValue
                    && _jumpFlySafetyPending
                    && (DateTime.UtcNow - _lastJumpFlyTime).TotalSeconds > interval)
                {
                    Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                    await Delay(100, ct);
                    _jumpFlySafetyPending = false;
                }

                if ((boarded || GetMavikaColorDifference(screen2) <= 15) && distance > PartyConfig.Distance)
                {
                    if (state.RunToDash == false && distance > 40 && waypoint.MoveMode == MoveModeEnum.Run.Code)
                    {
                        state.RunToDash = true;
                        state.DistanceHalf = distance * 2 / 4;
                        state.OriginalMoveMode = waypoint.MoveMode;
                        waypoint.MoveMode = MoveModeEnum.Dash.Code;
                    }
                    else if (state.RunToDash == true && distance < state.DistanceHalf)
                    {
                        waypoint.MoveMode = state.OriginalMoveMode ?? MoveModeEnum.Run.Code;
                        Task.Run(async () =>
                            {
                                Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyDown);
                                await Delay(1000, ct);
                                Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
                            }, ct);
                        state.RunToDash = null;
                    }

                    if (Bv.GetMotionStatus(screen2) == MotionStatus.Climb)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.Drop);
                        await Delay(500, ct);
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                    }

                    if (distance > 10)
                    {
                        if (waypoint.MoveMode == MoveModeEnum.Dash.Code)
                        {
                            Simulation.SendInput.SimulateAction(GIActions.SprintMouse);
                        }
                        else if (waypoint.MoveMode == MoveModeEnum.Run.Code)
                        {
                            state.RunCount++;
                            if (state.RunCount < 5)
                            {
                                Simulation.SendInput.SimulateAction(GIActions.SprintMouse);
                            }
                        }
                    }

                    var pos = screen2.SrcMat.At<Vec3b>(1012, 1574);
                    var pos2 = screen2.SrcMat.At<Vec3b>(1006, 1608);
                    var pos3 = screen2.SrcMat.At<Vec3b>(1028, 1584);
                    // 飞行/滑行/爬坡状态指示器两个端点的 RGB 欧氏距离
                    // < 15 → 指示器消失 → 玛薇卡在平地上（非空中/滑行/爬坡状态）
                    var slopeDiff = Math.Sqrt(
                        Math.Pow(pos.Item0 - pos2.Item0, 2) +
                        Math.Pow(pos.Item1 - pos2.Item1, 2) +
                        Math.Pow(pos.Item2 - pos2.Item2, 2)
                    );
                    // 指示器消失（slopeDiff < 15）→ 在平地上，如果此时 E 技能图标为白色则判定在空中
                    // 按普攻执行下落攻击快速落地
                    if (slopeDiff < 15)
                    {
                        if (pos3.Item0 >= 250 && pos3.Item1 >= 250 && pos3.Item2 >= 250)
                        {
                            state.MavikaSlopeCount++;
                            // Logger.LogInformation("[赶路调试] 玛薇卡 空中检测触发: slopeDiff={sd}, count={c}", Math.Round(slopeDiff, 1), state.MavikaSlopeCount);
                            if (state.MavikaSlopeCount > 5 && avatar.IsActive(screen2))
                            {
                                if (nextWaypoint?.MoveMode != MoveModeEnum.Fly.Code)
                                    Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                                state.MavikaSlopeCount = 0;
                                Logger.LogInformation("自动赶路：靠近节点切换 {t}...-h {t2}", "", waypoint?.MoveMode);
                            }
                        }
                    }

                    return true;
                }

                break;

            // case "瓦雷莎":
            //     if (state.PendingApproach)
            //     {
            //         var shouldApproach = ShouldApproach(distance, nextDistance, waypoint, nextWaypoint, avatar.Name);
            //
            //         if (shouldApproach)
            //         {
            //             Simulation.ReleaseAllKey();
            //             state.PendingApproach = false;
            //             if (PartyConfig.SwitchToWalkEnabled)
            //             {
            //                 var nextIdx = GetSwitchToWalkIndex();
            //                 Logger.LogInformation("自动赶路：瓦雷莎接近节点，切人步行 {t}", nextIdx);
            //                 Task.Run(async () =>
            //                 {
            //                     await SwitchAvatar(nextIdx);
            //                 }, ct);
            //             }
            //             else
            //             {
            //                 if (await AutoFightSkill.AvatarSkillAsync(Logger, avatar, false, 2, ct))
            //                 {
            //                     Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            //                     await Delay(300, ct);
            //                 }
            //
            //                 var lower = new Scalar(220, 150, 150);
            //                 var higher = new Scalar(230, 160, 180);
            //                 using var mask = OpenCvCommonHelper.Threshold(screen2.DeriveCrop(948, 410, 26, 30).SrcMat, lower, higher);
            //                 using var labels = new Mat();
            //                 using var stats = new Mat();
            //                 using var centroids = new Mat();
            //
            //                 var numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
            //                     connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);
            //
            //                 if (numLabels > 3 && numLabels < 40)
            //                 {
            //                     state.MavikaFlyCount++;
            //                     if (state.MavikaFlyCount > 2 && avatar.IsActive(screen2))
            //                     {
            //                         Task.Run(async () =>
            //                         {
            //                             await Delay(1000, ct);
            //                             using var region3 = CaptureToRectArea();
            //                             if (avatar.IsActive(region3))
            //                             {
            //                                 Simulation.SendInput.SimulateAction(GIActions.Jump);
            //                                 await Delay(100, ct);
            //                                 using var region4 = CaptureToRectArea();
            //                                 var isFlying = Bv.GetMotionStatus(region4) == MotionStatus.Fly;
            //                                 if (isFlying)
            //                                 {
            //                                     Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
            //                                     Logger.LogInformation("自动赶路：{t} 下落攻击...", "瓦蕾莎");
            //                                 }
            //                             }
            //                             state.MavikaFlyCount = 0;
            //                         }, ct);
            //                     }
            //                 }
            //             }
            //             return false;
            //         }
            //     }
            //
            //     if (distance > PartyConfig.Distance)
            //     {
            //         await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);
            //
            //         waypoint.MoveMode = MoveModeEnum.Run.Code;
            //
            //         await Delay(300, ct);
            //         if (!await AutoFightSkill.AvatarSkillAsync(Logger, avatar, false, 2, ct))
            //         {
            //             Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
            //             await Delay(300, ct);
            //             Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
            //             await Delay(200, ct);
            //             avatar.LastSkillTime = DateTime.UtcNow;
            //
            //             if (!await AutoFightSkill.AvatarSkillAsync(Logger, avatar, false, 2, ct))
            //             {
            //                 if (distance > 20)
            //                 {
            //                     if (waypoint.MoveMode == MoveModeEnum.Dash.Code)
            //                     {
            //                         Simulation.SendInput.SimulateAction(GIActions.SprintMouse);
            //                     }
            //                     else if (waypoint.MoveMode == MoveModeEnum.Run.Code)
            //                     {
            //                         if (state.RunCount < 2)
            //                         {
            //                             Simulation.SendInput.SimulateAction(GIActions.SprintMouse);
            //                         }
            //                     }
            //                 }
            //             }
            //             else
            //             {
            //                 var higher = new Scalar(0, 221, 250);
            //                 using var region2 = CaptureToRectArea();
            //                 using var mask = OpenCvCommonHelper.Threshold(region2.DeriveCrop(1686, 949, 10, 10).SrcMat, higher);
            //                 using var labels = new Mat();
            //                 using var stats = new Mat();
            //                 using var centroids = new Mat();
            //                 var numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
            //                     connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);
            //
            //                 if (numLabels > 1)
            //                 {
            //                     if (distance > 20)
            //                     {
            //                         if (waypoint.MoveMode == MoveModeEnum.Dash.Code)
            //                         {
            //                             Simulation.SendInput.SimulateAction(GIActions.SprintMouse);
            //                         }
            //                         else if (waypoint.MoveMode == MoveModeEnum.Run.Code)
            //                         {
            //                             if (state.RunCount < 2)
            //                             {
            //                                 Simulation.SendInput.SimulateAction(GIActions.SprintMouse);
            //                             }
            //                         }
            //                     }
            //                 }
            //             }
            //         }
            //     }
            //
            //     return true;
            // break;

            case "希诺宁":
                if (state.PendingApproach)
                {
                    var shouldApproach = ShouldApproach(distance, nextDistance, waypoint, nextWaypoint, avatar.Name);

                    if (shouldApproach)
                    {
                        Simulation.ReleaseAllKey();
                        // Logger.LogInformation("[赶路调试] 希诺宁 触发接近: dist={d}, spaceExist={s}",
                        //     Math.Round(distance, 1), SpaceAtSecondPlaceExist(state));
                        state.PendingApproach = false;
                        if (PartyConfig.SwitchToWalkEnabled)
                        {
                            var nextIdx = GetSwitchToWalkIndex();
                            Logger.LogInformation("自动赶路：希诺宁接近节点，切人步行 {t}", nextIdx);
                            Task.Run(async () =>
                            {
                                await SwitchAvatar(nextIdx);
                            }, ct);
                        }
                        else if (SpaceAtSecondPlaceExist(state))
                        {
                            Logger.LogInformation("自动赶路：希诺宁接近节点，关闭E技能赶路状态");
                            var retries = 0;
                            while (SpaceAtSecondPlaceExist(state) && retries < 10)
                            {
                                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                                await Delay(100, ct);
                                retries++;
                            }
                        }
                        return false;
                    }
                }

                if (distance > PartyConfig.Distance
                    && (waypoint?.MoveMode == MoveModeEnum.Run.Code || waypoint?.MoveMode == MoveModeEnum.Dash.Code))
                {
                    await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);

                    if ((DateTime.UtcNow - _lastSkillCheckTime).TotalSeconds < 1)
                        return false;
                    _lastSkillCheckTime = DateTime.UtcNow;

                    if (!SpaceAtSecondPlaceExist(state))
                    {
                        var cd = await ReadEskillCdAsync("希诺宁");
                        if (cd <= 0)
                        {
                            // Logger.LogInformation("[赶路调试] 希诺宁 启动E技能: spaceExist=false, cd={cd}", cd);
                            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                            await Delay(200, ct);
                            avatar.LastSkillTime = DateTime.UtcNow;
                        }
                    }

                    return false;
                }

                break;

            case "闲云":
            {
                if (distance > PartyConfig.Distance
                    && (waypoint?.MoveMode == MoveModeEnum.Run.Code || waypoint?.MoveMode == MoveModeEnum.Dash.Code))
                {
                    await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);

                    var cd = await ReadEskillCdAsync("闲云");
                    if (cd <= 0 && state.RotationStableCount >= 1)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                        await Delay((int)(interval / 2.0 * 1000), ct);
                        avatar.LastSkillTime = DateTime.UtcNow;
                        return true;
                    }

                    return false;
                }
                break;
            }

            case "桑多涅":
                try
                {
                    // Step 1: 状态同步 — 每次进入 reconcile FlyingState 与实际游戏状态
                    //    FlyingState=true 但实际技能已结束 → 主动降落
                    //    FlyingState=false 但实际技能已生效 → 同步状态
                    if (state.FlyingState && !DashAtSecondPlaceExist())
                    {
                        state.FlyingState = false;
                        _lastSandroneSkillTime = DateTime.UtcNow;
                        await SafeLanding(ct);
                        Logger.LogInformation("自动赶路：桑多涅技能耗尽，安全降落");
                        return false;
                    }
                    if (!state.FlyingState && DashAtSecondPlaceExist())
                    {
                        state.FlyingState = true;
                    }

                    // Step 2: 小于停止距离 → 主动下车
                    if (state.FlyingState && distance < PartyConfig.ApproachStopDistance)
                    {
                        var shouldApproach = ShouldApproach(distance, nextDistance, waypoint, nextWaypoint, avatar.Name);
                        if (shouldApproach)
                        {
                            Simulation.ReleaseAllKey();
                            state.FlyingState = false;
                            var retries = 0;
                            while (DashAtSecondPlaceExist() && retries < 10)
                            {
                                Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                                await Delay(50, ct);
                                retries++;
                            }
                            await Delay(150, ct);
                            await SafeLanding(ct);
                            Logger.LogInformation("自动赶路：桑多涅接近节点");
                            return false;
                        }
                    }

                    // Step 3: 大于启用距离且未上车 → 尝试上车
                    if (!state.FlyingState
                        && distance > PartyConfig.Distance
                        && (waypoint?.MoveMode == MoveModeEnum.Run.Code || waypoint?.MoveMode == MoveModeEnum.Dash.Code))
                    {
                        await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);

                        if (!DashAtSecondPlaceExist())
                        {
                            if ((DateTime.UtcNow - _lastSandroneSkillTime).TotalSeconds >= 1)
                            {
                                var sandroneCd = await ReadEskillCdAsync("桑多涅");
                                if (sandroneCd <= 0)
                                {
                                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                                    await Delay(150, ct);
                                    if (DashAtSecondPlaceExist())
                                    {
                                        _lastSandroneSkillTime = DateTime.UtcNow;
                                        state.FlyingState = true;
                                    }
                                    else
                                    {
                                        await SafeLanding(ct);
                                        _lastSandroneSkillTime = DateTime.UtcNow;
                                    }
                                }
                            }
                        }

                        return false;
                    }

                    // Step 4: 已上车：体力 < 120 时纯飘（桑多涅技能代步），否则正常步行/冲刺
                    if (state.FlyingState)
                    {
                        if (nextWaypoint?.MoveMode == MoveModeEnum.Fly.Code)
                        {
                            return true;
                        }
                        else if (DetectStamina() < 120)
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError(e, $"[{avatar.Name}] 赶路逻辑异常");
                    return false;
                }

                break;

            case "恰斯卡":
            case "伊法":
                try
                {
                    if (state.PendingApproach)
                    {
                        var shouldApproach = ShouldApproach(distance, nextDistance, waypoint, nextWaypoint, avatar.Name);

                        if (shouldApproach)
                        {
                            Simulation.ReleaseAllKey();
                            // Logger.LogInformation("[赶路调试] {name} 触发接近: dist={d}, flying={f}, spaceExist={s}",
                            //     avatar.Name, Math.Round(distance, 1), state.FlyingState, SpaceAtSecondPlaceExist(state));
                            state.PendingApproach = false;
                            // 同时检查状态字段和实时像素，确保终点（新 HurryOnState，FlyingState=false）也能下车
                            if (state.FlyingState || SpaceAtSecondPlaceExist(state))
                            {
                                if (SpaceAtSecondPlaceExist(state))
                                {
                                    Logger.LogInformation($"自动赶路：{avatar.Name}接近节点，关闭飞行状态");
                                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                                    for (var retries = 0; retries < 20; retries++)
                                    {
                                        await Delay(100, ct);
                                        var cd = await ReadEskillCdAsync(avatar.Name);
                                        if (cd > 0)
                                        {
                                            break;
                                        }
                                    }
                                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                                }
                                state.FlyingState = false;
                            }
                            return false;
                        }
                    }

                    if (state.FlyingState)
                    {
                        if ((DateTime.UtcNow - _lastSkillCheckTime).TotalSeconds < 0.5)
                            return true;
                        _lastSkillCheckTime = DateTime.UtcNow;

                        if (!SpaceAtSecondPlaceExist(state))
                        {
                            state.FlyingState = false;
                            _lastLandingTime = DateTime.UtcNow;
                            Logger.LogInformation($"自动赶路：{avatar.Name}飞行结束");
                            await SafeLanding(ct);
                            return false;
                        }
                        return true;
                    }

                    if (distance > PartyConfig.Distance
                        && (waypoint?.MoveMode == MoveModeEnum.Run.Code || waypoint?.MoveMode == MoveModeEnum.Dash.Code))
                    {
                        await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);

                        if ((DateTime.UtcNow - _lastSkillCheckTime).TotalSeconds < 0.5)
                            return false;
                        _lastSkillCheckTime = DateTime.UtcNow;

                        if (state.RotationStableCount >= 1)
                        {
                            if ((DateTime.UtcNow - _lastLandingTime).TotalSeconds < 3)
                                return false;

                            var cd = await ReadEskillCdAsync(avatar.Name);
                            if (cd <= 0)
                            {
                                // Logger.LogInformation("[赶路调试] {name} 启动飞行: dist={d}, rotStable={rs}, cd={cd}",
                                //     avatar.Name, Math.Round(distance, 1), state.RotationStableCount, cd);
                                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                                await Delay(50, ct);
                                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                                await Delay(100, ct);
                                Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyDown);

                                avatar.LastSkillTime = DateTime.UtcNow;
                                state.FlyingState = true;
                                Logger.LogInformation($"自动赶路：{avatar.Name}启动飞行");
                                return true;
                            }
                        }

                        return false;
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError(e, $"[{avatar.Name}] 赶路逻辑异常");
                    state.FlyingState = false;
                    return false;
                }

                break;

            case "流浪者":
                if (state.PendingApproach)
                {
                    var shouldApproach = ShouldApproach(distance, nextDistance, waypoint, nextWaypoint, avatar.Name);

                    if (shouldApproach)
                    {
                        Simulation.ReleaseAllKey();
                        // Logger.LogInformation("[赶路调试] 流浪者 触发接近: dist={d}, flying={f}, spaceExist={s}",
                        //     Math.Round(distance, 1), state.FlyingState, SpaceAtSecondPlaceExist(state));
                        state.PendingApproach = false;
                        // 同时检查状态字段和实时像素，确保终点（新 HurryOnState，FlyingState=false）也能下车
                        if (state.FlyingState || SpaceAtSecondPlaceExist(state))
                        {
                            if (SpaceAtSecondPlaceExist(state))
                            {
                                Logger.LogInformation("自动赶路：流浪者接近节点，关闭飞行状态");
                                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                                await SafeLanding(ct);
                            }
                            state.FlyingState = false;
                        }
                        return false;
                    }
                }

                if (state.FlyingState)
                {
                    if ((DateTime.UtcNow - _lastSkillCheckTime).TotalSeconds < 0.5)
                        return true;
                    _lastSkillCheckTime = DateTime.UtcNow;

                    if (!SpaceAtSecondPlaceExist(state))
                    {
                        state.FlyingState = false;
                        _lastLandingTime = DateTime.UtcNow;
                        Logger.LogInformation("自动赶路：流浪者飞行结束");
                        await SafeLanding(ct);
                        return false;
                    }
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                    state.WandererFlightCheckCount++;
                    if (state.WandererFlightCheckCount % 3 == 0)
                        Simulation.SendInput.Mouse.MiddleButtonClick();
                    return true;
                }

                if (distance > PartyConfig.Distance
                    && (waypoint?.MoveMode == MoveModeEnum.Run.Code || waypoint?.MoveMode == MoveModeEnum.Dash.Code))
                {
                    await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);

                    if ((DateTime.UtcNow - _lastSkillCheckTime).TotalSeconds < 0.5)
                        return false;
                    _lastSkillCheckTime = DateTime.UtcNow;

                    if (state.RotationStableCount >= 1)
                    {
                        if ((DateTime.UtcNow - _lastLandingTime).TotalSeconds < 3)
                            return false;

                        var cd = await ReadEskillCdAsync("流浪者");
                        if (cd <= 0)
                        {
                            // Logger.LogInformation("[赶路调试] 流浪者 启动飞行: dist={d}, rotStable={rs}, cd={cd}",
                            //     Math.Round(distance, 1), state.RotationStableCount, cd);
                            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                            Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
                            await Delay(50, ct);
                            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                            await Delay(100, ct);
                            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                            await Delay(50, ct);
                            Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyDown);

                            avatar.LastSkillTime = DateTime.UtcNow;
                            state.FlyingState = true;
                            Logger.LogInformation("自动赶路：流浪者启动飞行");
                            return true;
                        }
                    }

                    return false;
                }

                break;
        }

        if ((waypoint?.MoveMode == MoveModeEnum.Fly.Code && PartyConfig.TravelMode == "连续赶路"
                || waypoint?.Action == ActionEnum.StopFlying.Code
                || waypoint?.MoveMode == MoveModeEnum.Dash.Code)
            && distance > 4)
        {
            var isClimb = Bv.GetMotionStatus(screen2) == MotionStatus.Climb;
            if (isClimb && state.ClimbLogo < 2 && waypoint.MoveMode != MoveModeEnum.Climb.Code)
            {
                await Delay(1000, ct);
                Simulation.SendInput.SimulateAction(GIActions.Drop);
                await Delay(500, ct);
                state.ClimbLogo++;
            }
        }

        return false;
    }

    private double GetMavikaColorDifference(ImageRegion screen2)
    {
        var pos = screen2.SrcMat.At<Vec3b>(978, 1692);
        var pos2 = screen2.SrcMat.At<Vec3b>(995, 1702);
        return Math.Sqrt(
            Math.Pow(pos.Item0 - pos2.Item0, 2) +
            Math.Pow(pos.Item1 - pos2.Item1, 2) +
            Math.Pow(pos.Item2 - pos2.Item2, 2)
        );
    }

    /// <summary>
    /// 计算上一节点→当前节点→下一节点形成的转向夹角（度）。
    /// 使用 GameX/GameY（原神世界坐标）以保证坐标系一致。
    /// </summary>
    private static double CalculateTurnAngle(WaypointForTrack? prev, WaypointForTrack curr, WaypointForTrack? next)
    {
        if (prev == null || next == null) return 0;

        double baX = curr.GameX - prev.GameX;
        double baY = curr.GameY - prev.GameY;
        double bcX = next.GameX - curr.GameX;
        double bcY = next.GameY - curr.GameY;

        double dot = baX * bcX + baY * bcY;
        double magBA = Math.Sqrt(baX * baX + baY * baY);
        double magBC = Math.Sqrt(bcX * bcX + bcY * bcY);

        if (magBA < 0.001 || magBC < 0.001) return 0;

        double cosAngle = dot / (magBA * magBC);
        cosAngle = Math.Clamp(cosAngle, -1.0, 1.0);

        return Math.Acos(cosAngle) * 180.0 / Math.PI;
    }

    /// <summary>
    /// 检查当前路径转向角是否超过角色的阈值，是则应提前下车。
    /// </summary>
    private bool IsTurnTooSharp(WaypointForTrack waypoint, WaypointForTrack? nextWaypoint, string avatarName)
    {
        if (CurWaypoint.Item1 <= 0) return false;
        var prev = CurWaypoints.Item2[CurWaypoint.Item1 - 1];
        var angle = CalculateTurnAngle(prev, waypoint, nextWaypoint);
        var threshold = TurnAngleThresholds.GetValueOrDefault(avatarName, 120);
        return angle >= threshold;
    }

    private bool ShouldApproach(double distance, double? nextDistance, WaypointForTrack waypoint, WaypointForTrack? nextWaypoint, string avatarName)
    {
        var effectiveStopDist = Math.Min(PartyConfig.ApproachStopDistance, PartyConfig.Distance);

        // 精确接近模式下直接使用停止距离阈值
        if (PartyConfig.TravelMode == "精准靠近"
            && distance < effectiveStopDist)
        {
            return true;
        }

        if (PartyConfig.TravelMode == "连续赶路"                            // 连续赶路模式
            && distance < Math.Max(effectiveStopDist, 15)                   // 距离当前航点足够近
            && (nextDistance < 25                                           // 下一个航点也很近（密集区域）
                || nextWaypoint?.Type == WaypointType.Target.Code           // 下一个是目标点
                || (nextWaypoint == null                                        // 终点节点
                    || nextWaypoint?.Type == WaypointType.Teleport.Code)     // 或传送节点
                || (nextWaypoint?.MoveMode != MoveModeEnum.Run.Code         // 下一个节点不是Run/Dash
                    && nextWaypoint?.MoveMode != MoveModeEnum.Dash.Code)
                || waypoint?.Action == ActionEnum.Fight.Code                // 当前是战斗节点
                || waypoint.Type == WaypointType.Target.Code                // 当前是目标点
                || waypoint?.Action == ActionEnum.CombatScript.Code         // 当前节点有简易策略脚本
                || IsTurnTooSharp(waypoint, nextWaypoint, avatarName)))      // 路径转向角过大
        {
            // Logger.LogInformation("[赶路调试] ShouldApproach 连续赶路+特殊条件: dist={d}, stopDist={s}, nextDist={nd}, nextType={nt}, waypointType={wt}",
            //     Math.Round(distance, 1), effectiveStopDist, nextDistance, nextWaypoint?.Type, waypoint?.Type);
            return true;
        }

        // Logger.LogInformation("[赶路调试] ShouldApproach 不触发: dist={d}, stopDist={s}, travelMode={tm}, avatar={a}",
        //     Math.Round(distance, 1), effectiveStopDist, PartyConfig.TravelMode, avatarName);
        return false;
    }

    private int DetectStamina(ImageRegion? existingCapture = null)
    {
        // 进入新节点且上一个节点是战斗或传送时重置体力为满值
        if (CurWaypoint.Item1 != _lastWaypointIndex
            && CurWaypoint.Item1 > 0)
        {
            var prev = CurWaypoints.Item2[CurWaypoint.Item1 - 1];
            var isFightOrTeleport = prev.Type == WaypointType.Teleport.Code
                || prev.Action == ActionEnum.Fight.Code;
            if (isFightOrTeleport)
            {
                _lastStamina = 240;
                _lastWaypointIndex = CurWaypoint.Item1;
                RecordStaminaResult(240);
                return 240;
            }
        }

        var ownedCapture = existingCapture == null ? CaptureToRectArea() : null;
        try
        {
            var ra = ownedCapture ?? existingCapture!;

            // 体力条区域：1000,430 - 1100,630（1920×1080）
            using var crop = ra.DeriveCrop(1000, 430, 100, 200);

            // #FFC700 → BGR(0, 199, 255)，精确匹配，无色差
            using var mask = new Mat();
            Cv2.InRange(crop.SrcMat, new Scalar(0, 199, 255), new Scalar(0, 199, 255), mask);

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();

            var numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
                connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

            int totalArea = 0;
            for (int i = 1; i < numLabels; i++)
            {
                var area = stats.At<int>(i, 4); // CC_STAT_AREA = 4
                if (area >= 21)
                {
                    totalArea += area;
                }
            }

            if (totalArea > 0)
            {
                _lastStamina = totalArea;
                // Logger.LogInformation("INF 体力识别：{Value}", totalArea);
                RecordStaminaResult(totalArea);
                return totalArea;
            }

            // 无任何有效黄色连通域 → 检查历史趋势强制返回值
            int forced;
            if (HasStaminaStreak(0, 20, 39))
                forced = 0;
            else if (HasStaminaStreak(240, 20, 39))
                forced = 240;
            else
                forced = _lastStamina > 120 ? 240 : 0;

            // Logger.LogInformation("INF 体力识别：无有效区域，上次={Last}，强制={Forced}", _lastStamina, forced);
            RecordStaminaResult(forced);
            return forced;
        }
        finally
        {
            ownedCapture?.Dispose();
        }
    }

    /// <summary>
    /// 记录体力识别返回值，保留最近至多50次。
    /// </summary>
    private void RecordStaminaResult(int value)
    {
        _staminaHistory.Add(value);
        if (_staminaHistory.Count > 50)
        {
            _staminaHistory.RemoveRange(0, _staminaHistory.Count - 50);
        }
    }

    /// <summary>
    /// 在最近 lookback 次历史记录中，检查是否存在连续 streakCount 次等于 target 的记录。
    /// </summary>
    private bool HasStaminaStreak(int target, int streakCount, int lookback)
    {
        var count = _staminaHistory.Count;
        if (count < streakCount) return false;
        var start = Math.Max(0, count - lookback);
        var consecutive = 0;
        for (int i = start; i < count; i++)
        {
            if (_staminaHistory[i] == target)
            {
                consecutive++;
                if (consecutive >= streakCount) return true;
            }
            else
            {
                consecutive = 0;
            }
        }
        return false;
    }

    private bool DashAtSecondPlaceExist()
    {
        using var region = CaptureToRectArea().DeriveCrop(1595, 1028, 9, 7);
        using var mask = OpenCvCommonHelper.Threshold(region.SrcMat,
            new Scalar(242, 223, 39), new Scalar(255, 233, 44));
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();

        var numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
            connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

        return numLabels > 1;
    }

    private bool SpaceAtSecondPlaceExist(HurryOnState state)
    {
        using var region = CaptureToRectArea();
        var pixel = region.SrcMat.At<Vec3b>(1028, 1584);
        return pixel.Item0 >= 250 && pixel.Item1 >= 250 && pixel.Item2 >= 250;
    }

    private async Task SafeLanding(CancellationToken ct)
    {
        await Delay(250, ct);
        Simulation.SendInput.SimulateAction(GIActions.Jump);
        await Delay(150, ct);

        using var screen = CaptureToRectArea();
        var stamina = DetectStamina(screen);
        if (Bv.GetMotionStatus(screen) == MotionStatus.Fly
            || stamina == 0
            || stamina == 240)
        {
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
            await Delay(300, ct);
            for (int i = 0; i < 5; i++)
            {
                using var retryRegion = CaptureToRectArea();
                if (Bv.GetMotionStatus(retryRegion) == MotionStatus.Fly)
                {
                    Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                    await Delay(300, ct);
                }
                else break;
            }
        }
    }

    private static bool SwimmingConfirm(Region region)
    {
        var fullRegion = region.ToImageRegion();
        bool ownRegion = fullRegion != region;
        try
        {
            using var regionMat = fullRegion.DeriveCrop(1819, 1028, 9, 7);
            using var mask = OpenCvCommonHelper.Threshold(regionMat.SrcMat,
                new Scalar(242, 223, 39), new Scalar(255, 233, 44));
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();

            var numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
                connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

            return numLabels > 1;
        }
        finally
        {
            if (ownRegion) fullRegion.Dispose();
        }
    }

    private async Task<double> ReadEskillCdAsync(string avatarName)
    {
        using var cdRegion = CaptureToRectArea();
        var eRa = cdRegion.DeriveCrop(AutoFightAssets.Get(cdRegion).ECooldownRect);
        using var eRaWhite = OpenCvCommonHelper.InRangeHsv(eRa.SrcMat, new Scalar(0, 0, 235), new Scalar(0, 25, 255));
        var text = OcrFactory.Paddle.OcrWithoutDetector(eRaWhite);
        var cd = StringUtils.TryParseDouble(text);
        ESkillCdTracker.Record(avatarName, cd);
        if (cd <= 0)
        {
            ESkillCdTracker.ApplyFallback(avatarName, log: false);
        }
        return cd;
    }

    /// <summary>
    /// 尝试执行赶路技能逻辑（含旋转稳定性跟踪、节点类型过滤）
    /// </summary>
    /// <returns>true 表示赶路逻辑已处理，主循环应 continue</returns>
    private async Task<bool> TryHurryOnAsync(double diff, WaypointForTrack waypoint, double distance, ImageRegion screen, int num, HurryOnState hurryOnState)
    {
        try
        {
            // 更新旋转稳定性计数
            if (Math.Abs(diff) <= 60)
            {
                hurryOnState.RotationStableCount++;
            }
            else
            {
                hurryOnState.RotationStableCount = 0;
            }

            var avatar = _combatScenes?.SelectAvatar(_hurryOnAvatar);
            // Logger.LogInformation("[赶路调试] TryHurryOnAsync  entry: avatar={a}(hurryOn={ha}), dist={d}, moveMode={m}, type={t}, diff={df}, rotStable={rs}",
            //     _hurryOnAvatar, PartyConfig.HurryOnAvatar, Math.Round(distance, 1), waypoint?.MoveMode, waypoint?.Type, Math.Round(diff, 1), hurryOnState.RotationStableCount);
            // 从当前路线上下文解析下一个路径点
            WaypointForTrack? nextWaypoint = null;
            double? nextDistance = null;
            var currentList = CurWaypoints.Item2;
            var currentIndex = CurWaypoint.Item1;
            if (currentList != null && currentIndex >= 0 && currentIndex + 1 < currentList.Count)
            {
                nextWaypoint = currentList[currentIndex + 1];
                nextDistance = Navigation.GetDistance(waypoint, new Point2f((float)nextWaypoint.X, (float)nextWaypoint.Y));
            }

            var result = await ExecuteHurryOnAsync(waypoint, nextWaypoint, distance, nextDistance, true, avatar, screen, num, hurryOnState, default);
            // Logger.LogInformation("[赶路调试] TryHurryOnAsync  exit: result={r}", result);
            return result;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "赶路逻辑执行异常");
            return false;
        }
    }

    private void InitHurryOnConfig()
    {
        if (PartyConfig.HurryOnAvatar == "自动" && _combatScenes != null)
        {
            var avatars = _combatScenes.GetAvatars();

            // 第一步：检查行走位（MainAvatarIndex）对应的角色是否为赶路角色
            if (!string.IsNullOrEmpty(PartyConfig.MainAvatarIndex)
                && int.TryParse(PartyConfig.MainAvatarIndex, out var mainIdx)
                && mainIdx >= 1 && mainIdx <= avatars.Count)
            {
                var mainAvatar = avatars[mainIdx - 1];
                if (PartyConfig.HurryOnAvatarList.Contains(mainAvatar.Name))
                {
                    _hurryOnAvatar = mainAvatar.Name;
                    Logger.LogInformation("自动赶路角色：行走位 {Name}({Index})", mainAvatar.Name, mainIdx);
                    return;
                }
            }

            // 第二步：按 HurryOnAvatarList 顺序依次检查是否在队伍中
            foreach (var name in PartyConfig.HurryOnAvatarList)
            {
                if (string.IsNullOrEmpty(name) || name == "自动") continue;
                if (avatars.Any(a => a.Name == name))
                {
                    _hurryOnAvatar = name;
                    Logger.LogInformation("自动赶路角色：按优先级选择 {Name}", name);
                    return;
                }
            }

            _hurryOnAvatar = "";
        }
        else
        {
            _hurryOnAvatar = PartyConfig.HurryOnAvatar;

            // 验证手动指定的角色是否在队伍中，不在则不启用赶路
            if (_combatScenes != null && !string.IsNullOrEmpty(_hurryOnAvatar))
            {
                var avatars = _combatScenes.GetAvatars();
                if (!avatars.Any(a => a.Name == _hurryOnAvatar))
                {
                    Logger.LogWarning("手动指定的赶路角色 {Name} 不在当前队伍中，不启用赶路", _hurryOnAvatar);
                    _hurryOnAvatar = "";
                }
            }
        }

        if (string.IsNullOrEmpty(PartyConfig.TravelMode))
        {
            PartyConfig.TravelMode = "精准靠近";
        }
    }
}
