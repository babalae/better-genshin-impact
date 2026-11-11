using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 同一任务服务在运行开始时确定运行账户：跟随当前账户时读取实例当前账户并比对 UID，
/// 指定账户时切换并核验 UID，然后改写实例当前账户。普通计划和一条龙相同，只要计划含启用的游戏节点。
/// </summary>
public sealed partial class PuloniaTaskService
{
    // 切换与核验入口（显式构造的纯任务服务可不注册此游戏依赖）由 _localNodes 持有，运行账户的准备与 UID 读取都经过它。

    /// <summary>账户目录与实例当前账户的应用服务。</summary>
    private readonly IPuloniaAccountService _accounts;

    /// <summary>
    /// 取得游戏输入租约的入口；默认使用游戏任务协调器。单元测试可替换为不触碰游戏的租约，配合假的切号器验证账户流程。
    /// </summary>
    internal Func<CancellationToken, Task<PuloniaGameTaskLease>> GameLeaseFactory
    {
        get => _localNodes.AcquireLeaseAsync;
        init => _localNodes.AcquireLeaseAsync = value;
    }

    /// <summary>
    /// 判断快照是否固定了计划指定的账户：指定账户时为 true，跟随当前账户时为 false。续跑据此决定是否沿用原核验 UID。
    /// </summary>
    /// <param name="snapshot">运行快照。</param>
    internal static bool IsSwitchedAccount(PuloniaTaskSnapshot snapshot) => snapshot.GenshinAccountJson is not null;

    /// <summary>
    /// 运行账户确定后，把请求的服务器偏移改为该账户服务器时区的偏移，路线 CD 的日切按它计算；
    /// 续跑沿用原运行的偏移，不改写。只在本次运行的执行流程中调用。
    /// </summary>
    /// <param name="state">本次运行。</param>
    /// <param name="account">运行账户。</param>
    private void ApplyAccountServerTime(RunState state, PuloniaGenshinAccountProfile account)
    {
        if (state.ResumedFromRunId is not null)
            return;
        var offsetMinutes = PuloniaServerTime.GetUtcOffsetMinutes(account.ServerTimeZone);
        lock (_runsGate)
            state.UseServerUtcOffset(offsetMinutes);
    }

    // 运行总在本实例执行：运行账户取本实例的当前账户，完成记录的 instance 也记本实例（_accounts.CurrentInstanceKey）。

    /// <summary>
    /// 快照是否含启用的游戏节点；只有这样的运行才确定运行账户，只有 Shell、C# 的运行不确定。
    /// </summary>
    /// <param name="snapshot">运行快照。</param>
    private bool RequiresRunAccount(PuloniaTaskSnapshot snapshot)
        => EnumerateLeafTasks(snapshot.RootTask).Any(task => task.IsEnabled
            && _definitions.TryGetValue(task.TaskType, out var definition) && definition.RequiresGameSession);

    /// <summary>
    /// 在执行第一个节点前确定运行账户；实例当前账户在这时读取最新设置，而不是在提交时读取。
    /// 结果（以及指定账户的核验 UID）耐久保存后才能产生业务副作用。
    /// </summary>
    /// <param name="state">本次运行。</param>
    /// <param name="ct">运行取消令牌。</param>
    private async Task PrepareRunAccountAsync(RunState state, CancellationToken ct)
    {
        if (!RequiresRunAccount(state.Snapshot)) return;
        var instanceKey = _accounts.CurrentInstanceKey;
        if (state.Snapshot.GenshinAccountJson is { } accountJson)
            await PrepareSpecifiedAccountAsync(state, accountJson, instanceKey, ct).ConfigureAwait(false);
        else
            await ResolveInstanceCurrentAccountAsync(state, instanceKey, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定账户：沿用切号器准备并核验目标 UID（续跑时核验原运行的 UID），成功后把实例当前账户改为目标账户；
    /// 切换已经开始后失败时把实例当前账户改为“未确定”。
    /// </summary>
    /// <param name="state">本次运行。</param>
    /// <param name="accountJson">快照固定的账户资料。</param>
    /// <param name="instanceKey">执行实例的实例键。</param>
    /// <param name="ct">运行取消令牌。</param>
    private async Task PrepareSpecifiedAccountAsync(RunState state, string accountJson, string instanceKey, CancellationToken ct)
    {
        var account = PuloniaTaskJson.Read<PuloniaGenshinAccountProfile>(accountJson);
        var address = state.Snapshot.RootTask.TaskAddress + "/account";
        lock (_runsGate)
        {
            state.AccountId = account.Id;
            state.AccountSource = PuloniaTaskAccountSource.Specified;
            state.UidCheck = null;
            state.ObservedUid = null;
        }
        // 日切时区取计划指定账户的服务器（快照固定的账户资料），与核验结果一起落盘
        ApplyAccountServerTime(state, account);
        SetCurrentTask(state, address, "正在准备原神账户：“" + account.Name + "”。");
        // 1. 切换之前取得目标账户的占用（4.5）：避免切换后才发现它正在别处运行；失败时直接进入待处理
        AcquireAccountOccupancy(state, account, specified: true);
        // 2. 切换前检查（4.6）：另一个在线实例的当前账户就是目标账户时，切换会把它挤下线。
        //    只有本实例界面手动运行并确认过才继续；真的需要切换时（游戏中不是目标账户）才生效
        var displaced = await ListDisplacedInstancesAsync(account, ct).ConfigureAwait(false);
        var allowDisplace = state.Request.AllowDisplaceOnlineInstance;
        // 3. 在本实例的游戏中切换并核验；续跑时核验历史 UID（由原运行耐久保存）
        var switchStarted = false;
        string uid;
        try
        {
            uid = await _localNodes.PrepareAccountAsync(account, state.VerifiedUid, () =>
                {
                    // 切号器即将离开主界面：此时抛出不会登出当前账户，运行进入待处理
                    if (displaced.Count > 0 && !allowDisplace)
                        throw new PuloniaGenshinAccountException(DescribeDisplacement(displaced, account),
                            new InvalidOperationException());
                    switchStarted = true;
                }, ct)
                .ConfigureAwait(false);
            if (!Regex.IsMatch(uid, "^[0-9]{9,10}$"))
                throw new PuloniaGenshinAccountException("切换结果没有有效的完整 UID。", new InvalidOperationException());
        }
        catch (Exception ex)
        {
            // 运行结果只写入执行记录，这里同时写入日志窗口，避免停止后界面上没有任何说明
            if (ex is PuloniaGenshinAccountException)
                _logger.LogWarning("账户准备失败，未执行后续任务：{Message}", ex.Message);
            // 已离开主界面去登出或登录后失败（含停止）：游戏中登录的账户不再确定，后续跟随当前账户的运行进入待处理
            if (switchStarted)
                await TrySetInstanceCurrentAccountAsync(instanceKey, null).ConfigureAwait(false);
            throw;
        }
        // 4. 核验 UID 与“切换账户”节点结果在同一次写入中落盘，之后的节点才会产生业务副作用
        lock (_runsGate)
        {
            state.VerifiedUid = uid;
            state.NodeResults.Add(new PuloniaTaskNodeResult(address, "切换账户", "genshin.account.prepare", 1,
                PuloniaTaskNodeStatus.Succeeded, "账户已核验，实际 UID：" + uid, state.StartedAt!.Value, DateTimeOffset.UtcNow));
        }
        await PersistCriticalStateAsync().ConfigureAwait(false);
        // 5. 核验成功后实例当前账户就是目标账户
        await TrySetInstanceCurrentAccountAsync(instanceKey, account.Id).ConfigureAwait(false);
        RaiseRunChanged(state.RunId);
    }

    /// <summary>
    /// 跟随当前账户：运行账户就是实例当前账户；“未确定”时进入待处理。续跑要求当前账户与原运行账户相同。
    /// 当前账户填写了 UID 时只读地比对一次，不一致只记录警告，运行照常进行。
    /// </summary>
    /// <param name="state">本次运行。</param>
    /// <param name="instanceKey">执行实例的实例键。</param>
    /// <param name="ct">运行取消令牌。</param>
    private async Task ResolveInstanceCurrentAccountAsync(RunState state, string instanceKey, CancellationToken ct)
    {
        var address = state.Snapshot.RootTask.TaskAddress + "/account";
        SetCurrentTask(state, address, "正在确定运行账户。");
        // 1. 读取最新的实例当前账户；读不出时同样不能确定运行账户
        PuloniaGenshinAccountProfile? current;
        try { current = await _accounts.GetCurrentAccountAsync(instanceKey, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new PuloniaRunAccountException("无法读取实例当前账户，未执行任何任务：" + ex.Message); }
        if (current is null)
            throw new PuloniaRunAccountException("本实例当前账户未确定，请先在标题栏或账户页设置。");

        // 2. 续跑沿用原运行的运行账户：本实例当前账户必须与之相同
        string? originalAccountId;
        lock (_runsGate)
            originalAccountId = state.ResumedFromRunId is not null ? state.AccountId : null;
        if (originalAccountId is not null && originalAccountId != current.Id)
        {
            var original = (await _accounts.LoadAccountsAsync(ct).ConfigureAwait(false)).Accounts
                .FirstOrDefault(account => account.Id == originalAccountId)?.Name ?? originalAccountId;
            throw new PuloniaRunAccountException(
                $"原运行的运行账户是“{original}”，本实例当前账户是“{current.Name}”。续跑沿用原运行账户，请先把本实例当前账户改为“{original}”后再续跑。");
        }

        lock (_runsGate)
        {
            state.AccountId = current.Id;
            state.AccountSource = PuloniaTaskAccountSource.InstanceCurrent;
            state.UidCheck = PuloniaTaskUidCheck.Skipped;
            state.ObservedUid = null;
        }
        // 日切时区取实例当前账户的服务器，随运行账户一起落盘
        ApplyAccountServerTime(state, current);

        // 3. 取得运行账户的占用（4.5）：同一账户正在其他实例运行时直接进入待处理，不等待
        AcquireAccountOccupancy(state, current, specified: false);

        // 4. UID 比对：账户有 UID 时只读地读取一次（不按键，最多约 3 秒）；读不到时记为未比对
        if (current.Uid is { } expected)
        {
            SetCurrentTask(state, address, "正在比对游戏中的 UID。");
            if (await _localNodes.TryReadUidAsync(ct).ConfigureAwait(false) is { } observed)
            {
                var matched = observed == expected;
                lock (_runsGate)
                {
                    state.UidCheck = matched ? PuloniaTaskUidCheck.Matched : PuloniaTaskUidCheck.Mismatched;
                    state.ObservedUid = matched ? null : observed;
                }
                // 遮罩日志显示全部 Information 以上的日志，这条警告会直接出现在日志窗口；
                // 本实例的通知、标题栏徽标提示色与一键修改由界面订阅账户服务的比对结果完成。
                if (!matched)
                    _logger.LogWarning("{Warning}", $"本实例当前账户是“{current.Name}”（UID 尾号 {UidTail(expected)}），"
                        + $"游戏中读到尾号 {UidTail(observed)}。本次仍按“{current.Name}”记录进度。");
            }
        }
        // 5. 比对结果（含未比对）报告给账户服务，只在内存中
        PuloniaTaskUidCheck check;
        string? observedUid;
        lock (_runsGate)
        {
            check = state.UidCheck ?? PuloniaTaskUidCheck.Skipped;
            observedUid = state.ObservedUid;
        }
        _accounts.ReportUidCheck(current.Id, check, observedUid);
        await PersistCriticalStateAsync().ConfigureAwait(false);
        RaiseRunChanged(state.RunId);
    }

    /// <summary>
    /// 尽力改写实例当前账户；失败只记录日志，不影响本次运行（下一次运行开始时会重新读取设置）。
    /// </summary>
    /// <param name="instanceKey">实例键。</param>
    /// <param name="accountId">新的当前账户；为空表示“未确定”。</param>
    private async Task TrySetInstanceCurrentAccountAsync(string instanceKey, string? accountId)
    {
        // 使用独立时限：切换失败或停止时运行令牌可能已经取消，仍要把“未确定”写下。
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await _accounts.SetCurrentAccountAsync(instanceKey, accountId, cleanup.Token).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "无法把实例 {InstanceKey} 的当前账户改为 {AccountId}", instanceKey, accountId ?? "未确定");
        }
    }

    /// <summary>UID 尾号（最后 4 位），用于提示文字。</summary>
    /// <param name="uid">完整 UID。</param>
    private static string UidTail(string uid) => uid.Length <= 4 ? uid : uid[^4..];

    /// <summary>
    /// 取得运行账户的占用（统筹设计 4.5），句柄保存在运行状态中，执行流程结束时释放。
    /// 已被占用时不等待、不排队，运行进入待处理（A7）；占用文件无法建立时同样不执行任何任务。
    /// </summary>
    /// <param name="state">本次运行。</param>
    /// <param name="account">运行账户。</param>
    /// <param name="specified">是否为计划指定的账户（提示中说明未切换）。</param>
    /// <exception cref="PuloniaRunAccountException">账户已被占用或占用文件无法建立。</exception>
    private void AcquireAccountOccupancy(RunState state, PuloniaGenshinAccountProfile account, bool specified)
    {
        IDisposable? occupancy;
        PuloniaLockOwner? occupant;
        try
        {
            occupancy = _accounts.TryAcquireAccountOccupancy(account.Id, state.RunId, out occupant);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PuloniaTaskValidationException)
        {
            throw new PuloniaRunAccountException($"无法取得账户“{account.Name}”的占用，未执行任何任务：{ex.Message}");
        }
        if (occupancy is null)
        {
            var who = occupant is null ? "另一个实例" : PuloniaAccountService.DescribeOccupant(occupant);
            throw new PuloniaRunAccountException(specified
                ? $"账户“{account.Name}”正在{who}运行，本次未切换。同一账户不能同时在两个实例运行。"
                : $"账户“{account.Name}”正在{who}运行。多个实例同时运行时，请为每个实例设置不同的当前账户。");
        }
        state.AccountOccupancy = occupancy;
    }

    /// <summary>
    /// 切换前检查（统筹设计 4.6）：当前账户是目标账户、且正在线的其他实例；读不出实例设置时按没有处理，不阻止运行。
    /// </summary>
    /// <param name="account">目标账户。</param>
    /// <param name="ct">运行取消令牌。</param>
    private async Task<IReadOnlyList<string>> ListDisplacedInstancesAsync(PuloniaGenshinAccountProfile account, CancellationToken ct)
    {
        try
        {
            return await _accounts.ListOtherOnlineInstancesUsingAccountAsync(account.Id, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "切换前检查读取其他实例的当前账户失败，按没有冲突处理");
            return [];
        }
    }

    /// <summary>
    /// 不切换的原因：例如“云原神 · a 的当前账户是“丙”且正在线，切换到“丙”会把它挤下线，本次未切换。”
    /// </summary>
    /// <param name="instanceKeys">会被挤下线的实例。</param>
    /// <param name="account">目标账户。</param>
    private static string DescribeDisplacement(IReadOnlyList<string> instanceKeys, PuloniaGenshinAccountProfile account)
        => $"{string.Join("、", instanceKeys.Select(PuloniaInstanceKey.Describe))}的当前账户是“{account.Name}”且正在线，"
           + $"切换到“{account.Name}”会把它挤下线，本次未切换。可以在本实例界面手动运行并确认，或先修改那个实例的当前账户。";
}
