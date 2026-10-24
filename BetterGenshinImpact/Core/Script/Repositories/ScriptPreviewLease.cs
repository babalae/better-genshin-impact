using System;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>
/// 一次性预览目录的租约；预览界面仍在读取 README 时保持目录存在，释放后删除。
/// 进程崩溃残留的预览目录暂不清理。
/// </summary>
public sealed class ScriptPreviewLease : IDisposable
{
    /// <summary>释放时删除预览目录的回调。</summary>
    private readonly Action _release;

    /// <summary>是否已经释放，避免重复删除。</summary>
    private bool _disposed;

    /// <summary>预览目录的完整路径，包含 manifest.json、声明的设置文件和可选 README.md。</summary>
    public string Path { get; }

    /// <summary>建立预览租约。</summary>
    /// <param name="path">预览目录</param>
    /// <param name="release">释放时删除目录的回调</param>
    internal ScriptPreviewLease(string path, Action release)
    {
        Path = path;
        _release = release;
    }

    /// <summary>删除预览目录；删除失败只保留残留，不影响界面。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _release();
    }
}
