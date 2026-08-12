using System;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class AutoPathingScript
{
    private object? _config = null;
    private string _rootPath;
    private readonly LimitedFile _autoPathingFile;
    private readonly Action<string, Exception> _logFailure;

    public AutoPathingScript(string rootPath, object? config)
        : this(rootPath, config, new LimitedFile(Global.Absolute(@"User\AutoPathing")), LogFailure)
    {
    }

    internal AutoPathingScript(
        string rootPath,
        object? config,
        LimitedFile autoPathingFile,
        Action<string, Exception> logFailure)
    {
        _config = config;
        _rootPath = rootPath;
        _autoPathingFile = autoPathingFile;
        _logFailure = logFailure;
    }

    public async Task Run(string json)
    {
        try
        {
            var task = PathingTask.BuildFromJson(json);
            var pathExecutor = new PathExecutor(CancellationContext.Instance.Cts.Token);
            if (_config != null && _config is PathingPartyConfig patyConfig)
            {
                pathExecutor.PartyConfig = patyConfig;
            }

            await pathExecutor.Pathing(task);
        }
        catch (Exception e)
        {
            _logFailure("执行地图追踪时候发生错误", e);
            throw;
        }
    }

    public async Task RunFile(string path)
    {
        string json;
        try
        {
            json = await new LimitedFile(_rootPath).ReadTextOrThrow(path);
        }
        catch (Exception e)
        {
            _logFailure("读取文件时发生错误", e);
            throw;
        }

        await Run(json);
    }

    /// <summary>
    /// 从已订阅的内容中获取文件
    /// </summary>
    /// <param name="path">在 `\User\AutoPathing` 目录下获取文件</param>
    public async Task RunFileFromUser(string path)
    {
        var json = await AutoPathingFile.ReadTextOrThrow(path);
        await Run(json);
    }

    /// <summary>
    /// 判断 AutoPathing 目录下的路径是否存在
    /// </summary>
    /// <param name="subPath">相对于 User\AutoPathing 的路径</param>
    /// <returns>存在返回 true，否则返回 false</returns>
    public bool IsExists(string subPath) => AutoPathingFile.IsExists(subPath);

    /// <summary>
    /// 判断 AutoPathing 目录下的路径是否为文件
    /// </summary>
    /// <param name="subPath">相对于 User\AutoPathing 的路径</param>
    /// <returns>是文件返回 true，否则返回 false</returns>
    public bool IsFile(string subPath) => AutoPathingFile.IsFile(subPath);

    /// <summary>
    /// 判断 AutoPathing 目录下的路径是否为文件夹
    /// </summary>
    /// <param name="subPath">相对于 User\AutoPathing 的路径</param>
    /// <returns>是文件夹返回 true，否则返回 false</returns>
    public bool IsFolder(string subPath) => AutoPathingFile.IsFolder(subPath);

    /// <summary>
    /// 读取 AutoPathing 目录下指定文件夹的内容（非递归方式）
    /// 目录不存在时返回空数组，不会自动创建目录
    /// </summary>
    /// <param name="subPath">相对于 User\AutoPathing 的子目录路径，默认为相对根目录</param>
    /// <returns>文件夹内所有文件和文件夹的相对路径数组，出错时返回空数组</returns>
    public string[] ReadPathSync(string subPath = "./") => AutoPathingFile.ReadPathSync(subPath);

    /// <summary>
    /// 读取 AutoPathing 目录下指定文件的文本内容
    /// </summary>
    /// <param name="subPath">相对于 User\AutoPathing 的文件路径</param>
    /// <returns>文件文本内容，读取失败时返回空字符串</returns>
    public string ReadTextSync(string subPath) => AutoPathingFile.ReadTextSync(subPath);

    /// <summary>
    /// 读取 AutoPathing 目录下指定文件的文本内容，读取失败时保留原始异常。
    /// </summary>
    /// <param name="subPath">相对于 User\AutoPathing 的文件路径</param>
    /// <returns>文件文本内容</returns>
    public string ReadTextSyncOrThrow(string subPath) => AutoPathingFile.ReadTextSyncOrThrow(subPath);

    /// <summary>
    /// LimitedFile 实例，用于操作 AutoPathing 目录
    /// </summary>
    private LimitedFile AutoPathingFile => _autoPathingFile;

    private static void LogFailure(string message, Exception exception)
    {
        TaskControl.Logger.LogDebug(exception, message);
        TaskControl.Logger.LogError("{Message}: {ExceptionMessage}", message, exception.Message);
    }
}
