using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Config;
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
    QuickTeamList, //快速编队角色列表界面
    FilterPanel, //角色筛选面板
    SelectElementFilter, //选择元素筛选项
    SelectWeaponFilter, //选择武器筛选项
    ConfirmFilterPanel, //确认筛选面板
    BuildSwitchPlan, //构建本次切换计划
    ClearSelectedRoles, //取消本次需要替换的已选角色
    PrepareNextRole, //准备下一个目标角色
    OpenFilterPanel, //打开筛选面板
    FindAndClickAvatar, //查找并点击目标头像
    ClearFilter, //清除当前筛选条件
    VerifyRoleInSlot, //确认角色已进入目标槽位
    ClearMisplacedRole, //清理进入非目标槽位的角色
    SaveConfiguration, //保存当前队伍配置
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

    private readonly ILogger<SwitchCharacterStateMachineTask> _logger = App.GetLogger<SwitchCharacterStateMachineTask>();
    private readonly ReturnMainUiTask _returnMainUiTask = new();
    private readonly double _assetScale = TaskContext.Instance().SystemInfo.AssetScale;

    private SwitchCharacterState _workflowState;
    private AvatarGridIconRecognizer? _recognizer;
    private List<TargetRole> _targetRoles = [];
    private List<TeamSlotSnapshot> _initialSlots = [];
    private HashSet<int> _slotsToClear = [];
    private Queue<SelectionPlanItem> _selectionPlan = new();
    private TargetRole? _currentRole;
    private bool _currentRoleIsRefill;
    private int _currentRoleAttempt;
    private bool _currentAvatarFound;
    private bool _clearCombatScenesAfterReturn;
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

    private sealed record TeamSlotSnapshot(int Slot, string? Name, bool IsSelected, Rect? CardRect);

    private sealed record SelectionPlanItem(TargetRole Role, bool IsRefill);

    private sealed record SwitchPlanBuildResult(
        bool Success,
        HashSet<int> SlotsToClear,
        List<SelectionPlanItem> SelectionPlan,
        string? FailureReason);

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
                SwitchCharacterState.QuickTeamList
            ]),
            (SwitchCharacterState.MainUi, [SwitchCharacterState.PartyConfigUnavailablePrompt, SwitchCharacterState.PartyConfigPage]),
            (SwitchCharacterState.PartyConfigUnavailablePrompt, [SwitchCharacterState.MainUi, SwitchCharacterState.PartyConfigPage]),
            (SwitchCharacterState.PartyConfigPage, [SwitchCharacterState.QuickTeamList]),
            (SwitchCharacterState.QuickTeamList, [
                SwitchCharacterState.BuildSwitchPlan,
                SwitchCharacterState.FindAndClickAvatar,
                SwitchCharacterState.SaveConfiguration,
                SwitchCharacterState.ReturnMainUi
            ]),
            (SwitchCharacterState.BuildSwitchPlan, [SwitchCharacterState.ClearSelectedRoles, SwitchCharacterState.ReturnMainUi]),
            (SwitchCharacterState.ClearSelectedRoles, [SwitchCharacterState.PrepareNextRole]),
            (SwitchCharacterState.PrepareNextRole, [SwitchCharacterState.OpenFilterPanel, SwitchCharacterState.SaveConfiguration]),
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
            (SwitchCharacterState.FindAndClickAvatar, [SwitchCharacterState.ClearFilter]),
            (SwitchCharacterState.ClearFilter, [
                SwitchCharacterState.VerifyRoleInSlot,
                SwitchCharacterState.PrepareNextRole,
                SwitchCharacterState.ReturnMainUi
            ]),
            (SwitchCharacterState.VerifyRoleInSlot, [
                SwitchCharacterState.PrepareNextRole,
                SwitchCharacterState.ClearMisplacedRole,
                SwitchCharacterState.ReturnMainUi
            ]),
            (SwitchCharacterState.ClearMisplacedRole, [
                SwitchCharacterState.OpenFilterPanel,
                SwitchCharacterState.PrepareNextRole,
                SwitchCharacterState.ReturnMainUi
            ]),
            (SwitchCharacterState.SaveConfiguration, [SwitchCharacterState.ReturnMainUi]),
            (SwitchCharacterState.ReturnMainUi, [SwitchCharacterState.Completed])
        );
    }

    /// <summary>
    /// 按槽位切换当前队伍角色。
    /// </summary>
    /// <param name="slot1">1 号槽位角色名。</param>
    /// <param name="slot2">2 号槽位角色名。</param>
    /// <param name="slot3">3 号槽位角色名。</param>
    /// <param name="slot4">4 号槽位角色名。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完成保存并返回主界面返回 true；参数无效、目标角色未找到或流程失败返回 false。</returns>
    /// <remarks>slot1-slot4 均需传入字符串；空字符串或空白字符串表示跳过对应槽位。</remarks>
    public async Task<bool> Start(string slot1, string slot2, string slot3, string slot4, CancellationToken ct)
    {
        Initialize(ct, SwitchCharacterState.Unknown);
        var page = new BvPage(ct);
        string[] slots = [slot1, slot2, slot3, slot4];

        var roles = ParseRoles(slots);
        if (roles.Count == 0 || HasConflictingRoleTargets(roles))
        {
            throw new PartySetupFailedException("切换角色：未指定角色或同一实际角色被指定到多个槽位");
        }

        ResetWorkflow(roles);
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
    private void ResetWorkflow(IReadOnlyList<TargetRole> roles)
    {
        _workflowState = SwitchCharacterState.BuildSwitchPlan;
        _targetRoles = roles.ToList();
        _initialSlots = [];
        _slotsToClear = [];
        _selectionPlan = new Queue<SelectionPlanItem>();
        _currentRole = null;
        _currentRoleIsRefill = false;
        _currentRoleAttempt = 0;
        _currentAvatarFound = false;
        _clearCombatScenesAfterReturn = false;
        _pendingFilterElementType = null;
        _pendingFilterWeaponType = null;
    }

    /// <summary>
    /// 设置当前待处理角色，并初始化筛选条件。
    /// </summary>
    /// <param name="role">待处理角色。</param>
    /// <param name="isRefill">是否为补位角色。</param>
    private void SetCurrentRole(TargetRole role, bool isRefill)
    {
        _currentRole = role;
        _currentRoleIsRefill = isRefill;
        _currentRoleAttempt = 1;
        _currentAvatarFound = false;
        SetCurrentRoleFilter(role);
    }

    /// <summary>
    /// 设置当前角色所需的筛选条件。
    /// </summary>
    /// <param name="role">待处理角色。</param>
    private void SetCurrentRoleFilter(TargetRole role)
    {
        _pendingFilterElementType = role.SkipElementFilter ? null : Recognizer.GetElementType(role.PrimaryCandidateName);
        _pendingFilterWeaponType = role.ForcedWeaponType ?? Recognizer.GetWeaponName(role.PrimaryCandidateName);
        _logger.LogInformation("切换角色：{Slot}. {Name}，武器：{Weapon}，元素筛选：{ElementFilter}",
            role.Slot,
            role.Name,
            _pendingFilterWeaponType,
            _pendingFilterElementType ?? "跳过");
    }

    /// <summary>
    /// 当前角色失败时决定跳过补位或中止目标流程。
    /// </summary>
    /// <param name="message">日志消息。</param>
    /// <param name="exception">可选异常。</param>
    /// <returns>状态处理结果。</returns>
    private StateHandlerResult SkipRefillOrAbortTarget(string message, Exception? exception = null)
    {
        _pendingFilterElementType = null;
        _pendingFilterWeaponType = null;

        if (_currentRoleIsRefill)
        {
            if (exception == null)
            {
                _logger.LogWarning("{Message}，跳过补位角色 {Name}", message, _currentRole?.Name);
            }
            else
            {
                _logger.LogWarning(exception, "{Message}，跳过补位角色 {Name}", message, _currentRole?.Name);
            }

            _currentRole = null;
            _workflowState = SwitchCharacterState.PrepareNextRole;
            return StateHandlerResult.Success;
        }

        throw new PartySetupFailedException(exception == null ? message : $"{message}，{exception.Message}");
    }

    #region 状态检测器

    /// <summary>
    /// 检测构建切换计划状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>快速编队列表中等待构建计划时返回 true。</returns>
    [StateDetector(SwitchCharacterState.BuildSwitchPlan, Order = 11)]
    private bool DetectBuildSwitchPlan(ImageRegion capture)
    {
        return IsWorkflowQuickTeamState(capture, SwitchCharacterState.BuildSwitchPlan);
    }

    /// <summary>
    /// 检测取消已选角色状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>快速编队列表中等待取消选择时返回 true。</returns>
    [StateDetector(SwitchCharacterState.ClearSelectedRoles, Order = 12)]
    private bool DetectClearSelectedRoles(ImageRegion capture)
    {
        return IsWorkflowQuickTeamState(capture, SwitchCharacterState.ClearSelectedRoles);
    }

    /// <summary>
    /// 检测准备目标角色状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>快速编队列表中等待准备目标角色时返回 true。</returns>
    [StateDetector(SwitchCharacterState.PrepareNextRole, Order = 13)]
    private bool DetectPrepareNextRole(ImageRegion capture)
    {
        return IsWorkflowQuickTeamState(capture, SwitchCharacterState.PrepareNextRole)
               && !IsFilterApplied(capture);
    }

    /// <summary>
    /// 检测打开筛选面板状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>快速编队列表中等待打开筛选面板时返回 true。</returns>
    [StateDetector(SwitchCharacterState.OpenFilterPanel, Order = 14)]
    private bool DetectOpenFilterPanel(ImageRegion capture)
    {
        return IsWorkflowQuickTeamState(capture, SwitchCharacterState.OpenFilterPanel);
    }

    /// <summary>
    /// 检测头像查找状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>快速编队列表中等待查找头像时返回 true。</returns>
    [StateDetector(SwitchCharacterState.FindAndClickAvatar, Order = 15)]
    private bool DetectFindAndClickAvatar(ImageRegion capture)
    {
        return IsWorkflowQuickTeamState(capture, SwitchCharacterState.FindAndClickAvatar);
    }

    /// <summary>
    /// 检测清除筛选状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>快速编队列表中等待清除筛选时返回 true。</returns>
    [StateDetector(SwitchCharacterState.ClearFilter, Order = 16)]
    private bool DetectClearFilter(ImageRegion capture)
    {
        return IsWorkflowQuickTeamState(capture, SwitchCharacterState.ClearFilter);
    }

    /// <summary>
    /// 检测槽位确认状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>快速编队列表中等待确认槽位时返回 true。</returns>
    [StateDetector(SwitchCharacterState.VerifyRoleInSlot, Order = 17)]
    private bool DetectVerifyRoleInSlot(ImageRegion capture)
    {
        return IsWorkflowQuickTeamState(capture, SwitchCharacterState.VerifyRoleInSlot)
               && !IsFilterApplied(capture);
    }

    /// <summary>
    /// 检测误选清理状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>快速编队列表中等待清理误选角色时返回 true。</returns>
    [StateDetector(SwitchCharacterState.ClearMisplacedRole, Order = 18)]
    private bool DetectClearMisplacedRole(ImageRegion capture)
    {
        return IsWorkflowQuickTeamState(capture, SwitchCharacterState.ClearMisplacedRole)
               && !IsFilterApplied(capture);
    }

    /// <summary>
    /// 检测保存配置状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>快速编队列表中等待保存配置时返回 true。</returns>
    [StateDetector(SwitchCharacterState.SaveConfiguration, Order = 20)]
    private bool DetectSaveConfiguration(ImageRegion capture)
    {
        return IsWorkflowQuickTeamState(capture, SwitchCharacterState.SaveConfiguration)
               && !IsFilterApplied(capture);
    }

    /// <summary>
    /// 检测返回主界面状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>当前工作流需要返回主界面时返回 true。</returns>
    [StateDetector(SwitchCharacterState.ReturnMainUi, Order = 21)]
    private bool DetectReturnMainUi(ImageRegion capture)
    {
        if (_workflowState == SwitchCharacterState.SaveConfiguration)
        {
            return !IsQuickTeamList(capture) && (IsPartyConfigPage(capture) || Bv.IsInMainUi(capture));
        }

        return _workflowState == SwitchCharacterState.ReturnMainUi
               && (IsQuickTeamList(capture) || IsFilterPanel(capture) || IsPartyConfigPage(capture) || Bv.IsInMainUi(capture));
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
        if (!IsFilterPanel(capture))
        {
            return false;
        }

        return _workflowState == SwitchCharacterState.SelectWeaponFilter
               || (_workflowState == SwitchCharacterState.SelectElementFilter
                   && IsFilterTagSelected(capture, _pendingFilterElementType));
    }

    /// <summary>
    /// 检测确认筛选面板状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>武器筛选标签已出现、正在确认筛选面板或确认后面板尚未关闭时返回 true。</returns>
    [StateDetector(SwitchCharacterState.ConfirmFilterPanel, Order = 24)]
    private bool DetectConfirmFilterPanel(ImageRegion capture)
    {
        if (!IsFilterPanel(capture))
        {
            return false;
        }

        return _workflowState == SwitchCharacterState.ConfirmFilterPanel
               || (_workflowState == SwitchCharacterState.SelectWeaponFilter
                   && IsFilterTagSelected(capture, _pendingFilterWeaponType))
               || (CurrentState == SwitchCharacterState.ConfirmFilterPanel
                   && _workflowState == SwitchCharacterState.FindAndClickAvatar);
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
    /// 检测快速编队角色列表。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>识别到元素共鸣文字返回 true；确认筛选完成后才允许回到角色列表。</returns>
    [StateDetector(SwitchCharacterState.QuickTeamList, Order = 40)]
    private bool DetectQuickTeamList(ImageRegion capture)
    {
        if (!IsQuickTeamList(capture))
        {
            return false;
        }

        if (CurrentState == SwitchCharacterState.ConfirmFilterPanel)
        {
            return _workflowState == SwitchCharacterState.FindAndClickAvatar && !IsFilterPanel(capture);
        }

        return true;
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
    /// 判断截图是否为快速编队角色列表。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>识别到元素共鸣文字返回 true。</returns>
    private bool IsQuickTeamList(ImageRegion capture)
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

    /// <summary>
    /// 判断筛选面板底部是否已出现指定筛选标签。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <param name="text">筛选标签文本。</param>
    /// <returns>筛选标签已出现返回 true。</returns>
    private bool IsFilterTagSelected(ImageRegion capture, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var ocrTexts = OcrFilterTags(capture);
        var matched = ocrTexts.Any(ocrText => ocrText.Contains(text, StringComparison.Ordinal));
        _logger.LogDebug("切换角色：筛选标签 OCR=[{OcrTexts}]，目标={Text}，matched={Matched}",
            string.Join("|", ocrTexts),
            text,
            matched);
        return matched;
    }

    /// <summary>
    /// 对底部已选筛选标签做黑字二值化后 OCR，减少半透明标签栏下层文字干扰。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>识别到的筛选标签文本。</returns>
    private List<string> OcrFilterTags(ImageRegion capture)
    {
        using var tagRegion = capture.DeriveCrop(GetFilterTagsRoi());
        using var hsv = tagRegion.SrcMat.CvtColor(ColorConversionCodes.BGR2HSV);
        using var darkMask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 0), new Scalar(180, 95, 120), darkMask);

        using var binary = new Mat(tagRegion.SrcMat.Size(), MatType.CV_8UC3, Scalar.White);
        binary.SetTo(Scalar.Black, darkMask);

        var result = OcrFactory.Paddle.OcrResult(binary);
        return result.Regions
            .OrderBy(region => region.Rect.Center.Y)
            .ThenBy(region => region.Rect.Center.X)
            .Select(region => region.Text)
            .ToList();
    }

    /// <summary>
    /// 判断当前角色列表是否仍应用了筛选条件。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <returns>识别到清除按钮返回 true。</returns>
    private bool IsFilterApplied(ImageRegion capture)
    {
        return ContainsText(capture, "清除", Rect1080(699, 922, 55, 31));
    }

    /// <summary>
    /// 判断当前截图是否可执行指定快速编队业务状态。
    /// </summary>
    /// <param name="capture">当前截图。</param>
    /// <param name="state">期望业务状态。</param>
    /// <returns>内部状态匹配且位于快速编队列表时返回 true。</returns>
    private bool IsWorkflowQuickTeamState(ImageRegion capture, SwitchCharacterState state)
    {
        if (_workflowState != state || !IsQuickTeamList(capture))
        {
            return false;
        }

        if (CurrentState == SwitchCharacterState.ConfirmFilterPanel
            && state == SwitchCharacterState.FindAndClickAvatar)
        {
            return !IsFilterPanel(capture);
        }

        return true;
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
        Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
        await Delay(2000, _ct);
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
    /// <returns>点击快速编队按钮返回 Success；未找到按钮返回 Retry。</returns>
    [StateHandler(SwitchCharacterState.PartyConfigPage, RetryTimeout = 10000, RetryInterval = 500, TransitionTimeout = 6000)]
    private Task<StateHandlerResult> HandlePartyConfigPage(BvPage page)
    {
        if (!TryClickText(page, "快速编队", Rect1080(1294, 1003, 126, 32)))
        {
            _logger.LogWarning("切换角色：未找到快速编队按钮");
            return Task.FromResult(StateHandlerResult.Retry);
        }

        return Task.FromResult(StateHandlerResult.Success);
    }

    /// <summary>
    /// 处理快速编队角色列表。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>当前工作流已有待执行状态时返回 Success。</returns>
    [StateHandler(SwitchCharacterState.QuickTeamList, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 6000)]
    private Task<StateHandlerResult> HandleQuickTeamList(BvPage page)
    {
        return _workflowState switch
        {
            SwitchCharacterState.BuildSwitchPlan
                or SwitchCharacterState.FindAndClickAvatar
                or SwitchCharacterState.SaveConfiguration
                or SwitchCharacterState.ReturnMainUi => Task.FromResult(StateHandlerResult.Success),
            _ => Task.FromResult(StateHandlerResult.Fail)
        };
    }

    /// <summary>
    /// 构建本次切换计划。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>计划构建完成返回 Success。</returns>
    [StateHandler(SwitchCharacterState.BuildSwitchPlan, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private async Task<StateHandlerResult> HandleBuildSwitchPlan(BvPage page)
    {
        _initialSlots = await RecognizeTeamSlots(Recognizer, _ct);
        var plan = BuildSelectionPlan(_targetRoles, _initialSlots);
        if (!plan.Success)
        {
            throw new PartySetupFailedException($"切换角色：{plan.FailureReason}");
        }

        if (plan.SelectionPlan.Count == 0)
        {
            _logger.LogInformation("切换角色：目标角色已在指定槽位");
            _clearCombatScenesAfterReturn = false;
            _workflowState = SwitchCharacterState.ReturnMainUi;
            return StateHandlerResult.Success;
        }

        _slotsToClear = plan.SlotsToClear;
        _selectionPlan = new Queue<SelectionPlanItem>(plan.SelectionPlan);

        _logger.LogInformation("切换角色：需要取消槽位 {SlotsToClear}，选择计划 {Plan}",
            string.Join(",", _slotsToClear.OrderBy(slot => slot)),
            string.Join(",", plan.SelectionPlan.Select(item => $"{item.Role.Slot}.{item.Role.Name}{(item.IsRefill ? "(补位)" : string.Empty)}")));

        _workflowState = SwitchCharacterState.ClearSelectedRoles;
        return StateHandlerResult.Success;
    }

    /// <summary>
    /// 取消本次计划涉及的已选角色。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>取消完成返回 Success。</returns>
    [StateHandler(SwitchCharacterState.ClearSelectedRoles, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private async Task<StateHandlerResult> HandleClearSelectedRoles(BvPage page)
    {
        await ClearSelectedRoles(_initialSlots, _slotsToClear, _ct);
        _workflowState = SwitchCharacterState.PrepareNextRole;
        return StateHandlerResult.Success;
    }

    /// <summary>
    /// 准备下一个目标角色。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>存在计划项时进入筛选流程；计划耗尽时进入保存流程。</returns>
    [StateHandler(SwitchCharacterState.PrepareNextRole, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private Task<StateHandlerResult> HandlePrepareNextRole(BvPage page)
    {
        if (_selectionPlan.Count == 0)
        {
            _currentRole = null;
            _workflowState = SwitchCharacterState.SaveConfiguration;
            return Task.FromResult(StateHandlerResult.Success);
        }

        var item = _selectionPlan.Dequeue();
        SetCurrentRole(item.Role, item.IsRefill);
        _workflowState = SwitchCharacterState.OpenFilterPanel;
        return Task.FromResult(StateHandlerResult.Success);
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
            if (_currentRoleIsRefill)
            {
                _logger.LogWarning("切换角色：补位角色 {Name} 缺少武器筛选项，跳过当前补位", _currentRole?.Name);
                _pendingFilterElementType = null;
                _pendingFilterWeaponType = null;

                if (!TryClickText(page, "确认筛选", Rect1080(360, 999, 128, 40)))
                {
                    return Task.FromResult(StateHandlerResult.Retry);
                }

                _currentRole = null;
                _workflowState = SwitchCharacterState.PrepareNextRole;
                return Task.FromResult(StateHandlerResult.Success);
            }

            return Task.FromResult(SkipRefillOrAbortTarget("切换角色：筛选面板缺少武器筛选项"));
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
    /// <returns>已选择或无需选择元素时进入武器筛选；找不到选项时返回 Retry。</returns>
    [StateHandler(SwitchCharacterState.SelectElementFilter, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private Task<StateHandlerResult> HandleSelectElementFilter(BvPage page)
    {
        if (string.IsNullOrWhiteSpace(_pendingFilterElementType))
        {
            _workflowState = SwitchCharacterState.SelectWeaponFilter;
            return Task.FromResult(StateHandlerResult.Success);
        }

        using var capture = CaptureToRectArea();
        if (IsFilterTagSelected(capture, _pendingFilterElementType))
        {
            _workflowState = SwitchCharacterState.SelectWeaponFilter;
            return Task.FromResult(StateHandlerResult.Success);
        }

        if (!TryClickText(page, _pendingFilterElementType, GetElementFilterOptionsRoi()))
        {
            _logger.LogWarning("切换角色：未找到元素筛选项 {Text}", _pendingFilterElementType);
            return Task.FromResult(StateHandlerResult.Retry);
        }

        return Task.FromResult(StateHandlerResult.Success);
    }

    /// <summary>
    /// 选择武器筛选项。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>已选择武器时进入确认筛选；找不到选项时返回 Retry。</returns>
    [StateHandler(SwitchCharacterState.SelectWeaponFilter, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private Task<StateHandlerResult> HandleSelectWeaponFilter(BvPage page)
    {
        _workflowState = SwitchCharacterState.SelectWeaponFilter;
        if (string.IsNullOrWhiteSpace(_pendingFilterWeaponType))
        {
            return Task.FromResult(SkipRefillOrAbortTarget("切换角色：筛选面板缺少武器筛选项"));
        }

        using var capture = CaptureToRectArea();
        if (IsFilterTagSelected(capture, _pendingFilterWeaponType))
        {
            _workflowState = SwitchCharacterState.ConfirmFilterPanel;
            return Task.FromResult(StateHandlerResult.Success);
        }

        if (!TryClickText(page, _pendingFilterWeaponType, GetWeaponFilterOptionsRoi()))
        {
            _logger.LogWarning("切换角色：未找到武器筛选项 {Text}", _pendingFilterWeaponType);
            return Task.FromResult(StateHandlerResult.Retry);
        }

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
    /// <returns>头像查找完成后进入清除筛选状态。</returns>
    [StateHandler(SwitchCharacterState.FindAndClickAvatar, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private async Task<StateHandlerResult> HandleFindAndClickAvatar(BvPage page)
    {
        if (_currentRole == null)
        {
            throw new PartySetupFailedException("切换角色：当前角色状态为空");
        }

        _currentAvatarFound = await FindAndClickAvatar(_currentRole, Recognizer, _ct);
        _workflowState = SwitchCharacterState.ClearFilter;
        return StateHandlerResult.Success;
    }

    /// <summary>
    /// 清除当前筛选条件。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>根据头像查找结果进入确认、补位或返回主界面状态。</returns>
    [StateHandler(SwitchCharacterState.ClearFilter, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private Task<StateHandlerResult> HandleClearFilter(BvPage page)
    {
        using var capture = CaptureToRectArea();
        if (IsFilterApplied(capture))
        {
            ClearFilter(page);
            _workflowState = SwitchCharacterState.ClearFilter;
            return Task.FromResult(StateHandlerResult.Wait);
        }

        if (_currentRole == null)
        {
            throw new PartySetupFailedException("切换角色：当前角色状态为空");
        }

        if (_currentAvatarFound)
        {
            _workflowState = SwitchCharacterState.VerifyRoleInSlot;
            return Task.FromResult(StateHandlerResult.Success);
        }

        if (_currentRoleIsRefill)
        {
            _logger.LogWarning("切换角色：未找到补位角色 {Name}，保留空位", _currentRole.Name);
            _currentRole = null;
            _workflowState = SwitchCharacterState.PrepareNextRole;
            return Task.FromResult(StateHandlerResult.Success);
        }

        throw new PartySetupFailedException($"切换角色：未找到目标角色 {_currentRole.Name}");
    }

    /// <summary>
    /// 确认当前角色已进入目标槽位。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>确认成功后进入下一角色；失败时重试或结束。</returns>
    [StateHandler(SwitchCharacterState.VerifyRoleInSlot, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private async Task<StateHandlerResult> HandleVerifyRoleInSlot(BvPage page)
    {
        if (_currentRole == null)
        {
            throw new PartySetupFailedException("切换角色：当前角色状态为空");
        }

        if (await WaitForRoleInSlot(_currentRole, Recognizer, _ct))
        {
            _logger.LogInformation("切换角色：{Name} 已进入 {Slot} 号位", _currentRole.Name, _currentRole.Slot);
            _currentRole = null;
            _workflowState = SwitchCharacterState.PrepareNextRole;
            return StateHandlerResult.Success;
        }

        if (_currentRoleIsRefill)
        {
            _logger.LogWarning("切换角色：补位角色 {Name} 未进入槽位 {Slot}，保留空位", _currentRole.Name, _currentRole.Slot);
            _currentRole = null;
            _workflowState = SwitchCharacterState.PrepareNextRole;
            return StateHandlerResult.Success;
        }

        if (_currentRoleAttempt < 2)
        {
            _workflowState = SwitchCharacterState.ClearMisplacedRole;
            return StateHandlerResult.Success;
        }

        throw new PartySetupFailedException($"切换角色：未能将目标角色 {_currentRole.Name} 放入槽位 {_currentRole.Slot}");
    }

    /// <summary>
    /// 清理误进入其他槽位的目标角色。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>清理后重新进入筛选流程。</returns>
    [StateHandler(SwitchCharacterState.ClearMisplacedRole, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private async Task<StateHandlerResult> HandleClearMisplacedRole(BvPage page)
    {
        if (_currentRole == null)
        {
            throw new PartySetupFailedException("切换角色：当前角色状态为空");
        }

        await ClearMisplacedRole(_currentRole, Recognizer, _ct);
        _currentRoleAttempt++;
        SetCurrentRoleFilter(_currentRole);
        _workflowState = SwitchCharacterState.OpenFilterPanel;
        return StateHandlerResult.Success;
    }

    /// <summary>
    /// 保存当前配置。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    /// <returns>点击保存配置返回 Success；未找到按钮返回 Retry。</returns>
    [StateHandler(SwitchCharacterState.SaveConfiguration, RetryTimeout = 12000, RetryInterval = 500, TransitionTimeout = 3000)]
    private Task<StateHandlerResult> HandleSaveConfiguration(BvPage page)
    {
        if (!TryClickText(page, "保存配置", Rect1080(360, 999, 128, 40)))
        {
            _logger.LogWarning("切换角色：未找到保存配置按钮");
            return Task.FromResult(StateHandlerResult.Retry);
        }

        _clearCombatScenesAfterReturn = true;
        return Task.FromResult(StateHandlerResult.Success);
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
    /// 计算本次需要取消选择的槽位。
    /// </summary>
    /// <param name="rolesToSelect">需要重新选择的目标角色。</param>
    /// <param name="currentSlots">当前已选角色快照。</param>
    /// <returns>需要取消选择的槽位集合。</returns>
    private static HashSet<int> GetSlotsToClear(IReadOnlyCollection<TargetRole> rolesToSelect, IReadOnlyCollection<TeamSlotSnapshot> currentSlots)
    {
        HashSet<int> slotsToClear = rolesToSelect.Select(role => role.Slot).ToHashSet();

        foreach (var currentSlot in currentSlots)
        {
            if (currentSlot.Name == null)
            {
                continue;
            }

            foreach (var role in rolesToSelect)
            {
                if (role.Matches(currentSlot.Name) && role.Slot != currentSlot.Slot)
                {
                    slotsToClear.Add(currentSlot.Slot);
                }
            }
        }

        return slotsToClear;
    }

    /// <summary>
    /// 获取本次取消选择前被移出的原队角色，作为后续补位候选。
    /// </summary>
    /// <param name="initialSlots">取消选择前的已选角色快照。</param>
    /// <param name="slotsToClear">本次需要取消选择的槽位。</param>
    /// <param name="roles">目标槽位角色列表。</param>
    /// <returns>补位候选队列。</returns>
    private static Queue<string> GetRefillCandidates(
        IReadOnlyCollection<TeamSlotSnapshot> initialSlots,
        IReadOnlySet<int> slotsToClear,
        IReadOnlyCollection<TargetRole> roles)
    {
        var excludedNames = roles.SelectMany(role => role.ConflictNames).ToHashSet(StringComparer.Ordinal);
        Queue<string> candidates = new();

        foreach (var slot in initialSlots.OrderBy(slot => slot.Slot))
        {
            if (!slotsToClear.Contains(slot.Slot)
                || string.IsNullOrEmpty(slot.Name)
                || excludedNames.Contains(slot.Name))
            {
                continue;
            }

            candidates.Enqueue(slot.Name);
            excludedNames.Add(slot.Name);
        }

        return candidates;
    }

    /// <summary>
    /// 构建按槽位从小到大执行的角色选择计划。
    /// </summary>
    /// <param name="roles">目标槽位角色列表。</param>
    /// <param name="initialSlots">当前已选角色快照。</param>
    /// <returns>清空槽位和选择计划；前置空槽无法补齐时返回失败。</returns>
    private static SwitchPlanBuildResult BuildSelectionPlan(
        IReadOnlyCollection<TargetRole> roles,
        IReadOnlyCollection<TeamSlotSnapshot> initialSlots)
    {
        var rolesToSelect = GetRolesToSelect(roles, initialSlots);
        if (rolesToSelect.Count == 0)
        {
            return new SwitchPlanBuildResult(true, [], [], null);
        }

        HashSet<int> slotsToClear = GetSlotsToClear(rolesToSelect, initialSlots);
        Queue<string> refillCandidates = GetRefillCandidates(initialSlots, slotsToClear, roles);
        var targetBySlot = rolesToSelect.ToDictionary(role => role.Slot);
        var slotOccupants = Enumerable.Range(1, 4)
            .ToDictionary(
                slot => slot,
                slot => slotsToClear.Contains(slot)
                    ? null
                    : initialSlots.FirstOrDefault(snapshot => snapshot.Slot == slot)?.Name);

        List<SelectionPlanItem> plan = [];
        int maxTargetSlot = rolesToSelect.Max(role => role.Slot);
        int maxAffectedSlot = Math.Max(maxTargetSlot, slotsToClear.Max());
        for (int slot = 1; slot <= maxAffectedSlot; slot++)
        {
            if (targetBySlot.TryGetValue(slot, out var targetRole))
            {
                plan.Add(new SelectionPlanItem(targetRole, IsRefill: false));
                slotOccupants[slot] = targetRole.Name;
                continue;
            }

            if (!string.IsNullOrEmpty(slotOccupants[slot]))
            {
                continue;
            }

            bool shouldRefill = slotsToClear.Contains(slot)
                                || targetBySlot.Keys.Any(targetSlot => targetSlot > slot)
                                || slotsToClear.Any(slotToClear => slotToClear > slot);
            if (!shouldRefill)
            {
                continue;
            }

            if (!refillCandidates.TryDequeue(out var refillName))
            {
                string failureReason = $"目标槽位 {maxTargetSlot} 前存在无法补齐的空槽 {slot}，请同时指定前置槽位";
                return new SwitchPlanBuildResult(false, slotsToClear, plan, failureReason);
            }

            var refillRole = CreateTargetRole(slot, refillName);
            plan.Add(new SelectionPlanItem(refillRole, IsRefill: true));
            slotOccupants[slot] = refillRole.Name;
        }

        return new SwitchPlanBuildResult(true, slotsToClear, plan, null);
    }

    /// <summary>
    /// 取消本次需要替换的已选角色。
    /// </summary>
    /// <param name="currentSlots">当前已选角色快照。</param>
    /// <param name="slotsToClear">需要取消选择的槽位。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task ClearSelectedRoles(
        IEnumerable<TeamSlotSnapshot> currentSlots,
        IReadOnlySet<int> slotsToClear,
        CancellationToken ct)
    {
        foreach (var slot in currentSlots
                     .Where(slot => slot.IsSelected && slotsToClear.Contains(slot.Slot))
                     .OrderByDescending(slot => slot.Slot))
        {
            ClickTeamSlot(slot);
            await Delay(300, ct);
        }
    }

    /// <summary>
    /// 点击快速编队列表中的已选角色卡片。
    /// </summary>
    /// <param name="slot">已选角色快照。</param>
    private void ClickTeamSlot(TeamSlotSnapshot slot)
    {
        if (!slot.CardRect.HasValue)
        {
            _logger.LogWarning("切换角色：槽位 {Slot} 缺少网格卡片区域，无法点击取消", slot.Slot);
            return;
        }

        var cardRect = slot.CardRect.Value;
        GameCaptureRegion.GameRegionClick((_, _) => (
            cardRect.X + cardRect.Width / 2d,
            cardRect.Y + cardRect.Height / 2d));
    }

    /// <summary>
    /// 识别快速编队列表中 1-4 号已选角色。
    /// </summary>
    /// <param name="recognizer">头像模型识别器。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>当前已选角色快照。</returns>
    private async Task<List<TeamSlotSnapshot>> RecognizeTeamSlots(AvatarGridIconRecognizer recognizer, CancellationToken ct)
    {
        var gridScreen = new GridScreen(GridParams.Templates[GridScreenName.PartySetupCharacters], _logger, ct);
        List<(int X, string? Name, Rect CardRect)> cards = [];

        await foreach ((ImageRegion pageRegion, Rect itemRect) in gridScreen.WithCancellation(ct))
        {
            cards.Add(RecognizeTeamCard(pageRegion, itemRect, recognizer));

            if (cards.Count >= 4)
            {
                break;
            }
        }

        var slots = cards
            .OrderBy(card => card.X)
            .Select((card, index) => new TeamSlotSnapshot(index + 1, card.Name, true, card.CardRect))
            .ToList();

        for (int slot = slots.Count + 1; slot <= 4; slot++)
        {
            slots.Add(new TeamSlotSnapshot(slot, null, false, null));
        }

        return slots;
    }

    /// <summary>
    /// 识别快速编队列表中指定槽位对应的角色。
    /// </summary>
    /// <param name="slot">槽位编号，取值 1-4。</param>
    /// <param name="recognizer">头像模型识别器。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>指定槽位快照。</returns>
    private async Task<TeamSlotSnapshot> RecognizeTeamSlot(int slot, AvatarGridIconRecognizer recognizer, CancellationToken ct)
    {
        return (await RecognizeTeamSlots(recognizer, ct)).FirstOrDefault(snapshot => snapshot.Slot == slot)
               ?? new TeamSlotSnapshot(slot, null, false, null);
    }

    /// <summary>
    /// 从网格卡片中识别角色头像。
    /// </summary>
    /// <param name="pageRegion">当前网格页截图。</param>
    /// <param name="itemRect">当前网格卡片区域。</param>
    /// <param name="recognizer">头像模型识别器。</param>
    /// <returns>角色卡片横坐标、角色名和卡片区域。</returns>
    private (int X, string? Name, Rect CardRect) RecognizeTeamCard(ImageRegion pageRegion, Rect itemRect, AvatarGridIconRecognizer recognizer)
    {
        var cardRect = pageRegion.ConvertPositionToGameCaptureRegion(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height);
        using ImageRegion itemRegion = pageRegion.DeriveCrop(itemRect);
        using Mat icon = itemRegion.SrcMat.GetGridIcon();
        var candidate = recognizer.Recognize(icon);
        if (candidate.Score < MatchThreshold)
        {
            _logger.LogDebug("切换角色：网格卡片 X={X} 识别分数过低，score={Score:0.000}", itemRect.X, candidate.Score);
            return (itemRect.X, null, cardRect);
        }

        _logger.LogDebug("切换角色：网格卡片 X={X} 为 {CharacterName}，score={Score:0.000}",
            itemRect.X,
            candidate.CharacterName,
            candidate.Score);
        return (itemRect.X, candidate.CharacterName, cardRect);
    }

    /// <summary>
    /// 等待指定槽位识别为目标角色。
    /// </summary>
    /// <param name="role">目标角色。</param>
    /// <param name="recognizer">头像模型识别器。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>目标槽位识别为目标角色返回 true。</returns>
    private async Task<bool> WaitForRoleInSlot(TargetRole role, AvatarGridIconRecognizer recognizer, CancellationToken ct)
    {
        return await NewRetry.WaitForAction(async () =>
        {
            var snapshot = await RecognizeTeamSlot(role.Slot, recognizer, ct);
            return role.Matches(snapshot.Name);
        }, ct, 6, 300);
    }

    /// <summary>
    /// 清掉误进入其他槽位的目标角色。
    /// </summary>
    /// <param name="role">目标角色。</param>
    /// <param name="recognizer">头像模型识别器。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task ClearMisplacedRole(TargetRole role, AvatarGridIconRecognizer recognizer, CancellationToken ct)
    {
        var misplacedSlots = (await RecognizeTeamSlots(recognizer, ct))
            .Where(slot => slot.Slot != role.Slot && role.Matches(slot.Name))
            .OrderByDescending(slot => slot.Slot)
            .ToList();

        foreach (var slot in misplacedSlots)
        {
            _logger.LogWarning("切换角色：{Name} 进入了非目标槽位 {Slot}，取消选中后重试", role.Name, slot.Slot);
            ClickTeamSlot(slot);
            await Delay(300, ct);
        }
    }

    /// <summary>
    /// 获取筛选面板底部已选筛选标签区域。
    /// </summary>
    /// <returns>底部筛选标签区域。</returns>
    private Rect GetFilterTagsRoi()
    {
        return Rect1080(35, 910, 745, 55);
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
        var gridScreen = new GridScreen(GridParams.Templates[GridScreenName.PartySetupCharacters], _logger, ct);
        await foreach ((ImageRegion pageRegion, Rect itemRect) in gridScreen.WithCancellation(ct))
        {
            using ImageRegion itemRegion = pageRegion.DeriveCrop(itemRect);
            using Mat icon = itemRegion.SrcMat.GetGridIcon();
            var candidate = recognizer.Recognize(icon);
            _logger.LogDebug("切换角色：识别头像 {CharacterName}，score={Score:0.000}", candidate.CharacterName, candidate.Score);
            if (role.Matches(candidate.CharacterName) && candidate.Score >= MatchThreshold)
            {
                itemRegion.Click();
                await Delay(300, ct);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 尝试清除当前筛选条件，为下一个目标角色恢复完整角色列表。
    /// </summary>
    /// <param name="page">页面操作对象。</param>
    private void ClearFilter(BvPage page)
    {
        if (!TryClickText(page, "清除", Rect1080(699, 922, 55, 31)))
        {
            _logger.LogDebug("切换角色：未找到清除筛选按钮，继续执行");
        }
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
