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

    /// <summary>
    /// 判断快照固定的账号是否为需要切换的指定账号：没有账号准备或使用默认账号（当前登录账号）时为 false。
    /// 路线 CD 据此决定作用域身份：指定账号用核验 UID，否则与普通计划共用默认身份。
    /// </summary>
    /// <param name="snapshot">运行快照。</param>
    internal static bool IsSwitchedAccount(PuloniaTaskSnapshot snapshot)
        => snapshot.GenshinAccountJson is { } json && !PuloniaTaskJson.Read<PuloniaGenshinAccountProfile>(json).IsDefault;

    /// <summary>在任务树执行前准备账号，核验结果耐久保存后才能产生业务副作用。</summary>
    private async Task PrepareGenshinAccountAsync(RunState state, CancellationToken ct)
    {
        if (state.Snapshot.GenshinAccountJson is null || _accountSwitcher is null
            || !EnumerateLeafTasks(state.Snapshot.RootTask).Any(task => task.IsEnabled && _definitions[task.TaskType].RequiresGameSession)) return;
        var account = PuloniaTaskJson.Read<PuloniaGenshinAccountProfile>(state.Snapshot.GenshinAccountJson);
        var address = state.Snapshot.RootTask.TaskAddress + "/account";
        SetCurrentTask(state, address, "正在准备原神账号：“" + account.Name + "”。");
        // 1. 切换并核验：在子实例上执行时，切号与核验发生在该实例的游戏中；续跑时核验历史 UID（由原运行耐久保存）
        var uid = state.Request.ExecutionTarget is { } target
            ? await PrepareRemoteGenshinAccountAsync(state, target, ct).ConfigureAwait(false)
            : await _localNodes.PrepareAccountAsync(account, state.VerifiedUid, ct).ConfigureAwait(false);
        if (!System.Text.RegularExpressions.Regex.IsMatch(uid, "^[0-9]{9,10}$"))
            throw new PuloniaGenshinAccountException("切号结果没有有效的完整 UID。", new InvalidOperationException());
        // 2. 核验 UID 与账号节点结果在同一次写入中落盘，之后的节点才会产生业务副作用
        lock (_runsGate)
        {
            state.VerifiedUid = uid;
            state.NodeResults.Add(new PuloniaTaskNodeResult(address, "切换账号", "genshin.account.prepare", 1,
                PuloniaTaskNodeStatus.Succeeded, "账号已核验，实际 UID：" + uid, state.StartedAt!.Value, DateTimeOffset.UtcNow));
        }
        await PersistCriticalStateAsync().ConfigureAwait(false);
        RaiseRunChanged(state.RunId);
    }
}
