using System;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>仓库版本中一个普通文件的轻量描述，不读取文件正文。</summary>
public sealed class ScriptRepositoryEntry(string path, long length, DateTime lastWriteTime)
{
    /// <summary>相对于仓库根目录的普通文件路径。</summary>
    public string Path { get; } = path;
    /// <summary>文件原始字节数。</summary>
    public long Length { get; } = length;
    /// <summary>可获得的修改时间；Git 内容使用固定提交时间。</summary>
    public DateTime LastWriteTime { get; } = lastWriteTime;
}
