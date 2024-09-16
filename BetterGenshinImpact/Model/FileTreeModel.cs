using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Wpf.Ui.Controls;
using static Vanara.PInvoke.Gdi32;

namespace BetterGenshinImpact.Model;

public class FileTreeModel<T>(string rootPath) : ITreeModel where T : class
{
    public string? Name { get; set; }

    public DirectoryInfo? Directory { get; set; }

    public T? Data { get; set; }

    public IEnumerable GetChildren(object? parent)
    {
        parent ??= new FileTreeModel<T>(rootPath)
        {
            Directory = new DirectoryInfo(rootPath)
        };


        if (parent is FileTreeModel<T> { Directory: not null } model)
        {
            // 返回子目录
            foreach (var dir in model.Directory.GetDirectories())
            {
                yield return new FileTreeModel<T>(rootPath)
                {
                    Name = dir.Name,
                    Directory = dir
                };
            }

            // 返回文件
            foreach (var file in model.Directory.GetFiles())
            {
                var m = new FileTreeModel<T>(rootPath)
                {
                    Name = file.Name,
                };
                if (typeof(T) == typeof(PathingTask))
                {
                    m.Data = PathingTask.BuildFromFilePath(file.FullName) as T;
                }
                yield return m;
            }
        }
    }

    public bool HasChildren(object parent)
    {
        if (parent is FileTreeModel<T> { Directory: not null } model)
        {
            try
            {
                // 检查是否有子目录或文件
                return model.Directory.GetDirectories().Any() || model.Directory.GetFiles().Any();
            }
            catch (UnauthorizedAccessException)
            {
                // 捕获没有权限的异常并返回 false
                return false;
            }
            catch (Exception ex)
            {
                // 处理其他可能的异常
                Console.WriteLine($"An error occurred: {ex.Message}");
                return false;
            }
        }
        return false;
    }
}
