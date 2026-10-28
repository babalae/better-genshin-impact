using System;
using System.IO;
using System.Text;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Infrastructure.Storage;

/// <summary>
/// 就地原子写入：先写同目录的临时文件，再整体替换目标，避免进程中断时留下写了一半的文件。
/// 临时文件命名为 .{文件名}.{8 位随机}.tmp，必须与目标在同一目录才能原子替换。
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// 无 BOM 的 UTF-8 编码
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// 以 UTF-8（无 BOM）原子写入文本，目标所在目录不存在时自动创建
    /// </summary>
    public static void WriteAllText(string path, string contents) =>
        Write(path, tempPath => File.WriteAllText(tempPath, contents, Utf8NoBom));

    /// <summary>
    /// 原子写入二进制内容，目标所在目录不存在时自动创建
    /// </summary>
    public static void WriteAllBytes(string path, byte[] bytes) =>
        Write(path, tempPath => File.WriteAllBytes(tempPath, bytes));

    /// <summary>
    /// 判断文件名是否为本类生成的临时文件，供各存储方清理残留
    /// </summary>
    public static bool IsTempFile(string fileName) =>
        fileName.StartsWith('.') && fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 写入同目录的临时文件后整体替换目标
    /// </summary>
    /// <param name="path">目标路径</param>
    /// <param name="writeTemp">把内容写入给定临时文件路径</param>
    private static void Write(string path, Action<string> writeTemp)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new ArgumentException($"无法确定文件所在目录：{path}", nameof(path));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{ShortId.Create(null, 8)}.tmp");
        try
        {
            writeTemp(tempPath);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            // 替换成功后临时文件已不存在；失败时尽力删除，残留由所属存储方清理
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
