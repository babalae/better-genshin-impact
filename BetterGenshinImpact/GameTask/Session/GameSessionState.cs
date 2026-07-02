namespace BetterGenshinImpact.GameTask.Session;

/// <summary>
/// 游戏会话的生命周期状态。
/// </summary>
public enum GameSessionState
{
    /// <summary>会话已停止。</summary>
    Stopped,
    /// <summary>正在创建运行目标及相关资源。</summary>
    Starting,
    /// <summary>页面已打开，但尚未完成登录或 RTC 连接。</summary>
    WaitingForLogin,
    /// <summary>截图与输入通道均已就绪，可以启动任务。</summary>
    Ready,
    /// <summary>当前会话正在执行实时触发器或脚本任务。</summary>
    Running,
    /// <summary>页面、截图或 RTC 通道已经断开。</summary>
    Disconnected,
    /// <summary>页面私有 API 与当前注入脚本不兼容。</summary>
    Incompatible,
    /// <summary>WebView2 或会话基础设施发生不可恢复异常。</summary>
    Faulted
}
