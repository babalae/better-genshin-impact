using Fischless.WindowsInput;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Simulator;

/// <summary>
/// 游戏输入后端的统一抽象。
/// 既兼容现有 <see cref="IInputSimulator"/> 调用方式，也提供异步刷新、释放和重置能力，
/// 使任务代码无需感知输入最终由 Win32 还是云页面 RTC 通道执行。
/// </summary>
public interface IGameInputBackend : IInputSimulator, IAsyncDisposable
{
    /// <summary>
    /// 等待此前提交的输入命令全部处理完成。
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空待处理命令并释放当前后端记录的全部按键和鼠标按钮。
    /// </summary>
    Task ReleaseAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 将输入后端恢复到初始状态。
    /// </summary>
    Task ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 丢弃尚未消费的命令。页面刷新或断线时调用，禁止旧命令在重连后重放。
    /// </summary>
    void ClearPending();
}
