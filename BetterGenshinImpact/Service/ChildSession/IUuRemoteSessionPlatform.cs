using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>
/// 隔离 Windows 会话与进程操作，便于在不影响真实远程连接的情况下验证修复流程。
/// </summary>
internal interface IUuRemoteSessionPlatform
{
    /// <summary>当前实例是否为根实例。</summary>
    bool IsRoot { get; }

    /// <summary>当前进程是否具有管理员权限。</summary>
    bool IsElevated { get; }

    /// <summary>BetterGI 当前所在的 Windows 会话。</summary>
    int CurrentSessionId { get; }

    /// <summary>当前物理控制台会话；切换用户期间可能无法确定。</summary>
    uint? ConsoleSessionId { get; }

    /// <summary>读取用户、会话及 UU 串流进程的最新状态。</summary>
    UuRemoteSessionSnapshot CaptureSnapshot();

    /// <summary>请求断开指定会话，不注销用户或终止其中的程序。</summary>
    bool DisconnectSession(uint sessionId, out int errorCode);

    /// <summary>只停止非控制台 GameViewerServer；每个进程停止前均由调用方复核安全条件。</summary>
    Task StopStreamingProcessesAsync(uint consoleSessionId,
        Func<CancellationToken, Task> validateBeforeStopAsync, CancellationToken cancellationToken);

    /// <summary>读取当前 GameViewerServer 的进程与会话列表。</summary>
    IReadOnlyList<GameViewerServerInfo> GetStreamingProcesses();
}
