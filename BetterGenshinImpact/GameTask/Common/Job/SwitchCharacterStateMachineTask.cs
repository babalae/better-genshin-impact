using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Exceptions;
using BetterGenshinImpact.GameTask.Common.StateMachine;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 切换队伍角色任务的状态机状态。
/// </summary>
public enum SwitchCharacterState
{
    Unknown, //未识别到可处理的界面
    MainUi, //主界面
    PartyConfigUnavailablePrompt, //当前状态不可进行队伍配置的提示界面
    PartyConfigPage, //队伍配置界面
    FilterPanel, //角色筛选面板
    SelectElementFilter, //选择元素筛选项
    SelectWeaponFilter, //选择武器筛选项
    ConfirmFilterPanel, //确认筛选面板
    BuildSwitchPlan, //构建本次切换计划
    PrepareNextRole, //准备下一个目标角色
    OpenFilterPanel, //打开筛选面板
    FindAndClickAvatar, //查找并点击目标头像
    ReturnMainUi, //返回主界面
    Completed //任务已完成
}

/// <summary>
/// 按指定槽位重组当前队伍角色的状态机版本。
/// </summary>
/// <remarks>
/// 状态机负责界面流转和队伍业务步骤，任务入口仅初始化上下文并等待最终状态。
/// </remarks>
public sealed class SwitchCharacterStateMachineTask : StateMachineBase<SwitchCharacterState, BvPage>
{
    private const double MatchThreshold = 0.7;
    private const string TravelerAliasName = "旅行者";
    private const string PlayerBoyName = "空";
    private const string PlayerGirlName = "荧";
    private const string SwordWeaponType = "单手剑";
    private const int EmptyCardDetectionRetryCount = 3;
    private const int MaxRoleSwitchAttempts = 3;
    private const int RemoveConfirmationTimeoutMilliseconds = 3000;
    private static readonly Rect CharacterGridRoi1080 = new(26, 97, 763, 546);

    private readonly ILogger<SwitchCharacterStateMachineTask> _logger = App.GetLogger<SwitchCharacterStateMachineTask>();
    private readonly ReturnMainUiTask _returnMainUiTask = new();
    private readonly double _assetScale = TaskContext.Instance().SystemInfo.AssetScale;

    private SwitchCharacterState _workflowState;
    private AvatarGridIconRecognizer? _recognizer;
    private List<TargetRole> _targetRoles = [];
    private List<TargetRole> _requestedTargetRoles = [];
    private TargetRole? _currentRole;
    private bool _clearCombatScenesAfterReturn;
    private List<TeamSlotSnapshot> _currentTeamSlots = [];
    private bool _teamSnapshotDirty = true;
    private bool _needsFinalVerification;
    private Dictionary<int, int> _roleSwitchAttempts = [];
    private int _expectedTeamCount;
    private int? _rebuildStartSlot;
    private Queue<TargetRole> _rebuildRoles = [];
    private bool _isRebuildClearing;
    private bool _prepareSuffixRebuildAfterRemoval;
    private bool _isAppendingRole;
    private int _playerIndex;
    private int _multiGamePlayerCount = 1;
    private int _maxControlAvatarCount = 4;
    private Dictionary<int, int> _logicalToPhysicalSlot = Enumerable.Range(1, 4).ToDictionary(slot => slot);
    private bool _usePhysicalSlots = true;
    private string? _pendingFilterElementType;
    private string? _pendingFilterWeaponType;

    /// <summary>
    /// 状态机日志对象。
    /// </summary>
    protected override ILogger Logger => _logger;

    private sealed record TargetRole(
        int Slot,
        string Name,
        string[] CandidateNames,
        string[] ConflictNames,
        bool SkipElementFilter,
        string? ForcedWeaponType)
    {
        /// <summary>
        /// 用于读取角色配置的首选实际角色名。
        /// </summary>
        public string PrimaryCandidateName => CandidateNames[0];

        /// <summary>
        /// 判断识别到的角色名是否满足当前目标。
        /// </summary>
        /// <param name="characterName">识别到的角色名。</param>
        /// <returns>角色名属于当前目标候选时返回 true。</returns>
        public bool Matches(string? characterName)
        {
            return characterName != null && CandidateNames.Contains(characterName, StringComparer.Ordinal);
        }
    }

    private sealed record TeamSlotSnapshot(int Slot, string? Name);

    private AvatarGridIconRecognizer Recognizer =>
        _recognizer ?? throw new InvalidOperationException("切换角色：头像识别器未初始化");

    /// <summary>
    /// 初始化状态机版本的角色切换任务。
    /// </summary>
    public SwitchCharacterStateMachineTask()
    {
        RegisterStateMethodsByAttribute();
        RegisterStateTransitions(
            (SwitchCharacterState.Unknown, [
                SwitchCharacterState.MainUi,
                SwitchCharacterState.PartyConfigUnavailablePrompt,
                SwitchCharacterState.PartyConfigPage,
                SwitchCharacterState.BuildSwitchPlan
            ]),
            (SwitchCharacterState.MainUi, [
                SwitchCharacterState.PartyConfigUnavailablePrompt,
                SwitchCharacterState.PartyConfigPage
            ]),
            (SwitchCharacterState.PartyConfigUnavailablePrompt, [
                SwitchCharacterState.MainUi,
                SwitchCharacterState.PartyConfigPage
            ]),
            (SwitchCharacterState.PartyConfigPage, [
                SwitchCharacterState.BuildSwitchPlan,
                SwitchCharacterState.PrepareNextRole
            ]),
            (SwitchCharacterState.BuildSwitchPlan, [
                SwitchCharacterState.PrepareNextRole,
                SwitchCharacterState.ReturnMainUi
            ]),
            (SwitchCharacterState.PrepareNextRole, [
                SwitchCharacterState.OpenFilterPanel,
                SwitchCharacterState.ReturnMainUi
            ]),
            (SwitchCharacterState.OpenFilterPanel, [
                SwitchCharacterState.ReturnMainUi,
                SwitchCharacterState.FilterPanel,
                SwitchCharacterState.PrepareNextRole
            ]),
            (SwitchCharacterState.FilterPanel, [
                SwitchCharacterState.ReturnMainUi,
                SwitchCharacterState.SelectElementFilter,
                SwitchCharacterState.SelectWeaponFilter,
                SwitchCharacterState.PrepareNextRole
            ]),
            (SwitchCharacterState.SelectElementFilter, [
                SwitchCharacterState.ReturnMainUi,
                SwitchCharacterState.SelectWeaponFilter
            ]),
            (SwitchCharacterState.SelectWeaponFilter, [
                SwitchCharacterState.ReturnMainUi,
                SwitchCharacterState.ConfirmFilterPanel
            ]),
            (SwitchCharacterState.ConfirmFilterPanel, [
                SwitchCharacterState.ReturnMainUi,
                SwitchCharacterState.FindAndClickAvatar
            ]),
            (SwitchCharacterState.FindAndClickAvatar, [
                SwitchCharacterState.PrepareNextRole
            ]),
            (SwitchCharacterState.ReturnMainUi, [
                SwitchCharacterState.Completed
            ])
        );
    }

    /// <summary>
    /// 按槽位切换当前队伍角色。
    /// </summary>
    /// <param name="slot1">1 号槽位角色名。</param>
    /// <param name="slot2">2 号槽位角色名。</param>
    /// <param name="slot3">3 号槽位角色名。</param>
    /// <param name="slot4">4 号槽位角色名。</param>
    /// <param name="usePhysicalSlots">是否将 slot1-slot4 解释为队伍物理槽位；false 时按当前玩家可控角色顺序解释。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完成保存并返回主界面返回 true；参数无效、目标角色未找到或流程失败返回 false。</returns>
    /// <remarks>
    /// usePhysicalSlots 为 true 时，slot1-slot4 表示队伍中的物理槽位；
    /// 为 false 时，slot1-slot4 表示当前玩家的第 1-4 个可控角色。空字符串或空白字符串表示跳过对应槽位。
    /// 2 人联机时 1P 可操作 1、2 号槽位，2P 可操作 3、4 号槽位；
    /// 3 人联机时 1P 可操作 1、2 号槽位，2P 可操作 3 号槽位，3P 可操作 4 号槽位；
    /// 4 人联机时各玩家可操作与玩家编号相同的槽位。当前玩家不可操作的槽位参数会被忽略。
    /// </remarks>
    public async Task<bool> Start(
        string slot1,
        string slot2,
        string slot3,
        string slot4,
        bool usePhysicalSlots,
        CancellationToken ct)
    {
        Initialize(ct, SwitchCharacterState.Unknown);
        var page = new BvPage(ct);
        string[] slots = [slot1, slot2, slot3, slot4];

        var roles = ParseRoles(slots);
        if (roles.Count == 0 || HasConflictingRoleTargets(roles))
        {
            throw new PartySetupFailedException("切换角色：未指定角色或同一实际角色被指定到多个槽位");
        }

        ResetWorkflow(roles, usePhysicalSlots);
        using var recognizer = new AvatarGridIconRecognizer();
        _recognizer = recognizer;

        try
        {
            await RunStateMachineUntil(page, SwitchCharacterState.Completed);
            return true;
        }
        finally
        {
            _recognizer = null;
        }
    }

    /// <summary>
    /// 重置本次运行的工作流上下文。
    /// </summary>
    /// <param name="roles">解析后的目标角色。</param>
    /// <param name="usePhysicalSlots">是否使用队伍物理槽位解释目标。</param>
    private void ResetWorkflow(IReadOnlyList<TargetRole> roles, bool usePhysicalSlots)
    {
        _workflowState = SwitchCharacterState.BuildSwitchPlan;
        _targetRoles = roles.ToList();
        _requestedTargetRoles = [];
        _currentRole = null;
        _clearCombatScenesAfterReturn = false;
        _currentTeamSlots = [];
        _teamSnapshotDirty = true;
        _needsFinalVerification = false;
        _roleSwitchAttempts = [];
        _expectedTeamCount = 0;
        _rebuildStartSlot = null;
        _rebuildRoles = [];
        _isRebuildClearing = false;
        _prepareSuffixRebuildAfterRemoval = false;
        _isAppendingRole = false;
        _playerIndex = 0;
        _multiGamePlayerCount = 1;
        _maxControlAvatarCount = 4;
        _logicalToPhysicalSlot = Enumerable.Range(1, 4).ToDictionary(slot => slot);
        _usePhysicalSlots = usePhysicalSlots;
        _pendingFilterElementType = null;
        _pendingFilterWeaponType = null;
    }

    /// <summary>
    /// 设置当前待处理角色，并初始化筛选条件。
    /// </summary>
    /// <param name="role">待处理角色。</param>
    private void SetCurrentRole(TargetRole role)
    {
        _currentRole = role;
        SetCurrentRoleFilter(role);
    }

    /// <summary>
    /// 设置当前角色所需的筛选条件。
    /// </summary>
    /// <param name="role">待处理角色。</param>
    private void SetCurrentRoleFilter(TargetRole role)
    {
        _pendingFilterElementType = role.SkipElementFilter ? null : Recognizer.GetElementType(role.PrimaryCandidateName);
        _pendingFilterWeaponType = role.ForcedWeaponType ?? Recognizer.GetWeaponType(role.PrimaryCandidateName);
        _logger.LogDebug("切换角色：{Slot}. {Name}，武器：{Weapon}，元素筛选：{ElementFilter}",
            role.Slot,
            role.Name,
            _pendingFilterWeaponType,
            _pendingFilterElementType ?? "跳过");
    }

    #region 状态检测器

    /// <summary>
    /// 检测构建切换计划状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>队伍配置页中等待构建计划时返回 true。</returns>
    [StateDetector(SwitchCharacterState.BuildSwitchPlan, Order = 11)]
    private bool DetectBuildSwitchPlan(ImageRegion capture)
    {
        return _workflowState == SwitchCharacterState.BuildSwitchPlan && IsPartyConfigPage(capture);
    }

    /// <summary>
    /// 检测准备目标角色状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>队伍配置页中等待准备目标角色时返回 true。</returns>
    [StateDetector(SwitchCharacterState.PrepareNextRole, Order = 13)]
    private bool DetectPrepareNextRole(ImageRegion capture)
    {
        return _workflowState == SwitchCharacterState.PrepareNextRole && IsPartyConfigPage(capture);
    }

    /// <summary>
    /// 检测打开筛选面板状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>角色列表中等待打开筛选面板时返回 true。</returns>
    [StateDetector(SwitchCharacterState.OpenFilterPanel, Order = 14)]
    private bool DetectOpenFilterPanel(ImageRegion capture)
    {
        return _workflowState == SwitchCharacterState.OpenFilterPanel && IsCharacterList(capture);
    }

    /// <summary>
    /// 检测头像查找状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>角色列表中等待查找头像时返回 true。</returns>
    [StateDetector(SwitchCharacterState.FindAndClickAvatar, Order = 15)]
    private bool DetectFindAndClickAvatar(ImageRegion capture)
    {
        return _workflowState == SwitchCharacterState.FindAndClickAvatar
               && IsCharacterList(capture)
               && !IsFilterPanel(capture);
    }

    /// <summary>
    /// 检测返回主界面状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>当前工作流需要返回主界面时返回 true。</returns>
    [StateDetector(SwitchCharacterState.ReturnMainUi, Order = 21)]
    private bool DetectReturnMainUi(ImageRegion capture)
    {
        return _workflowState == SwitchCharacterState.ReturnMainUi
               && (IsCharacterList(capture) || IsFilterPanel(capture) || IsPartyConfigPage(capture) || Bv.IsInMainUi(capture));
    }

    /// <summary>
    /// 检测元素筛选项选择状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>筛选面板中等待选择元素筛选项时返回 true。</returns>
    [StateDetector(SwitchCharacterState.SelectElementFilter, Order = 22)]
    private bool DetectSelectElementFilter(ImageRegion capture)
    {
        return _workflowState == SwitchCharacterState.SelectElementFilter && IsFilterPanel(capture);
    }

    /// <summary>
    /// 检测武器筛选项选择状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>筛选面板中等待选择武器筛选项时返回 true。</returns>
    [StateDetector(SwitchCharacterState.SelectWeaponFilter, Order = 23)]
    private bool DetectSelectWeaponFilter(ImageRegion capture)
    {
        return _workflowState == SwitchCharacterState.SelectWeaponFilter && IsFilterPanel(capture);
    }

    /// <summary>
    /// 检测确认筛选面板状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>武器筛选标签已出现、正在确认筛选面板或确认后面板尚未关闭时返回 true。</returns>
    [StateDetector(SwitchCharacterState.ConfirmFilterPanel, Order = 24)]
    private bool DetectConfirmFilterPanel(ImageRegion capture)
    {
        return (_workflowState == SwitchCharacterState.ConfirmFilterPanel
                || (CurrentState == SwitchCharacterState.ConfirmFilterPanel
                    && _workflowState == SwitchCharacterState.FindAndClickAvatar))
               && IsFilterPanel(capture);
    }

    /// <summary>
    /// 检测筛选面板。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>识别到确认筛选按钮返回 true。</returns>
    [StateDetector(SwitchCharacterState.FilterPanel, Order = 30)]
    private bool DetectFilterPanel(ImageRegion capture)
    {
        return IsFilterPanel(capture);
    }

    /// <summary>
    /// 检测不可进行队伍配置提示。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>识别到提示文字返回 true。</returns>
    [StateDetector(SwitchCharacterState.PartyConfigUnavailablePrompt, Order = 50)]
    private bool DetectPartyConfigUnavailablePrompt(ImageRegion capture)
    {
        return ContainsText(capture, "当前状态不可进行队伍配置", Rect1080(806, 198, 314, 37));
    }

    /// <summary>
    /// 检测队伍配置界面。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>识别到队伍配置标题返回 true。</returns>
    [StateDetector(SwitchCharacterState.PartyConfigPage, Order = 60)]
    private bool DetectPartyConfigPage(ImageRegion capture)
    {
        return IsPartyConfigPage(capture);
    }

    /// <summary>
    /// 检测任务完成状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>工作流已完成且处于主界面时返回 true。</returns>
    [StateDetector(SwitchCharacterState.Completed, Order = 80)]
    private bool DetectCompleted(ImageRegion capture)
    {
        return _workflowState == SwitchCharacterState.Completed && Bv.IsInMainUi(capture);
    }

    /// <summary>
    /// 检测主界面。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>处于主界面返回 true。</returns>
    [StateDetector(SwitchCharacterState.MainUi, Order = 90)]
    private bool DetectMainUi(ImageRegion capture)
    {
        return Bv.IsInMainUi(capture);
    }

    /// <summary>
    /// 判断截图是否为角色列表。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>识别到元素共鸣文字返回 true。</returns>
    private bool IsCharacterList(ImageRegion capture)
    {
        return ContainsText(capture, "元素共鸣", Rect1080(1655, 32, 106, 30));
    }

    /// <summary>
    /// 判断截图是否为筛选面板。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>识别到确认筛选按钮返回 true。</returns>
    private bool IsFilterPanel(ImageRegion capture)
    {
        return ContainsText(capture, "确认筛选", Rect1080(360, 999, 128, 40));
    }

    /// <summary>
    /// 判断截图是否为队伍配置界面。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>识别到队伍配置标题返回 true。</returns>
    private bool IsPartyConfigPage(ImageRegion capture)
    {
        return ContainsText(capture, "队伍配置", Rect1080(119, 30, 108, 37));
    }

    #endregion

    #region 状态处理器

    /// <summary>
    /// 处理未识别界面。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>返回主界面后等待状态检测。</returns>
    [StateHandler(SwitchCharacterState.Unknown, RetryTimes = 3, RetryInterval = 500, TransitionTimeout = 6000)]
    private async Task<StateHandlerResult> HandleUnknownState(BvPage page)
    {
        _logger.LogWarning("切换角色：当前界面未识别，尝试返回主界面");
        await _returnMainUiTask.Start(_ct);
        return StateHandlerResult.Success;
    }

    /// <summary>
    /// 处理主界面。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>触发打开队伍配置后返回 Success。</returns>
    [StateHandler(SwitchCharacterState.MainUi, RetryTimeout = 15000, RetryInterval = 500, TransitionTimeout = 7000)]
    private async Task<StateHandlerResult> HandleMainUi(BvPage page)
    {
        await DetectPlayerIndexAndOpenPartyConfig(_ct);
        return StateHandlerResult.Success;
    }

    /// <summary>
    /// 处理不可进行队伍配置提示。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>传送到七天神像后返回 Success。</returns>
    [StateHandler(SwitchCharacterState.PartyConfigUnavailablePrompt, RetryTimes = 2, TransitionTimeout = 30000)]
    private async Task<StateHandlerResult> HandlePartyConfigUnavailablePrompt(BvPage page)
    {
        _logger.LogWarning("切换角色：当前状态不可进行队伍配置，传送到七天神像后重试");
        await new TpTask(_ct).TpToStatueOfTheSeven();
        return StateHandlerResult.Success;
    }

    /// <summary>
    /// 处理队伍配置界面。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>识别到队伍配置界面后直接返回 Success，由后续状态构建切换计划。</returns>
    [StateHandler(SwitchCharacterState.PartyConfigPage, RetryTimeout = 10000, RetryInterval = 500, TransitionTimeout = 6000)]
    private Task<StateHandlerResult> HandlePartyConfigPage(BvPage page)
    {
        return Task.FromResult(StateHandlerResult.Success);
    }

    /// <summary>
    /// 构建本次切换计划。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>计划构建完成返回 Success。</returns>
    [StateHandler(SwitchCharacterState.BuildSwitchPlan, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private async Task<StateHandlerResult> HandleBuildSwitchPlan(BvPage page)
    {
        if (_playerIndex == 0)
        {
            _logger.LogInformation("切换角色：尚未识别联机人数与玩家编号，先返回主界面完成识别");
            await _returnMainUiTask.Start(_ct);
            await DetectPlayerIndexAndOpenPartyConfig(_ct);
            return StateHandlerResult.Wait;
        }

        ConfigureOperableSlots();
        AdjustTargetsToOperableSlots();
        if (_targetRoles.Count == 0)
        {
            _logger.LogInformation("切换角色：传入目标中没有当前账号可操作的联机槽位，结束任务");
            _workflowState = SwitchCharacterState.ReturnMainUi;
            return StateHandlerResult.Success;
        }

        _requestedTargetRoles = _targetRoles.ToList();
        _currentTeamSlots = await RecognizeTeamSlotsFromCharacterList(Recognizer, _maxControlAvatarCount, _ct);
        _expectedTeamCount = _currentTeamSlots.Count;
        _teamSnapshotDirty = false;
        _targetRoles = BuildDesiredTeamRoles(_targetRoles, _currentTeamSlots);
        var rolesToSelect = GetRolesToSelect(_targetRoles, _currentTeamSlots);
        if (rolesToSelect.Count == 0)
        {
            _logger.LogInformation("切换角色：目标角色已在指定槽位");
            _clearCombatScenesAfterReturn = false;
            _workflowState = SwitchCharacterState.ReturnMainUi;
            return StateHandlerResult.Success;
        }

        _logger.LogInformation("切换角色：选择计划 {Plan}",
            string.Join(",", rolesToSelect.Select(role => $"{role.Slot}.{role.Name}")));

        _workflowState = SwitchCharacterState.PrepareNextRole;
        return StateHandlerResult.Success;
    }

    /// <summary>
    /// 准备下一个目标角色。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>存在未完成目标时进入换下或筛选流程；全部完成时返回主界面。</returns>
    [StateHandler(SwitchCharacterState.PrepareNextRole, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private async Task<StateHandlerResult> HandlePrepareNextRole(BvPage page)
    {
        if (_teamSnapshotDirty)
        {
            _currentTeamSlots = await RecognizeTeamSlotsFromCharacterList(Recognizer, _expectedTeamCount, _ct);
            _teamSnapshotDirty = false;
            if (_needsFinalVerification)
            {
                _logger.LogInformation("切换角色：已完成最终队伍识别，按实际队伍继续验证目标槽位");
                _needsFinalVerification = false;
            }
        }

        if (_prepareSuffixRebuildAfterRemoval)
        {
            var firstMismatchSlot = FindFirstMismatchSlot(_targetRoles, _currentTeamSlots);
            if (firstMismatchSlot == null)
            {
                throw new PartySetupFailedException("切换角色：换下错位角色后无法确定需要补回的槽位");
            }

            StartSuffixRebuild(firstMismatchSlot.Value);
            _prepareSuffixRebuildAfterRemoval = false;
        }

        if (_rebuildStartSlot is int rebuildStartSlot)
        {
            var minimumRetainedCount = Math.Max(1, rebuildStartSlot - 1);
            if (_isRebuildClearing && _currentTeamSlots.Count > minimumRetainedCount)
            {
                ClickFixedTeamSlot(rebuildStartSlot);
                await Delay(500, _ct);
                if (!TryClickText(page, "换下", Rect1080(382, 994, 87, 51)))
                {
                    throw new PartySetupFailedException($"切换角色：未找到 {rebuildStartSlot} 号位的换下按钮");
                }

                await WaitForRoleRemoved(rebuildStartSlot, _ct);
                _expectedTeamCount--;
                _teamSnapshotDirty = true;
                return StateHandlerResult.Wait;
            }

            _isRebuildClearing = false;

            if (_rebuildRoles.Count > 0)
            {
                var refillRole = _rebuildRoles.Peek();
                if (_roleSwitchAttempts.GetValueOrDefault(refillRole.Slot) >= MaxRoleSwitchAttempts)
                {
                    throw new PartySetupFailedException(
                        $"切换角色：{refillRole.Slot} 号位连续 {MaxRoleSwitchAttempts} 次未能补回 {refillRole.Name}");
                }

                SetCurrentRole(refillRole);
                _isAppendingRole = refillRole.Slot > _currentTeamSlots.Count;
                ClickFixedTeamSlot(refillRole.Slot);
                _workflowState = SwitchCharacterState.OpenFilterPanel;
                return StateHandlerResult.Success;
            }

            _rebuildStartSlot = null;
        }

        if (_currentRole == null)
        {
            var currentNameBySlot = _currentTeamSlots.ToDictionary(slot => slot.Slot, slot => slot.Name);
            var nextRole = _requestedTargetRoles
                .OrderBy(role => role.Slot)
                .FirstOrDefault(role =>
                    !currentNameBySlot.TryGetValue(role.Slot, out var currentName) || !role.Matches(currentName))
                ?? _targetRoles
                .OrderBy(role => role.Slot)
                .FirstOrDefault(role =>
                    !currentNameBySlot.TryGetValue(role.Slot, out var currentName) || !role.Matches(currentName));
            if (nextRole != null)
            {
                if (_roleSwitchAttempts.GetValueOrDefault(nextRole.Slot) >= MaxRoleSwitchAttempts)
                {
                    throw new PartySetupFailedException(
                        $"切换角色：{nextRole.Slot} 号位连续 {MaxRoleSwitchAttempts} 次未能切换为 {nextRole.Name}");
                }

                SetCurrentRole(nextRole);
            }
        }

        if (_currentRole == null)
        {
            if (_needsFinalVerification)
            {
                _logger.LogDebug("切换角色：所有目标已提交，开始统一识别并验证实际队伍");
                _teamSnapshotDirty = true;
                return StateHandlerResult.Wait;
            }

            _workflowState = SwitchCharacterState.ReturnMainUi;
            return StateHandlerResult.Success;
        }

        var misplaced = _currentTeamSlots.FirstOrDefault(slot => slot.Slot != _currentRole.Slot && _currentRole.Matches(slot.Name));
        if (misplaced != null)
        {
            if (_currentTeamSlots.Count == 1)
            {
                _logger.LogInformation("切换角色：队伍仅剩一个角色，保留该角色并从 1 号位开始通过更换重建队伍");
                StartSuffixRebuild(1);
                _currentRole = null;
                return StateHandlerResult.Wait;
            }

            ClickFixedTeamSlot(misplaced.Slot);
            await Delay(500, _ct);
            if (!TryClickText(page, "换下", Rect1080(382, 994, 87, 51)))
            {
                throw new PartySetupFailedException($"切换角色：未找到 {misplaced.Slot} 号位的换下按钮");
            }

            await WaitForRoleRemoved(misplaced.Slot, _ct);
            _expectedTeamCount--;
            _teamSnapshotDirty = true;
            _prepareSuffixRebuildAfterRemoval = true;
            _currentRole = null;
            return StateHandlerResult.Wait;
        }

        EnsureSlotIsOperable(_currentRole.Slot);
        ClickFixedTeamSlot(_currentRole.Slot);
        _workflowState = SwitchCharacterState.OpenFilterPanel;
        return StateHandlerResult.Success;
    }

    /// <summary>
    /// 打开筛选面板。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>点击筛选入口后返回 Success。</returns>
    [StateHandler(SwitchCharacterState.OpenFilterPanel, RetryTimeout = 9000, RetryInterval = 300, TransitionTimeout = 4000)]
    private Task<StateHandlerResult> HandleOpenFilterPanel(BvPage page)
    {
        GameCaptureRegion.GameRegion1080PPosClick(66, 46);
        return Task.FromResult(StateHandlerResult.Success);
    }

    /// <summary>
    /// 处理筛选面板。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>根据当前角色筛选需求进入元素或武器筛选状态。</returns>
    [StateHandler(SwitchCharacterState.FilterPanel, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 6000)]
    private Task<StateHandlerResult> HandleFilterPanel(BvPage page)
    {
        if (string.IsNullOrWhiteSpace(_pendingFilterWeaponType))
        {
            throw new PartySetupFailedException("切换角色：筛选面板缺少武器筛选项");
        }

        _workflowState = string.IsNullOrWhiteSpace(_pendingFilterElementType)
            ? SwitchCharacterState.SelectWeaponFilter
            : SwitchCharacterState.SelectElementFilter;
        return Task.FromResult(StateHandlerResult.Success);
    }

    /// <summary>
    /// 选择元素筛选项。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>点击元素选项后进入武器筛选；找不到选项时返回 Retry。</returns>
    [StateHandler(SwitchCharacterState.SelectElementFilter, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private Task<StateHandlerResult> HandleSelectElementFilter(BvPage page)
    {
        if (string.IsNullOrWhiteSpace(_pendingFilterElementType))
        {
            _workflowState = SwitchCharacterState.SelectWeaponFilter;
            return Task.FromResult(StateHandlerResult.Success);
        }

        if (!TryClickText(page, _pendingFilterElementType, GetElementFilterOptionsRoi()))
        {
            _logger.LogWarning("切换角色：未找到元素筛选项 {Text}", _pendingFilterElementType);
            return Task.FromResult(StateHandlerResult.Retry);
        }

        _workflowState = SwitchCharacterState.SelectWeaponFilter;
        return Task.FromResult(StateHandlerResult.Success);
    }

    /// <summary>
    /// 选择武器筛选项。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>点击武器选项后进入确认筛选；找不到选项时返回 Retry。</returns>
    [StateHandler(SwitchCharacterState.SelectWeaponFilter, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private Task<StateHandlerResult> HandleSelectWeaponFilter(BvPage page)
    {
        _workflowState = SwitchCharacterState.SelectWeaponFilter;
        if (string.IsNullOrWhiteSpace(_pendingFilterWeaponType))
        {
            throw new PartySetupFailedException("切换角色：筛选面板缺少武器筛选项");
        }

        if (!TryClickText(page, _pendingFilterWeaponType, GetWeaponFilterOptionsRoi()))
        {
            _logger.LogWarning("切换角色：未找到武器筛选项 {Text}", _pendingFilterWeaponType);
            return Task.FromResult(StateHandlerResult.Retry);
        }

        _workflowState = SwitchCharacterState.ConfirmFilterPanel;
        return Task.FromResult(StateHandlerResult.Success);
    }

    /// <summary>
    /// 确认筛选面板。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>点击确认筛选后返回 Success；找不到按钮时返回 Retry。</returns>
    [StateHandler(SwitchCharacterState.ConfirmFilterPanel, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 6000)]
    private Task<StateHandlerResult> HandleConfirmFilterPanel(BvPage page)
    {
        _workflowState = SwitchCharacterState.ConfirmFilterPanel;
        if (!TryClickText(page, "确认筛选", Rect1080(360, 999, 128, 40)))
        {
            _logger.LogWarning("切换角色：未找到确认筛选按钮");
            return Task.FromResult(StateHandlerResult.Retry);
        }

        _pendingFilterElementType = null;
        _pendingFilterWeaponType = null;
        _workflowState = SwitchCharacterState.FindAndClickAvatar;
        return Task.FromResult(StateHandlerResult.Success);
    }

    /// <summary>
    /// 查找并点击当前角色头像。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>头像查找并提交完成后进入准备下一个目标角色状态。</returns>
    [StateHandler(SwitchCharacterState.FindAndClickAvatar, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private async Task<StateHandlerResult> HandleFindAndClickAvatar(BvPage page)
    {
        if (_currentRole == null)
        {
            throw new PartySetupFailedException("切换角色：当前角色状态为空");
        }

        if (!await FindAndClickAvatar(_currentRole, Recognizer, _ct))
        {
            throw new PartySetupFailedException($"切换角色：未找到目标角色 {_currentRole.Name}");
        }

        if (!TryClickAnyText(page, ["更换", "加入"], Rect1080(382, 994, 87, 51)))
        {
            _logger.LogWarning("切换角色：未识别到“更换”或“加入”按钮");
            return StateHandlerResult.Retry;
        }

        if (_isAppendingRole)
        {
            _currentTeamSlots.Add(new TeamSlotSnapshot(_currentRole.Slot, _currentRole.PrimaryCandidateName));
            _currentTeamSlots = _currentTeamSlots.OrderBy(slot => slot.Slot).ToList();
            _expectedTeamCount++;
        }
        else
        {
            _currentTeamSlots = _currentTeamSlots
                .Select(slot => slot.Slot == _currentRole.Slot
                    ? slot with { Name = _currentRole.PrimaryCandidateName }
                    : slot)
                .ToList();
        }

        if (_rebuildStartSlot != null &&
            _rebuildRoles.Count > 0 &&
            _rebuildRoles.Peek().Slot == _currentRole.Slot)
        {
            _rebuildRoles.Dequeue();
            if (_rebuildRoles.Count == 0)
            {
                _rebuildStartSlot = null;
            }
        }

        _roleSwitchAttempts[_currentRole.Slot] = _roleSwitchAttempts.GetValueOrDefault(_currentRole.Slot) + 1;
        _needsFinalVerification = true;
        _logger.LogInformation("切换角色：已提交 {Name} 到 {Slot} 号位", _currentRole.Name, _currentRole.Slot);
        _clearCombatScenesAfterReturn = true;
        _currentRole = null;
        _isAppendingRole = false;
        _workflowState = SwitchCharacterState.PrepareNextRole;
        return StateHandlerResult.Success;
    }

    /// <summary>
    /// 返回主界面并结束工作流。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>返回主界面后进入完成状态。</returns>
    [StateHandler(SwitchCharacterState.ReturnMainUi, RetryTimeout = 15000, RetryInterval = 500, TransitionTimeout = 7000)]
    private async Task<StateHandlerResult> HandleReturnMainUi(BvPage page)
    {
        await _returnMainUiTask.Start(_ct);
        if (_clearCombatScenesAfterReturn)
        {
            RunnerContext.Instance.ClearCombatScenes();
        }

        _workflowState = SwitchCharacterState.Completed;
        return StateHandlerResult.Success;
    }

    #endregion

    /// <summary>
    /// 解析四个槽位参数，跳过空字符串槽位并转换角色名。
    /// </summary>
    /// <param name="slots">1-4 号槽位角色名。</param>
    /// <returns>目标槽位角色列表。</returns>
    private static List<TargetRole> ParseRoles(IReadOnlyList<string> slots)
    {
        List<TargetRole> roles = [];
        for (int i = 0; i < slots.Count; i++)
        {
            var name = slots[i].Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            roles.Add(CreateTargetRole(i + 1, name));
        }

        return roles;
    }

    /// <summary>
    /// 根据输入名称创建目标角色定义。
    /// </summary>
    /// <param name="slot">目标槽位。</param>
    /// <param name="name">输入角色名或别名。</param>
    /// <returns>目标角色定义。</returns>
    private static TargetRole CreateTargetRole(int slot, string name)
    {
        var standardName = ToConfiguredAvatarName(name);
        if (standardName == TravelerAliasName)
        {
            return new TargetRole(
                slot,
                TravelerAliasName,
                [PlayerBoyName, PlayerGirlName],
                [PlayerBoyName, PlayerGirlName],
                true,
                SwordWeaponType);
        }

        if (standardName is PlayerBoyName or PlayerGirlName)
        {
            return new TargetRole(
                slot,
                standardName,
                [standardName],
                [PlayerBoyName, PlayerGirlName],
                true,
                SwordWeaponType);
        }

        var skipElementFilter = standardName.StartsWith("奇偶", StringComparison.Ordinal);
        return new TargetRole(
            slot,
            standardName,
            [standardName],
            [standardName],
            skipElementFilter,
            null);
    }

    /// <summary>
    /// 将输入名称转换为配置中的标准名称。
    /// </summary>
    /// <param name="name">角色名或别名。</param>
    /// <returns>配置中的标准名称。</returns>
    private static string ToConfiguredAvatarName(string name)
    {
        if (DefaultAutoFightConfig.CombatAvatarMap.ContainsKey(name))
        {
            return name;
        }

        return DefaultAutoFightConfig.AvatarAliasToStandardName(name);
    }

    /// <summary>
    /// 判断目标槽位中是否存在同一实际角色的冲突。
    /// </summary>
    /// <param name="roles">目标槽位角色列表。</param>
    /// <returns>存在冲突返回 true。</returns>
    private static bool HasConflictingRoleTargets(IReadOnlyList<TargetRole> roles)
    {
        for (int i = 0; i < roles.Count; i++)
        {
            for (int j = i + 1; j < roles.Count; j++)
            {
                if (roles[i].ConflictNames.Intersect(roles[j].ConflictNames, StringComparer.Ordinal).Any())
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 将显式目标与当前队伍合并为完整的期望队伍，未指定角色优先保留原物理位置。
    /// </summary>
    private static List<TargetRole> BuildDesiredTeamRoles(
        IReadOnlyCollection<TargetRole> requestedRoles,
        IReadOnlyList<TeamSlotSnapshot> currentSlots)
    {
        var desired = new TargetRole?[currentSlots.Count];
        var remaining = currentSlots
            .Select(slot => (slot.Slot, Name: slot.Name
                ?? throw new PartySetupFailedException($"切换角色：{slot.Slot} 号位角色识别为空")))
            .ToList();

        foreach (var role in requestedRoles.OrderBy(role => role.Slot))
        {
            desired[role.Slot - 1] = role;
            var currentIndex = remaining.FindIndex(item => role.Matches(item.Name));
            if (currentIndex >= 0)
            {
                remaining.RemoveAt(currentIndex);
            }
        }

        for (var index = 0; index < desired.Length; index++)
        {
            if (desired[index] != null)
            {
                continue;
            }

            var originalIndex = remaining.FindIndex(item => item.Slot == index + 1);
            if (originalIndex < 0)
            {
                continue;
            }

            desired[index] = CreateTargetRole(index + 1, remaining[originalIndex].Name);
            remaining.RemoveAt(originalIndex);
        }

        for (var index = 0; index < desired.Length; index++)
        {
            if (desired[index] != null)
            {
                continue;
            }

            if (remaining.Count == 0)
            {
                throw new PartySetupFailedException("切换角色：无法为未指定槽位生成补位角色");
            }

            desired[index] = CreateTargetRole(index + 1, remaining[0].Name);
            remaining.RemoveAt(0);
        }

        return desired.Select(role => role!).ToList();
    }

    private static int? FindFirstMismatchSlot(
        IReadOnlyList<TargetRole> desiredRoles,
        IReadOnlyList<TeamSlotSnapshot> currentSlots)
    {
        return desiredRoles
            .OrderBy(role => role.Slot)
            .FirstOrDefault(role =>
                role.Slot > currentSlots.Count || !role.Matches(currentSlots[role.Slot - 1].Name))
            ?.Slot;
    }

    private void StartSuffixRebuild(int startSlot)
    {
        _rebuildStartSlot = startSlot;
        _isRebuildClearing = true;
        _rebuildRoles = new Queue<TargetRole>(_targetRoles
            .Where(role => role.Slot >= startSlot)
            .OrderBy(role => role.Slot));
        _logger.LogInformation(
            "切换角色：从逻辑槽位 {StartSlot} 重建队伍后缀，依次补回 {Roles}",
            startSlot,
            string.Join(",", _rebuildRoles.Select(role => $"{role.Slot}.{role.Name}")));
    }

    /// <summary>
    /// 获取当前不在目标槽位、需要重新选择的目标角色。
    /// </summary>
    /// <param name="roles">目标槽位角色列表。</param>
    /// <param name="currentSlots">当前已选角色快照。</param>
    /// <returns>需要重新选择的目标角色列表。</returns>
    private static List<TargetRole> GetRolesToSelect(IReadOnlyCollection<TargetRole> roles, IReadOnlyCollection<TeamSlotSnapshot> currentSlots)
    {
        var currentNameBySlot = currentSlots.ToDictionary(slot => slot.Slot, slot => slot.Name);
        return roles
            .Where(role => !currentNameBySlot.TryGetValue(role.Slot, out var currentName) || !role.Matches(currentName))
            .OrderBy(role => role.Slot)
            .ToList();
    }

    /// <summary>
    /// 在主界面识别联机身份并打开队伍配置页。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    private async Task DetectPlayerIndexAndOpenPartyConfig(CancellationToken ct)
    {
        await DetectPlayerIndex(ct);
        Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
        await Delay(2000, ct);
    }

    /// <summary>
    /// 识别联机人数及当前玩家编号。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    private async Task DetectPlayerIndex(CancellationToken ct)
    {
        const int timeoutMilliseconds = 5000;
        const int retryIntervalMilliseconds = 300;
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        string lastResult = "无";

        do
        {
            ct.ThrowIfCancellationRequested();
            using var capture = CaptureToRectArea(true);
            using var standAloneIcon = capture.Find(RecognitionAssets.Get(@"Common\Job\SwitchCharacter", "StandAloneIcon", capture));
            if (standAloneIcon.IsExist())
            {
                _multiGamePlayerCount = 1;
                _playerIndex = 1;
                _logger.LogInformation("切换角色：识别到单机图标，按 4 个可控槽位处理");
                return;
            }

            MultiGameStatus multiGameStatus;
            try
            {
                multiGameStatus = PartyAvatarSideIndexHelper.DetectedMultiGameStatus(
                    capture,
                    logger: Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            }
            catch (Exception ex)
            {
                lastResult = ex.Message;
                _logger.LogDebug(ex, "切换角色：联机人数尚未稳定识别，继续等待");
                await Delay(retryIntervalMilliseconds, ct);
                continue;
            }

            _multiGamePlayerCount = multiGameStatus.IsInMultiGame
                ? Math.Max(1, multiGameStatus.PlayerCount)
                : 1;
            if (_multiGamePlayerCount == 1)
            {
                if (multiGameStatus.IsInMultiGame)
                {
                    _playerIndex = 1;
                    _logger.LogInformation("切换角色：识别到 1 人联机状态，按 4 个可控槽位处理");
                    return;
                }

                lastResult = "未命中单机图标，也未识别到联机状态";
                var singlePlayerRemainingMilliseconds = deadline - Environment.TickCount64;
                if (singlePlayerRemainingMilliseconds <= 0)
                {
                    break;
                }

                await Delay((int)Math.Min(retryIntervalMilliseconds, singlePlayerRemainingMilliseconds), ct);
                continue;
            }

            var matches = Enumerable.Range(1, 4)
                .SelectMany(playerIndex => capture
                    .FindMulti(RecognitionAssets.Get(@"Common\Job\SwitchCharacter", $"{playerIndex}PTopLeft", capture))
                    .Select(region => (PlayerIndex: playerIndex, Region: region)))
                .ToList();
            try
            {
                var matchedPlayers = matches
                    .Select(match => match.PlayerIndex)
                    .Distinct()
                    .OrderBy(playerIndex => playerIndex)
                    .ToArray();
                lastResult = matchedPlayers.Length == 0 ? "无" : string.Join(",", matchedPlayers.Select(index => $"{index}P"));

                if (matchedPlayers.Length == 1 && matchedPlayers[0] <= _multiGamePlayerCount)
                {
                    _playerIndex = matchedPlayers[0];
                    _logger.LogInformation("切换角色：识别到 {PlayerCount} 人联机，当前玩家为 {PlayerIndex}P",
                        _multiGamePlayerCount, _playerIndex);
                    return;
                }

                _logger.LogDebug("切换角色：top_left 当前命中玩家 {Players}，继续等待唯一结果", lastResult);
            }
            finally
            {
                foreach (var match in matches)
                {
                    match.Region.Dispose();
                }
            }

            var remainingMilliseconds = deadline - Environment.TickCount64;
            if (remainingMilliseconds <= 0)
            {
                break;
            }

            await Delay((int)Math.Min(retryIntervalMilliseconds, remainingMilliseconds), ct);
        } while (Environment.TickCount64 < deadline);

        throw new PartySetupFailedException(
            $"切换角色：等待单机图标、联机状态及 top_left 玩家标记超时（{timeoutMilliseconds / 1000} 秒），最后结果：{lastResult}");
    }

    private void ConfigureOperableSlots()
    {
        int[] physicalSlots = (_multiGamePlayerCount, _playerIndex) switch
        {
            (1, _) => [1, 2, 3, 4],
            (2, 1) => [1, 2],
            (2, 2) => [3, 4],
            (3, 1) => [1, 2],
            (3, 2) => [3],
            (3, 3) => [4],
            (4, 1) => [1],
            (4, 2) => [2],
            (4, 3) => [3],
            (4, 4) => [4],
            _ => throw new PartySetupFailedException(
                $"切换角色：无法为 {_multiGamePlayerCount} 人联机的 {_playerIndex}P 生成可控槽位")
        };

        _maxControlAvatarCount = physicalSlots.Length;
        _logicalToPhysicalSlot = physicalSlots
            .Select((physical, index) => (Logical: index + 1, Physical: physical))
            .ToDictionary(pair => pair.Logical, pair => pair.Physical);

        _logger.LogInformation("切换角色：{PlayerCount} 人队伍，{PlayerIndex}P 可控物理槽位 {Slots}",
            _multiGamePlayerCount, _playerIndex, string.Join(",", physicalSlots));
    }

    private void EnsureSlotIsOperable(int logicalSlot)
    {
        if (!_logicalToPhysicalSlot.ContainsKey(logicalSlot))
        {
            throw new PartySetupFailedException($"切换角色：{logicalSlot} 号位超出当前账号可操作角色数 {_maxControlAvatarCount}");
        }
    }

    private void AdjustTargetsToOperableSlots()
    {
        int[] ignoredSlots;
        if (_usePhysicalSlots)
        {
            var logicalByPhysicalSlot = _logicalToPhysicalSlot
                .ToDictionary(pair => pair.Value, pair => pair.Key);
            ignoredSlots = _targetRoles
                .Where(role => !logicalByPhysicalSlot.ContainsKey(role.Slot))
                .Select(role => role.Slot)
                .OrderBy(slot => slot)
                .ToArray();

            _targetRoles = _targetRoles
                .Where(role => logicalByPhysicalSlot.ContainsKey(role.Slot))
                .Select(role => role with { Slot = logicalByPhysicalSlot[role.Slot] })
                .OrderBy(role => role.Slot)
                .ToList();
        }
        else
        {
            ignoredSlots = _targetRoles
                .Where(role => role.Slot > _maxControlAvatarCount)
                .Select(role => role.Slot)
                .OrderBy(slot => slot)
                .ToArray();

            _targetRoles = _targetRoles
                .Where(role => role.Slot <= _maxControlAvatarCount)
                .OrderBy(role => role.Slot)
                .ToList();
        }

        _logger.LogDebug(
            "切换角色：使用 {MappingMode}，保留 {Targets}，忽略输入槽位 {IgnoredSlots}",
            _usePhysicalSlots ? "物理槽位映射" : "可控顺序映射",
            string.Join(",", _targetRoles.Select(role =>
                $"逻辑{role.Slot}->物理{_logicalToPhysicalSlot[role.Slot]}:{role.Name}")),
            ignoredSlots.Length == 0 ? "无" : string.Join(",", ignoredSlots));
    }

    private void ClickFixedTeamSlot(int logicalSlot)
    {
        EnsureSlotIsOperable(logicalSlot);
        var xs = new[] { 470, 800, 1130, 1460 };
        var physicalSlot = _logicalToPhysicalSlot[logicalSlot];
        _logger.LogDebug("切换角色：点击逻辑槽位 {LogicalSlot} 对应的物理槽位 {PhysicalSlot}", logicalSlot, physicalSlot);
        GameCaptureRegion.GameRegion1080PPosClick(xs[physicalSlot - 1], 550);
    }

    /// <summary>
    /// 等待“换下”按钮连续两次消失，确认游戏已提交换下操作。
    /// </summary>
    private async Task WaitForRoleRemoved(int logicalSlot, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + RemoveConfirmationTimeoutMilliseconds;
        var consecutiveMissingCount = 0;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            using var capture = CaptureToRectArea();
            if (IsPartyConfigPage(capture) &&
                !ContainsText(capture, "换下", Rect1080(382, 994, 87, 51)))
            {
                consecutiveMissingCount++;
                if (consecutiveMissingCount >= 2)
                {
                    _logger.LogDebug("切换角色：已确认逻辑槽位 {Slot} 的换下操作生效", logicalSlot);
                    return;
                }
            }
            else
            {
                consecutiveMissingCount = 0;
            }

            await Delay(150, ct);
        }

        throw new PartySetupFailedException(
            $"切换角色：点击逻辑槽位 {logicalSlot} 的换下按钮后，未在 {RemoveConfirmationTimeoutMilliseconds / 1000} 秒内确认操作生效");
    }

    private async Task<List<TeamSlotSnapshot>> RecognizeTeamSlotsFromCharacterList(
        AvatarGridIconRecognizer recognizer,
        int expectedTeamCount,
        CancellationToken ct)
    {
        ClickFixedTeamSlot(1);
        var listOpened = await NewRetry.WaitForAction(() =>
        {
            using var capture = CaptureToRectArea();
            return IsCharacterList(capture);
        }, ct, 10, 300);
        if (!listOpened)
        {
            throw new PartySetupFailedException("切换角色：点击队伍槽位后未打开角色列表");
        }

        List<string?> characterNames = [];
        var lastDetectedCardCount = 0;
        try
        {
            var gridRoi = Rect1080(
                CharacterGridRoi1080.X,
                CharacterGridRoi1080.Y,
                CharacterGridRoi1080.Width,
                CharacterGridRoi1080.Height);
            for (var attempt = 1; attempt <= EmptyCardDetectionRetryCount; attempt++)
            {
                using var capture = CaptureToRectArea(true);
                using var gridRegion = capture.DeriveCrop(gridRoi);
                var cards = DetectCharacterCards(
                    gridRegion.SrcMat, out var rejectedCount, out var connectedComponentCount);
                lastDetectedCardCount = cards.Count;
                LogCardDetection(cards, rejectedCount, connectedComponentCount, attempt);
                if (cards.Count < expectedTeamCount)
                {
                    if (attempt < EmptyCardDetectionRetryCount)
                    {
                        await Delay(200, ct);
                        continue;
                    }

                    if (cards.Count == 0)
                    {
                        throw new PartySetupFailedException("切换角色：连续 3 次未检测到合法角色卡片");
                    }
                }

                foreach (var card in cards
                             .OrderBy(card => card.CardRect.Y)
                             .ThenBy(card => card.CardRect.X)
                             .Take(expectedTeamCount))
                {
                    using var avatar = gridRegion.SrcMat.SubMat(card.AvatarRect);
                    var candidate = recognizer.Recognize(avatar);
                    _logger.LogDebug(
                        "切换角色：RECT({X},{Y},{Width},{Height})，角色={CharacterName}，score={Score:0.000}",
                        card.CardRect.X,
                        card.CardRect.Y,
                        card.CardRect.Width,
                        card.CardRect.Height,
                        candidate.CharacterName,
                        candidate.Score);
                    characterNames.Add(candidate.Score >= MatchThreshold ? candidate.CharacterName : null);
                }

                break;
            }
        }
        finally
        {
            Simulation.SendInput.Keyboard.KeyPress(Vanara.PInvoke.User32.VK.VK_ESCAPE);
        }

        var returned = await NewRetry.WaitForAction(() =>
        {
            using var capture = CaptureToRectArea();
            return IsPartyConfigPage(capture);
        }, ct, 10, 300);
        if (!returned)
        {
            throw new PartySetupFailedException("切换角色：角色列表关闭后未返回队伍配置页");
        }

        if (characterNames.Count != expectedTeamCount || characterNames.Any(string.IsNullOrEmpty))
        {
            throw new PartySetupFailedException(
                $"切换角色：角色列表队伍识别不完整，期望 {expectedTeamCount} 个，" +
                $"末次检测到 {lastDetectedCardCount} 张卡片，成功识别 {characterNames.Count(name => name != null)} 个头像");
        }

        var result = characterNames
            .Select((name, index) => new TeamSlotSnapshot(index + 1, name))
            .ToList();
        _logger.LogDebug(
            "切换角色：当前账号可控角色 {ControlCount} 个，生成 {SnapshotCount} 项队伍快照，映射 {SlotMapping}",
            _maxControlAvatarCount,
            result.Count,
            string.Join(",", _logicalToPhysicalSlot
                .OrderBy(pair => pair.Key)
                .Select(pair => $"逻辑{pair.Key}->物理{pair.Value}")));

        return result;
    }

    /// <summary>
    /// 获取筛选面板中的元素选项区域。
    /// </summary>
    /// <returns>元素选项区域，不包含底部筛选标签。</returns>
    private Rect GetElementFilterOptionsRoi()
    {
        return Rect1080(35, 150, 745, 360);
    }

    /// <summary>
    /// 获取筛选面板中的武器选项区域。
    /// </summary>
    /// <returns>武器选项区域，不包含底部筛选标签。</returns>
    private Rect GetWeaponFilterOptionsRoi()
    {
        return Rect1080(35, 560, 745, 280);
    }

    /// <summary>
    /// 在当前筛选后的角色网格中查找目标角色头像并点击加入队伍。
    /// </summary>
    /// <param name="role">目标角色。</param>
    /// <param name="recognizer">头像模型识别器。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>找到并点击目标角色返回 true；遍历结束仍未找到返回 false。</returns>
    private async Task<bool> FindAndClickAvatar(TargetRole role, AvatarGridIconRecognizer recognizer, CancellationToken ct)
    {
        var gridParams = GridParams.Templates[GridScreenName.PartySetupCharacters];
        var scroller = new GridScroller(gridParams, _logger, Simulation.SendInput, ct);
        var gridRoi = Rect1080(
            CharacterGridRoi1080.X,
            CharacterGridRoi1080.Y,
            CharacterGridRoi1080.Width,
            CharacterGridRoi1080.Height);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            for (var attempt = 1; attempt <= EmptyCardDetectionRetryCount; attempt++)
            {
                using var capture = CaptureToRectArea(true);
                using var gridRegion = capture.DeriveCrop(gridRoi);
                var cards = DetectCharacterCards(
                    gridRegion.SrcMat, out var rejectedCount, out var connectedComponentCount);
                LogCardDetection(cards, rejectedCount, connectedComponentCount, attempt);
                if (cards.Count == 0)
                {
                    if (attempt < EmptyCardDetectionRetryCount)
                    {
                        await Delay(200, ct);
                        continue;
                    }

                    throw new PartySetupFailedException("切换角色：连续 3 次未检测到合法角色卡片");
                }

                foreach (var card in cards.OrderBy(card => card.CardRect.Y).ThenBy(card => card.CardRect.X))
                {
                    using var avatar = gridRegion.SrcMat.SubMat(card.AvatarRect);
                    var candidate = recognizer.Recognize(avatar);
                    _logger.LogDebug(
                        "切换角色：RECT({X},{Y},{Width},{Height})，角色={CharacterName}，score={Score:0.000}",
                        card.CardRect.X,
                        card.CardRect.Y,
                        card.CardRect.Width,
                        card.CardRect.Height,
                        candidate.CharacterName,
                        candidate.Score);
                    if (role.Matches(candidate.CharacterName) && candidate.Score >= MatchThreshold)
                    {
                        using var cardRegion = gridRegion.DeriveCrop(card.CardRect);
                        cardRegion.Click();
                        await Delay(300, ct);
                        return true;
                    }
                }

                break;
            }

            if (!await scroller.TryVerticalScollDown((src, _) =>
                DetectCharacterCards(src, out _, out _).Select(card => card.CardRect)))
            {
                return false;
            }
        }
    }

    private List<FixedSizeGridCard> DetectCharacterCards(
        Mat gridMat,
        out int rejectedCount,
        out int connectedComponentCount)
    {
        return FixedSizeGridCardDetector.Detect(
            gridMat,
            _assetScale,
            FixedSizeGridCardLayout.PartySetupCharacters,
            out rejectedCount,
            out connectedComponentCount);
    }

    private void LogCardDetection(
        IReadOnlyCollection<FixedSizeGridCard> cards,
        int rejectedCount,
        int connectedComponentCount,
        int attempt)
    {
        _logger.LogDebug(
            "切换角色：卡片检测：连通域={ConnectedComponentCount}，有效={CardCount}，丢弃={RejectedCount}，次数={Attempt}/{RetryCount}",
            connectedComponentCount,
            cards.Count,
            rejectedCount,
            attempt,
            EmptyCardDetectionRetryCount);
    }

    /// <summary>
    /// 在当前截图中 OCR 查找文本并点击一次。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <param name="text">目标文本。</param>
    /// <param name="roi">识别区域。</param>
    /// <returns>找到并点击文本返回 true。</returns>
    private static bool TryClickText(BvPage page, string text, Rect roi)
    {
        var regions = page.GetByText(text, roi).FindAll();
        try
        {
            var region = regions
                .OrderBy(region => region.Y)
                .ThenBy(region => region.X)
                .FirstOrDefault();
            if (region == null)
            {
                return false;
            }

            region.Click();
            return true;
        }
        finally
        {
            foreach (var region in regions)
            {
                region.Dispose();
            }
        }
    }

    private static bool TryClickAnyText(BvPage page, IReadOnlyCollection<string> texts, Rect roi)
    {
        foreach (var text in texts)
        {
            if (TryClickText(page, text, roi))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断截图指定区域内是否包含目标文本。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <param name="text">目标文本。</param>
    /// <param name="roi">识别区域。</param>
    /// <returns>包含目标文本返回 true。</returns>
    private static bool ContainsText(ImageRegion capture, string text, Rect roi)
    {
        var regions = capture.FindMulti(RecognitionObject.Ocr(roi));
        try
        {
            return regions.Any(region => region.Text.Contains(text, StringComparison.Ordinal));
        }
        finally
        {
            foreach (var region in regions)
            {
                region.Dispose();
            }
        }
    }

    /// <summary>
    /// 将 1080P 基准区域按当前资源缩放比例转换为实际识别区域。
    /// </summary>
    /// <param name="x">1080P 坐标系下的 X。</param>
    /// <param name="y">1080P 坐标系下的 Y。</param>
    /// <param name="width">1080P 坐标系下的宽度。</param>
    /// <param name="height">1080P 坐标系下的高度。</param>
    /// <returns>缩放后的识别区域。</returns>
    private Rect Rect1080(int x, int y, int width, int height)
    {
        return new Rect(
            (int)Math.Round(x * _assetScale),
            (int)Math.Round(y * _assetScale),
            (int)Math.Round(width * _assetScale),
            (int)Math.Round(height * _assetScale));
    }
}
