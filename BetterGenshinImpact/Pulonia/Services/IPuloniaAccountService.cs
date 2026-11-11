using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// Pulonia 账户的应用服务：账户目录、各实例的当前账户与账户进度（完成记录、首领领奖次数）。存储由 <see cref="PuloniaTaskStore"/> 负责，
/// 这里负责跨文件的规则（删除账户前的引用检查）、变化通知、完成记录的按修改时间缓存和本实例最近一次 UID 比对结果（只在内存中）。
/// </summary>
/// <remarks>
/// 账户占用（<c>run.lock</c>）与在线标记（<c>online.lock</c>）通过共享文件判断，账户进度的读写同样不经过 IPC。
/// </remarks>
public interface IPuloniaAccountService
{
    /// <summary>
    /// 本进程写入或清除了某个账户的账户进度后立即触发；在写入线程上触发，订阅方负责切换到自己的 UI 线程。
    /// 其他实例的写入不触发，靠 <see cref="ListRouteCompletionsAsync"/> 比较修改时间发现。
    /// </summary>
    event EventHandler<PuloniaAccountProgressChangedEventArgs>? AccountProgressChanged;

    /// <summary>
    /// 读取一个账户的完成记录（每份路线一条）。按文件修改时间缓存，文件变化才重新读取。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>只读列表；记录对象不可修改</returns>
    Task<IReadOnlyList<PuloniaRouteCompletion>> ListRouteCompletionsAsync(string accountId, CancellationToken ct = default);

    /// <summary>
    /// 读取一个账户按路线身份索引的完成记录，与 <see cref="ListRouteCompletionsAsync"/> 共用同一缓存；
    /// 运行判定、任务树标记和运行预览按路线查找时使用。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="ct">取消令牌</param>
    Task<PuloniaRouteCompletionIndex> GetRouteCompletionIndexAsync(string accountId, CancellationToken ct = default);

    /// <summary>
    /// 写入一条完成记录（同一路线只保留最新；同一运行、节点、尝试重复写入时幂等），落盘后触发 <see cref="AccountProgressChanged"/>。
    /// </summary>
    /// <param name="accountId">运行账户 ID</param>
    /// <param name="completion">本次完成的路线</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否写入了新记录；幂等重复时为 false</returns>
    Task<bool> RecordRouteCompletionAsync(string accountId, PuloniaRouteCompletion completion, CancellationToken ct = default);

    /// <summary>
    /// 清除一个账户中指定路线的完成记录，让这些路线下次照常执行；运行历史不受影响，不能撤销。
    /// 有记录被删除时触发 <see cref="AccountProgressChanged"/>。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="routes">要清除的路线身份</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>删除的条数</returns>
    Task<int> ClearRouteCompletionsAsync(string accountId, IReadOnlyCollection<string> routes, CancellationToken ct = default);

    /// <summary>
    /// 读取一个账户在一个首领讨伐节点当前重置批次内的累计成功领奖次数。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="planId">计划 ID</param>
    /// <param name="taskId">节点 ID</param>
    /// <param name="resetId">重置批次</param>
    /// <param name="ct">取消令牌</param>
    Task<int> GetBossRewardCountAsync(string accountId, string planId, string taskId, string resetId, CancellationToken ct = default);

    /// <summary>
    /// 确认一次领奖成功后把运行账户的累计次数加 1 并立即落盘，之后触发 <see cref="AccountProgressChanged"/>。
    /// </summary>
    /// <param name="accountId">运行账户 ID</param>
    /// <param name="planId">计划 ID</param>
    /// <param name="taskId">节点 ID</param>
    /// <param name="resetId">重置批次</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>加 1 后的累计次数</returns>
    Task<int> IncrementBossRewardCountAsync(string accountId, string planId, string taskId, string resetId, CancellationToken ct = default);

    /// <summary>本实例的实例键。</summary>
    string CurrentInstanceKey { get; }

    /// <summary>本进程成功保存账户目录后触发；在保存线程上触发，订阅方负责切换到自己的 UI 线程。</summary>
    event EventHandler? AccountsChanged;

    /// <summary>本进程修改了任一实例的当前账户后触发；在修改线程上触发，订阅方负责切换到自己的 UI 线程。</summary>
    event EventHandler<PuloniaCurrentAccountChangedEventArgs>? CurrentAccountChanged;

    /// <summary>
    /// 本实例最近一次 UID 比对的结果，只在内存中；尚未比对或本实例当前账户被修改后为 null。
    /// </summary>
    PuloniaUidCheckReport? LastUidCheck { get; }

    /// <summary>
    /// 本实例尚未解除的 UID 不一致：最近一次比对不一致之后，既没有比对一致，也没有修改本实例当前账户；没有时为 null。
    /// “未比对”的结果不解除不一致。
    /// </summary>
    PuloniaUidCheckReport? UidMismatch { get; }

    /// <summary>
    /// 报告了新的 UID 比对结果，或本实例当前账户被修改而清除结果后触发；在报告或修改线程上触发，订阅方负责切换到自己的 UI 线程。
    /// </summary>
    event EventHandler? UidCheckChanged;

    /// <summary>
    /// 读取账户目录；首次使用时建立只含“我的账户”的目录。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>账户目录的独立副本</returns>
    Task<PuloniaGenshinAccountSettings> LoadAccountsAsync(CancellationToken ct = default);

    /// <summary>
    /// 保存账户目录及本次填写的登录凭据（先保存目录、后写凭据）。被删除或清空 UID 的账户仍被计划指定、
    /// 或被删除的账户是某个实例的当前账户时拒绝保存。
    /// 已注册 <see cref="IPuloniaAccountDataHandler"/> 时，UID 修改先经各模块检查（如新 UID 已有狛荷屋记录时拒绝）；
    /// 目录保存后关联数据随 UID 改名、随账户删除，失败时由结果的 <see cref="PuloniaGenshinAccountSaveResult.DataWarning"/> 说明。
    /// 关联数据处理完成后才触发 <see cref="AccountsChanged"/>。
    /// </summary>
    /// <param name="settings">要保存的目录草稿，调用方对象不会被修改</param>
    /// <param name="credentials">以账户 ID 为键的新登录凭据</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>保存后的目录、凭据写入失败说明与关联数据警告</returns>
    /// <exception cref="InvalidOperationException">目录不合法、修订冲突、删除被拒绝或 UID 修改被关联数据模块拒绝</exception>
    Task<PuloniaGenshinAccountSaveResult> SaveAccountsAsync(PuloniaGenshinAccountSettings settings,
        IReadOnlyDictionary<string, (string UserName, string Password)> credentials, CancellationToken ct = default);

    /// <summary>
    /// 检查一个账户现在能否删除：仍被计划指定、是某个实例的当前账户，或引用无法确认时返回原因。
    /// </summary>
    /// <param name="accountId">要删除的账户 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝删除的原因；可以删除时为 null</returns>
    Task<string?> CheckAccountRemovalAsync(string accountId, CancellationToken ct = default);

    /// <summary>
    /// 读取实例当前账户 ID；实例首次使用时初始化为账户列表第一个账户。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>当前账户 ID；“未确定”时为 null</returns>
    Task<string?> GetCurrentAccountIdAsync(string instanceKey, CancellationToken ct = default);

    /// <summary>
    /// 读取实例当前账户的资料。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>当前账户的独立副本；“未确定”或账户已不在目录中时为 null</returns>
    Task<PuloniaGenshinAccountProfile?> GetCurrentAccountAsync(string instanceKey, CancellationToken ct = default);

    /// <summary>
    /// 修改实例当前账户并立即保存；正在运行的任务不受影响，下一次运行开始时生效。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    /// <param name="accountId">新的当前账户 ID；为空表示“未确定”</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="InvalidOperationException">账户不存在</exception>
    Task SetCurrentAccountAsync(string instanceKey, string? accountId, CancellationToken ct = default);

    /// <summary>
    /// 列出已建立设置文件的各实例及其当前账户；读不出的设置文件记入失败。供账户页显示“作为哪些实例的当前账户”。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>各实例目录的设置与读取失败的文件</returns>
    Task<PuloniaTaskFileListResult<PuloniaInstanceSettingsEntry>> ListInstanceSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// 报告本实例一次 UID 比对的结果（一致、不一致或未比对），只在内存中保存并触发 <see cref="UidCheckChanged"/>。
    /// </summary>
    /// <param name="accountId">比对时的实例当前账户 ID</param>
    /// <param name="result">比对结果</param>
    /// <param name="observedUid">不一致时游戏中读到的完整 UID；其他结果忽略</param>
    void ReportUidCheck(string accountId, PuloniaTaskUidCheck result, string? observedUid);

    /// <summary>
    /// 尝试取得账户占用（统筹设计 4.5）：同一账户同一时刻只能被一个运行占用，不论在哪个实例；不等待、不排队。
    /// 占用者为本实例；运行结束或测试切换结束时释放返回的句柄。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="runId">占用账户的运行 ID；账户页测试切换等非运行操作为 null</param>
    /// <param name="occupant">取得失败时的当前占用者；读不出时为 null</param>
    /// <returns>持有占用的句柄；账户已被占用时为 null</returns>
    IDisposable? TryAcquireAccountOccupancy(string accountId, string? runId, out PuloniaLockOwner? occupant);

    /// <summary>
    /// 账户当前的占用者（任一实例中正在使用该账户的运行或测试切换）；没有被占用时为 null。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    PuloniaLockOwner? GetAccountOccupant(string accountId);

    /// <summary>
    /// 实例是否在线：本实例总是在线，其他实例按它的在线标记判断。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    bool IsInstanceOnline(string instanceKey);

    /// <summary>
    /// 列出当前账户是指定账户、且正在线的其他实例（不含本实例），供切换前检查（统筹设计 4.6）：切换到该账户会把这些实例挤下线。
    /// 读不出的实例设置跳过。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>这些实例的实例键</returns>
    Task<IReadOnlyList<string>> ListOtherOnlineInstancesUsingAccountAsync(string accountId, CancellationToken ct = default);

    /// <summary>
    /// 指定了该账户作为运行账户的计划（普通计划与一条龙配置），按计划名称排序；读不出的计划文件跳过。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="ct">取消令牌</param>
    Task<IReadOnlyList<PuloniaAccountPlanUsage>> ListPlansSpecifyingAccountAsync(string accountId, CancellationToken ct = default);

    /// <summary>
    /// 删除该账户时会一并删除的数据：登录凭据、不再被引用的账户图片、完成记录、首领领奖次数，以及其他模块按 UID 存放的数据。
    /// 只读取，不删除；账户不在目录中时各项为空。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="ct">取消令牌</param>
    Task<PuloniaAccountRemovalImpact> DescribeAccountRemovalAsync(string accountId, CancellationToken ct = default);
}
