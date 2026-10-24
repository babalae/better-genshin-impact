using System.Collections.Generic;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 逐个读取一组文件（计划、运行历史）的结果：损坏的文件被跳过并记入 <see cref="Failures"/>，不阻断其余文件。
/// </summary>
/// <typeparam name="T">成功读取的对象类型。</typeparam>
public sealed class PuloniaTaskFileListResult<T>
{
    /// <summary>
    /// 不含任何文件的空结果。
    /// </summary>
    public static PuloniaTaskFileListResult<T> Empty { get; } = new([], []);

    /// <summary>
    /// 成功读取的对象。
    /// </summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// 被跳过的文件及原因；没有损坏文件时为空集合。
    /// </summary>
    public IReadOnlyList<PuloniaTaskFileLoadFailure> Failures { get; }

    /// <summary>
    /// 建立读取结果。
    /// </summary>
    /// <param name="items">成功读取的对象。</param>
    /// <param name="failures">被跳过的文件。</param>
    public PuloniaTaskFileListResult(IReadOnlyList<T> items, IReadOnlyList<PuloniaTaskFileLoadFailure> failures)
    {
        Items = items;
        Failures = failures;
    }
}

/// <summary>
/// 一份无法读取而被跳过的文件；文件本身保留在磁盘上，不会被删除或改写。
/// </summary>
/// <param name="Path">文件完整路径。</param>
/// <param name="Id">由文件名得到的稳定 ID（计划 ID 或运行 ID）；文件名本身不是合法 ID 时为空。</param>
/// <param name="Message">读取失败原因。</param>
public sealed record PuloniaTaskFileLoadFailure(string Path, string? Id, string Message)
{
    /// <summary>
    /// 便于界面直接显示的一行说明，形如“文件名：原因”。
    /// </summary>
    public override string ToString() => System.IO.Path.GetFileName(Path) + "：" + Message;
}
