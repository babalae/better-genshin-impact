using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>
/// 狛荷屋接入 Pulonia 账户目录：账户 UID 修改时 <c>User/AccountRecords/&lt;uid&gt;</c> 随之改名，删除账户时一并删除狛荷屋记录。
/// 狛荷屋依赖 Pulonia 的扩展点接口，Pulonia 不依赖狛荷屋。
/// </summary>
/// <remarks>
/// 改名与删除经 <see cref="AccountRecordsInteractionGate"/> 与页面人工操作串行；账户页保存不在门禁内调用，不会重入死锁。
/// </remarks>
public sealed class AccountRecordsAccountDataHandler : IPuloniaAccountDataHandler
{
    /// <summary>狛荷屋库存存储，提供数据目录的检查、改名与删除。</summary>
    private readonly InventoryStore _store;

    /// <summary>注入与狛荷屋页面共用的库存存储。</summary>
    /// <param name="store">狛荷屋库存存储</param>
    public AccountRecordsAccountDataHandler(InventoryStore store) => _store = store;

    /// <inheritdoc />
    public string DataName => "狛荷屋记录";

    /// <inheritdoc />
    /// <remarks>
    /// 新 UID 已有数据时一律拒绝（包括两个账户互换 UID），不合并两份记录；清空 UID 时旧数据无处可放，同样拒绝。
    /// 任务运行期间人工修改被锁定，有数据需要改名时也拒绝。
    /// </remarks>
    public async Task<string?> CheckUidChangeAsync(string oldUid, string? newUid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // 1. 新 UID 已有狛荷屋记录：改名会与它冲突
        if (newUid is not null && await _store.HasUidDataAsync(newUid))
            return $"UID {newUid} 已有狛荷屋记录，不能改为这个 UID。";
        // 2. 旧 UID 没有数据时，修改不影响狛荷屋
        if (!await _store.HasUidDataAsync(oldUid))
            return null;
        if (newUid is null)
            return "该账户有狛荷屋记录，不能清空 UID，可以改为其他 UID。";
        // 3. 新 UID 不能用作狛荷屋的数据目录（首位为 0），记录无法随之改名
        if (!InventoryStore.IsValidUid(newUid))
            return $"UID {newUid} 首位为 0，狛荷屋不支持这个 UID，该账户的狛荷屋记录无法随之改名。";
        // 4. 任务运行期间不能人工修改狛荷屋数据
        return AccountRecordsInteractionGate.Instance.IsLocked ? AccountRecordsInteractionGate.LockedMessage : null;
    }

    /// <inheritdoc />
    public async Task OnUidChangedAsync(string oldUid, string newUid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!await _store.HasUidDataAsync(oldUid))
            return;
        await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _store.RenameUidAsync(oldUid, newUid));
    }

    /// <inheritdoc />
    public async Task OnAccountRemovedAsync(string uid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!await _store.HasUidDataAsync(uid))
            return;
        // 与“删除 UID 及全部数据”相同：整个数据目录（库存、事件、快照）一并删除
        await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _store.ClearAsync(uid, true, true));
    }

    /// <inheritdoc />
    public async Task<bool> HasDataAsync(string uid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await _store.HasUidDataAsync(uid);
    }
}
