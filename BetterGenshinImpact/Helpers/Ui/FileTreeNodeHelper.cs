using BetterGenshinImpact.Model;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.Helpers.Ui;

public class FileTreeNodeHelper
{
    public static string[] AllowedExtensions { get; set; } = [".json"];

    public static FileTreeNode<T> LoadDirectory<T>(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var rootDirectoryInfo = new DirectoryInfo(directoryPath);
        var rootNode = new FileTreeNode<T>
        {
            FileName = rootDirectoryInfo.Name,
            IsDirectory = true,
            FilePath = rootDirectoryInfo.FullName
        };

        LoadSubDirectories(rootDirectoryInfo, rootNode);
        return rootNode;
    }

    public static void LoadSubDirectories<T>(DirectoryInfo directoryInfo, FileTreeNode<T> parentNode)
    {
        foreach (var directory in directoryInfo.GetDirectories())
        {
            var directoryNode = new FileTreeNode<T>
            {
                FileName = directory.Name,
                IsDirectory = true,
                FilePath = directory.FullName
            };

            parentNode.Children.Add(directoryNode);
            LoadSubDirectories(directory, directoryNode);
        }

        foreach (var file in directoryInfo.GetFiles().Where(f => AllowedExtensions.Contains(f.Extension)))
        {
            var fileNode = new FileTreeNode<T>
            {
                FileName = Path.GetFileNameWithoutExtension(file.Name),
                IsDirectory = false,
                FilePath = file.FullName
            };

            parentNode.Children.Add(fileNode);
        }
    }
}
