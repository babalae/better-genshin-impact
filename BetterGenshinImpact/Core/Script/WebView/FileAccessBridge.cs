using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Script.WebView;

public class FileSystemItem
{
    public string Name { get; set; }
    public string RelativePath { get; set; }
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
}

[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class FileAccessBridge
{
    private readonly string _allowedDirectory;
    private readonly string _normalizedAllowedPath;

    public FileAccessBridge(string allowedDirectory)
    {
        if (string.IsNullOrEmpty(allowedDirectory))
        {
            throw new ArgumentException("允许访问的目录不能为空", nameof(allowedDirectory));
        }

        if (!Directory.Exists(allowedDirectory))
        {
            throw new ArgumentException("目录不存在", nameof(allowedDirectory));
        }

        _allowedDirectory = Path.GetFullPath(allowedDirectory);
        _normalizedAllowedPath = _allowedDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private bool IsPathAllowed(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var normalizedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalizedPath.StartsWith(_normalizedAllowedPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public string ReadFile(string relativePath)
    {
        try
        {
            var fullPath = Path.Combine(_allowedDirectory, relativePath);
            if (!IsPathAllowed(fullPath))
                throw new UnauthorizedAccessException($"访问路径 '{relativePath}' 被拒绝");

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"文件 '{relativePath}' 不存在");

            return File.ReadAllText(fullPath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            throw new Exception($"读取文件失败: {ex.Message}");
        }
    }

    public void WriteFile(string relativePath, string content)
    {
        try
        {
            var fullPath = Path.Combine(_allowedDirectory, relativePath);
            if (!IsPathAllowed(fullPath))
                throw new UnauthorizedAccessException($"访问路径 '{relativePath}' 被拒绝");

            // var directory = Path.GetDirectoryName(fullPath);
            // if (!string.IsNullOrEmpty(directory))
            //     Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, content, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            throw new Exception($"写入文件失败: {ex.Message}");
        }
    }

    public bool FileExists(string relativePath)
    {
        try
        {
            var fullPath = Path.Combine(_allowedDirectory, relativePath);
            if (!IsPathAllowed(fullPath))
                return false;

            return File.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    public bool DirectoryExists(string relativePath)
    {
        try
        {
            var fullPath = Path.Combine(_allowedDirectory, relativePath);
            if (!IsPathAllowed(fullPath))
                return false;

            return Directory.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    public string ListItems(string relativePath = "")
    {
        try
        {
            var fullPath = string.IsNullOrEmpty(relativePath)
                ? _allowedDirectory
                : Path.Combine(_allowedDirectory, relativePath);

            if (!IsPathAllowed(fullPath))
                throw new UnauthorizedAccessException($"访问路径 '{relativePath}' 被拒绝");

            if (!Directory.Exists(fullPath))
                return JsonConvert.SerializeObject(new List<FileSystemItem>());

            var items = new List<FileSystemItem>();

            // 添加目录
            var directories = Directory.GetDirectories(fullPath);
            foreach (var directory in directories)
            {
                var relativeName = Path.GetRelativePath(_allowedDirectory, directory);
                var dirInfo = new DirectoryInfo(directory);
                items.Add(new FileSystemItem
                {
                    Name = Path.GetFileName(directory),
                    RelativePath = relativeName,
                    IsDirectory = true,
                    Size = 0,
                    LastModified = dirInfo.LastWriteTime
                });
            }

            // 添加文件
            var files = Directory.GetFiles(fullPath);
            foreach (var file in files)
            {
                var relativeName = Path.GetRelativePath(_allowedDirectory, file);
                var fileInfo = new FileInfo(file);
                items.Add(new FileSystemItem
                {
                    Name = Path.GetFileName(file),
                    RelativePath = relativeName,
                    IsDirectory = false,
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime
                });
            }

            return JsonConvert.SerializeObject(items);
        }
        catch (Exception ex)
        {
            throw new Exception($"列出项目失败: {ex.Message}");
        }
    }
}