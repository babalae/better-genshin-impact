using BetterGenshinImpact.Core.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
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
/// 角色技能加速赶路逻辑（玛薇卡、瓦雷莎、希诺宁、闲云、桑多涅、恰斯卡/伊法、流浪者、法尔伽、夜兰）
/// </summary>
public partial class PathExecutor
{
    private class HurryOnState
    {
        public bool SprintMouseLogo = true;
        public bool PendingApproach = true;
        public bool? RunToDash = false;
        public int MavikaSlopeCount;
        public int ClimbLogo;
        public int RotationStableCount;
        public bool FlyingState;
        public int WandererFlightCheckCount;
        /// <summary>
        /// 上次施放瓦雷莎 E 时的剩余距离。NaN = 本段尚未放 E。
        /// </summary>
        public double VaresaLastSkillDistance = double.NaN;
        /// <summary>
        /// 本次 E 之后是否已经观察到粉色夜魂条。看到 → 疾冲中；之后条消失 → 一段跑完。
        /// 用于区分"夜魂耗尽"与"刚放完 E、UI 还没把条画出来"，避免刚施放就连放下一段。
        /// </summary>
        public bool VaresaNightsoulSeen;
        /// <summary>
        /// 当前这段疾冲是否已提示过"两段充能都在冷却"，避免无充能时每帧刷同一条日志。
        /// </summary>
        public bool VaresaNoChargeLogged;
        /// <summary>
        /// 本次接近段尝试切步行位的次数，连续失败达到上限后放弃切换，避免卡住路线。
        /// </summary>
        public int VaresaWalkSwitchAttempts;
    }
    // 赶路切换角色黑名单，防止切人后触发夜魂传递
    private static readonly HashSet<string> HurryOnBlacklist = ["玛薇卡", "希诺宁", "瓦雷莎", "茜特菈莉", "伊法", "恰斯卡", "玛拉妮", "基尼奇"];

    /// <summary>
    /// 瓦雷莎技能赶路的最低启用距离（游戏内距离单位）。路段剩余距离低于该值时不进夜魂疾冲，
    /// 交还通用逻辑用普通速度接近并刹车——夜魂疾冲惯性滑行距离长，短路段用技能必冲过头。
    /// </summary>
    private const double HurryOnMinEntryDistance = 50;

    /// <summary>
    /// 赶路心跳日志的帧间隔。原来每 5 帧一条，瓦雷莎帧长约 400ms 时约 2 秒刷一条，日志噪声过大；
    /// 放稀到 25 帧（约 10 秒），只保留"还在赶路、剩余多少"的最低限度信息。
    /// </summary>
    private const int HurryOnLogFrameInterval = 25;

    /// <summary>
    /// 瓦雷莎 E 施放的最小间隔（ms），仅作防抖兜底。
    /// 两段 E 的实际节奏由夜魂条（是否有粉色填充）决定：条在 = 疾冲未结束，条消失 = 变回普通跑步才放下一段。
    /// </summary>
    private const int VaresaSkillIntervalMs = 500;

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
        { "法尔伽", 120 },
        { "夜兰", 120 },
    };

    private string _hurryOnAvatar = "";
    private DateTime _lastJumpFlyTime = DateTime.MinValue;
    private bool _jumpFlySafetyPending;
    private DateTime _lastMavikaBoardTime = DateTime.MinValue;
    /// <summary>
    /// 本次上车后已执行的冲刺跳飞次数（玛薇卡），上车时重置
    /// </summary>
    private int _mavikaSprintJumpCount;
    private DateTime _lastSkillCheckTime = DateTime.MinValue;
    private DateTime _lastVaresaSkillTime = DateTime.MinValue;
    private DateTime _lastLandingTime = DateTime.MinValue;

    /// <summary>
    /// 夜魂条确实出现过的像素下限。取低值，只用于确认"本段疾冲的条已经画出来了"，
    /// 避免第一段 E 刚施放、UI 还没刷新时把空条误判成"一段已跑完"而立刻连按第二段。
    /// </summary>
    private const int VaresaNightsoulSeenPixels = 100;

    /// <summary>
    /// 条填充降到该像素数以下即可放下一段 E。满条约 1400~1800 像素，800 约为消耗一半，
    /// 此时接第二段能把两段疾冲连起来，又不会一施放就把第二段浪费掉。
    /// </summary>
    private const int VaresaNextSkillPixels = 800;

    /// <summary>
    /// 上一帧识别的体力值，用于跨帧 fallback。初始为满值 240。
    /// </summary>
    private int _lastStamina = 240;
    private int _lastWaypointIndex = -1;
    private readonly List<int> _staminaHistory = new(50);
    private DateTime _lastSandroneSkillTime = DateTime.MinValue;
    /// <summary>
    /// 本次飞行开始时间（夜兰专用），用于飞行超时退出判断
    /// </summary>
    private DateTime _lastFlyStartTime = DateTime.MinValue;
    /// <summary>
    /// 飞行中上一帧距离（夜兰专用），用于检测飞行卡顿（连续2帧距离变化不超过8）
    /// </summary>
    private double _lastFlyDistance;
    /// <summary>
    /// 飞行中距离无明显变化的连续帧数（夜兰专用）
    /// </summary>
    private int _flyStillFrames;
    /// <summary>
    /// 法尔伽下次可施放E的时间（null 表示可施放），施放E后置为2秒后
    /// </summary>
    private DateTime? _falgaNextCheckTime;

    /// <summary>
    /// 获取切人步行目标序号：优先行走位（MainAvatarIndex），否则排除赶路角色自身 + 黑名单，取序号最靠前的有效角色。
    /// 若排除后无合法角色，则忽略黑名单再试一次。
    /// 返回 "1"/"2"/"3"/"4"，不会返回 null。
    /// </summary>
    private string GetSwitchToWalkIndex()
    {
        // 第一步：优先行走位（MainAvatarIndex），仍需排除赶路角色自身与黑名单
        // mainIdx 需在队伍实际人数范围内（AvatarCount），防止越界 SelectAvatar 抛异常
        if (!string.IsNullOrEmpty(PartyConfig.MainAvatarIndex)
            && int.TryParse(PartyConfig.MainAvatarIndex, out var mainIdx)
            && mainIdx >= 1
            && _combatScenes != null
            && mainIdx <= _combatScenes.AvatarCount)
        {
            var mainAvatar = _combatScenes?.SelectAvatar(mainIdx);
            if (mainAvatar != null
                && mainAvatar.Name != _hurryOnAvatar
                && !HurryOnBlacklist.Contains(mainAvatar.Name))
            {
                return mainIdx.ToString();
            }
        }

        // 两轮遍历均以队伍实际人数为上限，防止 SelectAvatar(int) 在 i > AvatarCount 时抛异常
        var avatarCount = _combatScenes?.AvatarCount ?? 0;
        for (var i = 1; i <= avatarCount; i++)
        {
            var avatar = _combatScenes?.SelectAvatar(i);
            if (avatar == null) continue;
            if (avatar.Name == _hurryOnAvatar) continue;
            if (HurryOnBlacklist.Contains(avatar.Name)) continue;
            return i.ToString();
        }

        for (var i = 1; i <= avatarCount; i++)
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

        if (num % HurryOnLogFrameInterval == 0)
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

                //满足条件时，尝试上车
                if (distance > PartyConfig.Distance)
                {
                    await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);

                    var boardIconState = GetMavikaESkillIconState(screen2);
                    // 内置冷却：玛薇卡上/下车动作后有约1秒无法再次上/下车，与E技能冷却无关（放宽至2秒防抖）
                    if ((DateTime.UtcNow - _lastMavikaBoardTime).TotalSeconds >= 2 && boardIconState is 1 or 2)
                    {
                        _lastMavikaBoardTime = DateTime.UtcNow;
                        InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
                        await Delay(200, ct);
                        InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
                        await Delay(300, ct);
                        InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
                        await Delay(300, ct);

                        // E技能CD跟踪：仅续技能（状态1）触发更新，上车（状态2）不触发
                        // 冲刺跳飞计数同样仅续技能（状态1）时重置，上车（状态2）不重置
                        if (boardIconState == 1)
                        {
                            await ReadEskillCdAsync("玛薇卡");
                            _mavikaSprintJumpCount = 0;
                        }

                        // 上车后不跳出当前帧，继续执行跳飞判定
                    }
                }

                // 刚上车后的2秒内跳过图标检测（上/下车动作期间图标不稳定），跳飞/骑行/禁用冲刺三处共用
                var justBoarded = (DateTime.UtcNow - _lastMavikaBoardTime).TotalSeconds < 2;
                // 本帧图标状态懒获取：首次用到才计算一次（screen2 未更新，重复调用结果相同），跳飞/骑行/禁用冲刺共用
                int? iconState = null;
                int GetMavikaIconState() => iconState ??= GetMavikaESkillIconState(screen2);

                //满足条件时，尝试跳飞
                if (PartyConfig.MwkJumpFlyEnabled
                    && distance > PartyConfig.MwkJumpFlyDistance
                    && state.RotationStableCount >= 1)
                {
                    // 非豁免期间仅下车图标（3）可跳飞；状态4（E可用）由刚上车豁免覆盖，无需额外OCR判断
                    if (!justBoarded && GetMavikaIconState() != 3)
                    {
                        return false;
                    }

                    if ((DateTime.UtcNow - _lastJumpFlyTime).TotalSeconds < interval)
                    {
                        return true;
                    }

                    Logger.LogInformation("自动赶路：玛薇卡跳飞赶路 距离下个节点距离 {d}", Math.Round(distance));
                    // 冲刺跳飞：上车后前若干次跳飞（计数小于配置值）且距离足够远时，跳飞前点按冲刺加速
                    if (_mavikaSprintJumpCount < PartyConfig.MwkJumpFlySprintCount
                        && distance > PartyConfig.MwkJumpFlyDistance * 1.3)
                    {
                        InputHub.Foreground.SimulateAction(GIActions.SprintMouse);
                        await Delay(100, ct);
                        _mavikaSprintJumpCount++;
                    }
                    InputHub.Foreground.SimulateAction(GIActions.Jump);
                    await Delay(150, ct);
                    InputHub.Foreground.SimulateAction(GIActions.Jump);
                    await Delay(100, ct);
                    InputHub.Foreground.SimulateAction(GIActions.Jump);
                    await Delay(10, ct);
                    InputHub.Foreground.SimulateAction(GIActions.Jump);
                    await Delay(150, ct);
                    _lastJumpFlyTime = DateTime.UtcNow;
                    _jumpFlySafetyPending = true;

                    using var jumpCheckRegion = CaptureToRectArea();
                    if (Bv.GetMotionStatus(jumpCheckRegion) == MotionStatus.Fly)
                    {
                        InputHub.Foreground.SimulateAction(GIActions.NormalAttack);
                        await Delay(300, ct);
                        for (int i = 0; i < 5; i++)
                        {
                            using var retryRegion = CaptureToRectArea();
                            if (Bv.GetMotionStatus(retryRegion) == MotionStatus.Fly)
                            {
                                InputHub.Foreground.SimulateAction(GIActions.NormalAttack);
                                await Delay(300, ct);
                            }
                            else break;
                        }
                        return false;
                    }

                    if (SpaceAtSecondPlaceExist(state))
                    {
                        InputHub.Foreground.SimulateAction(GIActions.Jump);
                    }

                    return true;
                }

                // 应该下车时尝试下车，下车成功后（PendingApproach=false）本航点内不再重复检测
                var mwkShouldApproach = ShouldApproach(distance, nextDistance, waypoint, nextWaypoint, avatar.Name);

                // 安全降落：仅当本帧未执行跳飞（跳飞块已 return）时才到达此处
                // 跳飞后间隔已过仍可能在空中，先普攻落地再继续后续逻辑（空中无法执行下车/切人）并防止摔伤
                // 放在接近处理之前，确保跳飞后快速接近时也能先落地；已下车成功（PendingApproach=false）则不再普攻
                // 已满足下车条件时（mwkShouldApproach），即使跳飞间隔未到也执行安全降落普攻，确保接近节点时先落地
                if (_lastJumpFlyTime != DateTime.MinValue
                    && _jumpFlySafetyPending
                    && state.PendingApproach
                    && ((DateTime.UtcNow - _lastJumpFlyTime).TotalSeconds > interval
                        || (mwkShouldApproach && state.PendingApproach)))
                {
                    InputHub.Foreground.SimulateAction(GIActions.NormalAttack);
                    Logger.LogInformation("自动赶路：玛薇卡安全降落 距离下个节点距离 {d}", Math.Round(distance));
                    // 普攻后点按一次空格打断后摇
                    await Delay(50, ct);
                    InputHub.Foreground.SimulateAction(GIActions.Jump);
                    _jumpFlySafetyPending = false;
                }

                if (mwkShouldApproach && state.PendingApproach)
                {
                    if (PartyConfig.SwitchToWalkEnabled)
                    {
                        // 切人下车：无需检测图标，直接切换步行角色
                        InputHub.ReleaseAll();
                        var nextIdx = GetSwitchToWalkIndex();
                        Logger.LogInformation("自动赶路：玛薇卡接近节点，切人步行 {t}", nextIdx);
                        var nextAvatar = await SwitchAvatar(nextIdx);
                        // 切人成功才认为下车成功，失败时保留 PendingApproach 以便后续帧重试
                        if (nextAvatar != null)
                        {
                            state.PendingApproach = false;
                            _jumpFlySafetyPending = false;
                        }
                    }
                    else
                    {
                        // 点按E下车：持续检测，图标为下车(3)时才松键并执行下车
                        var approachIconState = GetMavikaESkillIconState(screen2);
                        if (approachIconState == 3 && Bv.GetMotionStatus(screen2) != MotionStatus.Fly)
                        {
                            InputHub.ReleaseAll();
                            Logger.LogInformation("自动赶路：玛薇卡接近节点，下车步行");
                            InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
                            await Delay(100, ct);
                            InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
                            await Delay(50, ct);
                        }
                        // 检测到图标1/2（续技能/上车）即认为下车成功
                        if (approachIconState is 1 or 2)
                        {
                            state.PendingApproach = false;
                            _jumpFlySafetyPending = false;
                        }
                    }
                    return false;
                }

                // 先判断距离满足骑行条件，避免无谓读取冷却；刚上车2秒内或下车图标（3）视为在车上
                if (distance > PartyConfig.Distance
                    && (justBoarded || GetMavikaIconState() == 3))
                {
                    if (Bv.GetMotionStatus(screen2) == MotionStatus.Climb)
                    {
                        InputHub.Foreground.SimulateAction(GIActions.Drop);
                        await Delay(500, ct);
                        InputHub.Foreground.SimulateAction(GIActions.NormalAttack);
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
                                    InputHub.Foreground.SimulateAction(GIActions.NormalAttack);
                                state.MavikaSlopeCount = 0;
                                Logger.LogInformation("自动赶路：靠近节点切换 {t}...-h {t2}", "", waypoint?.MoveMode);
                            }
                        }
                    }

                }

                // 玛薇卡逻辑最后：勾选了禁用冲刺时，在车上（刚上车2秒内或下车图标）跳过本帧通用移动逻辑以禁用冲刺
                if (PartyConfig.MwkDisableSprintEnabled
                    && (justBoarded || GetMavikaIconState() == 3))
                {
                    // 通用移动逻辑可能已在此前（Run 路段）将冲刺键置为 KeyDown，这里松开一次防止上车后持续冲刺消耗夜魂值
                    InputHub.Foreground.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
                    return true;
                }

                break;

            case "瓦雷莎":
                if (state.PendingApproach)
                {
                    var shouldApproach = ShouldApproach(distance, nextDistance, waypoint, nextWaypoint, avatar.Name);

                    if (shouldApproach)
                    {
                        InputHub.ReleaseAll();
                        if (PartyConfig.SwitchToWalkEnabled)
                        {
                            var nextIdx = GetSwitchToWalkIndex();
                            Logger.LogInformation("自动赶路：瓦雷莎接近节点，切人步行 {t}", nextIdx);
                            state.VaresaWalkSwitchAttempts++;
                            var nextAvatar = await SwitchAvatar(nextIdx);
                            if (nextAvatar != null)
                            {
                                // 切人成功才结束接近态，失败保留 PendingApproach 下帧重试（与玛薇卡一致）
                                state.PendingApproach = false;
                            }
                            else if (state.VaresaWalkSwitchAttempts >= 3)
                            {
                                // 连续失败达上限：放弃切步行位，保留当前角色交还通用逻辑继续赶路，避免卡死路线
                                state.PendingApproach = false;
                                Logger.LogWarning("自动赶路：瓦雷莎切步行位连续 {n} 次失败，放弃切换继续赶路",
                                    state.VaresaWalkSwitchAttempts);
                            }
                        }
                        else
                        {
                            state.PendingApproach = false;
                            var approachEOnCd = await AutoFightSkill.AvatarSkillAsync(Logger, avatar, false, 2, ct);
                            if (approachEOnCd)
                            {
                                InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                                await Delay(300, ct);
                            }

                            // 接近节点时若仍在滑翔，下落攻击快速落在航点上，
                            // 防止滑过头；坠落态读作 Normal 无法与地面区分，靠自然落地即可不处理
                            if (Bv.GetMotionStatus(screen2) == MotionStatus.Fly)
                            {
                                Logger.LogInformation("自动赶路：瓦雷莎接近节点时在滑翔，执行安全落地");
                                await SafeLanding(ct);
                            }
                        }
                        return false;
                    }
                }

                // 前方（当前或下一个航点）有跳跃动作时停用技能赶路：
                // 跳跃路段大概率是要起跳起飞，瓦雷莎的E夜魂态/冲刺会干扰正常起跳，
                // 且本分支会强制路段为Run，会吞掉Jump路段的跳跃动作
                if (waypoint?.MoveMode == MoveModeEnum.Jump.Code
                    || nextWaypoint?.MoveMode == MoveModeEnum.Jump.Code)
                {
                    return true;
                }

                // 路段守卫：只在 Run/Dash 路段用技能赶路。
                // 入口的路段过滤对终点（nextWaypoint == null）放行，而 "fly 路段 + 终点" 意味着
                // 前方是悬崖/高空、需要走到边缘跳下开飞——此时用E夜魂疾冲会直接冲下悬崖摔死，
                // 且强制 MoveMode=Run 会破坏 fly 路段的起飞处理。
                if (waypoint?.MoveMode != MoveModeEnum.Run.Code
                    && waypoint?.MoveMode != MoveModeEnum.Dash.Code)
                {
                    return false;
                }

                // 技能赶路只在路段足够长时启用：夜魂疾冲速度约 17m/s，松键后靠惯性滑行减速，
                // 滑行距离可达 15m+。若在近距离（如 46m 的短路段）仍触发长按E，
                // 刹车时的惯性会直接冲过航点（实测阿如村路线冲进河里，日志 13:58:49 dist=46.1 技能赶路 → 13:58:55 游泳中）。
                // 因此离航点 50 以内不再进技能赶路，交还通用逻辑用普通速度跑完并正常刹车。
                if (distance > Math.Max(PartyConfig.Distance, HurryOnMinEntryDistance))
                {
                    await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);

                    waypoint.MoveMode = MoveModeEnum.Run.Code;

                    await Delay(300, ct);
                    // 一段 E 能跑多远由夜魂值决定：粉色夜魂条填充充足 = 仍在夜魂疾冲中，继续跑；
                    // 填充降到 VaresaNextSkillPixels 以下 = 这一段消耗得差不多了，才接第二段 E。
                    // 复用本帧截图，不额外截屏；能进到这个块本身就说明还有路程（distance > 50 门槛）。
                    var nsPixels = CountVaresaNightsoulPixels(screen2);
                    if (nsPixels >= VaresaNightsoulSeenPixels)
                    {
                        state.VaresaNightsoulSeen = true;
                    }

                    // 状态机：从未放过 E -> 放第一段；见过夜魂条且填充降到 VaresaNextSkillPixels 以下 -> 放第二段；
                    // 放过 E 但条从未出现（UI 还没画出来/识别失败）-> 什么都不做，避免刚施放就连按第二段。
                    var neverCast = double.IsNaN(state.VaresaLastSkillDistance);
                    var dashEnded = state.VaresaNightsoulSeen && nsPixels < VaresaNextSkillPixels;
                    var canDecide = (neverCast || dashEnded)
                        && (DateTime.UtcNow - _lastVaresaSkillTime).TotalMilliseconds >= VaresaSkillIntervalMs;
                    // 只在需要决定"是否放 E"时截屏数青色充能点；疾冲途中既不截屏也不做连通域识别。
                    if (canDecide)
                    {
                        // 瓦雷莎 E 有两段充能：按图标上方青色菱形点数判断，不用白色连通域 OCR
                        //（充能数字/夜魂态图标会让它误判）。
                        var charges = CountVaresaECharges();
                        if (charges > 0)
                        {
                            _lastVaresaSkillTime = DateTime.UtcNow;
                            InputHub.Foreground.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                            await Delay(300, ct);
                            InputHub.Foreground.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                            await Delay(200, ct);
                            avatar.LastSkillTime = DateTime.UtcNow;
                            state.VaresaLastSkillDistance = distance;
                            state.VaresaNightsoulSeen = false; // 等下一段夜魂条出现，再按 800 阈值判下一段
                            state.VaresaNoChargeLogged = false;
                            Logger.LogInformation("自动赶路：瓦雷莎 施放夜魂疾冲，剩余 {d}，可用充能 {n}",
                                Math.Round(distance), charges);
                            // 夜魂疾冲自身提供加速，不再额外点按 SprintMouse
                        }
                        else if (!state.VaresaNoChargeLogged)
                        {
                            state.VaresaNoChargeLogged = true;
                            Logger.LogDebug("自动赶路：瓦雷莎 夜魂条已消耗（像素 {px}），但两段充能都在冷却，继续跑", nsPixels);
                        }
                    }
                }

                return true;

            case "希诺宁":
                if (state.PendingApproach)
                {
                    var shouldApproach = ShouldApproach(distance, nextDistance, waypoint, nextWaypoint, avatar.Name);

                    if (shouldApproach)
                    {
                        InputHub.ReleaseAll();
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
                                InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
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
                            InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
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
                        InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
                        _lastJumpFlyTime = DateTime.UtcNow;
                        return false;
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
                            InputHub.ReleaseAll();
                            state.FlyingState = false;
                            var retries = 0;
                            while (DashAtSecondPlaceExist() && retries < 10)
                            {
                                InputHub.Foreground.SimulateAction(GIActions.NormalAttack);
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
                                    InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
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
                catch (Exception e) when (e is not RetryException
                    and not RetryNoCountException
                    and not NormalEndException
                    and not HandledException
                    and not OperationCanceledException)
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
                            InputHub.ReleaseAll();
                            // Logger.LogInformation("[赶路调试] {name} 触发接近: dist={d}, flying={f}, spaceExist={s}",
                            //     avatar.Name, Math.Round(distance, 1), state.FlyingState, SpaceAtSecondPlaceExist(state));
                            state.PendingApproach = false;
                            // 同时检查状态字段和实时像素，确保终点（新 HurryOnState，FlyingState=false）也能下车
                            if (state.FlyingState || SpaceAtSecondPlaceExist(state))
                            {
                                if (SpaceAtSecondPlaceExist(state))
                                {
                                    Logger.LogInformation($"自动赶路：{avatar.Name}接近节点，关闭飞行状态");
                                    InputHub.Foreground.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                                    for (var retries = 0; retries < 20; retries++)
                                    {
                                        await Delay(100, ct);
                                        var cd = await ReadEskillCdAsync(avatar.Name);
                                        if (cd > 0)
                                        {
                                            break;
                                        }
                                    }
                                    InputHub.Foreground.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
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
                                InputHub.Foreground.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                                await Delay(50, ct);
                                InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
                                await Delay(100, ct);
                                InputHub.Foreground.SimulateAction(GIActions.SprintMouse, KeyType.KeyDown);

                                avatar.LastSkillTime = DateTime.UtcNow;
                                state.FlyingState = true;
                                Logger.LogInformation($"自动赶路：{avatar.Name}启动飞行");
                                return true;
                            }
                        }

                        return false;
                    }
                }
                catch (Exception e) when (e is not RetryException
                    and not RetryNoCountException
                    and not NormalEndException
                    and not HandledException
                    and not OperationCanceledException)
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
                        InputHub.ReleaseAll();
                        // Logger.LogInformation("[赶路调试] 流浪者 触发接近: dist={d}, flying={f}, spaceExist={s}",
                        //     Math.Round(distance, 1), state.FlyingState, SpaceAtSecondPlaceExist(state));
                        state.PendingApproach = false;
                        // 同时检查状态字段和实时像素，确保终点（新 HurryOnState，FlyingState=false）也能下车
                        if (state.FlyingState || SpaceAtSecondPlaceExist(state))
                        {
                            if (SpaceAtSecondPlaceExist(state))
                            {
                                Logger.LogInformation("自动赶路：流浪者接近节点，关闭飞行状态");
                                InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
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
                    InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                    state.WandererFlightCheckCount++;
                    if (state.WandererFlightCheckCount % 3 == 0)
                        InputHub.Foreground.Mouse.MiddleButtonClick();
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
                            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                            InputHub.Foreground.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
                            await Delay(50, ct);
                            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                            await Delay(100, ct);
                            InputHub.Foreground.SimulateAction(GIActions.ElementalSkill);
                            await Delay(50, ct);
                            InputHub.Foreground.SimulateAction(GIActions.SprintMouse, KeyType.KeyDown);

                            avatar.LastSkillTime = DateTime.UtcNow;
                            state.FlyingState = true;
                            Logger.LogInformation("自动赶路：流浪者启动飞行");
                            return true;
                        }
                    }

                    return false;
                }

                break;

            case "法尔伽":
            {
                if (distance > PartyConfig.Distance
                    && (waypoint?.MoveMode == MoveModeEnum.Run.Code || waypoint?.MoveMode == MoveModeEnum.Dash.Code))
                {
                    await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);

                    // 法尔伽不在场时E技能CD识别无效，跳过检测（等上场后再检测记录）
                    if (!avatar.IsActive(screen2))
                    {
                        return false;
                    }

                    // 非阻塞时间间隔：施放E后2秒内不再施放（null 表示可施放）
                    if (_falgaNextCheckTime.HasValue && DateTime.UtcNow < _falgaNextCheckTime.Value)
                    {
                        return false;
                    }

                    // 0.5秒节流，避免频繁OCR
                    if ((DateTime.UtcNow - _lastSkillCheckTime).TotalSeconds < 0.5)
                        return false;
                    _lastSkillCheckTime = DateTime.UtcNow;

                    // 正常检测并记录CD（识别结果原样记录，无识别结果时由兜底逻辑处理）
                    var cd = await ReadEskillCdAsync("法尔伽");

                    // 视角稳定且CD就绪时施放E（长按500ms，内部try/finally保证取消时也松开按键），施放后直接记录长按CD 8秒
                    if (cd <= 0 && state.RotationStableCount >= 2)
                    {
                        Logger.LogInformation("雷霆大跳！");
                        await TaskControl.SimulateHoldActionAsync(GIActions.ElementalSkill, 500, ct);
                        ESkillCdTracker.Record("法尔伽", 8);
                        _falgaNextCheckTime = DateTime.UtcNow.AddSeconds(2);
                        return false;
                    }

                    return false;
                }
                break;
            }

            case "夜兰":
            {
                try
                {
                    // 1. 接近处理：距离小于接近距离且需要接近 → 退出赶路（点按跳跃）
                    if (state.PendingApproach)
                    {
                        var shouldApproach = ShouldApproach(distance, nextDistance, waypoint, nextWaypoint, avatar.Name);

                        if (shouldApproach)
                        {
                            InputHub.ReleaseAll();
                            state.PendingApproach = false;
                            if (state.FlyingState)
                            {
                                state.FlyingState = false;
                                _lastLandingTime = DateTime.UtcNow;
                                Logger.LogInformation("自动赶路：夜兰接近节点，退出赶路");
                                InputHub.Foreground.SimulateAction(GIActions.Jump); // 第1种：点按跳跃
                            }
                            return false;
                        }
                    }

                    // 赶路状态中
                    if (state.FlyingState)
                    {
                        // 0.5秒节流，避免频繁OCR
                        if ((DateTime.UtcNow - _lastSkillCheckTime).TotalSeconds < 0.5)
                            return true;
                        _lastSkillCheckTime = DateTime.UtcNow;

                        // 2. 识别到CD（技能结束进入冷却）→ 退出赶路（不点按跳跃）
                        // 进入飞行后1.5秒内不进行OCR识别退出，避免刚启动时OCR误判导致误退出
                        if ((DateTime.UtcNow - _lastFlyStartTime).TotalSeconds >= 1.5)
                        {
                            var cd = await ReadEskillCdAsync("夜兰");
                            if (cd > 0)
                            {
                                state.FlyingState = false;
                                _lastLandingTime = DateTime.UtcNow;
                                Logger.LogInformation("自动赶路：夜兰技能CD出现，赶路结束");
                                return false;
                            }
                        }

                        // 3. 超过5秒超时 → 退出赶路（点按跳跃）
                        if ((DateTime.UtcNow - _lastFlyStartTime).TotalSeconds >= 5)
                        {
                            state.FlyingState = false;
                            _lastLandingTime = DateTime.UtcNow;
                            Logger.LogInformation("自动赶路：夜兰超时，退出赶路");
                            InputHub.Foreground.SimulateAction(GIActions.Jump); // 第3种：点按跳跃
                            return false;
                        }

                        // 4. 连续2帧distance变化不超过8（卡顿）→ 退出赶路（点按跳跃）
                        if (Math.Abs(distance - _lastFlyDistance) <= 8)
                        {
                            _flyStillFrames++;
                        }
                        else
                        {
                            _flyStillFrames = 0;
                        }
                        _lastFlyDistance = distance;

                        if (_flyStillFrames >= 2)
                        {
                            state.FlyingState = false;
                            _lastLandingTime = DateTime.UtcNow;
                            Logger.LogInformation("自动赶路：夜兰卡顿，退出赶路");
                            InputHub.Foreground.SimulateAction(GIActions.Jump); // 第4种：点按跳跃
                            return false;
                        }

                        return true;
                    }

                    // 2. 进入飞行：大于赶路距离且E技能CD识别无结果（可用）时长按E 300ms
                    if (distance > PartyConfig.Distance
                        && (waypoint?.MoveMode == MoveModeEnum.Run.Code || waypoint?.MoveMode == MoveModeEnum.Dash.Code))
                    {
                        await SwitchToHurryAvatarAsync(screen2, avatar, distance, num, ct);

                        if (!avatar.IsActive(screen2))
                        {
                            return false;
                        }

                        // 0.5秒节流，避免频繁OCR
                        if ((DateTime.UtcNow - _lastSkillCheckTime).TotalSeconds < 0.5)
                            return false;
                        _lastSkillCheckTime = DateTime.UtcNow;

                        var cd = await ReadEskillCdAsync("夜兰");
                        // 技能可用：OCR 无 CD，或技能槽位出现可用高亮色 #00DCFA（OCR 非唯一判断）
                        var skillReady = cd <= 0 || secondESkillAvailable();
                        if (skillReady && state.RotationStableCount >= 1
                            && (DateTime.UtcNow - _lastLandingTime).TotalSeconds >= 2)
                        {
                            Logger.LogInformation("自动赶路：夜兰启动赶路");
                            await TaskControl.SimulateHoldActionAsync(GIActions.ElementalSkill, 300, ct);
                            state.FlyingState = true;
                            _lastFlyStartTime = DateTime.UtcNow;
                            _lastFlyDistance = distance;
                            _flyStillFrames = 0;
                            return true;
                        }

                        return false;
                    }
                }
                catch (Exception e) when (e is not RetryException
                    and not RetryNoCountException
                    and not NormalEndException
                    and not HandledException
                    and not OperationCanceledException)
                {
                    Logger.LogError(e, $"[{avatar.Name}] 赶路逻辑异常");
                    state.FlyingState = false;
                    return false;
                }
                break;
            }

            default:
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
                InputHub.Foreground.SimulateAction(GIActions.Drop);
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
        using var capture = CaptureToRectArea();
        using var region = capture.DeriveCrop(1595, 1028, 9, 7);
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

    /// <summary>
    /// 第二个E技能位是否可用：以技能槽位坐标 (1691,953)（1920×1080 基准）为中心的 11×11 区域内存在面积≥5 的 #00DCFA（RGB 0,220,250）连通域
    /// </summary>
    private bool secondESkillAvailable()
    {
        using var region = CaptureToRectArea();
        // 参考 AutoFightAssets：以 1920 宽为基准，按实际捕获宽度缩放坐标（不超过1）
        var scale = Math.Min(region.Width / 1920d, 1d);
        // 技能槽位中心 (1691,953) → 11×11 区域左上角 (1686,948)
        using var crop = region.DeriveCrop(
            (int)(1686 * scale), (int)(948 * scale),
            Math.Max(1, (int)(11 * scale)), Math.Max(1, (int)(11 * scale)));
        // Threshold 内部先 BGR2RGB，Scalar 为 RGB 顺序：#00DCFA = R0,G220,B250
        return HasColoredBlob(crop.SrcMat, new Scalar(0, 210, 240), new Scalar(15, 235, 255), 5);
    }

    /// <summary>
    /// 瓦雷莎 E 当前充能点数（0~2）：技能图标上方最多两个 #00DCFA 青色菱形点，按一次 E 消耗一个。
    /// 坐标以 1920×1080 为基准，按 SystemInfo.AssetScale 缩放（覆盖 DPI / 窗口分辨率差异），
    /// 并从画面右下角锚定，避免非 16:9 或缩放时从左上角定位产生偏移。
    /// </summary>
    private int CountVaresaECharges()
    {
        using var region = CaptureToRectArea();
        var s = TaskContext.Instance().SystemInfo.AssetScale;
        // 两个充能点中心约 (1676,953)、(1691,953)，各约 10px 宽；
        // 取覆盖两者的横条 (1666,944) 40×18，右边缘距画面右边 214、下边缘距底边 118。
        const int rightMargin1080 = 214;
        const int bottomMargin1080 = 118;
        const int width1080 = 40;
        const int height1080 = 18;

        var w = Math.Max(12, (int)Math.Round(width1080 * s));
        var h = Math.Max(8, (int)Math.Round(height1080 * s));
        var x = region.Width - (int)Math.Round(rightMargin1080 * s) - w;
        var y = region.Height - (int)Math.Round(bottomMargin1080 * s) - h;
        if (x < 0 || y < 0 || x + w > region.Width || y + h > region.Height)
        {
            return 0;
        }

        using var crop = region.DeriveCrop(x, y, w, h);
        // Threshold 内部先 BGR2RGB，Scalar 为 RGB 顺序：#00DCFA = R0,G220,B250
        using var mask = OpenCvCommonHelper.Threshold(crop.SrcMat,
            new Scalar(0, 200, 225), new Scalar(30, 240, 255));
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
            connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);
        // 单个菱形约 50 像素，面积按 scale² 缩放；上下限过滤抗锯齿噪点与图标高亮大块
        var minArea = Math.Max(3, (int)Math.Round(8 * s * s));
        var maxArea = Math.Max(60, (int)Math.Round(300 * s * s));
        var count = 0;
        for (var i = 1; i < numLabels && count < 2; i++)
        {
            var area = stats.At<int>(i, 4);
            if (area >= minArea && area <= maxArea) count++;
        }
        return count;
    }

    /// <summary>
    /// 数出屏幕中心左侧粉色夜魂条的填充像素数。条填充充足 = 仍在夜魂疾冲中；
    /// 填充随夜魂值消耗而减少，降到 VaresaNextSkillPixels 以下才接下一段 E。
    /// 坐标以 1920×1080 为基准、相对画面中心锚定（角色固定在中心附近），按 AssetScale 缩放以适配 DPI/分辨率。
    /// 复用主循环本帧已有的截图，不额外截屏。
    /// </summary>
    private int CountVaresaNightsoulPixels(ImageRegion screen)
    {
        var s = TaskContext.Instance().SystemInfo.AssetScale;
        // 以 BGI 自身截图校准：夜魂条是 "(" 形弧线，会随角色移动上下漂移约 60px，
        // 实测纵向分布约 y 420..660、横向 x 785..845，中心相对画面中心约 (-134,-40)。
        // ROI 取 x=791..861、y=420..660：纵向完整覆盖漂移范围，右边缘 861 离角色身上的粉色（x870+）仍有 9px 余量；
        // 横向不加宽，避免把角色算进来；横向漂移由 800 阈值本身留出的余量吸收。
        const int offsetX1080 = -134;
        const int offsetY1080 = 0;
        const int width1080 = 70;
        const int height1080 = 240;

        var w = Math.Max(20, (int)Math.Round(width1080 * s));
        var h = Math.Max(45, (int)Math.Round(height1080 * s));
        var x = (int)Math.Round(screen.Width / 2d + (offsetX1080 - width1080 / 2d) * s);
        var y = (int)Math.Round(screen.Height / 2d + (offsetY1080 - height1080 / 2d) * s);
        if (x < 0 || y < 0 || x + w > screen.Width || y + h > screen.Height)
        {
            return 0;
        }

        using var crop = screen.DeriveCrop(x, y, w, h);
        var mat = crop.SrcMat;
        // BGI 图里亮粉是 (240,208,208)、暗粉 (192,128,128)，共同特征是 R 明显高于 G。
        // 只写逐通道上下限会把浅蓝白天空、黄叶、沙滩一并算进来（实测无条时天空区假阳性 153 像素），
        // 导致条已消失仍判为"疾冲中"，第二段 E 永远不触发。按校准时的判据补上通道间色差约束。
        // 注意 SrcMat 是 BGR 顺序：Item0=B / Item1=G / Item2=R。
        var pixels = 0;
        for (var row = 0; row < mat.Rows; row++)
        {
            for (var col = 0; col < mat.Cols; col++)
            {
                var c = mat.At<Vec3b>(row, col);
                var b = c.Item0;
                var g = c.Item1;
                var r = c.Item2;
                if (r >= 160 && g >= 80 && g <= 225 && b >= 95 && b <= 245
                    && r - g > 30 && b - g > -10)
                {
                    pixels++;
                }
            }
        }

        return pixels;
    }

    private async Task SafeLanding(CancellationToken ct)
    {
        await Delay(250, ct);
        InputHub.Foreground.SimulateAction(GIActions.Jump);
        await Delay(150, ct);

        using var screen = CaptureToRectArea();
        var stamina = DetectStamina(screen);
        if (Bv.GetMotionStatus(screen) == MotionStatus.Fly
            || stamina == 0
            || stamina == 240)
        {
            InputHub.Foreground.SimulateAction(GIActions.NormalAttack);
            await Delay(300, ct);
            for (int i = 0; i < 5; i++)
            {
                using var retryRegion = CaptureToRectArea();
                if (Bv.GetMotionStatus(retryRegion) == MotionStatus.Fly)
                {
                    InputHub.Foreground.SimulateAction(GIActions.NormalAttack);
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
            return HasColoredBlob(fullRegion, 1819, 1028, 9, 7,
                new Scalar(242, 223, 39), new Scalar(255, 233, 44), 4);
        }
        finally
        {
            if (ownRegion) fullRegion.Dispose();
        }
    }

    /// <summary>
    /// 检测当前是否处于无法使用赶路的状态。
    /// 枫丹水下：（1500,1031）的图标呈黄色，（1380,1031）无黄色图标（用于和希诺宁区分），据此判定角色当前处于水下；
    /// 附身龙魂/阿夏：（832,1010）附近存在 #FFCC32 橙色连通域（面积大于4）。
    /// 任一成立即认为无法使用赶路。
    /// </summary>
    private static bool CannotUseHurryConfirm(Region region)
    {
        var fullRegion = region.ToImageRegion();
        bool ownRegion = fullRegion != region;
        try
        {
            var underwater = HasColoredBlob(fullRegion, 1500, 1031, 9, 7, new Scalar(242, 223, 39), new Scalar(255, 233, 44), 4)
                && !HasColoredBlob(fullRegion, 1380, 1031, 9, 7, new Scalar(242, 223, 39), new Scalar(255, 233, 44), 4);
            var possessed = HasColoredBlob(fullRegion, 832, 1010, 9, 7, new Scalar(245, 194, 40), new Scalar(255, 214, 60), 5);
            return underwater || possessed;
        }
        finally
        {
            if (ownRegion) fullRegion.Dispose();
        }
    }

    /// <summary>
    /// 指定起点坐标和尺寸的区域内是否存在面积 ≥ <paramref name="minArea"/> 的指定颜色连通域（便捷重载）。
    /// <paramref name="lower"/>/<paramref name="upper"/> 为 RGB 顺序阈值（Threshold 内部先做 BGR2RGB）。
    /// </summary>
    private static bool HasColoredBlob(ImageRegion region, int x, int y, int w, int h, Scalar lower, Scalar upper, int minArea)
    {
        using var regionMat = region.DeriveCrop(x, y, w, h);
        return HasColoredBlob(regionMat.SrcMat, lower, upper, minArea);
    }

    /// <summary>
    /// 图像中是否存在面积 ≥ <paramref name="minArea"/> 的指定颜色连通域。
    /// <paramref name="lower"/>/<paramref name="upper"/> 为 RGB 顺序阈值（Threshold 内部先做 BGR2RGB）。
    /// </summary>
    private static bool HasColoredBlob(Mat src, Scalar lower, Scalar upper, int minArea)
    {
        using var mask = OpenCvCommonHelper.Threshold(src, lower, upper);
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();

        var numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
            connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

        // 排除小于 minArea 的孤立噪点（CC_STAT_AREA = 4）
        for (int i = 1; i < numLabels; i++)
        {
            if (stats.At<int>(i, 4) >= minArea) return true;
        }
        return false;
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
            // 枫丹水下或附身龙魂/阿夏时无法使用赶路，跳过任何赶路逻辑，不进入角色分支
            if (CannotUseHurryConfirm(screen))
            {
                return false;
            }

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
            if (currentList != null
                && currentIndex >= 0
                && currentIndex + 1 < currentList.Count)
            {
                nextWaypoint = currentList[currentIndex + 1];
                nextDistance = Navigation.GetDistance(waypoint, new Point2f((float)nextWaypoint.X, (float)nextWaypoint.Y));
            }

            var result = await ExecuteHurryOnAsync(waypoint, nextWaypoint, distance, nextDistance, true, avatar, screen, num, hurryOnState, default);
            // Logger.LogInformation("[赶路调试] TryHurryOnAsync  exit: result={r}", result);
            return result;
        }
        catch (Exception e) when (e is not RetryException
            and not RetryNoCountException
            and not NormalEndException
            and not HandledException
            and not OperationCanceledException)
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
                else
                {
                    Logger.LogInformation("自动赶路角色：手动指定 {Name}", _hurryOnAvatar);
                }
            }
        }

        if (string.IsNullOrEmpty(PartyConfig.TravelMode))
        {
            PartyConfig.TravelMode = "精准靠近";
        }
    }
}
