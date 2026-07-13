using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    
        /// <summary>
    /// 根据路径过滤树形结构，排除指定路径的节点
    /// </summary>
    /// <typeparam name="T">节点数据类型</typeparam>
    /// <param name="nodes">树形结构的根节点集合</param>
    /// <param name="folderNames">要排除的路径集合</param>
    /// <returns>过滤后的树形结构</returns>
    public static ObservableCollection<FileTreeNode<T>> FilterTree<T>(
        ObservableCollection<FileTreeNode<T>> nodes,
        List<string> folderNames)
    {
        // 递归过滤节点
        ObservableCollection<FileTreeNode<T>> FilterNodes(ObservableCollection<FileTreeNode<T>> inputNodes, List<string> paths)
        {
            var filteredNodes = new ObservableCollection<FileTreeNode<T>>();

            foreach (var node in inputNodes)
            {
                // 获取当前层需要排除的路径
                var matchedPaths = paths
                    .Where(path => IsPathMatch(node.FileName ??"", path))
                    .Select(path => GetRemainingPath(node.FileName ?? "" , path))
                    .Where(remainingPath => remainingPath != null)
                    .ToList();

                // 如果当前路径完全匹配，跳过当前节点
                if (matchedPaths.Any(path => path == ""))
                {
                    continue;
                }

                // 递归对子节点过滤 
                node.Children = FilterNodes(node.Children, matchedPaths);

                // 添加过滤后的节点
                filteredNodes.Add(node);
            }

            return filteredNodes;
        }

        return FilterNodes(nodes, folderNames);
    }

    /// <summary>
    /// 判断文件路径是否匹配多级路径的前缀
    /// </summary>
    /// <param name="fileName">当前节点路径</param>
    /// <param name="path">目标路径</param>
    /// <returns>是否匹配</returns>
    private static bool IsPathMatch(string fileName, string path)
    {
        // 匹配路径是否以指定前缀开始，路径分隔符对齐
        return path.StartsWith(fileName, StringComparison.OrdinalIgnoreCase) 
               && (path.Length == fileName.Length || path[fileName.Length] == '\\');
    }

    /// <summary>
    /// 获取路径中去掉当前节点后的剩余路径
    /// </summary>
    /// <param name="fileName">当前节点路径</param>
    /// <param name="path">完整路径</param>
    /// <returns>剩余路径，如果不匹配返回 null</returns>
    private static string? GetRemainingPath(string fileName, string path)
    {
        if (IsPathMatch(fileName, path))
        {
            return path.Length > fileName.Length ? path.Substring(fileName.Length + 1) : "";
        }
        return null;
    }
    public static ObservableCollection<FileTreeNode<T>> FilterEmptyNodes<T>(ObservableCollection<FileTreeNode<T>> nodes)
    {
        // 递归过滤节点
        ObservableCollection<FileTreeNode<T>> Filter(ObservableCollection<FileTreeNode<T>> inputNodes)
        {
            var filteredNodes = new ObservableCollection<FileTreeNode<T>>();

            foreach (var node in inputNodes)
            {
                // 递归处理子节点
                node.Children = Filter(node.Children);

                // 如果是目录并且没有子节点，跳过当前节点
                if (node.IsDirectory && !node.Children.Any())
                {
                    continue;
                }

                // 其他情况保留节点
                filteredNodes.Add(node);
            }

            return filteredNodes;
        }

        return Filter(nodes);
    }
}
