using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 在调用方已经持有游戏输入租约时准备、切换和核验 PC 官服账户。
/// 所有方法都使用租约里的运行环境（<see cref="PuloniaGameTaskLease.Runtime"/>），不查询当前绑定
/// </summary>
public interface IPuloniaGenshinAccountSwitcher
{
    /// <summary>
    /// 准备目标账户并返回完整核验 UID；不持有或创建额外的输入租约。
    /// 先回到主界面读取 UID，等于目标 UID 时不切换；否则按登录资料切换，切换后必须核验到目标 UID。
    /// </summary>
    /// <param name="runtime">调用方租约持有的运行环境</param>
    /// <param name="account">目标账户，必须填写完整 UID</param>
    /// <param name="switchStarted">即将离开主界面去登出或登录时调用一次；此后失败时游戏中登录的账户不再确定</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>核验通过的完整 UID</returns>
    Task<string> PrepareAsync(GameRuntime runtime, PuloniaGenshinAccountProfile account, Action switchStarted, CancellationToken ct);
    /// <summary>只核验当前账户，失败时禁止继续业务操作。</summary>
    Task VerifyAsync(GameRuntime runtime, string expectedUid, CancellationToken ct);
    /// <summary>
    /// 只读取当前登录的 UID：只在主界面截图识别，不按键，最多约 3 秒；
    /// 与核验相同，连续两帧新截图得到相同的完整 UID 才采用。供跟随当前账户的 UID 比对与“从当前游戏读取”使用。
    /// </summary>
    /// <param name="runtime">调用方租约持有的运行环境</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>完整 UID；不在主界面或识别失败时为 null</returns>
    Task<string?> TryReadUidAsync(GameRuntime runtime, CancellationToken ct);
    /// <summary>识别当前 UID、为指定账户新建登录标识图片并返回主界面；图片永远新建，不覆盖已有文件。</summary>
    /// <param name="runtime">调用方租约持有的运行环境</param>
    /// <param name="accountId">图片所属账户 ID，用作新图片文件名前缀</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>核验 UID 与新图片相对于 User/Pulonia 的路径</returns>
    Task<(string Uid, string ImagePath)> CaptureCurrentAccountAsync(GameRuntime runtime, string accountId, CancellationToken ct);
}
