﻿using Microsoft.ClearScript;

namespace BetterGenshinImpact.Core.Script.Dependence.Model;

/// <summary>
/// 独立任务
/// </summary>
public class SoloTask
{
    /// <summary>
    /// 独立任务名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 独立任务配置
    /// </summary>
    public object? Config { get; set; }

    public SoloTask(string name)
    {
        Name = name;
    }

    public SoloTask(string name, ScriptObject config)
    {
        Name = name;
        Config = config;
    }
}
