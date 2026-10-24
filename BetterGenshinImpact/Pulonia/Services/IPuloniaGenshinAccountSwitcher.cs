using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 在调用方已经持有游戏输入租约时准备、切换和核验 PC 官服账号。
/// 所有方法都使用租约里的运行环境（<see cref="PuloniaGameTaskLease.Runtime"/>），不查询当前绑定
/// </summary>
public interface IPuloniaGenshinAccountSwitcher
{
    /// <summary>准备目标账号并返回完整核验 UID；不持有或创建额外的输入租约。</summary>
    Task<string> PrepareAsync(GameRuntime runtime, PuloniaGenshinAccountProfile account, CancellationToken ct);
    /// <summary>只核验当前账号，失败时禁止继续业务操作。</summary>
    Task VerifyAsync(GameRuntime runtime, string expectedUid, CancellationToken ct);
    /// <summary>识别当前 UID、为指定账号新建登录标识图片并返回主界面；图片永远新建，不覆盖已有文件。</summary>
    /// <param name="runtime">调用方租约持有的运行环境</param>
    /// <param name="accountId">图片所属账号 ID，用作新图片文件名前缀</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>核验 UID 与新图片相对于 User/Pulonia 的路径</returns>
    Task<(string Uid, string ImagePath)> CaptureCurrentAccountAsync(GameRuntime runtime, string accountId, CancellationToken ct);
}
