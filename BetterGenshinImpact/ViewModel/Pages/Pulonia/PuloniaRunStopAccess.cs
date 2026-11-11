namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 执行记录中一个未结束的运行能否从本实例停止（统筹设计 6.5：每个实例只能停止本实例的运行，主实例还可以停止自己下发的运行）。
/// </summary>
public enum PuloniaRunStopAccess
{
    /// <summary>本实例的运行，经本实例的任务服务停止。</summary>
    Local,

    /// <summary>主实例下发到子实例、连接正常的运行，经下发客户端请求子实例停止。</summary>
    Dispatched,

    /// <summary>主实例下发的运行，但与执行实例的连接已断开，不能远程停止。</summary>
    Disconnected,

    /// <summary>其他实例自己提交的运行，只能在该实例界面停止。</summary>
    OtherInstance
}
