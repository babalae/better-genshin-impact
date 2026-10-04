using System;
using System.IO;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>持有脚本可写目录的跨进程独占权，执行结束后释放。</summary>
public sealed class ScriptWorkspaceLease(string path, FileStream gate) : IDisposable
{
    /// <summary>经过校验的本次脚本执行目录。</summary>
    public string Path { get; } = path;
    /// <summary>跨进程工作目录锁，不能提前释放。</summary>
    private readonly FileStream _gate = gate;
    /// <summary>退出脚本作用域后释放目录独占权。</summary>
    public void Dispose() => _gate.Dispose();
}
