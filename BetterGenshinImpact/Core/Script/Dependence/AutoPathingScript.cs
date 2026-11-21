using System;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Dependence.Model;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class AutoPathingScript
{
    private object? _config = null;
    private string _rootPath;
    private readonly LimitedFile _autoPathingFile;

    public AutoPathingScript(string rootPath, object? config)
    {
        _config = config;
        _rootPath = rootPath;
        _autoPathingFile = new LimitedFile(Global.Absolute(@"User\AutoPathing"));
    }

    /// <summary>
    /// 通过 JSON 字符串执行地图追踪，返回执行结果
    /// </summary>
    /// <param name="json">地图追踪路径的 JSON 内容</param>
    /// <returns>执行结果（是否成功等）</returns>
    public async Task<PathingRunResult> Run(string json)
    {
        PathingTask task;
        try
        {
            task = PathingTask.BuildFromJson(json);
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "地图追踪 JSON 解析失败");
            TaskControl.Logger.LogError("地图追踪 JSON 解析失败: {Msg}", e.Message);
            return PathingRunResult.Fail($"地图追踪 JSON 解析失败: {e.Message}");
        }

        try
        {
            var pathExecutor = new PathExecutor(CancellationContext.Instance.Cts.Token);
            if (_config != null && _config is PathingPartyConfig patyConfig)
            {
                pathExecutor.PartyConfig = patyConfig;
            }

            await pathExecutor.Pathing(task);

            // 只有真正正常完整走完所有路径才算成功
            if (pathExecutor.SuccessEnd && !pathExecutor.EndByHandledException)
            {
                return PathingRunResult.Ok();
            }

            // 与 Pathing 使用同一个取消令牌，独立运行时 Pathing 内部会静默吞掉取消异常，这里补判定
            if (CancellationContext.Instance.Cts.IsCancellationRequested)
            {
                TaskControl.Logger.LogInformation("地图追踪被手动取消");
                return PathingRunResult.Fail("地图追踪被手动取消");
            }

            // 中途放弃路径（HandledException）
            if (pathExecutor.EndByHandledException)
            {
                return PathingRunResult.Fail("点位无法识别，放弃路线");
            }

            return PathingRunResult.Fail("此追踪脚本未正常走完！");
        }
        catch (OperationCanceledException)
        {
            TaskControl.Logger.LogInformation("地图追踪被手动取消");
            return PathingRunResult.Fail("地图追踪被手动取消");
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "执行地图追踪时候发生错误");
            TaskControl.Logger.LogError("执行地图追踪时候发生错误: {Msg}", e.Message);
            return PathingRunResult.Fail($"地图追踪执行失败: {e.Message}");
        }
    }

    /// <summary>
    /// 通过脚本目录下的路径文件执行地图追踪，返回执行结果
    /// </summary>
    /// <param name="path">相对于脚本根目录的路径文件路径</param>
    /// <returns>执行结果（是否成功等）</returns>
    public async Task<PathingRunResult> RunFile(string path)
    {
        var limitedFile = new LimitedFile(_rootPath);
        if (!limitedFile.IsFile(path))
        {
            TaskControl.Logger.LogError("地图追踪文件不存在: {Path}", path);
            return PathingRunResult.Fail($"地图追踪文件不存在: {path}");
        }

        var json = await limitedFile.ReadText(path);
        // ReadText 读取失败时返回空字符串，路径 JSON 内容非空，空串视为读取失败
        if (string.IsNullOrEmpty(json))
        {
            TaskControl.Logger.LogError("地图追踪文件读取失败: {Path}", path);
            return PathingRunResult.Fail($"地图追踪文件读取失败: {path}");
        }

        return await Run(json);
    }

    /// <summary>
    /// 从已订阅的内容中获取文件并执行地图追踪，返回执行结果
    /// </summary>
    /// <param name="path">在 `\User\AutoPathing` 目录下获取文件</param>
    /// <returns>执行结果（是否成功等）</returns>
    public async Task<PathingRunResult> RunFileFromUser(string path)
    {
        if (!AutoPathingFile.IsFile(path))
        {
            TaskControl.Logger.LogError("地图追踪文件不存在: {Path}", path);
            return PathingRunResult.Fail($"地图追踪文件不存在: {path}");
        }

        var json = await AutoPathingFile.ReadText(path);
        // ReadText 读取失败时返回空字符串，路径 JSON 内容非空，空串视为读取失败
        if (string.IsNullOrEmpty(json))
        {
            TaskControl.Logger.LogError("地图追踪文件读取失败: {Path}", path);
            return PathingRunResult.Fail($"地图追踪文件读取失败: {path}");
        }

        return await Run(json);
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
    /// LimitedFile 实例，用于操作 AutoPathing 目录
    /// </summary>
    private LimitedFile AutoPathingFile => _autoPathingFile;
}