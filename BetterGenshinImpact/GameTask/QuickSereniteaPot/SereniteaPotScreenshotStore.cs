using System;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

internal static class SereniteaPotScreenshotStore
{
    internal const int MaxFiles = 32;
    internal const long MaxBytes = 64L * 1024 * 1024;
    internal static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    // 仅管理此处生成的 pot-*.png，不清理其他文件。
    internal static string? Save(string directory, string stage, Func<string, bool> write,
        int maxFiles = MaxFiles, long maxBytes = MaxBytes, DateTime? utcNow = null)
    {
        if (maxFiles < 1 || maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxFiles));
        Directory.CreateDirectory(directory);
        FileStream gate;
        try
        {
            // 两个 Windows 会话可共用安装目录，进程内 lock 不能保护共同的额度。
            gate = new FileStream(Path.Combine(directory, ".pot-screenshots.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { return null; } // 诊断竞争不能阻塞任务。

        using (gate)
        {
            var now = utcNow ?? DateTime.UtcNow;
            var safeStage = new string(stage.Where(c => char.IsLetterOrDigit(c) || c == '-').Take(64).ToArray());
            var path = Path.Combine(directory, $"pot-{now:yyyyMMdd-HHmmss-fff}-{safeStage}-{Guid.NewGuid():N}.png");
            try
            {
                Trim(directory, now, maxFiles - 1, maxBytes);
                if (!write(path)) { File.Delete(path); return null; }
                Trim(directory, now, maxFiles, maxBytes);
                return File.Exists(path) ? path : null;
            }
            catch
            {
                // 编码器出错时也不留下半张截图。
                File.Delete(path);
                throw;
            }
        }
    }

    private static void Trim(string directory, DateTime now, int maxFiles, long maxBytes)
    {
        var files = new DirectoryInfo(directory).GetFiles("pot-*.png", SearchOption.TopDirectoryOnly)
            .Where(f => Guid.TryParseExact(Path.GetFileNameWithoutExtension(f.Name).Split('-').Last(), "N", out _))
            .Where(f => (f.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();
        long bytes = files.Sum(f => f.Length);
        var count = files.Count;
        foreach (var file in files)
        {
            if (count <= maxFiles && bytes <= maxBytes && now - file.LastWriteTimeUtc <= MaxAge) break;
            var length = file.Length;
            file.Delete();
            bytes -= length;
            count--;
        }
    }
}
