using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.ViewModel.Resin;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>自动首领讨伐任务的专属设置草稿；对应 auto_boss 的常用参数，沿用旧一条龙卡片布局。</summary>
public partial class AutoBossSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <inheritdoc />
    public override string[] EditedParameterNames =>
    [
        "strategy_name", "combat_strategy_path", "boss_name", "team_name", "return_to_statue_after_each_round",
        "reward_recognition_enabled", "revive_retry_count", "total_run_count_limit", "reward_count_reset_id", "resin_usage"
    ];

    /// <summary>载入时的 strategy_name 原值。</summary>
    private readonly string _originalStrategyNameValue;
    /// <summary>载入时的 combat_strategy_path 原值。</summary>
    private readonly string _originalCombatStrategyPath;
    /// <summary>载入时的战斗策略显示名快照（按任务实际使用的策略换算）。</summary>
    private readonly string _originalStrategyName;
    /// <summary>载入时的首领名快照。</summary>
    private readonly string _originalBossName;
    /// <summary>载入时的队伍名快照。</summary>
    private readonly string _originalTeamName;
    /// <summary>载入时的回雕像开关快照。</summary>
    private readonly bool _originalReturnToStatue;
    /// <summary>载入时的奖励识别快照。</summary>
    private readonly bool _originalRewardRecognitionEnabled;
    /// <summary>载入时的死亡重试次数快照。</summary>
    private readonly int _originalReviveRetryCount;

    // 策略目录与当前值显示由统一的 CombatStrategyPicker 负责。

    /// <summary>本任务使用的战斗策略名称；保存时同时写入 strategy_name 与 combat_strategy_path。</summary>
    [ObservableProperty] private string _strategyName;
    /// <summary>讨伐的首领名称。</summary>
    [ObservableProperty] private string _bossName;
    /// <summary>进入战斗前切换的队伍名称，留空不更换。</summary>
    [ObservableProperty] private string _teamName;
    /// <summary>每轮领奖后是否返回七天神像。</summary>
    [ObservableProperty] private bool _returnToStatueAfterEachRound;
    /// <summary>是否启用奖励名称识别。</summary>
    [ObservableProperty] private bool _rewardRecognitionEnabled;
    /// <summary>角色死亡后的复活重试次数。</summary>
    [ObservableProperty] private int _reviveRetryCount;

    /// <summary>跨次运行累计的成功领奖上限，0 表示不限制。</summary>
    [ObservableProperty] private int _totalRunCountLimit;
    /// <summary>当前草稿的累计批次，清空累计通过生成新批次实现，保存前不写入存储。</summary>
    private string _rewardCountResetId;
    /// <summary>累计统计存储，由 DI 提供；纯草稿测试或新增任务不要求读取统计。</summary>
    private readonly PuloniaTaskStore? _progressStore;
    /// <summary>所属计划的稳定 ID。</summary>
    private string? _progressPlanId;
    /// <summary>所属节点的稳定 ID。</summary>
    private string? _progressTaskId;
    /// <summary>展示各账号累计次数，读取错误保留在界面上。</summary>
    [ObservableProperty] private string _completedRunCountText = "新任务的累计次数为 0。";

    /// <summary>从当前有效参数读取首领讨伐配置。</summary>
    /// <param name="original">当前有效参数快照。</param>
    /// <param name="progressStore">累计次数存储；新增任务或测试可以不提供。</param>
    public AutoBossSettingsViewModel(JObject original, PuloniaTaskStore? progressStore = null) : this(original, progressStore, null) { }

    /// <summary>载入时的树脂快照。</summary>
    private readonly ResinUsage _originalResin;
    /// <summary>本节点的树脂设置入口，不直接改写全局配置。</summary>
    public ResinUsageEntryViewModel Resin { get; }

    /// <summary>注入累计存储与可选树脂弹窗服务。</summary>
    [ActivatorUtilitiesConstructor]
    public AutoBossSettingsViewModel(JObject original, PuloniaTaskStore? progressStore = null, IResinSettingsDialogs? dialogs = null) : base(original)
    {
        _originalResin = ResinUsageViewModelSupport.Read(Original["resin_usage"], AutoBossTask.ResinCapability);
        Resin = new("自动首领讨伐", AutoBossTask.ResinCapability, _originalResin, dialogs);
        TrackEdits([Resin]);
        _progressStore = progressStore;
        _totalRunCountLimit = ReadInt(Original, PuloniaSoloTaskExecutor.BossTotalRunCountLimitParameter);
        _rewardCountResetId = ReadString(Original, PuloniaSoloTaskExecutor.BossRewardCountResetParameter);
        _originalStrategyNameValue = ReadString(Original, "strategy_name");
        _originalCombatStrategyPath = ReadString(Original, "combat_strategy_path");
        // 任务优先使用非空的 combat_strategy_path，为空时才按 strategy_name 生成，显示与之一致
        _originalStrategyName = ToStrategyName(string.IsNullOrWhiteSpace(_originalCombatStrategyPath)
            ? _originalStrategyNameValue
            : _originalCombatStrategyPath);
        _originalBossName = ReadString(Original, "boss_name");
        _originalTeamName = ReadString(Original, "team_name");
        _originalReturnToStatue = ReadBool(Original, "return_to_statue_after_each_round");
        _originalRewardRecognitionEnabled = ReadBool(Original, "reward_recognition_enabled");
        _originalReviveRetryCount = ReadInt(Original, "revive_retry_count", 3);
        _strategyName = _originalStrategyName;
        _bossName = _originalBossName;
        _teamName = _originalTeamName;
        _returnToStatueAfterEachRound = _originalReturnToStatue;
        _rewardRecognitionEnabled = _originalRewardRecognitionEnabled;
        _reviveRetryCount = _originalReviveRetryCount;
    }

    /// <summary>收集首领讨伐配置的真实变化；累计上限与清空批次属于编排参数，已完成次数由存储维护。</summary>
    /// <param name="changes">接收变化的对象。</param>
    public override void CollectChanges(JObject changes)
    {
        if (Resin.Value != _originalResin) changes["resin_usage"] = JObject.Parse(SoloTaskJson.Serialize(Resin.Value));
        // 修改策略时两个字段都写成策略名，避免旧路径继续覆盖新选择
        if (!string.Equals(_originalStrategyName, StrategyName, StringComparison.Ordinal))
        {
            WriteString(changes, "strategy_name", _originalStrategyNameValue, StrategyName);
            WriteString(changes, "combat_strategy_path", _originalCombatStrategyPath, StrategyName);
        }
        WriteString(changes, "boss_name", _originalBossName, BossName);
        WriteString(changes, "team_name", _originalTeamName, TeamName);
        WriteBool(changes, "return_to_statue_after_each_round", _originalReturnToStatue, ReturnToStatueAfterEachRound);
        WriteBool(changes, "reward_recognition_enabled", _originalRewardRecognitionEnabled, RewardRecognitionEnabled);
        WriteInt(changes, "revive_retry_count", _originalReviveRetryCount, ReviveRetryCount);
        WriteInt(changes, PuloniaSoloTaskExecutor.BossTotalRunCountLimitParameter,
            ReadInt(Original, PuloniaSoloTaskExecutor.BossTotalRunCountLimitParameter), TotalRunCountLimit);
        WriteString(changes, PuloniaSoloTaskExecutor.BossRewardCountResetParameter,
            ReadString(Original, PuloniaSoloTaskExecutor.BossRewardCountResetParameter), _rewardCountResetId);
    }

    /// <summary>宿主提供稳定节点身份后异步读取累计；运行统计与设置草稿分别保存。</summary>
    public Task SetProgressScopeAsync(string planId, string taskId)
    {
        _progressPlanId = planId;
        _progressTaskId = taskId;
        return RefreshProgress();
    }

    /// <summary>重新读取各账号累计；草稿已清空时继续显示待保存状态。</summary>
    [RelayCommand]
    private async Task RefreshProgress()
    {
        if (_rewardCountResetId != ReadString(Original, PuloniaSoloTaskExecutor.BossRewardCountResetParameter))
        {
            CompletedRunCountText = "保存设置后清空累计，重新从 0 开始。";
            return;
        }
        if (_progressStore is null || _progressPlanId is null || _progressTaskId is null) return;
        try
        {
            var counts = await _progressStore.ReadBossRewardCountsAsync(_progressPlanId, _progressTaskId, _rewardCountResetId);
            var accounts = await _progressStore.LoadAccountSettingsAsync();
            var names = accounts.Accounts.ToDictionary(account => account.Id, account => account.Name);
            // 读取期间可能点击清空，不能让旧读取结果覆盖清空提示。
            if (_rewardCountResetId != ReadString(Original, PuloniaSoloTaskExecutor.BossRewardCountResetParameter)) return;
            CompletedRunCountText = counts.Count == 0 ? "累计成功领奖：0 次。"
                : string.Join(Environment.NewLine, counts.OrderBy(pair => pair.Key).Select(pair =>
                    $"{(pair.Key.Length == 0 ? "当前游戏" : names.GetValueOrDefault(pair.Key, "账号 " + pair.Key))}：{pair.Value} 次"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException
                                       or PuloniaTaskValidationException or InvalidOperationException)
        {
            CompletedRunCountText = "读取累计失败：" + ex.Message;
        }
    }

    /// <summary>清空全部账号的当前累计，仅改变草稿，取消弹窗不会修改已保存累计。</summary>
    [RelayCommand]
    private void ResetProgress()
    {
        _rewardCountResetId = Guid.NewGuid().ToString("N");
        CompletedRunCountText = "保存设置后清空累计，重新从 0 开始。";
    }
}
