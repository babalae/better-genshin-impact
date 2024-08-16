using BetterGenshinImpact.Core.Script.Project;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Core.Script.Group;

public partial class ScriptGroupProject : ObservableObject
{
    [ObservableProperty]
    private int _index;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string FolderName { get; set; } = string.Empty;

    [ObservableProperty]
    private string _type = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [JsonIgnore]
    public string StatusDesc => ScriptGroupProjectExtensions.StatusDescriptions[Status];

    /// <summary>
    /// 执行周期
    /// 不在 ScheduleDescriptions 中则会被视为自定义Cron表达式
    /// </summary>
    [ObservableProperty]
    private string _schedule = string.Empty;

    [JsonIgnore]
    public string ScheduleDesc => ScriptGroupProjectExtensions.ScheduleDescriptions.GetValueOrDefault(Schedule, "自定义周期");

    [ObservableProperty]
    private string _runNum = string.Empty;

    [JsonIgnore]
    public ScriptProject? Project { get; set; }

    public ScriptGroupProject()
    {
    }

    public ScriptGroupProject(ScriptProject project)
    {
        Id = project.Manifest.Id;
        Name = project.Manifest.Name;
        FolderName = project.FolderName;
        Status = "Enabled";
        Schedule = "Daily";
        Project = project;
    }

    /// <summary>
    /// 通过 FolderName 查找 ScriptProject
    /// </summary>
    public void BuildScriptProjectRelation()
    {
        if (string.IsNullOrEmpty(FolderName))
        {
            throw new Exception("FolderName 为空");
        }
        Project = new ScriptProject(FolderName);
    }
}

public static class ScriptGroupProjectExtensions
{
    public static readonly Dictionary<string, string> StatusDescriptions = new()
    {
        { "Enabled", "启用" },
        { "Disabled", "禁用" }
    };

    public static readonly Dictionary<string, string> ScheduleDescriptions = new()
    {
        { "Daily", "每日" },
        { "EveryTwoDays", "间隔两天" },
        { "Monday", "每周一" },
        { "Tuesday", "每周二" },
        { "Wednesday", "每周三" },
        { "Thursday", "每周四" },
        { "Friday", "每周五" },
        { "Saturday", "每周六" },
        { "Sunday", "每周日" }
    };
}
