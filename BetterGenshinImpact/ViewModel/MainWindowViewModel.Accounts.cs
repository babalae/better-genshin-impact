using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// 主窗口标题栏的 Pulonia 账户部分：本实例的账户徽标（“账户：乙”，点击改为其他账户或管理账户；
/// 当前账户未确定或最近一次 UID 比对不一致时用提示色），UID 不一致时的一次提示，
/// 以及主实例在线子实例列表中各子实例的当前账户。账户目录只有一个账户时这些都不显示。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>账户目录、实例当前账户与 UID 比对结果。</summary>
    private readonly IPuloniaAccountService _puloniaAccounts;

    /// <summary>最近一次读取的账户目录。</summary>
    private IReadOnlyList<PuloniaGenshinAccountProfile> _accountDirectory = [];

    /// <summary>最近一次读取的本实例当前账户 ID；“未确定”时为 null。</summary>
    private string? _currentAccountId;

    /// <summary>账户读取的版本号；较早发起、较晚完成的读取结果被丢弃。</summary>
    private int _accountRefreshVersion;

    /// <summary>已经弹出过提示的 UID 不一致结果，同一结果只提示一次。</summary>
    private PuloniaUidCheckReport? _notifiedUidMismatch;

    /// <summary>收到新的比对结果、尚未检查是否需要提示；下一次读取完成后检查，不因读取版本被替换而漏掉提示。</summary>
    private bool _uidMismatchNotificationPending;

    /// <summary>UID 不一致时建议改为的账户 ID；没有建议时为 null。</summary>
    private string? _suggestedAccountId;

    /// <summary>账户徽标下拉中的账户，选择即设为本实例当前账户。</summary>
    public ObservableCollection<PuloniaAccountChoiceViewModel> BadgeAccounts { get; } = [];

    /// <summary>是否显示账户徽标：账户目录有多个账户时显示。</summary>
    [ObservableProperty] private bool _isAccountBadgeVisible;

    /// <summary>账户徽标文字，如“账户：乙”。</summary>
    [ObservableProperty] private string _accountBadgeText = "";

    /// <summary>账户徽标是否显示提示色：当前账户未确定，或最近一次 UID 比对不一致。</summary>
    [ObservableProperty] private bool _isAccountBadgeWarning;

    /// <summary>账户徽标的悬停说明。</summary>
    [ObservableProperty] private string _accountBadgeToolTip = "";

    /// <summary>账户徽标下拉是否展开。</summary>
    [ObservableProperty] private bool _isAccountMenuOpen;

    /// <summary>UID 不一致且目录中有读到的 UID 对应账户时，下拉顶部的建议，如“游戏中读到尾号 2222，改为乙”。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccountSuggestion))]
    private string? _accountSuggestionText;

    /// <summary>是否显示改为读到的 UID 对应账户的建议。</summary>
    public bool HasAccountSuggestion => AccountSuggestionText is not null;

    /// <summary>UID 不一致提示中“改为该账户”按钮的文字，如“把当前账户改为乙”；没有建议时为空。</summary>
    [ObservableProperty] private string _accountSuggestionButtonText = "";

    /// <summary>是否显示 UID 不一致提示（统筹设计 4.4）；关闭、修改当前账户或下一次比对一致后隐藏。</summary>
    [ObservableProperty] private bool _isUidMismatchNoticeOpen;

    /// <summary>UID 不一致提示的说明，如“本实例当前账户是甲（UID 尾号 1111），游戏中读到尾号 2222。本次仍按甲记录进度。”</summary>
    [ObservableProperty] private string _uidMismatchNoticeText = "";

    /// <summary>关闭 UID 不一致提示；标题栏徽标的提示色保留，直到修改当前账户或比对一致。</summary>
    [RelayCommand]
    private void DismissUidMismatchNotice() => IsUidMismatchNoticeOpen = false;

    /// <summary>订阅账户变化并读取一次；在构造函数末尾调用。</summary>
    private void InitializeAccountBadge()
    {
        _puloniaAccounts.AccountsChanged += OnPuloniaAccountsChanged;
        _puloniaAccounts.CurrentAccountChanged += OnPuloniaCurrentAccountChanged;
        _puloniaAccounts.UidCheckChanged += OnPuloniaUidCheckChanged;
        _ = RefreshAccountBadgeAsync();
    }

    /// <summary>解除账户事件订阅；在释放时调用。</summary>
    private void DisposeAccountBadge()
    {
        _puloniaAccounts.AccountsChanged -= OnPuloniaAccountsChanged;
        _puloniaAccounts.CurrentAccountChanged -= OnPuloniaCurrentAccountChanged;
        _puloniaAccounts.UidCheckChanged -= OnPuloniaUidCheckChanged;
    }

    /// <summary>账户目录在本进程保存后刷新徽标与子实例账户。</summary>
    private void OnPuloniaAccountsChanged(object? sender, EventArgs e) => RunOnUi(() => _ = RefreshAccountBadgeAsync());

    /// <summary>任一实例的当前账户在本进程被修改后刷新徽标与子实例账户。</summary>
    private void OnPuloniaCurrentAccountChanged(object? sender, PuloniaCurrentAccountChangedEventArgs e)
        => RunOnUi(() => _ = RefreshAccountBadgeAsync());

    /// <summary>UID 比对结果变化（来自运行线程）后刷新徽标，新的不一致结果弹出一次提示。</summary>
    private void OnPuloniaUidCheckChanged(object? sender, EventArgs e)
        => RunOnUi(() =>
        {
            _uidMismatchNotificationPending = true;
            _ = RefreshAccountBadgeAsync();
        });

    /// <summary>在线子实例变化后读取各子实例的当前账户与正在运行的计划。</summary>
    partial void OnConnectedChildInstancesChanged(IReadOnlyList<ConnectedInstanceItem> value)
    {
        _ = RefreshChildInstanceAccountsAsync(value);
        // 正在运行的计划与定时读取，见 MainWindowViewModel.Runs.cs
        UpdateChildInstanceRunWatch(value);
    }

    /// <summary>把账户事件切回 UI 线程；退出阶段不再处理。</summary>
    /// <param name="action">在 UI 线程执行的操作</param>
    private void RunOnUi(Action action)
    {
        if (_disposed)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.BeginInvoke(action);
    }

    /// <summary>
    /// 读取账户目录与本实例当前账户，重建徽标和子实例账户，并对新的 UID 不一致结果弹出一次提示；
    /// 读取失败时隐藏徽标，只记录日志。
    /// </summary>
    private async Task RefreshAccountBadgeAsync()
    {
        var version = ++_accountRefreshVersion;
        try
        {
            var settings = await _puloniaAccounts.LoadAccountsAsync();
            var currentId = await _puloniaAccounts.GetCurrentAccountIdAsync(_puloniaAccounts.CurrentInstanceKey);
            if (_disposed || version != _accountRefreshVersion)
            {
                return;
            }

            _accountDirectory = settings.Accounts;
            _currentAccountId = currentId;
            RebuildAccountBadge();
            if (_uidMismatchNotificationPending)
            {
                _uidMismatchNotificationPending = false;
                NotifyUidMismatchOnce();
            }

            await RefreshChildInstanceAccountsAsync(ConnectedChildInstances);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "读取 Pulonia 账户失败，标题栏暂不显示账户");
            if (version == _accountRefreshVersion)
            {
                IsAccountBadgeVisible = false;
            }
        }
    }

    /// <summary>
    /// 按最近一次读取的账户目录、本实例当前账户和 UID 比对结果重建徽标文字、提示色、说明与下拉内容。
    /// </summary>
    private void RebuildAccountBadge()
    {
        // 1. 只有一个账户时不显示徽标（当前账户“未确定”时也不例外，可在账户页顶部重新选择）
        var current = _accountDirectory.FirstOrDefault(account => account.Id == _currentAccountId);
        IsAccountBadgeVisible = _accountDirectory.Count > 1;
        BadgeAccounts.Clear();
        foreach (var account in _accountDirectory)
        {
            var accountId = account.Id;
            BadgeAccounts.Add(new PuloniaAccountChoiceViewModel(account, accountId == _currentAccountId,
                new AsyncRelayCommand(() => SelectCurrentAccountAsync(accountId))));
        }

        // 2. 只考虑针对当前账户的不一致；当前账户改过之后的旧结果不再适用
        var mismatch = _puloniaAccounts.UidMismatch is { } report && report.AccountId == _currentAccountId ? report : null;
        if (current is null)
        {
            AccountBadgeText = "账户：未确定";
            IsAccountBadgeWarning = true;
            AccountBadgeToolTip = "本实例当前账户未确定，跟随当前账户的运行会进入待处理。点击选择游戏中现在登录的账户。";
        }
        else if (mismatch?.ObservedUid is { } observed)
        {
            AccountBadgeText = "账户：" + current.Name;
            IsAccountBadgeWarning = true;
            AccountBadgeToolTip = $"本实例最近一次运行开始时，游戏中读到 UID 尾号 {PuloniaAccountChoiceViewModel.UidTail(observed)}，"
                                  + $"与当前账户“{current.Name}”（UID 尾号 {PuloniaAccountChoiceViewModel.UidTail(current.Uid ?? "")}）不一致。"
                                  + "请确认游戏中登录的账户，必要时修改本实例当前账户。";
        }
        else
        {
            AccountBadgeText = "账户：" + current.Name;
            IsAccountBadgeWarning = false;
            AccountBadgeToolTip = $"本实例当前账户：{PuloniaAccountChoiceViewModel.Describe(current)}。点击改为其他账户或管理账户。";
        }

        // 3. 目录中有读到的 UID 对应的账户时，建议一键改为该账户
        var suggested = mismatch?.ObservedUid is { } uid
            ? _accountDirectory.FirstOrDefault(account => account.Uid == uid && account.Id != _currentAccountId)
            : null;
        _suggestedAccountId = suggested?.Id;
        AccountSuggestionText = suggested is null || mismatch?.ObservedUid is not { } suggestedUid
            ? null
            : $"游戏中读到尾号 {PuloniaAccountChoiceViewModel.UidTail(suggestedUid)}，改为{suggested.Name}";
        AccountSuggestionButtonText = suggested is null ? "" : $"把当前账户改为{suggested.Name}";
        // 不一致已解除（修改了当前账户或比对一致）时，提示随之关闭
        if (mismatch is null)
            IsUidMismatchNoticeOpen = false;
    }

    /// <summary>
    /// 本实例最近一次比对不一致、且尚未提示过时，用通知提示一次（文字按统筹设计 4.4 节）。
    /// </summary>
    private void NotifyUidMismatchOnce()
    {
        if (_puloniaAccounts.LastUidCheck is not { Result: PuloniaTaskUidCheck.Mismatched, ObservedUid: { } observed } report
            || ReferenceEquals(report, _notifiedUidMismatch))
        {
            return;
        }

        _notifiedUidMismatch = report;
        var account = _accountDirectory.FirstOrDefault(item => item.Id == report.AccountId);
        var name = account?.Name ?? "未知账户";
        var expected = account?.Uid is { } uid ? PuloniaAccountChoiceViewModel.UidTail(uid) : "未知";
        // 提示卡片在关闭前一直显示；目录中有读到的 UID 对应的账户时，卡片带“把当前账户改为乙”按钮（统筹设计 4.4）
        UidMismatchNoticeText = $"本实例当前账户是{name}（UID 尾号 {expected}），游戏中读到尾号 {PuloniaAccountChoiceViewModel.UidTail(observed)}。本次仍按{name}记录进度。";
        IsUidMismatchNoticeOpen = true;
    }

    /// <summary>
    /// 读取各在线子实例的当前账户（按实例键读共享设置）；账户目录只有一个账户时不显示账户部分。
    /// </summary>
    /// <param name="items">在线子实例展示项</param>
    private async Task RefreshChildInstanceAccountsAsync(IReadOnlyList<ConnectedInstanceItem> items)
    {
        var showAccounts = _accountDirectory.Count > 1;
        foreach (var item in items)
        {
            if (!showAccounts || item.InstanceKey is not { } instanceKey)
            {
                item.ClearAccount();
                continue;
            }

            try
            {
                // 子实例首次使用时，读取会按“账户列表第一个账户”初始化它的设置，与子实例自己读取的结果相同。
                var accountId = await _puloniaAccounts.GetCurrentAccountIdAsync(instanceKey);
                var current = _accountDirectory.FirstOrDefault(account => account.Id == accountId);
                item.SetAccount(current?.Name ?? "未确定", _accountDirectory.Select(account =>
                {
                    var targetId = account.Id;
                    return new PuloniaAccountChoiceViewModel(account, targetId == accountId,
                        new AsyncRelayCommand(() => SetChildInstanceAccountAsync(item, targetId)));
                }));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning(ex, "读取子实例 {Instance} 的当前账户失败", item.DisplayName);
                item.ClearAccount();
            }
        }
    }

    /// <summary>打开账户徽标下拉。</summary>
    [RelayCommand]
    private void OpenAccountMenu() => IsAccountMenuOpen = true;

    /// <summary>改为 UID 不一致时读到的 UID 对应的账户（标题栏徽标下拉与 UID 不一致提示共用）。</summary>
    [RelayCommand]
    private Task ApplyAccountSuggestionAsync()
    {
        IsUidMismatchNoticeOpen = false;
        return _suggestedAccountId is { } accountId ? SelectCurrentAccountAsync(accountId) : Task.CompletedTask;
    }

    /// <summary>跳转到账户页。</summary>
    [RelayCommand]
    private void ManageAccounts()
    {
        IsAccountMenuOpen = false;
        _navigationService.Navigate(typeof(PuloniaAccountsPage));
    }

    /// <summary>
    /// 把本实例当前账户改为指定账户并立即保存；正在运行的任务不受影响，下一次运行开始时生效。
    /// </summary>
    /// <param name="accountId">新的当前账户 ID</param>
    private async Task SelectCurrentAccountAsync(string accountId)
    {
        IsAccountMenuOpen = false;
        try
        {
            await _puloniaAccounts.SetCurrentAccountAsync(_puloniaAccounts.CurrentInstanceKey, accountId);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "修改本实例当前账户失败");
            await ThemedMessageBox.WarningAsync("未能修改本实例当前账户：" + ex.Message, "修改当前账户");
        }
    }

    /// <summary>
    /// 主实例修改子实例的当前账户：直接写入共享的实例设置文件，不经过 IPC；子实例下一次运行开始时生效。
    /// </summary>
    /// <param name="item">子实例展示项</param>
    /// <param name="accountId">新的当前账户 ID</param>
    private async Task SetChildInstanceAccountAsync(ConnectedInstanceItem item, string accountId)
    {
        if (item.InstanceKey is not { } instanceKey)
        {
            return;
        }

        try
        {
            await _puloniaAccounts.SetCurrentAccountAsync(instanceKey, accountId);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "修改子实例 {Instance} 的当前账户失败", item.DisplayName);
            await ThemedMessageBox.WarningAsync($"未能修改「{item.DisplayName}」的当前账户：{ex.Message}", "修改当前账户");
        }
    }
}
