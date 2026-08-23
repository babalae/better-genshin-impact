using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.StateMachine;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.CharacterDevelopment;

/// <summary>
/// 单个角色的信息识别结果。
/// </summary>
public sealed class CharacterDevelopmentResult
{
    /// <summary>调用方传入并规范化后的角色名称。</summary>
    public string CharacterName { get; set; } = string.Empty;

    /// <summary>角色元素类型；旅行者和奇偶由模型识别，其余角色来自头像原型表。</summary>
    public string? ElementType { get; set; }

    /// <summary>角色当前等级。</summary>
    public int? Level { get; set; }

    /// <summary>角色当前等级上限。</summary>
    public int? LevelLimit { get; set; }

    /// <summary>经物品原型表编辑距离纠错后的标准武器名称。</summary>
    public string? WeaponName { get; set; }

    /// <summary>武器当前等级。</summary>
    public int? WeaponLevel { get; set; }

    /// <summary>武器当前等级上限。</summary>
    public int? WeaponLevelLimit { get; set; }

    /// <summary>普通攻击的界面显示等级。</summary>
    public int? AttackLevel { get; set; }

    /// <summary>普通攻击是否显示命座带来的固定等级加成。</summary>
    public bool? AttackHasBonus { get; set; }

    /// <summary>元素战技的界面显示等级。</summary>
    public int? SkillLevel { get; set; }

    /// <summary>元素战技是否显示“天赋等级+3”。</summary>
    public bool? SkillHasBonus { get; set; }

    /// <summary>元素爆发的界面显示等级。</summary>
    public int? BurstLevel { get; set; }

    /// <summary>元素爆发是否显示“天赋等级+3”。</summary>
    public bool? BurstHasBonus { get; set; }
}

/// <summary>
/// 可选的信息分类。未请求分类对应的结果字段保持 <see langword="null"/>。
/// </summary>
[Flags]
internal enum CharacterDevelopmentCategory
{
    None = 0,
    Attribute = 1,
    Weapon = 1 << 1,
    Talent = 1 << 2,
    All = Attribute | Weapon | Talent
}

/// <summary>
/// 角色养成识别任务入口。
/// </summary>
public sealed class CharacterDevelopmentTask
{
    /// <summary>
    /// 获取单个角色的信息。
    /// </summary>
    /// <param name="characterName">目标角色名或已有别名。</param>
    /// <param name="categories">使用分号分隔的分类：属性、武器、天赋；null 表示全部。</param>
    public async Task<CharacterDevelopmentResult> GetCharacter(string characterName, string? categories = null)
    {
        var normalizedName = NormalizeCharacterName(characterName);
        var categoryFlags = ParseCategories(categories);
        var results = await new CharacterDevelopmentStateMachineTask([normalizedName], categoryFlags)
            .Start(CancellationContext.Instance.Cts.Token);
        return results.Single();
    }

    /// <summary>
    /// 获取多个角色的信息。兼容 C# 字符串集合以及实现 <see cref="IList"/> 的 ClearScript JS Array。
    /// </summary>
    /// <param name="characterNames">目标角色名集合。</param>
    /// <param name="categories">使用分号分隔的分类：属性、武器、天赋；null 表示全部。</param>
    public async Task<List<CharacterDevelopmentResult>> GetMultiCharacters(object characterNames, string? categories = null)
    {
        var names = ParseCharacterNames(characterNames);
        var categoryFlags = ParseCategories(categories);
        return await new CharacterDevelopmentStateMachineTask(names, categoryFlags)
            .Start(CancellationContext.Instance.Cts.Token);
    }

    internal static List<string> ParseCharacterNames(object characterNames)
    {
        ArgumentNullException.ThrowIfNull(characterNames);
        if (characterNames is string)
        {
            throw new ArgumentException("多角色接口需要字符串集合或 JS Array，单个字符串请使用 GetCharacter。", nameof(characterNames));
        }

        if (characterNames is not IEnumerable enumerable)
        {
            throw new ArgumentException("角色名参数必须是字符串集合或 JS Array。", nameof(characterNames));
        }

        List<string> names = [];
        foreach (var item in enumerable)
        {
            if (item is not string name)
            {
                throw new ArgumentException("角色名集合中的每个元素都必须是字符串。", nameof(characterNames));
            }

            names.Add(NormalizeCharacterName(name));
        }

        if (names.Count == 0)
        {
            throw new ArgumentException("角色名集合不能为空。", nameof(characterNames));
        }

        return names;
    }

    private static string NormalizeCharacterName(string characterName)
    {
        var name = characterName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("角色名不能为空。", nameof(characterName));
        }

        return name;
    }

    internal static CharacterDevelopmentCategory ParseCategories(string? categories)
    {
        if (categories == null)
        {
            return CharacterDevelopmentCategory.All;
        }

        if (string.IsNullOrWhiteSpace(categories))
        {
            throw new ArgumentException("读取分类不能为空字符串。", nameof(categories));
        }

        CharacterDevelopmentCategory result = CharacterDevelopmentCategory.None;
        foreach (var rawCategory in categories.Split(';', StringSplitOptions.None))
        {
            var category = rawCategory.Trim();
            result |= category switch
            {
                "属性" => CharacterDevelopmentCategory.Attribute,
                "武器" => CharacterDevelopmentCategory.Weapon,
                "天赋" => CharacterDevelopmentCategory.Talent,
                _ => throw new ArgumentException($"未知的角色信息分类：{rawCategory}", nameof(categories))
            };
        }

        return result;
    }
}

internal enum CharacterDevelopmentState
{
    Unknown,
    MainUi,
    OpenCharacterList,
    OpenFilterPanel,
    FilterPanel,
    SelectElementFilter,
    SelectWeaponFilter,
    ConfirmFilterPanel,
    FindAndClickAvatar,
    SelectedCharacter,
    SwitchCategory,
    PrepareNextCharacter,
    ReadCategory,
    FindTalentEntries,
    OpenTalent,
    ReadTalent,
    ReturnMainUi,
    Completed
}

/// <summary>
/// 在一次角色界面会话中完成一个或多个角色的信息读取，并在结束时统一返回主界面。
/// </summary>
/// <remarks>
/// 多角色流程会复用角色界面和头像列表；切换下一个角色前必须先回到“属性”页，
/// 因为角色选择成功后的名称确认区域只在该页布局下稳定可用。
/// </remarks>
internal sealed class CharacterDevelopmentStateMachineTask : StateMachineBase<CharacterDevelopmentState, BvPage>
{
    private const string AttackTalentType = "普通攻击";
    private const string SkillTalentType = "元素战技";
    private const string BurstTalentType = "元素爆发";
    // 最终返回的数据要求解析结果连续三帧一致；十次采样仍不稳定则终止当前任务。
    private const int MaxOcrAttempts = 10;
    private const int RequiredStableOcrCount = 3;
    private const int OcrRetryDelayMilliseconds = 200;

    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex TalentBonusRegex = new(@"天赋\s*等级\s*[+＋]\s*3", RegexOptions.Compiled);

    private readonly ILogger<CharacterDevelopmentStateMachineTask> _logger = App.GetLogger<CharacterDevelopmentStateMachineTask>();
    private readonly ReturnMainUiTask _returnMainUiTask = new();
    private readonly CharacterDevelopmentCategory _categories;
    private readonly List<CharacterSelectionTarget> _targets;
    private readonly List<CharacterDevelopmentResult> _results = [];
    private readonly double _assetScale = TaskContext.Instance().SystemInfo.AssetScale;
    private readonly CharacterDevelopmentAssets _assets;

    private CharacterDevelopmentState _workflowState = CharacterDevelopmentState.OpenCharacterList;
    private AvatarGridIconRecognizer? _recognizer;
    private int _characterIndex;
    private CharacterSelectionTarget? _currentTarget;
    private CharacterDevelopmentResult? _currentResult;
    private Queue<CharacterDevelopmentCategory> _pendingCategories = new();
    private CharacterDevelopmentCategory _currentCategory;
    private string? _pendingElementType;
    private string? _pendingWeaponType;
    private List<Point> _talentPoints = [];
    private int _talentIndex;
    private readonly HashSet<string> _readTalentTypes = new(StringComparer.Ordinal);

    protected override ILogger Logger => _logger;

    private AvatarGridIconRecognizer Recognizer =>
        _recognizer ?? throw new InvalidOperationException("角色养成识别：头像识别器尚未初始化。");

    private CharacterSelectionTarget CurrentTarget =>
        _currentTarget ?? throw new InvalidOperationException("角色养成识别：当前角色尚未初始化。");

    private CharacterDevelopmentResult CurrentResult =>
        _currentResult ?? throw new InvalidOperationException("角色养成识别：当前结果尚未初始化。");

    public CharacterDevelopmentStateMachineTask(IReadOnlyList<string> characterNames, CharacterDevelopmentCategory categories)
    {
        if (characterNames.Count == 0)
        {
            throw new ArgumentException("至少需要指定一个角色。", nameof(characterNames));
        }

        if (categories == CharacterDevelopmentCategory.None)
        {
            throw new ArgumentException("至少需要指定一个读取分类。", nameof(categories));
        }

        _categories = categories;
        _targets = characterNames.Select(CharacterSelectionHelper.CreateTarget).ToList();
        var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        _assets = CharacterDevelopmentAssets.Get(captureRect.Width, captureRect.Height);

        RegisterStateMethodsByAttribute();
        RegisterStateTransitions(
            (CharacterDevelopmentState.Unknown, [CharacterDevelopmentState.MainUi, CharacterDevelopmentState.OpenCharacterList]),
            (CharacterDevelopmentState.MainUi, [CharacterDevelopmentState.OpenCharacterList]),
            (CharacterDevelopmentState.OpenCharacterList, [CharacterDevelopmentState.OpenFilterPanel]),
            (CharacterDevelopmentState.OpenFilterPanel, [CharacterDevelopmentState.FilterPanel]),
            (CharacterDevelopmentState.FilterPanel, [CharacterDevelopmentState.SelectElementFilter, CharacterDevelopmentState.SelectWeaponFilter]),
            (CharacterDevelopmentState.SelectElementFilter, [CharacterDevelopmentState.SelectWeaponFilter]),
            (CharacterDevelopmentState.SelectWeaponFilter, [CharacterDevelopmentState.ConfirmFilterPanel]),
            (CharacterDevelopmentState.ConfirmFilterPanel, [CharacterDevelopmentState.FindAndClickAvatar]),
            (CharacterDevelopmentState.FindAndClickAvatar, [CharacterDevelopmentState.SelectedCharacter]),
            (CharacterDevelopmentState.SelectedCharacter, [CharacterDevelopmentState.SwitchCategory]),
            (CharacterDevelopmentState.SwitchCategory, [
                CharacterDevelopmentState.ReadCategory,
                CharacterDevelopmentState.FindTalentEntries,
                CharacterDevelopmentState.PrepareNextCharacter,
                CharacterDevelopmentState.ReturnMainUi
            ]),
            (CharacterDevelopmentState.PrepareNextCharacter, [CharacterDevelopmentState.OpenCharacterList]),
            (CharacterDevelopmentState.ReadCategory, [CharacterDevelopmentState.SwitchCategory]),
            (CharacterDevelopmentState.FindTalentEntries, [CharacterDevelopmentState.OpenTalent]),
            (CharacterDevelopmentState.OpenTalent, [CharacterDevelopmentState.ReadTalent]),
            (CharacterDevelopmentState.ReadTalent, [CharacterDevelopmentState.OpenTalent, CharacterDevelopmentState.SwitchCategory]),
            (CharacterDevelopmentState.ReturnMainUi, [CharacterDevelopmentState.Completed])
        );
    }

    public async Task<List<CharacterDevelopmentResult>> Start(CancellationToken ct)
    {
        Initialize(ct, CharacterDevelopmentState.Unknown);
        using var recognizer = new AvatarGridIconRecognizer();
        _recognizer = recognizer;
        PrepareCharacter(0);

        try
        {
            await RunStateMachineUntil(new BvPage(ct), CharacterDevelopmentState.Completed);
            return _results;
        }
        catch
        {
            if (!ct.IsCancellationRequested)
            {
                try
                {
                    await _returnMainUiTask.Start(ct);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(cleanupException, "角色养成识别失败后返回主界面也失败");
                }
            }

            throw;
        }
        finally
        {
            _recognizer = null;
        }
    }

    private void PrepareCharacter(int index)
    {
        _characterIndex = index;
        _currentTarget = _targets[index];
        _currentResult = new CharacterDevelopmentResult { CharacterName = CurrentTarget.Name };
        _results.Add(CurrentResult);
        _pendingCategories = new Queue<CharacterDevelopmentCategory>(GetOrderedCategories(_categories));
        _currentCategory = CharacterDevelopmentCategory.None;
        _talentPoints = [];
        _talentIndex = 0;
        _readTalentTypes.Clear();

        (_pendingElementType, _pendingWeaponType) = CharacterSelectionHelper.GetFilterTypes(CurrentTarget, Recognizer);
        _logger.LogInformation(
            "角色养成识别：准备角色 {Name}，元素筛选={Element}，武器筛选={Weapon}",
            CurrentTarget.Name,
            _pendingElementType ?? "跳过",
            _pendingWeaponType);
    }

    private static IEnumerable<CharacterDevelopmentCategory> GetOrderedCategories(CharacterDevelopmentCategory categories)
    {
        if (categories.HasFlag(CharacterDevelopmentCategory.Attribute))
        {
            yield return CharacterDevelopmentCategory.Attribute;
        }

        if (categories.HasFlag(CharacterDevelopmentCategory.Weapon))
        {
            yield return CharacterDevelopmentCategory.Weapon;
        }

        if (categories.HasFlag(CharacterDevelopmentCategory.Talent))
        {
            yield return CharacterDevelopmentCategory.Talent;
        }
    }

    #region State detectors

    [StateDetector(CharacterDevelopmentState.OpenCharacterList, Order = 10)]
    private bool DetectOpenCharacterList(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.OpenCharacterList && IsCharacterOverview(capture);
    }

    [StateDetector(CharacterDevelopmentState.OpenFilterPanel, Order = 11)]
    private bool DetectOpenFilterPanel(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.OpenFilterPanel && IsCharacterList(capture);
    }

    [StateDetector(CharacterDevelopmentState.FindAndClickAvatar, Order = 12)]
    private bool DetectFindAndClickAvatar(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.FindAndClickAvatar
               && IsCharacterList(capture)
               && !CharacterSelectionHelper.IsFilterPanel(capture, _assetScale);
    }

    [StateDetector(CharacterDevelopmentState.SelectedCharacter, Order = 13)]
    private bool DetectSelectedCharacter(ImageRegion capture)
    {
        if (_workflowState != CharacterDevelopmentState.SelectedCharacter)
        {
            return false;
        }

        if (CurrentTarget.SkipDisplayNameConfirmation)
        {
            return true;
        }

        var text = OcrText(capture, Rect1080(1466, 131, 244, 38));
        var matched = CurrentTarget.MatchesDisplayText(text);
        return matched;
    }

    [StateDetector(CharacterDevelopmentState.SwitchCategory, Order = 14)]
    private bool DetectSwitchCategory(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.SwitchCategory && IsCharacterOverview(capture);
    }

    [StateDetector(CharacterDevelopmentState.ReturnMainUi, Order = 15)]
    private bool DetectReturnMainUi(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.ReturnMainUi && IsCharacterOverview(capture);
    }

    [StateDetector(CharacterDevelopmentState.PrepareNextCharacter, Order = 16)]
    private bool DetectPrepareNextCharacter(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.PrepareNextCharacter
               && IsCategoryActive(capture, CharacterDevelopmentCategory.Attribute);
    }

    [StateDetector(CharacterDevelopmentState.SelectElementFilter, Order = 20)]
    private bool DetectSelectElementFilter(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.SelectElementFilter
               && CharacterSelectionHelper.IsFilterPanel(capture, _assetScale);
    }

    [StateDetector(CharacterDevelopmentState.SelectWeaponFilter, Order = 21)]
    private bool DetectSelectWeaponFilter(ImageRegion capture)
    {
        if (!CharacterSelectionHelper.IsFilterPanel(capture, _assetScale))
        {
            return false;
        }

        return _workflowState == CharacterDevelopmentState.SelectWeaponFilter
               || (_workflowState == CharacterDevelopmentState.SelectElementFilter
                   && CharacterSelectionHelper.IsFilterTagSelected(
                       capture,
                       _pendingElementType,
                       _assetScale));
    }

    [StateDetector(CharacterDevelopmentState.ConfirmFilterPanel, Order = 22)]
    private bool DetectConfirmFilterPanel(ImageRegion capture)
    {
        if (!CharacterSelectionHelper.IsFilterPanel(capture, _assetScale))
        {
            return false;
        }

        return _workflowState == CharacterDevelopmentState.ConfirmFilterPanel
               || (_workflowState == CharacterDevelopmentState.SelectWeaponFilter
                   && CharacterSelectionHelper.IsFilterTagSelected(
                       capture,
                       _pendingWeaponType,
                       _assetScale));
    }

    [StateDetector(CharacterDevelopmentState.FilterPanel, Order = 23)]
    private bool DetectFilterPanel(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.FilterPanel
               && CharacterSelectionHelper.IsFilterPanel(capture, _assetScale);
    }

    [StateDetector(CharacterDevelopmentState.ReadCategory, Order = 30)]
    private bool DetectReadCategory(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.ReadCategory
               && _currentCategory is not CharacterDevelopmentCategory.None and not CharacterDevelopmentCategory.Talent
               && IsCategoryActive(capture, _currentCategory);
    }

    [StateDetector(CharacterDevelopmentState.FindTalentEntries, Order = 31)]
    private bool DetectFindTalentEntries(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.FindTalentEntries
               && IsCategoryActive(capture, CharacterDevelopmentCategory.Talent)
               && TryFindTalentPoints(capture, out _);
    }

    [StateDetector(CharacterDevelopmentState.OpenTalent, Order = 32)]
    private bool DetectOpenTalent(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.OpenTalent
               && (IsTalentDetail(capture) || IsCategoryActive(capture, CharacterDevelopmentCategory.Talent));
    }

    [StateDetector(CharacterDevelopmentState.ReadTalent, Order = 33)]
    private bool DetectReadTalent(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.ReadTalent
               && IsTalentDetail(capture);
    }

    [StateDetector(CharacterDevelopmentState.Completed, Order = 80)]
    private bool DetectCompleted(ImageRegion capture)
    {
        return _workflowState == CharacterDevelopmentState.Completed && Bv.IsInMainUi(capture);
    }

    [StateDetector(CharacterDevelopmentState.MainUi, Order = 90)]
    private bool DetectMainUi(ImageRegion capture)
    {
        return Bv.IsInMainUi(capture);
    }

    #endregion

    #region State handlers

    [StateHandler(CharacterDevelopmentState.Unknown, RetryTimes = 3, RetryInterval = 500, TransitionTimeout = 7000)]
    private async Task<StateHandlerResult> HandleUnknown(BvPage page)
    {
        _logger.LogWarning("角色养成识别：当前界面未知，尝试返回主界面");
        await _returnMainUiTask.Start(_ct);
        _workflowState = CharacterDevelopmentState.OpenCharacterList;
        return StateHandlerResult.Success;
    }

    [StateHandler(CharacterDevelopmentState.MainUi, RetryTimeout = 15000, RetryInterval = 500, TransitionTimeout = 7000)]
    private async Task<StateHandlerResult> HandleMainUi(BvPage page)
    {
        Simulation.SendInput.SimulateAction(GIActions.OpenCharacterScreen);
        _workflowState = CharacterDevelopmentState.OpenCharacterList;
        await Delay(500, _ct);
        return StateHandlerResult.Success;
    }

    [StateHandler(CharacterDevelopmentState.OpenCharacterList, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 5000)]
    private Task<StateHandlerResult> HandleOpenCharacterList(BvPage page)
    {
        if (!TryClickTemplate(_assets.MenuRo))
        {
            return Task.FromResult(StateHandlerResult.Retry);
        }

        _workflowState = CharacterDevelopmentState.OpenFilterPanel;
        return Task.FromResult(StateHandlerResult.Success);
    }

    [StateHandler(CharacterDevelopmentState.OpenFilterPanel, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 5000)]
    private Task<StateHandlerResult> HandleOpenFilterPanel(BvPage page)
    {
        if (!TryClickTemplate(_assets.FilterRo))
        {
            return Task.FromResult(StateHandlerResult.Retry);
        }

        _workflowState = CharacterDevelopmentState.FilterPanel;
        return Task.FromResult(StateHandlerResult.Success);
    }

    [StateHandler(CharacterDevelopmentState.FilterPanel, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 5000)]
    private Task<StateHandlerResult> HandleFilterPanel(BvPage page)
    {
        using var capture = CaptureToRectArea();
        if (CharacterSelectionHelper.IsFilterApplied(capture, _assetScale))
        {
            CharacterSelectionHelper.ClearFilter(page, _assetScale, _logger);
            return Task.FromResult(StateHandlerResult.Wait);
        }

        if (string.IsNullOrWhiteSpace(_pendingWeaponType))
        {
            throw new InvalidOperationException($"角色 {CurrentTarget.Name} 缺少武器筛选类型。");
        }

        _workflowState = string.IsNullOrWhiteSpace(_pendingElementType)
            ? CharacterDevelopmentState.SelectWeaponFilter
            : CharacterDevelopmentState.SelectElementFilter;
        return Task.FromResult(StateHandlerResult.Success);
    }

    [StateHandler(CharacterDevelopmentState.SelectElementFilter, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private Task<StateHandlerResult> HandleSelectElementFilter(BvPage page)
    {
        if (string.IsNullOrWhiteSpace(_pendingElementType))
        {
            _workflowState = CharacterDevelopmentState.SelectWeaponFilter;
            return Task.FromResult(StateHandlerResult.Success);
        }

        using var capture = CaptureToRectArea();
        if (CharacterSelectionHelper.IsFilterTagSelected(
                capture,
                _pendingElementType,
                _assetScale))
        {
            _workflowState = CharacterDevelopmentState.SelectWeaponFilter;
            return Task.FromResult(StateHandlerResult.Success);
        }

        if (!CharacterSelectionHelper.TryClickText(
                page,
                _pendingElementType,
                CharacterSelectionHelper.GetElementFilterOptionsRoi(_assetScale)))
        {
            _logger.LogDebug("角色养成识别：未找到元素筛选项 {Element}", _pendingElementType);
            return Task.FromResult(StateHandlerResult.Retry);
        }

        return Task.FromResult(StateHandlerResult.Success);
    }

    [StateHandler(CharacterDevelopmentState.SelectWeaponFilter, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private Task<StateHandlerResult> HandleSelectWeaponFilter(BvPage page)
    {
        _workflowState = CharacterDevelopmentState.SelectWeaponFilter;
        if (string.IsNullOrWhiteSpace(_pendingWeaponType))
        {
            throw new InvalidOperationException($"角色 {CurrentTarget.Name} 缺少武器筛选类型。");
        }

        using var capture = CaptureToRectArea();
        if (CharacterSelectionHelper.IsFilterTagSelected(
                capture,
                _pendingWeaponType,
                _assetScale))
        {
            _workflowState = CharacterDevelopmentState.ConfirmFilterPanel;
            return Task.FromResult(StateHandlerResult.Success);
        }

        if (!CharacterSelectionHelper.TryClickText(
                page,
                _pendingWeaponType,
                CharacterSelectionHelper.GetWeaponFilterOptionsRoi(_assetScale)))
        {
            _logger.LogDebug("角色养成识别：未找到武器筛选项 {Weapon}", _pendingWeaponType);
            return Task.FromResult(StateHandlerResult.Retry);
        }

        return Task.FromResult(StateHandlerResult.Success);
    }

    [StateHandler(CharacterDevelopmentState.ConfirmFilterPanel, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 6000)]
    private async Task<StateHandlerResult> HandleConfirmFilterPanel(BvPage page)
    {
        _workflowState = CharacterDevelopmentState.ConfirmFilterPanel;
        if (!CharacterSelectionHelper.TryClickText(
                page,
                "确认筛选",
                CharacterSelectionHelper.GetConfirmFilterRoi(_assetScale)))
        {
            return StateHandlerResult.Retry;
        }

        await Delay(500, _ct);
        _workflowState = CharacterDevelopmentState.FindAndClickAvatar;
        return StateHandlerResult.Success;
    }

    [StateHandler(CharacterDevelopmentState.FindAndClickAvatar, RetryTimeout = 30000, RetryInterval = 300, TransitionTimeout = 5000)]
    private async Task<StateHandlerResult> HandleFindAndClickAvatar(BvPage page)
    {
        var candidate = await CharacterSelectionHelper.FindAndClickAvatar(
            CurrentTarget,
            Recognizer,
            _assetScale,
            _logger,
            _ct);
        if (candidate == null)
        {
            throw new InvalidOperationException($"未找到目标角色 {CurrentTarget.Name}。");
        }

        CurrentResult.ElementType = candidate.ElementType;

        _workflowState = CharacterDevelopmentState.SelectedCharacter;
        return StateHandlerResult.Success;
    }

    [StateHandler(CharacterDevelopmentState.SelectedCharacter, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 5000)]
    private Task<StateHandlerResult> HandleSelectedCharacter(BvPage page)
    {
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        _workflowState = CharacterDevelopmentState.SwitchCategory;
        return Task.FromResult(StateHandlerResult.Success);
    }

    [StateHandler(CharacterDevelopmentState.SwitchCategory, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 5000)]
    private Task<StateHandlerResult> HandleSwitchCategory(BvPage page)
    {
        if (_pendingCategories.Count == 0)
        {
            if (_characterIndex + 1 < _targets.Count)
            {
                if (!CharacterSelectionHelper.TryClickText(
                        page,
                        GetCategoryText(CharacterDevelopmentCategory.Attribute),
                        GetLeftTabsRoi()))
                {
                    _logger.LogDebug("角色养成识别：切换下一角色前未找到属性标签");
                    return Task.FromResult(StateHandlerResult.Retry);
                }

                _workflowState = CharacterDevelopmentState.PrepareNextCharacter;
            }
            else
            {
                _workflowState = CharacterDevelopmentState.ReturnMainUi;
            }

            return Task.FromResult(StateHandlerResult.Success);
        }

        var category = _pendingCategories.Peek();
        if (!CharacterSelectionHelper.TryClickText(page, GetCategoryText(category), GetLeftTabsRoi()))
        {
            _logger.LogDebug("角色养成识别：未找到标签页 {Category}", GetCategoryText(category));
            return Task.FromResult(StateHandlerResult.Retry);
        }

        _pendingCategories.Dequeue();
        _currentCategory = category;
        _workflowState = category == CharacterDevelopmentCategory.Talent
            ? CharacterDevelopmentState.FindTalentEntries
            : CharacterDevelopmentState.ReadCategory;
        return Task.FromResult(StateHandlerResult.Success);
    }

    [StateHandler(CharacterDevelopmentState.PrepareNextCharacter, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 5000)]
    private Task<StateHandlerResult> HandlePrepareNextCharacter(BvPage page)
    {
        PrepareCharacter(_characterIndex + 1);
        _workflowState = CharacterDevelopmentState.OpenCharacterList;
        return Task.FromResult(StateHandlerResult.Success);
    }

    [StateHandler(CharacterDevelopmentState.ReadCategory, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private async Task<StateHandlerResult> HandleReadCategory(BvPage page)
    {
        switch (_currentCategory)
        {
            case CharacterDevelopmentCategory.Attribute:
                await ReadAttribute();
                break;
            case CharacterDevelopmentCategory.Weapon:
                await ReadWeapon();
                break;
            default:
                throw new InvalidOperationException($"无法读取分类 {_currentCategory}。");
        }

        _workflowState = CharacterDevelopmentState.SwitchCategory;
        return StateHandlerResult.Success;
    }

    [StateHandler(CharacterDevelopmentState.FindTalentEntries, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 3000)]
    private Task<StateHandlerResult> HandleFindTalentEntries(BvPage page)
    {
        using var capture = CaptureToRectArea();
        if (!TryFindTalentPoints(capture, out var points))
        {
            return Task.FromResult(StateHandlerResult.Retry);
        }

        _talentPoints = points;
        _talentIndex = 0;
        _readTalentTypes.Clear();
        _workflowState = CharacterDevelopmentState.OpenTalent;
        return Task.FromResult(StateHandlerResult.Success);
    }

    [StateHandler(CharacterDevelopmentState.OpenTalent, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 5000)]
    private Task<StateHandlerResult> HandleOpenTalent(BvPage page)
    {
        if (_talentIndex < 0 || _talentIndex >= _talentPoints.Count)
        {
            throw new InvalidOperationException("天赋点击位置索引越界。");
        }

        using var capture = CaptureToRectArea();
        var point = _talentPoints[_talentIndex];
        capture.ClickTo(point.X, point.Y);
        _workflowState = CharacterDevelopmentState.ReadTalent;
        return Task.FromResult(StateHandlerResult.Success);
    }

    [StateHandler(CharacterDevelopmentState.ReadTalent, RetryTimeout = 12000, RetryInterval = 300, TransitionTimeout = 5000)]
    private async Task<StateHandlerResult> HandleReadTalent(BvPage page)
    {
        var (type, level, hasBonus) = await ReadTalentDetailWithRetry();

        if (!_readTalentTypes.Add(type))
        {
            return StateHandlerResult.Retry;
        }

        ApplyTalentResult(CurrentResult, type, level, hasBonus);

        _talentIndex++;
        if (_talentIndex < _talentPoints.Count)
        {
            _workflowState = CharacterDevelopmentState.OpenTalent;
        }
        else
        {
            if (!_readTalentTypes.SetEquals([AttackTalentType, SkillTalentType, BurstTalentType]))
            {
                throw new InvalidOperationException("未能完整识别普通攻击、元素战技和元素爆发三个天赋。");
            }

            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            _workflowState = CharacterDevelopmentState.SwitchCategory;
        }

        return StateHandlerResult.Success;
    }

    [StateHandler(CharacterDevelopmentState.ReturnMainUi, RetryTimeout = 15000, RetryInterval = 500, TransitionTimeout = 7000)]
    private async Task<StateHandlerResult> HandleReturnMainUi(BvPage page)
    {
        await _returnMainUiTask.Start(_ct);
        _workflowState = CharacterDevelopmentState.Completed;
        return StateHandlerResult.Success;
    }

    #endregion

    private bool IsCharacterOverview(ImageRegion capture)
    {
        return HasTemplate(capture, _assets.MenuRo);
    }

    private bool IsCharacterList(ImageRegion capture)
    {
        return HasTemplate(capture, _assets.FilterRo);
    }

    private static bool HasTemplate(ImageRegion capture, RecognitionObject recognitionObject)
    {
        using var region = capture.Find(recognitionObject);
        return region.IsExist();
    }

    private bool TryClickTemplate(RecognitionObject recognitionObject)
    {
        using var capture = CaptureToRectArea();
        return TryClickTemplate(capture, recognitionObject);
    }

    private static bool TryClickTemplate(ImageRegion capture, RecognitionObject recognitionObject)
    {
        using var region = capture.Find(recognitionObject);
        if (!region.IsExist())
        {
            return false;
        }

        region.Click();
        return true;
    }

    private bool IsCategoryActive(ImageRegion capture, CharacterDevelopmentCategory category)
    {
        using var leftRegion = capture.DeriveCrop(GetLeftTabsRoi());
        using var hsv = leftRegion.SrcMat.CvtColor(ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 243), new Scalar(30, 19, 249), mask);
        using var binary = mask.CvtColor(ColorConversionCodes.GRAY2BGR);
        var ocrResult = OcrFactory.Paddle.OcrResult(binary);
        var categoryText = GetCategoryText(category);
        var matched = ocrResult.Regions.Any(region => region.Text.Contains(categoryText, StringComparison.Ordinal));
        return matched;
    }

    private bool IsTalentDetail(ImageRegion capture)
    {
        var text = OcrText(capture, Rect1080(100, 219, 125, 33));
        var matched = text.Contains("天赋介绍", StringComparison.Ordinal);
        return matched;
    }

    private async Task ReadAttribute()
    {
        var (level, limit) = await ReadLevelPairWithRetry(Rect1080(1467, 207, 172, 35), "角色等级");
        CurrentResult.Level = level;
        CurrentResult.LevelLimit = limit;
    }

    private async Task ReadWeapon()
    {
        var weaponName = await ReadWeaponNameWithRetry();
        var (level, limit) = await ReadLevelPairWithRetry(Rect1080(1464, 319, 147, 73), "武器等级");
        CurrentResult.WeaponName = weaponName;
        CurrentResult.WeaponLevel = level;
        CurrentResult.WeaponLevelLimit = limit;
    }

    /// <summary>
    /// 重复读取等级文本，直到解析后的“当前等级/等级上限”连续三次一致。
    /// </summary>
    private async Task<(int Level, int Limit)> ReadLevelPairWithRetry(Rect roi, string fieldName)
    {
        var stableValues = new StableValueAccumulator<(int Level, int Limit)>(RequiredStableOcrCount);
        var lastText = string.Empty;
        for (var attempt = 1; attempt <= MaxOcrAttempts; attempt++)
        {
            using var capture = CaptureToRectArea();
            lastText = OcrText(capture, roi);
            if (TryParseLevelPair(lastText, out var level, out var limit))
            {
                var isStable = stableValues.Add((level, limit));
                if (isStable)
                {
                    return (level, limit);
                }
            }
            else
            {
                stableValues.Reset();
                _logger.LogDebug("角色养成识别：{FieldName} OCR 第 {Attempt}/{MaxAttempts} 次解析失败，结果={Text}",
                    fieldName, attempt, MaxOcrAttempts, lastText);
            }

            if (attempt < MaxOcrAttempts)
            {
                await Delay(OcrRetryDelayMilliseconds, _ct);
            }
        }

        throw new InvalidOperationException(
            $"{fieldName} OCR 在 {MaxOcrAttempts} 次内未达到连续 {RequiredStableOcrCount} 次一致，" +
            $"最大连续次数={stableValues.MaxConsecutiveCount}，末次结果={lastText}");
    }

    /// <summary>
    /// 重复 OCR 武器名，并先通过标准武器表纠错；纠错后的名称连续三次一致才返回。
    /// </summary>
    private async Task<string> ReadWeaponNameWithRetry()
    {
        var stableNames = new StableValueAccumulator<string>(RequiredStableOcrCount);
        var lastOcrText = string.Empty;
        var lastMatchedName = string.Empty;
        for (var attempt = 1; attempt <= MaxOcrAttempts; attempt++)
        {
            using var capture = CaptureToRectArea();
            lastOcrText = OcrText(
                capture,
                Rect1080(1465, 132, 346, 42)).Trim();
            if (string.IsNullOrWhiteSpace(lastOcrText))
            {
                stableNames.Reset();
                _logger.LogDebug("角色养成识别：武器名称 OCR 第 {Attempt}/{MaxAttempts} 次结果为空",
                    attempt, MaxOcrAttempts);
            }
            else
            {
                var weaponType = _pendingWeaponType
                                 ?? throw new InvalidOperationException("角色养成识别：当前角色武器类型尚未初始化。");
                var match = WeaponNameMatcher.Match(lastOcrText, weaponType);
                lastMatchedName = match.Name;
                if (!match.IsReliable)
                {
                    stableNames.Reset();
                    _logger.LogDebug(
                        "角色养成识别：武器名称 OCR 第 {Attempt}/{MaxAttempts} 次匹配不可信，原文={OcrText}，候选={MatchedName}，编辑距离={Distance}",
                        attempt, MaxOcrAttempts, lastOcrText, match.Name, match.Distance);
                }
                else
                {
                    var isStable = stableNames.Add(match.Name);
                    if (isStable)
                    {
                        return match.Name;
                    }
                }
            }

            if (attempt < MaxOcrAttempts)
            {
                await Delay(OcrRetryDelayMilliseconds, _ct);
            }
        }

        throw new InvalidOperationException(
            $"武器名称 OCR 在 {MaxOcrAttempts} 次内未达到连续 {RequiredStableOcrCount} 次一致，" +
            $"最大连续次数={stableNames.MaxConsecutiveCount}，末次原文={lastOcrText}，末次匹配={lastMatchedName}");
    }

    private bool TryFindTalentPoints(ImageRegion capture, out List<Point> points)
    {
        var roi = GetTalentListRoi(capture);
        var regions = capture.FindMulti(RecognitionObject.Ocr(roi));
        try
        {
            var candidates = regions
                .Where(region => region.Text.Contains("Lv", StringComparison.OrdinalIgnoreCase))
                .OrderBy(region => region.Y + region.Height / 2)
                .ToList();
            var minDistance = Math.Max(1, (int)Math.Round(30 * _assetScale));
            points = [];
            foreach (var region in candidates)
            {
                var center = new Point(region.X + region.Width / 2, region.Y + region.Height / 2);
                if (points.All(existing => Math.Abs(existing.Y - center.Y) > minDistance))
                {
                    points.Add(center);
                }
            }

            if (points.Count != 3)
            {
                points = [];
                return false;
            }

            points = points.OrderBy(point => point.Y).ToList();
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
    /// 从一帧天赋详情页解析天赋类型、显示等级和命座加成标记。
    /// </summary>
    /// <remarks>三项组成一个不可拆分的稳定性比较单元，不能依赖天赋点击顺序。</remarks>
    private bool TryReadTalentDetail(ImageRegion capture, out string type, out int level, out bool hasBonus)
    {
        type = NormalizeTalentType(OcrText(capture, Rect1080(242, 13, 98, 36)));
        level = 0;
        hasBonus = false;
        if (string.IsNullOrEmpty(type))
        {
            return false;
        }

        var levelText = OcrText(capture, Rect1080(256, 168, 66, 25));
        if (!TryParseTalentLevel(levelText, out level))
        {
            return false;
        }

        var bonusText = OcrText(capture, Rect1080(35, 285, 146, 30));
        hasBonus = HasTalentBonus(bonusText);
        return true;
    }

    /// <summary>
    /// 重复读取天赋详情，直到类型、等级和 HasBonus 三元组连续三次一致。
    /// </summary>
    private async Task<(string Type, int Level, bool HasBonus)> ReadTalentDetailWithRetry()
    {
        var stableValues = new StableValueAccumulator<(string Type, int Level, bool HasBonus)>(RequiredStableOcrCount);
        var lastResult = "<解析失败>";
        for (var attempt = 1; attempt <= MaxOcrAttempts; attempt++)
        {
            using var capture = CaptureToRectArea();
            if (TryReadTalentDetail(capture, out var type, out var level, out var hasBonus))
            {
                var value = (type, level, hasBonus);
                lastResult = $"{type}, Lv.{level}, HasBonus={hasBonus}";
                var isStable = stableValues.Add(value);
                if (isStable)
                {
                    return value;
                }
            }
            else
            {
                stableValues.Reset();
                lastResult = "<解析失败>";
                _logger.LogDebug("角色养成识别：天赋详情 OCR 第 {Attempt}/{MaxAttempts} 次解析失败",
                    attempt, MaxOcrAttempts);
            }

            if (attempt < MaxOcrAttempts)
            {
                await Delay(OcrRetryDelayMilliseconds, _ct);
            }
        }

        throw new InvalidOperationException(
            $"天赋详情 OCR 在 {MaxOcrAttempts} 次内未达到连续 {RequiredStableOcrCount} 次一致，" +
            $"最大连续次数={stableValues.MaxConsecutiveCount}，末次结果={lastResult}");
    }

    internal static string NormalizeTalentType(string text)
    {
        if (text.Contains(AttackTalentType, StringComparison.Ordinal))
        {
            return AttackTalentType;
        }

        if (text.Contains(SkillTalentType, StringComparison.Ordinal))
        {
            return SkillTalentType;
        }

        if (text.Contains(BurstTalentType, StringComparison.Ordinal))
        {
            return BurstTalentType;
        }

        return string.Empty;
    }

    internal static bool TryParseTalentLevel(string text, out int level)
    {
        level = 0;
        var match = NumberRegex.Match(text);
        return match.Success
               && int.TryParse(match.Value, out level)
               && level > 0;
    }

    internal static bool HasTalentBonus(string text)
    {
        return TalentBonusRegex.IsMatch(text);
    }

    internal static void ApplyTalentResult(CharacterDevelopmentResult result, string type, int level, bool hasBonus)
    {
        ArgumentNullException.ThrowIfNull(result);
        switch (type)
        {
            case AttackTalentType:
                result.AttackLevel = level;
                result.AttackHasBonus = hasBonus;
                break;
            case SkillTalentType:
                result.SkillLevel = level;
                result.SkillHasBonus = hasBonus;
                break;
            case BurstTalentType:
                result.BurstLevel = level;
                result.BurstHasBonus = hasBonus;
                break;
            default:
                throw new InvalidOperationException($"未知天赋类型：{type}");
        }
    }

    internal static (int Level, int Limit) ParseLevelPair(string text, string fieldName)
    {
        if (!TryParseLevelPair(text, out var level, out var limit))
        {
            throw new InvalidOperationException($"无法从 {fieldName} OCR 结果中解析等级：{text}");
        }

        return (level, limit);
    }

    internal static bool TryParseLevelPair(string text, out int level, out int limit)
    {
        level = 0;
        limit = 0;
        var matches = NumberRegex.Matches(text);
        return matches.Count >= 2
               && int.TryParse(matches[0].Value, out level)
               && int.TryParse(matches[1].Value, out limit)
               && level > 0
               && limit > 0;
    }

    private static string OcrText(ImageRegion capture, Rect roi)
    {
        var safeRoi = roi.ClampTo(capture.Width, capture.Height);
        if (safeRoi.Width <= 0 || safeRoi.Height <= 0)
        {
            return string.Empty;
        }

        using var region = capture.DeriveCrop(safeRoi);
        return OcrFactory.Paddle.OcrResult(region.SrcMat).Text.Trim();
    }

    private Rect Rect1080(int x, int y, int width, int height)
    {
        return new Rect(
            (int)Math.Round(x * _assetScale),
            (int)Math.Round(y * _assetScale),
            (int)Math.Round(width * _assetScale),
            (int)Math.Round(height * _assetScale));
    }

    private Rect GetLeftTabsRoi()
    {
        var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        return new Rect(0, 0, (int)(captureRect.Width * 0.2), captureRect.Height);
    }

    private static Rect GetTalentListRoi(ImageRegion capture)
    {
        var x = (int)(capture.Width * 0.8);
        return new Rect(x, 0, capture.Width - x, capture.Height);
    }

    private static string GetCategoryText(CharacterDevelopmentCategory category)
    {
        return category switch
        {
            CharacterDevelopmentCategory.Attribute => "属性",
            CharacterDevelopmentCategory.Weapon => "武器",
            CharacterDevelopmentCategory.Talent => "天赋",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "未知角色信息分类。")
        };
    }
}
