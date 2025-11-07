using BetterGenshinImpact.Core.Script.Utils;
using System;
using System.IO;
using System.Threading.Tasks;
using OpenCvSharp;
using System.Linq;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class LimitedFile(string rootPath)
{
    /// <summary>
    /// 读取指定文件夹内所有文件和文件夹的路径（非递归方式）。
    /// </summary>
    /// <param name="folderPath">文件夹路径（相对于根目录）</param>
    /// <returns>文件夹内所有文件和文件夹的路径数组</returns>
    public string[] ReadPathSync(string folderPath)
    {
        try
        {
            // 对传入的文件夹路径进行标准化
            string normalizedFolderPath = NormalizePath(folderPath);

            // 确保目录存在
            if (!Directory.Exists(normalizedFolderPath))
            {
                Directory.CreateDirectory(normalizedFolderPath);
            }

            // 获取指定文件夹下的所有文件（非递归）
            string[] files = Directory.GetFiles(normalizedFolderPath, "*", SearchOption.TopDirectoryOnly);

            // 获取指定文件夹下的所有子文件夹（非递归）
            string[] directories = Directory.GetDirectories(normalizedFolderPath, "*", SearchOption.TopDirectoryOnly);

            // 合并文件和文件夹路径
            string[] combined = files.Concat(directories).ToArray();

            // 将绝对路径转换为相对于 rootPath 的相对路径
            return combined.Select(path => Path.GetRelativePath(rootPath, path)).ToArray();
        }
        catch (Exception ex)
        {
            // 记录异常并返回空数组
            TaskControl.Logger.LogError("ReadPathSync 异常: {Message}", ex.Message);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 判断指定路径是否为文件夹。
    /// </summary>
    /// <param name="path">文件或文件夹路径（相对于根目录）。</param>
    /// <returns>如果该路径是文件夹则返回 true，否则返回 false。</returns>
    public bool IsFolder(string path)
    {
        try
        {
            // 对传入的路径进行标准化处理
            string normalizedPath = NormalizePath(path);

            // 使用 Directory.Exists 判断标准化路径是否为文件夹
            return Directory.Exists(normalizedPath);
        }
        catch (Exception ex)
        {
            // 记录异常并返回 false
            TaskControl.Logger.LogError("IsFolder 异常: {Message}", ex.Message);
            return false;
        }
    }

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
        try
        {
            path = NormalizePath(path);
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            // 记录异常并返回空字符串
            TaskControl.Logger.LogError("ReadTextSync 异常: {Message}", ex.Message);
            return string.Empty;
        }
    }

    /// <summary>
    /// Read all text from a file.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <returns>Text read from file.</returns>
    public async Task<string> ReadText(string path)
    {
        try
        {
            path = NormalizePath(path);
            var ret = await File.ReadAllTextAsync(path);
            return ret;
        }
        catch (Exception ex)
        {
            // 记录异常并返回空字符串
            TaskControl.Logger.LogError("ReadText 异常: {Message}", ex.Message);
            return string.Empty;
        }
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
        try
        {
            path = NormalizePath(path);
            using var stream = File.OpenRead(path);
            var mat = Mat.FromStream(stream, ImreadModes.Color);
            return mat;
        }
        catch (Exception ex)
        {
            // 记录异常并返回空的Mat
            TaskControl.Logger.LogError("ReadImageMatSync 异常: {Message}", ex.Message);
            return new Mat();
        }
    }
    
    /// <summary>
    /// 读取图像文件为Mat对象，并调整到指定尺寸
    /// </summary>
    /// <param name="path">图像文件路径</param>
    /// <param name="width">调整后的宽度</param>
    /// <param name="height">调整后的高度</param>
    /// <param name="interpolation">插值算法，默认为双线性插值(1)</param>
    /// <returns>调整尺寸后的Mat图像对象</returns>
    /// <remarks>
    /// 支持的插值算法：
    /// <list type="bullet">
    /// <item><description>最近邻插值 (0)</description></item>
    /// <item><description>双线性插值 (1) - 默认</description></item>
    /// <item><description>双三次插值 (2)</description></item>
    /// <item><description>像素区域关系重采样 (3)</description></item>
    /// <item><description>Lanczos插值 (4)</description></item>
    /// <item><description>精确双线性插值 (5)</description></item>
    /// </list>
    /// </remarks>
    public Mat ReadImageMatWithResizeSync(string path, double width, double height, int interpolation = 1)
    {
        try
        {
            if (width <= 0 || height <= 0)
            {
                throw new Exception("ReadImageMatWithResizeSync: 宽度和高度必须为正数");
            }

            if (interpolation is < 0 or > 5)
            {
                throw new Exception($"ReadImageMatWithResizeSync: 不支持的插值算法 {interpolation}");
            }

            path = NormalizePath(path);
            using var stream = File.OpenRead(path);
            using var mat = Mat.FromStream(stream, ImreadModes.Color);
            var rsz = new Mat();
            Cv2.Resize(mat, rsz, new Size(width, height), 0, 0, (InterpolationFlags)interpolation);
            return rsz;
        }
        catch (Exception ex)
        {
            // 记录异常并返回空的Mat
            TaskControl.Logger.LogError("ReadImageMatWithResizeSync 异常: {Message}", ex.Message);
            return new Mat();
        }
    }

    
    /// <summary>
    /// 允许的文件扩展名白名单
    /// </summary>
    private readonly string[] _allowedExtensions = [".txt", ".json", ".log", ".csv", ".xml", ".html", ".css", ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".webp"];
    
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
        try
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
        catch (Exception ex)
        {
            // 记录异常并返回 false
            TaskControl.Logger.LogError("IsValid 异常: {Message}", ex.Message);
            return false;
        }
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
    
    /// <summary>
    /// 同步写入图片到文件（默认PNG格式）
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="mat">OpenCV Mat对象</param>
    /// <returns>是否写入成功</returns>
    public bool WriteImageSync(string path, Mat mat)
    {
        try
        {
            // 自动追加.png后缀
            path = EnsureImageExtension(path);
            path = NormalizePath(path);
            if (!IsValidImagePath(path))
            {
                return false;
            }

            // 确保目录存在
            string? directoryPath = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // 使用OpenCV保存图片（默认PNG格式）
            Cv2.ImWrite(path, mat);
            return true;
        }
        catch (Exception ex)
        {
            // 记录异常并返回 false
            TaskControl.Logger.LogError("WriteImageSync 异常: {Message}", ex.Message);
            return false;
        }
    }
    
    /// <summary>
    /// 确保图片路径有正确的扩展名，如果没有则自动追加.png
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>带有正确扩展名的路径</returns>
    private string EnsureImageExtension(string path)
    {
        string extension = Path.GetExtension(path).ToLower();
        string[] imageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".webp"];
        
        // 如果已经有图片扩展名，直接返回
        if (Array.Exists(imageExtensions, ext => ext == extension))
        {
            return path;
        }
        
        // 如果没有扩展名，自动追加.png
        return path + ".png";
    }
    
    /// <summary>
    /// 验证图片路径是否合法
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>是否合法</returns>
    private bool IsValidImagePath(string path)
    {
        // 验证文件扩展名
        string extension = Path.GetExtension(path).ToLower();
        string[] imageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".webp"];
        
        if (!Array.Exists(imageExtensions, ext => ext == extension))
        {
            return false;
        }
        
        return true;
    }
}
