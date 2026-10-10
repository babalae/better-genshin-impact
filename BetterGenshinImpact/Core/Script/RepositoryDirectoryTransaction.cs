using BetterGenshinImpact.Helpers;
using System;
using System.IO;

namespace BetterGenshinImpact.Core.Script;

public static class RepositoryDirectoryTransaction
{
    public static void Replace(string repositoryPath, Action<string> prepare, Action<string> validate)
    {
        var target = NormalizeTarget(repositoryPath);
        var staged = target + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            prepare(staged);
            validate(staged);
            Promote(staged, target);
        }
        finally
        {
            TryCleanup(staged);
        }
    }

    public static void Promote(string stagedPath, string repositoryPath)
    {
        var target = NormalizeTarget(repositoryPath);
        var staged = NormalizeTarget(stagedPath);
        if (!string.Equals(Path.GetDirectoryName(target), Path.GetDirectoryName(staged), StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, staged, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("临时仓库必须位于目标仓库的同级目录。", nameof(stagedPath));
        }

        if (!Directory.Exists(staged))
        {
            throw new DirectoryNotFoundException(staged);
        }

        var backup = target + ".backup-" + Guid.NewGuid().ToString("N");
        var hadOriginal = Directory.Exists(target);
        if (hadOriginal)
        {
            Directory.Move(target, backup);
        }

        try
        {
            Directory.Move(staged, target);
        }
        catch
        {
            // 网络及校验都在改动原目录之前完成；目录切换失败时尽量原位恢复。
            if (hadOriginal)
            {
                Directory.Move(backup, target);
            }

            throw;
        }

        TryCleanup(backup);
    }

    private static string NormalizeTarget(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Path.GetDirectoryName(fullPath) == null)
        {
            throw new ArgumentException("不能替换根目录。", nameof(path));
        }

        if (Directory.Exists(fullPath) && File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("不能替换链接目录。");
        }

        return fullPath;
    }

    private static void TryCleanup(string path)
    {
        try
        {
            DirectoryHelper.DeleteDirectoryWithReadOnlyCheck(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败仅留下备份，不把已成功的更新报告为失败，更不能删除新仓库。
            System.Diagnostics.Debug.WriteLine($"清理仓库临时目录失败 {path}: {ex.Message}");
        }
    }
}
