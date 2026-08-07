﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
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

                //满足条件时，尝试上车
                if (distance > PartyConfig.Distance)
                {
                    await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);

                    var boardIconState = GetMavikaESkillIconState(screen2);
                    // 内置冷却：玛薇卡上/下车动作后有约1秒无法再次上/下车，与E技能冷却无关（放宽至2秒防抖）
                    if ((DateTime.UtcNow - _lastMavikaBoardTime).TotalSeconds >= 2 && boardIconState is 1 or 2)
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
                    // 刚上车后的2秒内跳过图标检测（上/下车动作期间图标不稳定），强制视为通过
                    var justBoarded = (DateTime.UtcNow - _lastMavikaBoardTime).TotalSeconds < 2;
                    if (!justBoarded && !(jumpFlyIconState == 3 || jumpFlyIconState == 4 && await ReadEskillCdAsync("玛薇卡", updateTracking: false) < 1))
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

                // 玛薇卡逻辑最后：勾选了禁用冲刺时，在车上（下车图标刚上车）跳过本帧通用移动逻辑以禁用冲刺
                if (PartyConfig.MwkDisableSprintEnabled
                    && (iconState == 3 || iconState == 4 && await ReadEskillCdAsync("玛薇卡", updateTracking: false) < 1))
                {
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
                Type = "F2", Channel = "V", X = 1676, Y = 971, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9824, Weight = 0.8834,
                RefHist = [0.0277, 0, 0, 0, 0, 0, 0.0114, 0.9609],
                ProbTable = [0, 0, 0, 0.0001, 0.0002, 0.0007, 0.0018, 0.0049, 0.0133, 0.0355, 0.0908, 0.2136, 0.4247, 0.6674, 0.8451, 0.9368, 0.9758, 0.991, 0.9967, 0.9988, 0.9995]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "S", X = 1691, Y = 988, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9954, Weight = 0.879,
                RefHist = [0, 0.0052, 0.9827, 0.012, 0, 0, 0, 0],
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0013, 0.0036, 0.0096, 0.0258, 0.0671, 0.1635, 0.347, 0.5909, 0.797, 0.9143, 0.9667]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1692, Y = 988, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9796, Weight = 0.8914,
                RefHist = [0, 0.0203, 0.9703, 0.0094, 0, 0, 0, 0],
                ProbTable = [0, 0.0001, 0.0003, 0.0009, 0.0024, 0.0064, 0.0171, 0.0452, 0.114, 0.2591, 0.4873, 0.7209, 0.8754, 0.9502, 0.9811, 0.993, 0.9974, 0.999, 0.9996, 0.9999, 1]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1728, Y = 990, W = 3, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9995, Weight = 0.9179,
                RefHist = [0, 0, 0, 0, 0.0011, 0.9916, 0.0068, 0.0005],
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0007, 0.0019, 0.0052, 0.0141, 0.0375, 0.0957, 0.2234, 0.4388, 0.68, 0.8524, 0.9401, 0.9771]
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
                Type = "F2", Channel = "V", X = 1685, Y = 962, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9923, Weight = 0.8962,
                RefHist = [0, 0, 0, 0.0019, 0.9895, 0.0085, 0, 0],
                ProbTable = [0, 0, 0, 0, 0.0001, 0.0002, 0.0004, 0.0012, 0.0032, 0.0086, 0.023, 0.0601, 0.148, 0.3208, 0.5622, 0.7773, 0.9046, 0.9627, 0.9859, 0.9948, 0.9981]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "S", X = 1694, Y = 974, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9914, Weight = 0.8569,
                RefHist = [0, 0, 0.0149, 0.9851, 0, 0, 0, 0],
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0006, 0.0017, 0.0046, 0.0125, 0.0333, 0.0855, 0.2027, 0.4087, 0.6527, 0.8363, 0.9328, 0.9742, 0.9903, 0.9964]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "S", X = 1710, Y = 992, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9884, Weight = 0.8708,
                RefHist = [0, 0, 0, 0, 0.009, 0.9751, 0.0159, 0],
                ProbTable = [0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0026, 0.0069, 0.0187, 0.0491, 0.1231, 0.2763, 0.5092, 0.7383, 0.8846, 0.9542, 0.9827, 0.9935, 0.9976, 0.9991, 0.9997]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1712, Y = 993, W = 2, H = 3,
                IsCircular = false, Range = 1, RefVal = 0.9942, Weight = 0.8852,
                RefHist = [0, 0, 0, 0.0006, 0.0014, 0.9909, 0.0071, 0],
                ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0021, 0.0056, 0.0151, 0.0401, 0.1019, 0.2357, 0.456, 0.695, 0.861, 0.9439, 0.9786, 0.992]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1717, Y = 1011, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9934, Weight = 0.8908,
                RefHist = [0, 0, 0, 0, 0.008, 0.992, 0, 0],
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0006, 0.0015, 0.0042, 0.0113, 0.03, 0.0776, 0.1861, 0.3833, 0.6281, 0.8212, 0.9258, 0.9714, 0.9893, 0.996]
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
                Type = "F2", Channel = "V", X = 1697, Y = 966, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9821, Weight = 0.8096,
                RefHist = [0, 0, 0.0062, 0.954, 0.0398, 0, 0, 0],
                ProbTable = [0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.001, 0.0028, 0.0075, 0.02, 0.0526, 0.131, 0.2907, 0.527, 0.7518, 0.8917, 0.9572, 0.9838, 0.994, 0.9978]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1705, Y = 988, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9913, Weight = 0.8934,
                RefHist = [0, 0, 0, 0, 0, 0, 0.0175, 0.9825],
                ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0038, 0.0102, 0.0272, 0.0706, 0.1711, 0.3594, 0.604, 0.8057, 0.9185, 0.9684, 0.9881]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1706, Y = 991, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9947, Weight = 0.8306,
                RefHist = [0, 0, 0, 0, 0, 0, 0.0222, 0.9778],
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0022, 0.006, 0.0162, 0.0428, 0.1084, 0.2483, 0.4731, 0.7094, 0.869, 0.9475]
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
