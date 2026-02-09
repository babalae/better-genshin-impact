using BetterGenshinImpact.Helpers;
﻿using System;
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
            TaskControl.Logger.LogDebug(e,Lang.S["Gen_10177_1223f5"]);
            TaskControl.Logger.LogError(Lang.S["Gen_10176_09de4d"],e.Message);
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
            TaskControl.Logger.LogDebug(e,Lang.S["Gen_10175_0d917f"]);
            TaskControl.Logger.LogError(Lang.S["Gen_10174_9ed3c8"],e.Message);
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
}