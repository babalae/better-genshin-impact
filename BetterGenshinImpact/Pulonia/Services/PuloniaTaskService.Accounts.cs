using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>同一任务服务的账号准备和实际 UID 作用域，不依赖一条龙页面。</summary>
public sealed partial class PuloniaTaskService
{
    /// <summary>切号与核验入口；显式构造的纯任务服务可不注册此游戏依赖。</summary>
    private readonly IPuloniaGenshinAccountSwitcher? _accountSwitcher;

    /// <summary>一条龙只允许一个有效账号绑定，缺省使用当前账号。</summary>
    internal static string ResolveOneDragonAccountId(PuloniaTaskPlan plan)
    {
        var bindings = plan.Accounts.Where(account => account.Enabled).ToArray();
        if (bindings.Length > 1) throw new InvalidOperationException("一条龙配置只能绑定一个账号，请在账号设置中选择。");
        return bindings.FirstOrDefault()?.AccountId ?? PuloniaGenshinAccountProfile.DefaultId;
    }

    /// <summary>在任务树执行前准备账号，核验结果耐久保存后才能产生业务副作用。</summary>
    private async Task PrepareGenshinAccountAsync(RunState state, CancellationToken ct)
    {
        if (state.Snapshot.GenshinAccountJson is null || _accountSwitcher is null
            || !EnumerateLeafTasks(state.Snapshot.RootTask).Any(task => task.IsEnabled && _definitions[task.TaskType].RequiresGameSession)) return;
        var account = PuloniaTaskJson.Read<PuloniaGenshinAccountProfile>(state.Snapshot.GenshinAccountJson);
        await SetCurrentTaskAsync(state, state.Snapshot.RootTask.TaskAddress + "/account",
            "正在准备原神账号：“" + account.Name + "”。").ConfigureAwait(false);
        PuloniaGameTaskLease preparedLease;
        try { preparedLease = await _gameTaskCoordinator.AcquireAsync(ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new PuloniaGenshinAccountException("无法准备账号的游戏环境：" + ex.Message, ex); }
        using var lease = preparedLease;
        string uid;
        try
        {
            // 续跑默认账号也必须核验历史实际 UID，不能自动采用另一个当前登录账号。
            if (state.VerifiedUid is { } expected)
            {
                if (!account.IsDefault) await _accountSwitcher.PrepareAsync(account, ct).ConfigureAwait(false);
                await _accountSwitcher.VerifyAsync(expected, ct).ConfigureAwait(false);
                uid = expected;
            }
            else uid = await _accountSwitcher.PrepareAsync(account, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PuloniaGenshinAccountException(ex.Message, ex);
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(uid, "^[0-9]{9,10}$"))
            throw new PuloniaGenshinAccountException("切号结果没有有效的完整 UID。", new InvalidOperationException());
        lock (state.SyncRoot) state.VerifiedUid = uid;
        await PersistCriticalStateAsync().ConfigureAwait(false);
        await AddNodeResultAsync(state, new PuloniaTaskNodeResult(state.Snapshot.RootTask.TaskAddress + "/account",
            "切换账号", "genshin.account.prepare", 1, PuloniaTaskNodeStatus.Succeeded,
            "账号已核验，实际 UID：" + uid, state.StartedAt!.Value, DateTimeOffset.UtcNow)).ConfigureAwait(false);
    }

    /// <summary>取得每个游戏节点的输入后再次核验，身份改变时不进入重试业务。</summary>
    private async Task VerifyGenshinAccountAsync(RunState state, CancellationToken ct)
    {
        if (state.VerifiedUid is not { } uid || _accountSwitcher is null) return;
        try { await _accountSwitcher.VerifyAsync(uid, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new PuloniaGenshinAccountException(ex.Message, ex); }
    }

    /// <summary>账号身份异常必须进入待处理，不能作为普通节点失败继续业务。</summary>
    private sealed class PuloniaGenshinAccountException(string message, Exception inner) : Exception(message, inner);
}
