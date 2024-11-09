using System.IO;

namespace BetterGenshinImpact.Helpers;

public class DirectoryHelper
{
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
}
