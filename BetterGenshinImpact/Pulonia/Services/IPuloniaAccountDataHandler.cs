using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 按 UID 存放账户数据的其他模块（例如狛荷屋）接入账户目录的扩展点。由这些模块实现并在依赖注入中注册，
/// Pulonia 账户服务只通过本接口调用，不依赖具体模块：账户 UID 修改时数据随之改名，删除账户时一并删除。
/// </summary>
/// <remarks>
/// 调用顺序：保存账户目录前先调用 <see cref="CheckUidChangeAsync"/>，任一实现返回拒绝原因时不保存；
/// 目录保存成功后再调用 <see cref="OnUidChangedAsync"/> 与 <see cref="OnAccountRemovedAsync"/>，失败只作为警告返回，不回滚目录。
/// </remarks>
public interface IPuloniaAccountDataHandler
{
    /// <summary>数据的显示名称，用于拒绝原因和警告，例如“狛荷屋记录”。</summary>
    string DataName { get; }

    /// <summary>
    /// 检查一个已有 UID 的账户能否把 UID 改为新值或清空。只在 UID 确实变化时调用；没有 UID 的账户填写 UID 时不调用。
    /// </summary>
    /// <param name="oldUid">修改前的完整 UID</param>
    /// <param name="newUid">修改后的完整 UID；清空 UID 时为 null</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝修改的原因；可以修改时为 null</returns>
    Task<string?> CheckUidChangeAsync(string oldUid, string? newUid, CancellationToken ct = default);

    /// <summary>
    /// 账户目录已保存后，把旧 UID 的数据改到新 UID 名下；旧 UID 没有数据时不做任何事。
    /// </summary>
    /// <param name="oldUid">修改前的完整 UID</param>
    /// <param name="newUid">修改后的完整 UID</param>
    /// <param name="ct">取消令牌</param>
    Task OnUidChangedAsync(string oldUid, string newUid, CancellationToken ct = default);

    /// <summary>
    /// 账户目录已保存、账户已被删除后，删除该 UID 的数据；没有数据时不做任何事。
    /// </summary>
    /// <param name="uid">被删除账户的完整 UID</param>
    /// <param name="ct">取消令牌</param>
    Task OnAccountRemovedAsync(string uid, CancellationToken ct = default);

    /// <summary>
    /// 该 UID 是否有数据；删除账户前据此列出会一并删除的数据。
    /// </summary>
    /// <param name="uid">完整 UID</param>
    /// <param name="ct">取消令牌</param>
    Task<bool> HasDataAsync(string uid, CancellationToken ct = default);
}
