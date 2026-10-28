using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Infrastructure.Network.Download;

/// <summary>
/// 一个下载任务。由 <see cref="IDownloadService.Start"/> 创建，进度和结果在后台线程更新
/// </summary>
public sealed class DownloadTask
{
    /// <summary>
    /// 取消下载；与调用方传入的取消令牌联动
    /// </summary>
    private readonly CancellationTokenSource _cts;

    /// <summary>
    /// 下载结果
    /// </summary>
    private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 最新进度
    /// </summary>
    private DownloadProgress _progress = DownloadProgress.Queued;

    /// <param name="request">下载请求</param>
    /// <param name="ct">调用方的取消令牌</param>
    internal DownloadTask(DownloadRequest request, CancellationToken ct)
    {
        Request = request;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    }

    /// <summary>
    /// 任务标识
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// 下载请求
    /// </summary>
    public DownloadRequest Request { get; }

    /// <summary>
    /// 最新进度
    /// </summary>
    public DownloadProgress Progress => Volatile.Read(ref _progress);

    /// <summary>
    /// 进度变化，约每 150 毫秒一次，在后台线程触发
    /// </summary>
    public event EventHandler<DownloadProgress>? ProgressChanged;

    /// <summary>
    /// 完成后返回文件路径；失败或取消时抛出异常
    /// </summary>
    public Task<string> Completion => _completion.Task;

    /// <summary>
    /// 完成后的文件路径，未完成时为 null
    /// </summary>
    public string? ResultPath => _completion.Task.IsCompletedSuccessfully ? _completion.Task.Result : null;

    /// <summary>
    /// 下载使用的取消令牌
    /// </summary>
    internal CancellationToken Token => _cts.Token;

    /// <summary>
    /// 取消下载，已下载部分保留，可续传
    /// </summary>
    public void Cancel()
    {
        if (_completion.Task.IsCompleted)
        {
            return;
        }

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 任务刚好结束
        }
    }

    /// <summary>
    /// 更新进度并通知订阅者
    /// </summary>
    internal void Report(DownloadProgress progress)
    {
        Volatile.Write(ref _progress, progress);
        ProgressChanged?.Invoke(this, progress);
    }

    /// <summary>
    /// 以成功结束
    /// </summary>
    internal void Complete(string path, DownloadProgress progress)
    {
        Report(progress);
        _completion.TrySetResult(path);
        _cts.Dispose();
    }

    /// <summary>
    /// 以失败或取消结束
    /// </summary>
    internal void Fail(Exception exception, DownloadProgress progress)
    {
        Report(progress);
        if (exception is OperationCanceledException canceled)
        {
            _completion.TrySetCanceled(canceled.CancellationToken);
        }
        else
        {
            _completion.TrySetException(exception);
        }

        _cts.Dispose();
    }
}
