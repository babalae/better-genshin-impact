using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 本实例的在线标记（统筹设计 4.6）：宿主启动时打开 <c>instances/&lt;目录名&gt;/online.lock</c> 并保持到程序退出，
/// 其他实例据此判断本实例是否在线（切换前检查、提交前检查）。主实例与子实例都启用；进程退出由系统释放。
/// </summary>
public sealed class PuloniaInstancePresence : IHostedService, IDisposable
{
    /// <summary>Pulonia 存储，提供在线标记文件。</summary>
    private readonly PuloniaTaskStore _store;

    /// <summary>本实例的实例键。</summary>
    private readonly string _instanceKey;

    /// <summary>日志。</summary>
    private readonly ILogger<PuloniaInstancePresence> _logger;

    /// <summary>保护在线标记句柄的取得与释放。</summary>
    private readonly object _gate = new();

    /// <summary>持有中的在线标记；未启动、取得失败或已停止时为空。</summary>
    private IDisposable? _presence;

    /// <summary>
    /// 建立在线标记；构造时不打开文件。
    /// </summary>
    /// <param name="store">Pulonia 存储</param>
    /// <param name="instanceKeyProvider">本实例的实例键</param>
    /// <param name="logger">日志；为空时不记录</param>
    public PuloniaInstancePresence(PuloniaTaskStore store, IPuloniaInstanceKeyProvider instanceKeyProvider,
        ILogger<PuloniaInstancePresence>? logger = null)
    {
        _store = store;
        _instanceKey = instanceKeyProvider.CurrentInstanceKey;
        _logger = logger ?? NullLogger<PuloniaInstancePresence>.Instance;
    }

    /// <summary>
    /// 打开在线标记；同一实例键已被另一个进程持有（不应发生，实例键在同一 Windows 用户下唯一）或文件无法写入时只记录日志，
    /// 本实例照常运行，只是其他实例看不到它在线。
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_presence is not null)
                return;
            try
            {
                _presence = _store.TryAcquireInstancePresence(new PuloniaLockOwner
                {
                    InstanceKey = _instanceKey, ProcessId = Environment.ProcessId, StartedAt = DateTimeOffset.UtcNow
                });
                if (_presence is null)
                    _logger.LogWarning("Pulonia 在线标记已被另一个进程持有（实例 {InstanceKey}），其他实例的在线判断可能不准确", _instanceKey);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PuloniaTaskValidationException)
            {
                _logger.LogWarning(ex, "无法建立 Pulonia 在线标记（实例 {InstanceKey}），其他实例将视本实例为不在线", _instanceKey);
            }
        }
    }

    /// <summary>释放在线标记。</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _presence?.Dispose();
            _presence = null;
        }
    }

    /// <inheritdoc />
    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => Stop();
}
