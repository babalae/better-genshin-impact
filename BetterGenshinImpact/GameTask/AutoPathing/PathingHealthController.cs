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
    /// 定义自动寻路生命值检查的最终状态。上层任务应根据此状态决定是否继续跑图或重置逻辑。
    /// </summary>
    public enum HealthRecoveryResult
    {
        HealthyAndContinue,
        PartyHealedAndContinue,
        TeleportedToStatueRequiresRetry
    }

    public interface IPartyService
    {
        Task SwitchToAvatarAsync(string avatarId, bool isInstant = false, CancellationToken ct = default);
    }

    public interface IInputService
    {
        void ExecuteAction(GIActions action);
    }

    public interface IVisionService
    {
        bool ClickIfInReviveModal();
        bool IsCurrentAvatarLowHp();
        Task WaitForMainUiAsync(CancellationToken ct);
    }

    public interface ITeleportService
    {
        Task TeleportToStatueOfTheSevenAsync(CancellationToken ct);
    }

    public interface IHealerStrategy
    {
        string TargetAvatarName { get; }
        Task ExecuteHealSequenceAsync(IInputService inputService, CancellationToken ct);
    }

    public class BaizhuHealerStrategy : IHealerStrategy
    {
        public string TargetAvatarName => "白术";

        public async Task ExecuteHealSequenceAsync(IInputService inputService, CancellationToken ct)
        {
            inputService.ExecuteAction(GIActions.ElementalSkill);
            await Task.Delay(800, ct);
            inputService.ExecuteAction(GIActions.ElementalSkill);
            await Task.Delay(800, ct); 
        }
    }

    public class SigewinneHealerStrategy : IHealerStrategy
    {
        public string TargetAvatarName => "希格雯";

        public async Task ExecuteHealSequenceAsync(IInputService inputService, CancellationToken ct)
        {
            inputService.ExecuteAction(GIActions.ElementalSkill);
            await Task.Delay(11000, ct); 
        }
    }

    public class KokomiHealerStrategy : IHealerStrategy
    {
        public string TargetAvatarName => "珊瑚宫心海";

        public async Task ExecuteHealSequenceAsync(IInputService inputService, CancellationToken ct)
        {
            inputService.ExecuteAction(GIActions.ElementalSkill);
            await Task.Delay(500, ct);
            inputService.ExecuteAction(GIActions.ElementalBurst);
        }
    }
}

namespace BetterGenshinImpact.GameTask.AutoPathing
{
    using Domain;
    
    public class PathingHealthController
    {
        private readonly ILogger<PathingHealthController> _logger;
        private readonly IVisionService _visionService;
        private readonly IPartyService _partyService;
        private readonly IInputService _inputService;
        private readonly ITeleportService _teleportService;
        private readonly IReadOnlyDictionary<string, IHealerStrategy> _healerStrategies;

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

        public async Task TpStatueOfTheSevenAsync(CancellationToken ct = default) => await _teleportService.TeleportToStatueOfTheSevenAsync(ct);

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
    /// 提供与遗留静态代码兼容的工厂，方便你在还没有全套 DI 的情况下直接使用这套新架构
    /// </summary>
    public static class PathingHealthControllerFactory
    {
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
            public LegacyPartyService(Func<string, bool, Task<Avatar?>> switchAvatarFunc) => _switchAvatarFunc = switchAvatarFunc;
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
                var tpTask = new TpTask(_ct);
                await RunnerContext.Instance.StopAutoPickRunTask(async () => await tpTask.TpToStatueOfTheSeven(), 5);
            }
        }
    }
}


