using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>在调用方已经持有游戏输入租约时准备、切换和核验 PC 官服账号。</summary>
public interface IPuloniaGenshinAccountSwitcher
{
    /// <summary>准备目标账号并返回完整核验 UID；不持有或创建额外的输入租约。</summary>
    Task<string> PrepareAsync(PuloniaGenshinAccountProfile account, CancellationToken ct);
    /// <summary>只核验当前账号，失败时禁止继续业务操作。</summary>
    Task VerifyAsync(string expectedUid, CancellationToken ct);
    /// <summary>识别当前 UID、采集登录标识并返回主界面。</summary>
    Task<(string Uid, string ImagePath)> CaptureCurrentAccountAsync(CancellationToken ct);
}
