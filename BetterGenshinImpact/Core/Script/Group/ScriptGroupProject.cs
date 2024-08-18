using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Script.Project;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Core.Script.Group;

public partial class ScriptGroupProject : ObservableObject
{
    [ObservableProperty]
    private int _index;

    public string Name { get; set; } = string.Empty;

    public string FolderName { get; set; } = string.Empty;

    [ObservableProperty]
    private string _type = string.Empty;

    [JsonIgnore]
    public string TypeDesc => ScriptGroupProjectExtensions.TypeDescriptions[Type];

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
    private int _runNum = 1;

    [JsonIgnore]
    public ScriptProject? Project { get; set; }

    public ScriptGroupProject()
    {
    }

    public ScriptGroupProject(ScriptProject project)
    {
        Name = project.Manifest.Name;
        FolderName = project.FolderName;
        Status = "Enabled";
        Schedule = "Daily";
        Project = project;
        Type = "Javascript";
    }

    public ScriptGroupProject(string kmName)
    {
        Name = kmName;
        FolderName = kmName;
        Status = "Enabled";
        Schedule = "Daily";
        Project = null; // 不是Js脚本
        Type = "KeyMouse";
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

    public async Task Run()
    {
        if (Type == "Javascript")
        {
            if (Project == null)
            {
                throw new Exception("JS脚本未初始化");
            }
            await Project.ExecuteAsync();
        }
        if (Type == "KeyMouse")
        {
            // 加载并执行
            var json = await File.ReadAllTextAsync(Global.Absolute(@$"User\KeyMouseScript\{Name}"));
            await KeyMouseMacroPlayer.PlayMacro(json, CancellationContext.Instance.Cts.Token, false);
        }
        else
        {
            throw new Exception("不支持的脚本类型");
        }
    }

    partial void OnTypeChanged(string value)
    {
        OnPropertyChanged(nameof(TypeDesc));
    }

    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(StatusDesc));
    }

    partial void OnScheduleChanged(string value)
    {
        OnPropertyChanged(nameof(ScheduleDesc));
    }
}

public class ScriptGroupProjectExtensions
{
    public static readonly Dictionary<string, string> TypeDescriptions = new()
    {
        { "Javascript", "JS脚本" },
        { "KeyMouse", "键鼠脚本" }
    };

    public static readonly Dictionary<string, string> StatusDescriptions = new()
    {
        { "Enabled", "启用" },
        { "Disabled", "禁用" }
    };

    public Dictionary<string, string> GetStatusDescriptions()
    {
        return StatusDescriptions;
    }

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

    public Dictionary<string, string> GetScheduleDescriptions()
    {
        return ScheduleDescriptions;
    }
}
