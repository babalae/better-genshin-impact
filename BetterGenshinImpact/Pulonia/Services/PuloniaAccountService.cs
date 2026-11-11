using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 账户应用服务：读写账户目录、实例当前账户与账户进度，删除账户前检查计划与实例设置的引用，并发布变化通知。
/// 所有持久化都委托给 <see cref="PuloniaTaskStore"/>，不直接访问文件；完成记录按文件修改标记缓存。
/// 其他模块按 UID 存放的数据（如狛荷屋记录）通过 <see cref="IPuloniaAccountDataHandler"/> 随 UID 改名、随账户删除，
/// 本服务不依赖这些模块。
/// </summary>
public sealed class PuloniaAccountService : IPuloniaAccountService
{
    /// <summary>账户目录、实例设置与计划的存储入口。</summary>
    private readonly PuloniaTaskStore _store;

    /// <summary>按 UID 存放数据的其他模块；没有注册时为空。</summary>
    private readonly IReadOnlyList<IPuloniaAccountDataHandler> _dataHandlers;

    /// <summary>记录关联数据改名或删除失败等不阻断保存的问题。</summary>
    private readonly ILogger<PuloniaAccountService> _logger;

    /// <summary>保护 UID 比对结果的读写；报告来自运行线程，清除来自界面线程。</summary>
    private readonly object _uidCheckGate = new();

    /// <summary>本实例最近一次 UID 比对结果。</summary>
    private PuloniaUidCheckReport? _lastUidCheck;

    /// <summary>本实例尚未解除的 UID 不一致。</summary>
    private PuloniaUidCheckReport? _uidMismatch;

    /// <summary>保护 <see cref="_completionCache"/>；只做字典读写，不在锁内等待文件。</summary>
    private readonly object _progressGate = new();

    /// <summary>
    /// 账户 ID → 最近一次读取的完成记录及读取前的文件修改标记；标记不变时直接复用，本进程写入后移除对应项。
    /// </summary>
    private readonly Dictionary<string, (PuloniaFileStamp? Stamp, PuloniaRouteCompletionIndex Index)> _completionCache =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public event EventHandler<PuloniaAccountProgressChangedEventArgs>? AccountProgressChanged;

    /// <inheritdoc />
    public string CurrentInstanceKey { get; }

    /// <inheritdoc />
    public event EventHandler? AccountsChanged;

    /// <inheritdoc />
    public event EventHandler<PuloniaCurrentAccountChangedEventArgs>? CurrentAccountChanged;

    /// <inheritdoc />
    public event EventHandler? UidCheckChanged;

    /// <inheritdoc />
    public PuloniaUidCheckReport? LastUidCheck
    {
        get { lock (_uidCheckGate) return _lastUidCheck; }
    }

    /// <inheritdoc />
    public PuloniaUidCheckReport? UidMismatch
    {
        get { lock (_uidCheckGate) return _uidMismatch; }
    }

    /// <summary>
    /// 建立账户服务。
    /// </summary>
    /// <param name="store">Pulonia 存储</param>
    /// <param name="instanceKeyProvider">当前进程的实例键</param>
    /// <param name="dataHandlers">按 UID 存放数据的其他模块；为空时 UID 修改与删除账户不影响其他模块</param>
    /// <param name="logger">日志；为空时不记录</param>
    public PuloniaAccountService(PuloniaTaskStore store, IPuloniaInstanceKeyProvider instanceKeyProvider,
        IEnumerable<IPuloniaAccountDataHandler>? dataHandlers = null, ILogger<PuloniaAccountService>? logger = null)
    {
        _store = store;
        CurrentInstanceKey = instanceKeyProvider.CurrentInstanceKey;
        _dataHandlers = dataHandlers?.ToArray() ?? [];
        _logger = logger ?? NullLogger<PuloniaAccountService>.Instance;
    }

    /// <inheritdoc />
    public Task<PuloniaGenshinAccountSettings> LoadAccountsAsync(CancellationToken ct = default)
        => _store.LoadAccountSettingsAsync(ct);

    /// <inheritdoc />
    public async Task<PuloniaGenshinAccountSaveResult> SaveAccountsAsync(PuloniaGenshinAccountSettings settings,
        IReadOnlyDictionary<string, (string UserName, string Password)> credentials, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // 1. 找出本次删除的账户、清空 UID 的账户与 UID 有变化的账户，只对它们检查引用
        var previous = await _store.LoadAccountSettingsAsync(ct).ConfigureAwait(false);
        // 重复或缺失的账户由存储层统一报错，这里只按 ID 收集，不因格式问题提前抛出其他异常。
        var next = new Dictionary<string, PuloniaGenshinAccountProfile>(StringComparer.Ordinal);
        foreach (var account in settings.Accounts ?? [])
            if (account?.Id is { } id) next.TryAdd(id, account);
        var removed = previous.Accounts.Where(account => !next.ContainsKey(account.Id)).ToArray();
        var uidCleared = previous.Accounts.Where(account => account.Uid is not null
            && next.TryGetValue(account.Id, out var edited) && string.IsNullOrEmpty(edited.Uid)).ToArray();
        // 原来有 UID、本次改为其他 UID 或清空的账户；空字符串按清空处理（存储层会另行拒绝空字符串）
        var uidChanges = previous.Accounts
            .Select(account => (Account: account, NewUid: next.TryGetValue(account.Id, out var edited)
                ? (string.IsNullOrEmpty(edited.Uid) ? null : edited.Uid)
                : account.Uid))
            .Where(change => change.Account.Uid is not null && change.NewUid != change.Account.Uid)
            .Select(change => new UidChange(change.Account, change.Account.Uid!, change.NewUid))
            .ToArray();
        // 2. 引用检查与保存不在同一把写锁内，检查后到保存前另一实例新增引用的窄竞态不处理，最坏由运行准备报“账户不存在”。
        if (FindOccupancyBlocker(removed) is { } occupancyBlocker)
            throw new InvalidOperationException(occupancyBlocker);
        if (await FindPlanBlockerAsync(removed.Concat(uidCleared).ToArray(), ct).ConfigureAwait(false) is { } planBlocker)
            throw new InvalidOperationException(planBlocker);
        if (await FindInstanceBlockerAsync(removed, ct).ConfigureAwait(false) is { } instanceBlocker)
            throw new InvalidOperationException(instanceBlocker);
        // 3. 其他模块按 UID 存放的数据：UID 修改会使数据无处可放或与已有数据冲突时拒绝，不保存
        if (await FindDataBlockerAsync(uidChanges, ct).ConfigureAwait(false) is { } dataBlocker)
            throw new InvalidOperationException(dataBlocker);
        // 4. 委托存储层校验目录结构、检查修订并按“先存目录、后写凭据”保存
        var result = await _store.SaveAccountSettingsAsync(settings, credentials, ct).ConfigureAwait(false);
        // 5. 目录已保存，关联数据随 UID 改名、随账户删除；失败不回滚目录，作为警告返回给账户页
        var dataWarning = await ApplyDataChangesAsync(uidChanges, removed).ConfigureAwait(false);
        AccountsChanged?.Invoke(this, EventArgs.Empty);
        return dataWarning is null ? result : result with { DataWarning = dataWarning };
    }

    /// <inheritdoc />
    public async Task<string?> CheckAccountRemovalAsync(string accountId, CancellationToken ct = default)
    {
        var accounts = await _store.LoadAccountSettingsAsync(ct).ConfigureAwait(false);
        if (accounts.Accounts.SingleOrDefault(account => account.Id == accountId) is not { } target)
            return null;
        return FindOccupancyBlocker([target])
               ?? await FindPlanBlockerAsync([target], ct).ConfigureAwait(false)
               ?? await FindInstanceBlockerAsync([target], ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IDisposable? TryAcquireAccountOccupancy(string accountId, string? runId, out PuloniaLockOwner? occupant)
        => _store.TryAcquireAccountOccupancy(accountId, new PuloniaLockOwner
        {
            InstanceKey = CurrentInstanceKey, RunId = runId, ProcessId = Environment.ProcessId, StartedAt = DateTimeOffset.UtcNow
        }, out occupant);

    /// <inheritdoc />
    public PuloniaLockOwner? GetAccountOccupant(string accountId) => _store.GetAccountOccupant(accountId);

    /// <inheritdoc />
    public bool IsInstanceOnline(string instanceKey) => instanceKey == CurrentInstanceKey || _store.IsInstanceOnline(instanceKey);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListOtherOnlineInstancesUsingAccountAsync(string accountId, CancellationToken ct = default)
    {
        var instances = await _store.ListInstanceSettingsAsync(ct).ConfigureAwait(false);
        return instances.Items
            .Where(item => item.Settings.CurrentAccountId == accountId)
            .Select(item => PuloniaInstanceKey.TryFromDirectoryName(item.DirectoryName))
            .OfType<string>()
            .Where(key => key != CurrentInstanceKey && _store.IsInstanceOnline(key))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PuloniaAccountPlanUsage>> ListPlansSpecifyingAccountAsync(string accountId,
        CancellationToken ct = default)
    {
        var plans = await _store.ListPlansWithFailuresAsync(ct).ConfigureAwait(false);
        return plans.Items.Where(plan => plan.AccountId == accountId)
            .Select(plan => new PuloniaAccountPlanUsage(plan.Id, plan.Name, plan.Purpose))
            .OrderBy(plan => plan.Purpose).ThenBy(plan => plan.PlanName, StringComparer.CurrentCulture)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<PuloniaAccountRemovalImpact> DescribeAccountRemovalAsync(string accountId, CancellationToken ct = default)
    {
        var settings = await _store.LoadAccountSettingsAsync(ct).ConfigureAwait(false);
        if (settings.Accounts.FirstOrDefault(account => account.Id == accountId) is not { } target)
            return new PuloniaAccountRemovalImpact(0, 0, false, 0, []);
        // 1. Pulonia 自己的数据：完成记录、首领领奖次数、凭据与不再被引用的图片
        var completions = (await ListRouteCompletionsAsync(accountId, ct).ConfigureAwait(false)).Count;
        var bossRewards = await _store.CountBossRewardEntriesAsync(accountId, ct).ConfigureAwait(false);
        var images = _store.ListRemovableAccountImages(target, settings.Accounts).Count;
        // 2. 其他模块按 UID 存放的数据：读取失败时不列出，删除时照常尝试
        var others = new List<string>();
        if (target.Uid is { } uid)
        {
            foreach (var handler in _dataHandlers)
            {
                try
                {
                    if (await handler.HasDataAsync(uid, ct).ConfigureAwait(false))
                        others.Add(handler.DataName);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "读取账户 {Account} 的{Data}失败", accountId, handler.DataName);
                }
            }
        }
        return new PuloniaAccountRemovalImpact(completions, bossRewards, target.CredentialReference is not null, images, others);
    }

    /// <summary>
    /// 要删除的账户正被运行（或测试切换）占用时拒绝：删除会同时删除它的进度目录，正在运行的运行还要写入完成记录。
    /// </summary>
    /// <param name="accounts">将被删除的账户</param>
    /// <returns>拒绝原因；没有阻碍时为 null</returns>
    private string? FindOccupancyBlocker(IReadOnlyCollection<PuloniaGenshinAccountProfile> accounts)
    {
        foreach (var account in accounts)
        {
            if (_store.GetAccountOccupant(account.Id) is { } occupant)
                return $"账户“{account.Name}”正在{DescribeOccupant(occupant)}运行，请等它结束后再删除。";
        }
        return null;
    }

    /// <summary>
    /// 占用者的说明，例如“云原神 · a（运行 run_…）”；文件内容读不出时为“另一个实例”。
    /// </summary>
    /// <param name="occupant">占用者</param>
    internal static string DescribeOccupant(PuloniaLockOwner occupant)
    {
        if (string.IsNullOrEmpty(occupant.InstanceKey))
            return "另一个实例";
        var instance = PuloniaInstanceKey.Describe(occupant.InstanceKey);
        return occupant.RunId is { } runId ? $"{instance}（运行 {runId}）" : instance;
    }

    /// <inheritdoc />
    public async Task<string?> GetCurrentAccountIdAsync(string instanceKey, CancellationToken ct = default)
        => (await _store.LoadInstanceSettingsAsync(instanceKey, ct).ConfigureAwait(false)).CurrentAccountId;

    /// <inheritdoc />
    public async Task<PuloniaGenshinAccountProfile?> GetCurrentAccountAsync(string instanceKey, CancellationToken ct = default)
    {
        if (await GetCurrentAccountIdAsync(instanceKey, ct).ConfigureAwait(false) is not { } accountId)
            return null;
        // 设置引用的账户已不在目录中（例如被手工修改）时同样视为“未确定”，不猜测为其他账户。
        var accounts = await _store.LoadAccountSettingsAsync(ct).ConfigureAwait(false);
        return accounts.Accounts.SingleOrDefault(account => account.Id == accountId);
    }

    /// <inheritdoc />
    public async Task SetCurrentAccountAsync(string instanceKey, string? accountId, CancellationToken ct = default)
    {
        await _store.SaveInstanceCurrentAccountAsync(instanceKey, accountId, ct).ConfigureAwait(false);
        // 本实例当前账户被修改后，之前的比对结果不再描述新的当前账户，一并清除（不一致提示随之解除）。
        if (instanceKey == CurrentInstanceKey)
            ClearUidCheck();
        CurrentAccountChanged?.Invoke(this, new PuloniaCurrentAccountChangedEventArgs(instanceKey, accountId));
    }

    /// <inheritdoc />
    public Task<PuloniaTaskFileListResult<PuloniaInstanceSettingsEntry>> ListInstanceSettingsAsync(CancellationToken ct = default)
        => _store.ListInstanceSettingsAsync(ct);

    /// <inheritdoc />
    public void ReportUidCheck(string accountId, PuloniaTaskUidCheck result, string? observedUid)
    {
        ArgumentException.ThrowIfNullOrEmpty(accountId);
        var report = new PuloniaUidCheckReport(accountId, result,
            result == PuloniaTaskUidCheck.Mismatched ? observedUid : null, DateTimeOffset.UtcNow);
        lock (_uidCheckGate)
        {
            _lastUidCheck = report;
            // 一致时解除不一致；不一致时记下；未比对（读不到 UID）不能说明账户已经对上，保留原来的不一致。
            if (result == PuloniaTaskUidCheck.Matched)
                _uidMismatch = null;
            else if (result == PuloniaTaskUidCheck.Mismatched)
                _uidMismatch = report;
        }
        UidCheckChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PuloniaRouteCompletion>> ListRouteCompletionsAsync(string accountId, CancellationToken ct = default)
        => (await GetRouteCompletionIndexAsync(accountId, ct).ConfigureAwait(false)).Completions;

    /// <inheritdoc />
    public async Task<PuloniaRouteCompletionIndex> GetRouteCompletionIndexAsync(string accountId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(accountId);
        // 1. 先取修改标记再读内容：读取期间文件被替换时，缓存的是新内容配旧标记，下次比较一定重读，不会漏掉变化
        var stamp = _store.GetRouteCompletionsStamp(accountId);
        lock (_progressGate)
        {
            if (_completionCache.TryGetValue(accountId, out var cached) && cached.Stamp == stamp)
                return cached.Index;
        }
        // 2. 标记变化（含其他实例写入）或首次读取：重新读取并建立索引
        var index = stamp is null
            ? PuloniaRouteCompletionIndex.Empty
            : new PuloniaRouteCompletionIndex(await _store.ReadRouteCompletionsAsync(accountId, ct).ConfigureAwait(false));
        lock (_progressGate)
            _completionCache[accountId] = (stamp, index);
        return index;
    }

    /// <inheritdoc />
    public async Task<bool> RecordRouteCompletionAsync(string accountId, PuloniaRouteCompletion completion, CancellationToken ct = default)
    {
        bool written;
        try { written = await _store.PutRouteCompletionAsync(accountId, completion, ct).ConfigureAwait(false); }
        finally { InvalidateCompletions(accountId); }
        if (written)
            AccountProgressChanged?.Invoke(this, new PuloniaAccountProgressChangedEventArgs(accountId));
        return written;
    }

    /// <inheritdoc />
    public async Task<int> ClearRouteCompletionsAsync(string accountId, IReadOnlyCollection<string> routes, CancellationToken ct = default)
    {
        int removed;
        try { removed = await _store.RemoveRouteCompletionsAsync(accountId, routes, ct).ConfigureAwait(false); }
        finally { InvalidateCompletions(accountId); }
        if (removed > 0)
            AccountProgressChanged?.Invoke(this, new PuloniaAccountProgressChangedEventArgs(accountId));
        return removed;
    }

    /// <inheritdoc />
    public Task<int> GetBossRewardCountAsync(string accountId, string planId, string taskId, string resetId,
        CancellationToken ct = default)
        => _store.ReadBossRewardCountAsync(accountId, planId, taskId, resetId, ct);

    /// <inheritdoc />
    public async Task<int> IncrementBossRewardCountAsync(string accountId, string planId, string taskId, string resetId,
        CancellationToken ct = default)
    {
        var count = await _store.IncrementBossRewardCountAsync(accountId, planId, taskId, resetId, ct).ConfigureAwait(false);
        AccountProgressChanged?.Invoke(this, new PuloniaAccountProgressChangedEventArgs(accountId));
        return count;
    }

    /// <summary>本进程写入或清除后移除缓存项（写入失败时也移除，文件可能已部分变化），下次读取按文件重新建立。</summary>
    /// <param name="accountId">账户 ID</param>
    private void InvalidateCompletions(string accountId)
    {
        lock (_progressGate)
            _completionCache.Remove(accountId);
    }

    /// <summary>清除本实例的 UID 比对结果；原本就没有结果时不触发事件。</summary>
    private void ClearUidCheck()
    {
        lock (_uidCheckGate)
        {
            if (_lastUidCheck is null && _uidMismatch is null)
                return;
            _lastUidCheck = null;
            _uidMismatch = null;
        }
        UidCheckChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 检查这些账户是否仍被计划指定；计划指定的账户必须存在且填写 UID。有计划文件读不出时无法确认引用，同样拒绝。
    /// </summary>
    /// <param name="accounts">将被删除或清空 UID 的账户</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝原因；没有阻碍时为 null</returns>
    private async Task<string?> FindPlanBlockerAsync(IReadOnlyCollection<PuloniaGenshinAccountProfile> accounts, CancellationToken ct)
    {
        if (accounts.Count == 0)
            return null;
        var plans = await _store.ListPlansWithFailuresAsync(ct).ConfigureAwait(false);
        if (plans.Failures.Count > 0)
            return "有计划文件无法读取，暂时不能确认账户未被计划指定，请先修复或移走这些文件："
                   + string.Join("；", plans.Failures);
        foreach (var account in accounts)
        {
            var users = plans.Items.Where(plan => plan.AccountId == account.Id).Select(plan => "“" + plan.Name + "”").ToArray();
            if (users.Length > 0)
                return $"计划 {string.Join("、", users)} 指定了账户“{account.Name}”，请先把这些计划改为其他账户或跟随当前账户。";
        }
        return null;
    }

    /// <summary>
    /// 检查要删除的账户是否是某个实例的当前账户；有实例设置读不出时无法确认，同样拒绝。
    /// </summary>
    /// <param name="accounts">将被删除的账户</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝原因；没有阻碍时为 null</returns>
    private async Task<string?> FindInstanceBlockerAsync(IReadOnlyCollection<PuloniaGenshinAccountProfile> accounts, CancellationToken ct)
    {
        if (accounts.Count == 0)
            return null;
        var instances = await _store.ListInstanceSettingsAsync(ct).ConfigureAwait(false);
        if (instances.Failures.Count > 0)
            return "有实例设置文件无法读取，暂时不能确认账户不是某个实例的当前账户，请先修复或移走这些文件："
                   + string.Join("；", instances.Failures);
        foreach (var account in accounts)
        {
            var users = instances.Items.Where(item => item.Settings.CurrentAccountId == account.Id)
                .Select(item => PuloniaInstanceKey.DescribeDirectory(item.DirectoryName)).ToArray();
            if (users.Length > 0)
                return $"账户“{account.Name}”是{string.Join("、", users)}的当前账户，请先把这些实例改为其他账户。";
        }
        return null;
    }

    /// <summary>
    /// 请各关联数据模块检查 UID 修改；任一模块拒绝时返回带账户名称的原因。检查与保存之间的窄竞态不处理，
    /// 保存后改名失败时由 <see cref="ApplyDataChangesAsync"/> 作为警告返回。
    /// </summary>
    /// <param name="changes">原来有 UID、本次改为其他 UID 或清空的账户</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝原因；没有阻碍时为 null</returns>
    private async Task<string?> FindDataBlockerAsync(IReadOnlyCollection<UidChange> changes, CancellationToken ct)
    {
        foreach (var change in changes)
        foreach (var handler in _dataHandlers)
        {
            if (await handler.CheckUidChangeAsync(change.OldUid, change.NewUid, ct).ConfigureAwait(false) is { } reason)
                return $"不能修改账户“{change.Account.Name}”的 UID：{reason}";
        }
        return null;
    }

    /// <summary>
    /// 目录保存成功后，逐个通知关联数据模块：UID 改为新值的账户改名数据，被删除且有 UID 的账户删除数据。
    /// 目录已经保存，这一步不再响应取消；单项失败记录日志并继续处理其他项。
    /// </summary>
    /// <param name="changes">原来有 UID、本次改为其他 UID 或清空的账户；清空的账户没有数据可改名（检查阶段已拒绝有数据的情况）</param>
    /// <param name="removed">本次删除的账户</param>
    /// <returns>失败说明；全部完成时为 null</returns>
    private async Task<string?> ApplyDataChangesAsync(IReadOnlyCollection<UidChange> changes,
        IReadOnlyCollection<PuloniaGenshinAccountProfile> removed)
    {
        if (_dataHandlers.Count == 0)
            return null;
        var failures = new List<string>();
        foreach (var handler in _dataHandlers)
        {
            // 1. UID 改为新值：数据随之改名
            foreach (var change in changes.Where(item => item.NewUid is not null))
            {
                try { await handler.OnUidChangedAsync(change.OldUid, change.NewUid!, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "账户 {Account} 的 UID 已从 {OldUid} 改为 {NewUid}，但{Data}未能随之改名",
                        change.Account.Id, change.OldUid, change.NewUid, handler.DataName);
                    failures.Add($"“{change.Account.Name}”的{handler.DataName}未能从 UID {change.OldUid} 改到 {change.NewUid}：{ex.Message}");
                }
            }
            // 2. 被删除的账户：一并删除它的数据；没有 UID 的账户没有数据
            foreach (var account in removed.Where(item => item.Uid is not null))
            {
                try { await handler.OnAccountRemovedAsync(account.Uid!, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "账户 {Account}（UID {Uid}）已删除，但{Data}未能删除", account.Id, account.Uid, handler.DataName);
                    failures.Add($"“{account.Name}”的{handler.DataName}未能删除：{ex.Message}");
                }
            }
        }
        return failures.Count == 0 ? null : string.Join("；", failures) + "。";
    }

    /// <summary>一次保存中某个账户的 UID 修改。</summary>
    /// <param name="Account">修改前已保存的账户</param>
    /// <param name="OldUid">修改前的完整 UID</param>
    /// <param name="NewUid">修改后的完整 UID；清空时为 null</param>
    private sealed record UidChange(PuloniaGenshinAccountProfile Account, string OldUid, string? NewUid);
}
