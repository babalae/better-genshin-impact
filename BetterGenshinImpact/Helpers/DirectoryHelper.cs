using System.IO;

namespace BetterGenshinImpact.Helpers;

public class DirectoryHelper
{
    /// <summary>
    /// 删除指定目录（如果存在）
    /// </summary>
    /// <param name="directoryPath">要删除的目录路径</param>
    /// <param name="recursive">是否递归删除子目录和文件，默认为true</param>
    public static void DeleteDirectory(string directoryPath, bool recursive = true)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive);
        }
    }
    
    public static void DeleteReadOnlyDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            // 获取目录信息
            var directoryInfo = new DirectoryInfo(directoryPath);

            // 移除目录及其内容的只读属性
            RemoveReadOnlyAttribute(directoryInfo);

            // 删除目录
            Directory.Delete(directoryPath, true);
        }
    }

    public static void DeleteDirectoryWithReadOnlyCheck(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            // 获取目录信息
            var directoryInfo = new DirectoryInfo(directoryPath);

            // 递归删除目录及其内容
            DeleteDirectory(directoryInfo);
        }
    }

    private static void DeleteDirectory(DirectoryInfo directoryInfo)
    {
        
        //通过软链接生成的目录，直接删除该链接目录，而不涉及其文件本体
        var attributes = directoryInfo.Attributes;
        if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
        {
            directoryInfo.Delete();
            return;
        }

        // 递归处理子目录
        foreach (var subDirectory in directoryInfo.GetDirectories())
        {
            DeleteDirectory(subDirectory);
        }

        // 移除文件的只读属性并删除文件
        foreach (var file in directoryInfo.GetFiles())
        {
            if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }
            file.Delete();
        }

        // 移除目录的只读属性并删除目录
        if (directoryInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
        {
            directoryInfo.Attributes &= ~FileAttributes.ReadOnly;
        }
        directoryInfo.Delete();
    }

    private static void RemoveReadOnlyAttribute(DirectoryInfo directoryInfo)
    {
        // 移除目录的只读属性
        if (directoryInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
        {
            directoryInfo.Attributes &= ~FileAttributes.ReadOnly;
        }

        // 移除文件的只读属性
        foreach (var file in directoryInfo.GetFiles())
        {
            if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }
        }

        // 递归处理子目录
        foreach (var subDirectory in directoryInfo.GetDirectories())
        {
            RemoveReadOnlyAttribute(subDirectory);
        }
    }
    
    public static void CopyDirectory(string sourceDir, string destDir)
    {
        // 创建目标目录
        Directory.CreateDirectory(destDir);

        // 获取源目录中的所有文件
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true); // 覆盖同名文件
        }

        // 获取源目录中的所有子目录
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir); // 递归拷贝子目录
        }
    }
    
    /// <summary>
    /// 递归删除指定目录及其所有子目录和文件
    /// </summary>
    /// <param name="directoryPath">要删除的目录的路径</param>
    public static void DeleteDirectoryRecursively(string directoryPath)
    {
        // 检查目录是否存在
        if (Directory.Exists(directoryPath))
        {
            // 获取目录中的所有子目录
            string[] subDirectories = Directory.GetDirectories(directoryPath);
            foreach (string subDirectory in subDirectories)
            {
                // 递归调用删除子目录
                DeleteDirectoryRecursively(subDirectory);
            }

            // 获取目录中的所有文件
            string[] files = Directory.GetFiles(directoryPath);
            foreach (string file in files)
            {
                // 删除文件
                File.Delete(file);
            }

            // 删除空目录
            Directory.Delete(directoryPath);
        }
    }
}
