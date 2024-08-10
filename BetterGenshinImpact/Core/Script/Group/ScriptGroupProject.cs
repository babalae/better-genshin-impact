using BetterGenshinImpact.Core.Script.Project;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Core.Script.Group;

public partial class ScriptGroupProject : ObservableObject
{
    [ObservableProperty]
    private int _order;

    public string Id { get; set; }

    public string Name { get; set; }

    [ObservableProperty]
    private string _status;

    [JsonIgnore]
    [ObservableProperty]
    private string _statusDesc;

    /// <summary>
    /// 执行周期
    /// 不在 ScheduleDescriptions 中则会被视为自定义Cron表达式
    /// </summary>
    public string Schedule { get; set; }

    [JsonIgnore]
    public string ScheduleDesc { get; set; }

    [JsonIgnore]
    public ScriptProject Project { get; set; }

    public ScriptGroupProject(ScriptProject project)
    {
        Id = project.Manifest.Id;
        Name = project.Manifest.Name;
        Status = "Enabled";
        StatusDesc = ScriptGroupProjectExtensions.StatusDescriptions[Status];
        Schedule = "Daily";
        ScheduleDesc = ScriptGroupProjectExtensions.ScheduleDescriptions.GetValueOrDefault(Schedule, "自定义周期");

        Project = project;
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
