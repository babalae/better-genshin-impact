﻿﻿using System;
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
    private static readonly HashSet<string> HurryOnBlacklist = ["玛薇卡", "希诺宁", "瓦雷莎", "茜特菈莉", "伊法", "恰斯卡", "玛拉妮", "基尼奇"];

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
    private DateTime _lastMavikaSprintTime = DateTime.MinValue;
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
    /// 获取切人步行目标序号：优先行走位（MainAvatarIndex），否则排除赶路角色自身 + 黑名单，取序号最靠前的有效角色。
    /// 若排除后无合法角色，则忽略黑名单再试一次。
    /// 返回 "1"/"2"/"3"/"4"，不会返回 null。
    /// </summary>
    private string GetSwitchToWalkIndex()
    {
        // 第一步：优先行走位（MainAvatarIndex），仍需排除赶路角色自身与黑名单
        if (!string.IsNullOrEmpty(PartyConfig.MainAvatarIndex)
            && int.TryParse(PartyConfig.MainAvatarIndex, out var mainIdx)
            && mainIdx >= 1 && mainIdx <= 4)
        {
            var mainAvatar = _combatScenes?.SelectAvatar(mainIdx);
            if (mainAvatar != null
                && mainAvatar.Name != _hurryOnAvatar
                && !HurryOnBlacklist.Contains(mainAvatar.Name))
            {
                return mainIdx.ToString();
            }
        }

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

                //应该下车时尝试下车，下车成功后（PendingApproach=false）本航点内不再重复检测
                var mwkShouldApproach = ShouldApproach(distance, nextDistance, waypoint, nextWaypoint, avatar.Name);
                if (mwkShouldApproach && state.PendingApproach)
                {
                    if (PartyConfig.SwitchToWalkEnabled)
                    {
                        // 切人下车：无需检测图标，直接切换步行角色
                        Simulation.ReleaseAllKey();
                        var nextIdx = GetSwitchToWalkIndex();
                        Logger.LogInformation("自动赶路：玛薇卡接近节点，切人步行 {t}", nextIdx);
                        await SwitchAvatar(nextIdx);
                        // 切人成功即认为下车成功
                        state.PendingApproach = false;
                    }
                    else
                    {
                        // 点按E下车：持续检测，图标为下车(3)时才松键并执行下车
                        var approachIconState = GetMavikaESkillIconState(screen2);
                        if (approachIconState == 3 && Bv.GetMotionStatus(screen2) != MotionStatus.Fly)
                        {
                            Simulation.ReleaseAllKey();
                            Logger.LogInformation("自动赶路：玛薇卡接近节点，下车步行");
                            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                            await Delay(100, ct);
                            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                            await Delay(50, ct);
                        }
                        // 检测到图标1/2（续技能/上车）即认为下车成功
                        if (approachIconState is 1 or 2)
                        {
                            state.PendingApproach = false;
                        }
                    }
                    return false;
                }

                // 不满足冲刺条件（旋转稳定且在车上）时松开冲刺键，避免残留按住状态
                var gapIconState = GetMavikaESkillIconState(screen2);
                if (state.RotationStableCount < 1
                    || !(gapIconState == 3 || gapIconState == 4 && await ReadEskillCdAsync("玛薇卡", updateTracking: false) < 1))
                {
                    Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
                    // 重置冲刺计时，避免恢复稳定后立即补一次冲刺（无视CD）
                    _lastMavikaSprintTime = DateTime.UtcNow;
                }

                //满足条件时，尝试上车
                if (distance > PartyConfig.Distance)
                {
                    await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);

                    var boardIconState = GetMavikaESkillIconState(screen2);
                    // 内置冷却：玛薇卡上/下车动作后有约1秒无法再次上/下车，与E技能冷却无关（放宽至3秒防抖）
                    if ((DateTime.UtcNow - _lastMavikaBoardTime).TotalSeconds >= 3 && boardIconState is 1 or 2)
                    {
                        _lastMavikaBoardTime = DateTime.UtcNow;
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                        await Delay(200, ct);
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                        await Delay(300, ct);
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                        await Delay(700, ct);

                        // E技能CD跟踪：仅续技能（状态1）触发更新，上车（状态2）不触发
                        if (boardIconState == 1)
                        {
                            await ReadEskillCdAsync("玛薇卡");
                        }

                        // 上车后不跳出当前帧，继续执行跳飞判定
                    }
                }

                //满足条件时，尝试跳飞
                if (PartyConfig.MwkJumpFlyEnabled && distance > PartyConfig.MwkJumpFlyDistance && state.RotationStableCount >= 1)
                {
                    var jumpFlyIconState = GetMavikaESkillIconState(screen2);
                    if (!(jumpFlyIconState == 3 || jumpFlyIconState == 4 && await ReadEskillCdAsync("玛薇卡", updateTracking: false) < 1))
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

                var iconState = GetMavikaESkillIconState(screen2);
                if ((iconState == 3 || iconState == 4 && await ReadEskillCdAsync("玛薇卡", updateTracking: false) < 1) && distance > PartyConfig.Distance)
                {
                    if (Bv.GetMotionStatus(screen2) == MotionStatus.Climb)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.Drop);
                        await Delay(500, ct);
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
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

                }

                // 玛薇卡逻辑最后：在车上（下车图标刚上车）时跳过本帧通用移动逻辑，旋转稳定时才按冲刺间隔配置冲刺
                if (iconState == 3 || iconState == 4 && await ReadEskillCdAsync("玛薇卡", updateTracking: false) < 1)
                {
                    // 旋转稳定才执行冲刺；旋转不稳定时不冲刺，但仍跳过通用移动逻辑
                    if (state.RotationStableCount >= 1 && PartyConfig.MwkSprintIntervalSeconds > 0)
                    {
                        if ((DateTime.UtcNow - _lastMavikaSprintTime).TotalSeconds >= PartyConfig.MwkSprintIntervalSeconds)
                        {
                            _lastMavikaSprintTime = DateTime.UtcNow;
                            // 松开当前按住状态，下一帧会重新按住，形成一次冲刺
                            Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
                        }
                        else
                        {
                            Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyDown);
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

                    // C1 闲云有两次 E 可用，直接尝试施放；E 不可用时按键无副作用，不需要 OCR 检测 CD
                    // 使用非阻塞时间间隔（复用 _lastJumpFlyTime，类似火神跳飞），避免阻塞期间无法转向
                    if (state.RotationStableCount >= 1
                        && (DateTime.UtcNow - _lastJumpFlyTime).TotalSeconds >= interval / 2.0)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                        _lastJumpFlyTime = DateTime.UtcNow;
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

    /// <summary>
    /// 玛薇卡E技能图标状态识别阈值（评分须严格大于该值才视为匹配，防止空模型误判）
    /// </summary>
    private const double MavikaESkillIconThreshold = 0.5;

    /// <summary>
    /// 玛薇卡E技能图标状态模型：1=续技能
    /// 特征模型数据由训练工具导出（指标-续技能.json）
    /// </summary>
    private static readonly FeatureScorerExportData MavikaESkillContinueModel = new()
    {
        Features =
        {
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1700, Y = 960, W = 3, H = 4,
                IsCircular = false, Range = 1, RefVal = 0.9917, Weight = 0.766,
                RefHist = [0.0012543556332223562, 0.0011810046506758249, 0.0017439523425184357, 0.002752956346626525, 0.0018676885904965039, 0.02072012951858713, 0.9298806206255811, 0.04059929229229214],
                ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0007, 0.0019, 0.0052, 0.014, 0.0371, 0.0947, 0.2214, 0.436, 0.6775, 0.851, 0.9395, 0.9769, 0.9914]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1676, Y = 970, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9899, Weight = 0.7734,
                RefHist = [0, 0, 0, 0, 0, 0, 0.06034348986743187, 0.9396565101325682],
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0039, 0.0105, 0.028, 0.0727, 0.1756, 0.3667, 0.6115, 0.8106, 0.9208, 0.9693]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "S", X = 1691, Y = 988, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 1, Weight = 0.7986,
                RefHist = [0, 0, 1, 0, 0, 0, 0, 0],
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0029, 0.0078, 0.0209, 0.0548, 0.1362, 0.3001, 0.5382, 0.7601, 0.896]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1696, Y = 970, W = 3, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9932, Weight = 0.7837,
                RefHist = [0.03324684585190858, 0.9562541387048271, 0.010499015443264311, 0, 0, 0, 0, 0],
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0011, 0.003, 0.0081, 0.0218, 0.057, 0.1412, 0.3088, 0.5484, 0.7675, 0.8997, 0.9606]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1728, Y = 990, W = 3, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9983, Weight = 0.9122,
                RefHist = [0, 0, 0, 0, 0, 0.9800984894385059, 0.018037805644772822, 0.0018637049167212296],
                ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0006, 0.0017, 0.0046, 0.0123, 0.0328, 0.0844, 0.2003, 0.405, 0.6492, 0.8342, 0.9318, 0.9738, 0.9902]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1697, Y = 1022, W = 2, H = 3,
                IsCircular = false, Range = 1, RefVal = 0.9739, Weight = 0.7739,
                RefHist = [0, 0, 0, 0.014463663026665165, 0, 0.05699667473142457, 0.9045152581853189, 0.024024404056591495],
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0024, 0.0064, 0.0171, 0.0453, 0.1141, 0.2594, 0.4877, 0.7213, 0.8755, 0.9503, 0.9811, 0.993, 0.9974]
            },
        }
    };

    /// <summary>
    /// 玛薇卡E技能图标状态模型：2=上车
    /// 特征模型数据由训练工具导出（指标-上车.json）
    /// </summary>
    private static readonly FeatureScorerExportData MavikaESkillBoardModel = new()
    {
        Features =
        {
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1686, Y = 963, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9903, Weight = 0.8903,
                RefHist = [0, 0, 0, 0, 0.9892811138283595, 0.010718886171640567, 0, 0],
                ProbTable = [0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0021, 0.0056, 0.015, 0.0398, 0.1012, 0.2343, 0.454, 0.6933, 0.86, 0.9435, 0.9785, 0.992, 0.997, 0.9989]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "S", X = 1692, Y = 970, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9882, Weight = 0.8568,
                RefHist = [0, 0.016642596135750816, 0.9638889598441912, 0.01946844402005793, 0, 0, 0, 0],
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0013, 0.0034, 0.0092, 0.0245, 0.064, 0.1567, 0.3356, 0.5786, 0.7887, 0.9103, 0.965, 0.9868, 0.9951]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "S", X = 1700, Y = 979, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9931, Weight = 0.8834,
                RefHist = [0, 0, 0.0014888332289077013, 0.9851538051239543, 0.013357361647137802, 0, 0, 0],
                ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0039, 0.0106, 0.0283, 0.0734, 0.1772, 0.3693, 0.6141, 0.8123, 0.9216, 0.9697, 0.9886]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "S", X = 1711, Y = 990, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.991, Weight = 0.8805,
                RefHist = [0, 0, 0, 0.0017864597868968, 0.016793844158299848, 0.9759536625405839, 0.005466033514219401, 0],
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0004, 0.001, 0.0026, 0.0071, 0.019, 0.05, 0.1252, 0.28, 0.5139, 0.7419, 0.8865, 0.955, 0.983, 0.9937, 0.9977]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1717, Y = 1011, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9921, Weight = 0.863,
                RefHist = [0, 0, 0, 0, 0.009584958224132892, 0.9904150417758671, 0, 0],
                ProbTable = [0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0015, 0.0039, 0.0107, 0.0284, 0.0737, 0.1778, 0.3703, 0.6151, 0.8129, 0.9219, 0.9698, 0.9887, 0.9958, 0.9985, 0.9994]
            },
        }
    };

    /// <summary>
    /// 玛薇卡E技能图标状态模型：3=下车
    /// 特征模型数据由训练工具导出（指标-下车.json）
    /// </summary>
    private static readonly FeatureScorerExportData MavikaESkillDismountModel = new()
    {
        Features =
        {
            new FeatureScorerItem
            {
                Type = "F2", Channel = "S", X = 1698, Y = 966, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9928, Weight = 0.9068,
                RefHist = [0, 0, 0.02356926501913827, 0.9612878755834396, 0.015142859397422133, 0, 0, 0],
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.002, 0.0055, 0.0149, 0.0394, 0.1004, 0.2328, 0.4519, 0.6915, 0.859, 0.9431, 0.9783, 0.9919, 0.997]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1692, Y = 961, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9999, Weight = 0.8876,
                RefHist = [0, 0, 0.0018366169880083437, 0, 0, 0.9944093896826848, 0.00375399332930691, 0],
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0004, 0.001, 0.0026, 0.0071, 0.019, 0.05, 0.1251, 0.28, 0.5138, 0.7418, 0.8865, 0.955]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1700, Y = 961, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9993, Weight = 0.7434,
                RefHist = [0.003399565999189457, 0.0033261401384723347, 0.000976956062830274, 0.007602934387829995, 0.004166150296940107, 0.9744626499323689, 0.006065603182368956, 0],
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0013, 0.0035, 0.0095, 0.0253, 0.0659, 0.161, 0.3427, 0.5863, 0.7939, 0.9128]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1692, Y = 983, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9726, Weight = 0.8227,
                RefHist = [0, 0, 0.0010456854604408044, 0.002458671439219928, 0.0012471232672327697, 0.009836431437600022, 0.07023793250520412, 0.9151741558903024],
                ProbTable = [0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0024, 0.0065, 0.0174, 0.0459, 0.1156, 0.2622, 0.4913, 0.7242, 0.8771, 0.951, 0.9814, 0.9931, 0.9974, 0.9991]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1705, Y = 988, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9821, Weight = 0.7976,
                RefHist = [0, 0, 0, 0, 0, 0, 0.037842264161779195, 0.9621577358382208],
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0023, 0.0062, 0.0166, 0.0439, 0.1109, 0.2532, 0.4797, 0.7148, 0.872, 0.9488, 0.9805]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1706, Y = 991, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9933, Weight = 0.8081,
                RefHist = [0, 0, 0, 0, 0, 0, 0.025740694314201038, 0.974259305685799],
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0021, 0.0056, 0.0152, 0.0402, 0.1021, 0.2362, 0.4567, 0.6956, 0.8613, 0.9441]
            },
        }
    };

    /// <summary>
    /// 识别当前帧玛薇卡E技能图标状态。
    /// 1=续技能图标，2=上车图标，3=下车图标，4=其他/未知。
    /// 对三种图标特征模型分别评分，取最高分且严格超过阈值者作为当前状态。
    /// </summary>
    private int GetMavikaESkillIconState(ImageRegion screen)
    {
        try
        {
            var continueScore = ImageFeatureScorer.Score(MavikaESkillContinueModel, screen.SrcMat);
            var boardScore = ImageFeatureScorer.Score(MavikaESkillBoardModel, screen.SrcMat);
            var dismountScore = ImageFeatureScorer.Score(MavikaESkillDismountModel, screen.SrcMat);

            if (continueScore >= boardScore && continueScore >= dismountScore && continueScore > MavikaESkillIconThreshold)
            {
                return 1;
            }
            if (boardScore >= continueScore && boardScore >= dismountScore && boardScore > MavikaESkillIconThreshold)
            {
                return 2;
            }
            if (dismountScore >= continueScore && dismountScore >= boardScore && dismountScore > MavikaESkillIconThreshold)
            {
                return 3;
            }
            return 4;
        }
        catch (Exception e)
        {
            Logger.LogWarning("玛薇卡E技能图标状态识别异常: {Message}", e.Message);
            return 4;
        }
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

    /// <summary>
    /// 读取指定角色 E 技能冷却秒数。
    /// <paramref name="updateTracking"/> 为 true（默认）时同时更新冷却跟踪（Record + 兜底），
    /// 仅用于技能施放后刷新跟踪器；纯读取判断请传 false 避免副作用。
    /// </summary>
    private async Task<double> ReadEskillCdAsync(string avatarName, bool updateTracking = true)
    {
        using var cdRegion = CaptureToRectArea();
        var eRa = cdRegion.DeriveCrop(AutoFightAssets.Get(cdRegion).ECooldownRect);
        using var eRaWhite = OpenCvCommonHelper.InRangeHsv(eRa.SrcMat, new Scalar(0, 0, 235), new Scalar(0, 25, 255));
        var text = OcrFactory.Paddle.OcrWithoutDetector(eRaWhite);
        var cd = StringUtils.TryParseDouble(text);
        // OCR 常丢失小数点：如 "0.3" 被读成 "03"，此时按 0.x 秒处理
        if (text != null && text.Length == 2 && text[0] == '0' && char.IsAsciiDigit(text[1]))
        {
            cd = (text[1] - '0') / 10.0;
        }
        if (updateTracking)
        {
            ESkillCdTracker.Record(avatarName, cd);
            if (cd <= 0)
            {
                ESkillCdTracker.ApplyFallback(avatarName, log: false);
            }
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
