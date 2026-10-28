using System;
using System.Threading;
using BetterGenshinImpact.Infrastructure.Storage;

namespace BetterGenshinImpact.Infrastructure.Temp;

/// <summary>
/// 一次性临时目录，由 <see cref="ITempFileService.CreateScope"/> 创建，释放时删除
/// </summary>
public sealed class TempScope : IDisposable
{
    /// <summary>
    /// 释放时的回调，由临时文件服务删除目录并解除登记
    /// </summary>
    private readonly Action<TempScope> _onDispose;

    /// <summary>
    /// 是否已释放，保证回调只执行一次
    /// </summary>
    private int _disposed;

    /// <param name="directoryPath">已创建的临时目录</param>
    /// <param name="onDispose">释放时的回调</param>
    internal TempScope(string directoryPath, Action<TempScope> onDispose)
    {
        DirectoryPath = directoryPath;
        _onDispose = onDispose;
    }

    /// <summary>
    /// 临时目录的完整路径，创建时已存在
    /// </summary>
    public string DirectoryPath { get; }

    /// <summary>
    /// 返回目录内的路径
    /// </summary>
    /// <exception cref="ArgumentException">相对路径是绝对路径或越出了临时目录</exception>
    public string GetPath(string relativePath) => SafePath.Combine(DirectoryPath, relativePath);

    /// <summary>
    /// 删除目录（含只读文件）。失败只记警告，由进程退出或下次启动回收
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _onDispose(this);
        }
    }
}
