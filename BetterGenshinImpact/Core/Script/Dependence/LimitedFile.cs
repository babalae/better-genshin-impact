using BetterGenshinImpact.Core.Script.Utils;
using System;
using System.IO;
using System.Threading.Tasks;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class LimitedFile(string rootPath)
{
    /// <summary>
    /// Normalize and validate a path.
    /// </summary>
    private string NormalizePath(string path)
    {
        return ScriptUtils.NormalizePath(rootPath, path);
    }

    /// <summary>
    /// Read all text from a file.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <returns>Text read from file.</returns>
    public string ReadTextSync(string path)
    {
        path = NormalizePath(path);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Read all text from a file.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <returns>Text read from file.</returns>
    public async Task<string> ReadText(string path)
    {
        path = NormalizePath(path);
        var ret = await File.ReadAllTextAsync(path);
        return ret;
    }

    /// <summary>
    /// Read all text from a file.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <param name="callbackFunc">Callback function.</param>
    /// <returns>Text read from file.</returns>
    public async Task<string> ReadText(string path, dynamic callbackFunc)
    {
        try
        {
            path = NormalizePath(path);
            var ret = await File.ReadAllTextAsync(path);
            callbackFunc(null, ret);
            return ret;
        }
        catch (Exception ex)
        {
            callbackFunc(ex.ToString(), null);
            return string.Empty;
        }
    }
    
    /// <summary>
    /// 读取Mat图片
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public Mat ReadImageMatSync(string path)
    {
        path = NormalizePath(path);
        var mat = Mat.FromStream(File.OpenRead(path), ImreadModes.Color);
        return mat;
    }
    
    /// <summary>
    /// 允许的文件扩展名白名单
    /// </summary>
    private readonly string[] _allowedExtensions = [".txt", ".json", ".log", ".csv", ".xml", ".html", ".css"];
    
    /// <summary>
    /// 最大允许的文件大小（字节）
    /// </summary>
    private const long MaxFileSize = 999 * 1024 * 1024; // 999 MB
    
    /// <summary>
    /// 验证路径和内容是否合法，并确保目录存在
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="content">要写入的内容，如果为null则只验证路径</param>
    /// <returns>是否合法</returns>
    private bool IsValid(string path, string? content = null)
    {
        // 验证文件扩展名
        string extension = Path.GetExtension(path).ToLower();
        if (!Array.Exists(_allowedExtensions, ext => ext == extension))
        {
            return false;
        }
        
        // 确保目录存在
        var normalizedPath = NormalizePath(path);
        string? directoryPath = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
        
        // 如果提供了内容，验证内容是否合法
        if (content != null)
        {
            // 检查文件大小
            if (content.Length > MaxFileSize)
            {
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// 同步写入文本到文件
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="content">要写入的内容</param>
    /// <param name="append">是否追加到文件末尾，默认为false（覆盖）</param>
    /// <returns>是否写入成功</returns>
    public bool WriteTextSync(string path, string content, bool append = false)
    {
        try
        {
            path = NormalizePath(path);
            if (!IsValid(path, content))
            {
                return false;
            }

            if (append && File.Exists(path))
            {
                File.AppendAllText(path, content);
            }
            else
            {
                File.WriteAllText(path, content);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 异步写入文本到文件
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="content">要写入的内容</param>
    /// <param name="append">是否追加到文件末尾，默认为false（覆盖）</param>
    /// <returns>是否写入成功</returns>
    public async Task<bool> WriteText(string path, string content, bool append = false)
    {
        try
        {
            path = NormalizePath(path);
            if (!IsValid(path, content))
            {
                return false;
            }
            
            if (append && File.Exists(path))
            {
                await File.AppendAllTextAsync(path, content);
            }
            else
            {
                await File.WriteAllTextAsync(path, content);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 异步写入文本到文件（带回调）
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="content">要写入的内容</param>
    /// <param name="callbackFunc">回调函数</param>
    /// <param name="append">是否追加到文件末尾，默认为false（覆盖）</param>
    /// <returns>是否写入成功</returns>
    public async Task<bool> WriteText(string path, string content, dynamic callbackFunc, bool append = false)
    {
        try
        {
            path = NormalizePath(path);
            if (!IsValid(path, content))
            {
                callbackFunc("路径不合法或文件内容不合法", null);
                return false;
            }
            
            if (append && File.Exists(path))
            {
                await File.AppendAllTextAsync(path, content);
            }
            else
            {
                await File.WriteAllTextAsync(path, content);
            }
            callbackFunc(null, true);
            return true;
        }
        catch (Exception ex)
        {
            callbackFunc(ex.ToString(), null);
            return false;
        }
    }
}
