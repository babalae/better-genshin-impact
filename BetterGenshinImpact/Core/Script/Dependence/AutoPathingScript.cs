using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Utils;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class AutoPathingScript
{
    private object? _config = null;
    private string _rootPath;

    public AutoPathingScript(string rootPath, object? config)
    {
        _config = config;
        _rootPath = rootPath;
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
            TaskControl.Logger.LogDebug(e,"执行地图追踪时候发生错误");
            TaskControl.Logger.LogError("执行地图追踪时候发生错误: {Msg}",e.Message);
        }
    }

    public async Task RunFile(string path)
    {
        try
        {
            var json = await new LimitedFile(_rootPath).ReadText(path);
            await Run(json);
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e,"读取文件时发生错误");
            TaskControl.Logger.LogError("读取文件时发生错误: {Msg}",e.Message);
        }
    }

    /// <summary>
    /// 从已订阅的内容中获取文件
    /// </summary>
    /// <param name="path">在 `\User\AutoPathing` 目录下获取文件</param>
    public async Task RunFileFromUser(string path)
    {
        var json = await new LimitedFile(Global.Absolute(@"User\AutoPathing")).ReadText(path);
        await Run(json);
    }

    /// <summary>
    /// 判断 AutoPathing 目录下的路径是否存在
    /// </summary>
    /// <param name="subPath">相对于 User\AutoPathing 的路径</param>
    /// <returns>存在返回 true，否则返回 false</returns>
    public bool IsExists(string subPath)
    {
        try
        {
            string autoPathingDir = Global.Absolute(@"User\AutoPathing");
            string targetPath = ScriptUtils.NormalizePath(autoPathingDir, subPath);
            return File.Exists(targetPath) || Directory.Exists(targetPath);
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogError("IsExists 异常: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 判断 AutoPathing 目录下的路径是否为文件
    /// </summary>
    /// <param name="subPath">相对于 User\AutoPathing 的路径</param>
    /// <returns>是文件返回 true，否则返回 false</returns>
    public bool IsFile(string subPath)
    {
        try
        {
            string autoPathingDir = Global.Absolute(@"User\AutoPathing");
            string targetPath = ScriptUtils.NormalizePath(autoPathingDir, subPath);
            return File.Exists(targetPath);
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogError("IsFile 异常: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 判断 AutoPathing 目录下的路径是否为文件夹
    /// </summary>
    /// <param name="subPath">相对于 User\AutoPathing 的路径</param>
    /// <returns>是文件夹返回 true，否则返回 false</returns>
    public bool IsFolder(string subPath)
    {
        try
        {
            string autoPathingDir = Global.Absolute(@"User\AutoPathing");
            string targetPath = ScriptUtils.NormalizePath(autoPathingDir, subPath);
            return Directory.Exists(targetPath);
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogError("IsFolder 异常: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 读取 AutoPathing 目录下指定文件夹的内容（非递归方式）
    /// </summary>
    /// <param name="subPath">相对于 User\AutoPathing 的子目录路径，默认为空（根目录）</param>
    /// <returns>文件夹内所有文件和文件夹的相对路径数组，出错时返回空数组</returns>
    public string[] ReadPathSync(string subPath = "")
    {
        try
        {
            string autoPathingDir = Global.Absolute(@"User\AutoPathing");

            // 拼接子路径
            string targetDir = string.IsNullOrEmpty(subPath)
                ? autoPathingDir
                : ScriptUtils.NormalizePath(autoPathingDir, subPath);

            if (!Directory.Exists(targetDir))
            {
                TaskControl.Logger.LogWarning("ReadPathSync 目录不存在: {Path}", targetDir);
                return Array.Empty<string>();
            }

            // 获取指定文件夹下的所有文件（非递归）
            string[] files = Directory.GetFiles(targetDir, "*", SearchOption.TopDirectoryOnly);

            // 获取指定文件夹下的所有子文件夹（非递归）
            string[] directories = Directory.GetDirectories(targetDir, "*", SearchOption.TopDirectoryOnly);

            // 合并文件和文件夹路径，统一使用 / 作为路径分隔符
            return files.Concat(directories)
                .Select(path => Path.GetRelativePath(targetDir, path).Replace('\\', '/'))
                .ToArray();
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogError("ReadPathSync 异常: {Message}", ex.Message);
            return Array.Empty<string>();
        }
    }
}