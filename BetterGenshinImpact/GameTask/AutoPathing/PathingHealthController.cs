using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using static BetterGenshinImpact.GameTask.SystemControl;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Domain
{
    /// <summary>
    /// 定义自动寻路生命值检查的最终状态。上层任务应根据此状态决定是否继续跑图或重置逻辑。 / Defines the final state of auto-pathing health checks. The upper-level task should decide whether to continue the pathing or reset logic based on this state.
    /// </summary>
    public enum HealthRecoveryResult
    {
        /// <summary>
        /// 健康并继续 / Healthy and continue.
        /// </summary>
        HealthyAndContinue,
        /// <summary>
        /// 队伍已治疗并继续 / Party healed and continue.
        /// </summary>
        PartyHealedAndContinue,
        /// <summary>
        /// 传送到神像需要重试 / Teleported to statue, requires retry.
        /// </summary>
        TeleportedToStatueRequiresRetry
    }

    /// <summary>
    /// 队伍服务接口 / Party service interface.
    /// </summary>
    public interface IPartyService
    {
        /// <summary>
        /// 切换到角色 / Switches to the avatar.
        /// </summary>
        /// <param name="avatarId">角色ID / Avatar ID.</param>
        /// <param name="isInstant">是否瞬间完成 / Whether it's instant.</param>
        /// <param name="ct">取消令牌 / Cancellation token.</param>
        /// <returns>异步任务 / Asynchronous task.</returns>
        Task SwitchToAvatarAsync(string avatarId, bool isInstant = false, CancellationToken ct = default);
    }

    /// <summary>
    /// 输入服务接口 / Input service interface.
    /// </summary>
    public interface IInputService
    {
        /// <summary>
        /// 执行操作 / Executes an action.
        /// </summary>
        /// <param name="action">游戏动作 / Game action.</param>
        void ExecuteAction(GIActions action);
    }

    /// <summary>
    /// 视觉服务接口 / Vision service interface.
    /// </summary>
    public interface IVisionService
    {
        /// <summary>
        /// 如果在复苏模态框中则点击 / Clicks if in the revive modal.
        /// </summary>
        /// <returns>是否点击成功 / Whether clicked successfully.</returns>
        bool ClickIfInReviveModal();

        /// <summary>
        /// 当前角色是否低血量 / Checks if the current avatar has low HP.
        /// </returns>
        bool IsCurrentAvatarLowHp();

        /// <summary>
        /// 等待主界面 / Waits for the main UI.
        /// </summary>
        /// <param name="ct">取消令牌 / Cancellation token.</param>
        /// <returns>异步任务 / Asynchronous task.</returns>
        Task WaitForMainUiAsync(CancellationToken ct);
    }

    /// <summary>
    /// 传送服务接口 / Teleport service interface.
    /// </summary>
    public interface ITeleportService
    {
        /// <summary>
        /// 传送到七天神像 / Teleports to the Statue of the Seven.
        /// </summary>
        /// <param name="ct">取消令牌 / Cancellation token.</param>
        /// <returns>异步任务 / Asynchronous task.</returns>
        Task TeleportToStatueOfTheSevenAsync(CancellationToken ct);
    }

    /// <summary>
    /// 治疗策略接口 / Healer strategy interface.
    /// </summary>
    public interface IHealerStrategy
    {
        /// <summary>
        /// 目标角色名称 / Target avatar name.
        /// </summary>
        string TargetAvatarName { get; }

        /// <summary>
        /// 执行治疗序列 / Executes the heal sequence.
        /// </summary>
        /// <param name="inputService">输入服务 / Input service.</param>
        /// <param name="ct">取消令牌 / Cancellation token.</param>
        /// <returns>异步任务 / Asynchronous task.</returns>
        Task ExecuteHealSequenceAsync(IInputService inputService, CancellationToken ct);
    }

    /// <summary>
    /// 白术治疗策略 / Baizhu healer strategy.
    /// </summary>
    public class BaizhuHealerStrategy : IHealerStrategy
    {
        /// <inheritdoc />
        public string TargetAvatarName => "白术";

        /// <inheritdoc />
        public async Task ExecuteHealSequenceAsync(IInputService inputService, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(inputService);

            inputService.ExecuteAction(GIActions.ElementalSkill);
            await Task.Delay(800, ct);
            inputService.ExecuteAction(GIActions.ElementalSkill);
            await Task.Delay(800, ct); 
        }
    }

    /// <summary>
    /// 希格雯治疗策略 / Sigewinne healer strategy.
    /// </summary>
    public class SigewinneHealerStrategy : IHealerStrategy
    {
        /// <inheritdoc />
        public string TargetAvatarName => "希格雯";

        /// <inheritdoc />
        public async Task ExecuteHealSequenceAsync(IInputService inputService, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(inputService);

            inputService.ExecuteAction(GIActions.ElementalSkill);
            await Task.Delay(11000, ct); 
        }
    }

    /// <summary>
    /// 珊瑚宫心海治疗策略 / Kokomi healer strategy.
    /// </summary>
    public class KokomiHealerStrategy : IHealerStrategy
    {
        /// <inheritdoc />
        public string TargetAvatarName => "珊瑚宫心海";

        /// <inheritdoc />
        public async Task ExecuteHealSequenceAsync(IInputService inputService, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(inputService);

            inputService.ExecuteAction(GIActions.ElementalSkill);
            await Task.Delay(500, ct);
            inputService.ExecuteAction(GIActions.ElementalBurst);
        }
    }
}

namespace BetterGenshinImpact.GameTask.AutoPathing
{
    using Domain;
    
    /// <summary>
    /// 自动寻路生命值控制器 / Auto-pathing health controller.
    /// </summary>
    public class PathingHealthController
    {
        private readonly ILogger<PathingHealthController> _logger;
        private readonly IVisionService _visionService;
        private readonly IPartyService _partyService;
        private readonly IInputService _inputService;
        private readonly ITeleportService _teleportService;
        private readonly IReadOnlyDictionary<string, IHealerStrategy> _healerStrategies;

        /// <summary>
        /// 构造函数 / Constructor.
        /// </summary>
        public PathingHealthController(
            ILogger<PathingHealthController> logger,
            IVisionService visionService,
            IPartyService partyService,
            IInputService inputService,
            ITeleportService teleportService,
            IEnumerable<IHealerStrategy> healerStrategies)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _visionService = visionService ?? throw new ArgumentNullException(nameof(visionService));
            _partyService = partyService ?? throw new ArgumentNullException(nameof(partyService));
            _inputService = inputService ?? throw new ArgumentNullException(nameof(inputService));
            _teleportService = teleportService ?? throw new ArgumentNullException(nameof(teleportService));
            
            _healerStrategies = healerStrategies?.ToDictionary(h => h.TargetAvatarName) 
                                ?? throw new ArgumentNullException(nameof(healerStrategies));
        }

        /// <summary>
        /// 检查并尝试恢复状态 / Checks and attempts health recovery.
        /// </summary>
        /// <param name="waypoint">航点信息 / Waypoint information.</param>
        /// <param name="combatScenes">战斗场景信息 / Combat scenes information.</param>
        /// <param name="partyConfig">队伍配置 / Party configuration.</param>
        /// <param name="ct">取消标志 / Cancellation token.</param>
        /// <returns>恢复操作结果 / Recovery result.</returns>
        public async Task<HealthRecoveryResult> CheckAndAttemptRecoveryAsync(
            WaypointForTrack waypoint, 
            CombatScenes? combatScenes, 
            PathingPartyConfig partyConfig, 
            CancellationToken ct)
        {
            if (waypoint == null || partyConfig == null) return HealthRecoveryResult.HealthyAndContinue;
            if (partyConfig.OnlyInTeleportRecover && waypoint.Type != WaypointType.Teleport.Code)
                return HealthRecoveryResult.HealthyAndContinue;

            if (_visionService.ClickIfInReviveModal())
            {
                _logger.LogInformation("检测到角色死亡，已执行复苏。");
                await _visionService.WaitForMainUiAsync(ct);
                await Task.Delay(4000, ct);
                await _teleportService.TeleportToStatueOfTheSevenAsync(ct);
                _logger.LogInformation("已护送残兵败将前往神像，等待上层重置路线。");
                return HealthRecoveryResult.TeleportedToStatueRequiresRetry;
            }

            if (!_visionService.IsCurrentAvatarLowHp())
            {
                return HealthRecoveryResult.HealthyAndContinue;
            }

            _logger.LogWarning("当前角色生命值告急，尝试触发队伍维生协议...");

            if (await TryHealViaPartyAsync(combatScenes, partyConfig.MainAvatarIndex, ct))
            {
                if (!_visionService.IsCurrentAvatarLowHp())
                {
                    _logger.LogInformation("队伍治疗生效，血线安全。");
                    return HealthRecoveryResult.PartyHealedAndContinue;
                }
            }

            _logger.LogWarning("队内治疗能力不足或失效，正在前往七天神像进行深度恢复。");
            await _teleportService.TeleportToStatueOfTheSevenAsync(ct);
            return HealthRecoveryResult.TeleportedToStatueRequiresRetry;
        }

        /// <summary>
        /// 传送到七天神像 / Teleports to the Statue of the Seven.
        /// </summary>
        /// <param name="ct">取消标志 / Cancellation token.</param>
        /// <returns>异步任务 / Asynchronous task.</returns>
        public async Task TpStatueOfTheSevenAsync(CancellationToken ct = default) => await _teleportService.TeleportToStatueOfTheSevenAsync(ct);

        /// <summary>
        /// 尝试通过队伍治疗 / Attempts to heal via party.
        /// </summary>
        /// <param name="combatScenes">战斗场景 / Combat scenes.</param>
        /// <param name="mainAvatarId">主角色ID / Main avatar ID.</param>
        /// <param name="ct">取消令牌 / Cancellation token.</param>
        /// <returns>是否治疗成功 / Whether healing was successful.</returns>
        private async Task<bool> TryHealViaPartyAsync(CombatScenes? combatScenes, string mainAvatarId, CancellationToken ct)
        {
            var avatars = combatScenes?.GetAvatars();
            if (avatars == null || !avatars.Any()) return false;

            foreach (var avatar in avatars)
            {
                if (string.IsNullOrEmpty(avatar?.Name)) continue;
                if (_healerStrategies.TryGetValue(avatar.Name, out var strategy))
                {
                    if (avatar.TrySwitch())
                    {
                        await strategy.ExecuteHealSequenceAsync(_inputService, ct);
                        await _partyService.SwitchToAvatarAsync(mainAvatarId, false, ct);
                        await Task.Delay(4000, ct); 
                        return true;
                    }
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 自动寻路生命值控制器工厂 / Pathing health controller factory.
    /// 提供与遗留静态代码兼容的工厂，方便在没有全套 DI 的情况下直接使用这套新架构。
    /// </summary>
    public static class PathingHealthControllerFactory
    {
        /// <summary>
        /// 创建控制器实例 / Creates a controller instance.
        /// </summary>
        /// <param name="ct">取消令牌 / Cancellation token.</param>
        /// <param name="switchAvatarFunc">切换角色委托 / Switch avatar function.</param>
        /// <param name="logger">日志记录器 / Logger.</param>
        /// <returns>控制器实例 / Controller instance.</returns>
        public static PathingHealthController Create(CancellationToken ct, Func<string, bool, Task<Avatar?>> switchAvatarFunc, ILogger<PathingHealthController>? logger = null)
        {
            return new PathingHealthController(
                logger ?? NullLogger<PathingHealthController>.Instance,
                new LegacyVisionService(ct),
                new LegacyPartyService(switchAvatarFunc),
                new LegacyInputService(),
                new LegacyTeleportService(ct),
                new IHealerStrategy[] { new BaizhuHealerStrategy(), new SigewinneHealerStrategy(), new KokomiHealerStrategy() }
            );
        }

        private class LegacyVisionService : IVisionService
        {
            private readonly CancellationToken _ct;
            public LegacyVisionService(CancellationToken ct) => _ct = ct;
            public bool ClickIfInReviveModal() { using var region = CaptureToRectArea(); return Bv.ClickIfInReviveModal(region); }
            public bool IsCurrentAvatarLowHp() { using var region = CaptureToRectArea(); return Bv.CurrentAvatarIsLowHp(region); }
            public async Task WaitForMainUiAsync(CancellationToken ct) => await Bv.WaitForMainUi(ct);
        }
        
        private class LegacyPartyService : IPartyService
        {
            private readonly Func<string, bool, Task<Avatar?>> _switchAvatarFunc;
            public LegacyPartyService(Func<string, bool, Task<Avatar?>> switchAvatarFunc) => _switchAvatarFunc = switchAvatarFunc ?? throw new ArgumentNullException(nameof(switchAvatarFunc));
            public async Task SwitchToAvatarAsync(string avatarId, bool isInstant = false, CancellationToken ct = default) => await _switchAvatarFunc(avatarId, isInstant);
        }
        
        private class LegacyInputService : IInputService
        {
            public void ExecuteAction(GIActions action) => Simulation.SendInput.SimulateAction(action);
        }
        
        private class LegacyTeleportService : ITeleportService
        {
            private readonly CancellationToken _ct;
            public LegacyTeleportService(CancellationToken ct) => _ct = ct;
            public async Task TeleportToStatueOfTheSevenAsync(CancellationToken ct)
            {
                var tpTask = new TpTask(ct);
                await RunnerContext.Instance.StopAutoPickRunTask(async () => await tpTask.TpToStatueOfTheSeven(), 5);
            }
        }
    }
}


