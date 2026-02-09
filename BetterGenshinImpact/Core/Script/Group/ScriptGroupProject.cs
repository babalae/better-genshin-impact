using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Shell;
using BetterGenshinImpact.ViewModel.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.FarmingPlan;
using BetterGenshinImpact.GameTask.LogParse;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Group;

public partial class ScriptGroupProject : ObservableObject
{
    [ObservableProperty]
    private int _index;

    /// <summary>
    /// 理论上是文件名
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 理论上是当前类型脚本根目录到脚本文件所在目录的相对路径
    /// 但是：
    /// 1. JS 脚本的文件名是内部的名称，文件夹名脚本所在文件夹，这个也是唯一标识
    /// 2. KeyMouse 脚本的文件名和文件夹名相同，文件夹名暂时无意义
    /// 3. Pathing 文件名就是实际脚本的文件名，文件夹名是脚本所在的相对目录
    /// </summary>
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
    public string ScheduleDesc => ScriptGroupProjectExtensions.ScheduleDescriptions.GetValueOrDefault(Schedule, Lang.S["Gen_10264_0210ab"]);

    [ObservableProperty]
    private int _runNum = 1;

    [JsonIgnore]
    public ScriptProject? Project { get; set; }

    public ExpandoObject? JsScriptSettingsObject { get; set; }

    /// <summary>
    /// 所属配置组
    /// </summary>
    [JsonIgnore]
    public ScriptGroup? GroupInfo { get; set; }

    private bool? _nextFlag = false;
    private bool? _skipFlag = false;

    /// <summary>
    /// 下一个从此执行标志
    /// </summary>
    [JsonIgnore]
    public bool? NextFlag
    {
        get => _nextFlag;
        set => SetProperty(ref _nextFlag, value);
    }

    /// <summary>
    /// 直接跳过标志
    /// </summary>
    [JsonIgnore]
    public bool? SkipFlag
    {
        get => _skipFlag;
        set => SetProperty(ref _skipFlag, value);
    }
    
    
    [ObservableProperty]
    private bool? _allowJsNotification = true;

    [ObservableProperty]
    private string? _allowJsHTTPHash = "";

    /// <summary>
    /// 是否允许JS脚本发送HTTP请求，通过验证Hash来控制
    /// </summary>
    [JsonIgnore]
    public bool AllowJsHTTP
    {
        get
        {
            return GetHttpAllowedUrlsHash() == AllowJsHTTPHash;
        }
    }


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

    /// <summary>
    ///
    /// </summary>
    /// <param name="name"></param>
    /// <param name="folder"></param>
    /// <param name="type">KeyMouse|Pathing</param>
    public ScriptGroupProject(string name, string folder, string type)
    {
        Name = name;
        FolderName = folder;
        Status = "Enabled";
        Schedule = "Daily";
        Project = null; // 不是JS脚本
        Type = type;
    }

    public static ScriptGroupProject BuildKeyMouseProject(string name)
    {
        return new ScriptGroupProject(name, name, "KeyMouse");
    }

    public static ScriptGroupProject BuildShellProject(string command)
    {
        return new ScriptGroupProject(command, "", "Shell");
    }

    public static ScriptGroupProject BuildPathingProject(string name, string folder)
    {
        return new ScriptGroupProject(name, folder, "Pathing");
    }

    /// <summary>
    /// 通过 FolderName 查找 ScriptProject
    /// </summary>
    public void BuildScriptProjectRelation()
    {
        if (string.IsNullOrEmpty(FolderName))
        {
            throw new Exception(Lang.S["Gen_10263_587252"]);
        }
        Project = new ScriptProject(FolderName);
    }

    public string GetHttpAllowedUrlsHash()
    {
        if (Project == null)
        {
            BuildScriptProjectRelation();
        }
        if (Project == null)
        {
            return "";
        }
        return string.Join("|", Project.Manifest.HttpAllowedUrls);
    }

    public async Task Run()
    {
        //执行记录
        ExecutionRecord executionRecord = new ExecutionRecord()
        {
            ServerStartTime =
                GroupInfo?.Config.PathingConfig.TaskCompletionSkipRuleConfig.IsBoundaryTimeBasedOnServerTime ?? false
                    ? ServerTimeHelper.GetServerTimeNow()
                    : DateTimeOffset.Now,
            StartTime = DateTime.Now,
            GroupName = GroupInfo?.Name ?? "",
            FolderName = FolderName,
            ProjectName = Name,
            Type = Type
        };
        ExecutionRecordStorage.SaveExecutionRecord(executionRecord);
        if (Type == "Javascript")
        {
            if (Project == null)
            {
                throw new Exception(Lang.S["Gen_10262_c48739"]);
            }
            JsScriptSettingsObject ??= new ExpandoObject();

            // 清理配置中的无效值
            CleanInvalidSettingsValues();

            var pathingPartyConfig = GroupInfo?.Config.PathingConfig;
            await Project.ExecuteAsync(JsScriptSettingsObject, pathingPartyConfig);
        }
        else if (Type == "KeyMouse")
        {
            // 加载并执行
            var json = await File.ReadAllTextAsync(Global.Absolute(@$"User\KeyMouseScript\{Name}"));
            await KeyMouseMacroPlayer.PlayMacro(json, CancellationContext.Instance.Cts.Token, false);
        }
        else if (Type == "Pathing")
        {
            // 加载并执行
            var task = PathingTask.BuildFromFilePath(Path.Combine(MapPathingViewModel.PathJsonPath, FolderName, Name));
            if (task == null)
            {
                return;
            }
            var pathingTask = new PathExecutor(CancellationContext.Instance.Cts.Token);
            pathingTask.PartyConfig = GroupInfo?.Config.PathingConfig;
            if (pathingTask.PartyConfig is null || pathingTask.PartyConfig.AutoPickEnabled)
            {
                TaskTriggerDispatcher.Instance().AddTrigger("AutoPick", null);
            }
            await pathingTask.Pathing(task);

            
            executionRecord.IsSuccessful = pathingTask.SuccessEnd;
            OtherConfig.AutoRestart autoRestart = TaskContext.Instance().Config.OtherConfig.AutoRestartConfig;
            if (!pathingTask.SuccessEnd)
            {
                TaskControl.Logger.LogWarning(Lang.S["Gen_10261_0929f0"]);
                if (autoRestart.Enabled && autoRestart.IsPathingFailureExceptional && !pathingTask.SuccessEnd)
                {
                    throw new Exception(Lang.S["Gen_10260_a774bf"]);
                }
            }

            if (task.FarmingInfo.AllowFarmingCount)
            {
                var successFight = pathingTask.SuccessEnd;
                var fightCount = 0;
               
                //未走完完整路径下，才校验打架次数
                if (!successFight)
                {
                    fightCount = task.Positions.Count(pos => pos.Action == ActionEnum.Fight.Code);
                    successFight = pathingTask.SuccessFight >= fightCount;
                    //判断为锄地脚本
                    if (task.FarmingInfo.PrimaryTarget!="disable")
                    {

                        if (autoRestart.Enabled
                            &&autoRestart.IsFightFailureExceptional
                            &&!successFight)
                        {
                            throw new Exception($"{Lang.S["Gen_10259_ad92f0"]});
                        }
                    }
                }

                if (successFight)
                {
                    //每日锄地记录
                    FarmingStatsRecorder.RecordFarmingSession(task.FarmingInfo, new FarmingRouteInfo
                    {
                        GroupName = GroupInfo?.Name ?? "",
                        FolderName = FolderName,
                        ProjectName = Name
                    });
                }
                else
                {
                    TaskControl.Logger.LogWarning($"{Lang.S["Gen_10258_90f32c"]});
                }

            }
            

            

        }
        else if (Type == "Shell")
        {
            ShellConfig? shellConfig = null;
            if (GroupInfo?.Config.EnableShellConfig ?? false)
            {
                shellConfig = GroupInfo?.Config.ShellConfig;
            }

            var task = new ShellTask(ShellTaskParam.BuildFromConfig(Name, shellConfig ?? new ShellConfig()));
            await task.Start(CancellationContext.Instance.Cts.Token);
        }

        if (Type != "Pathing")
        {
            executionRecord.IsSuccessful = true;
        }

        executionRecord.ServerEndTime =
            GroupInfo?.Config.PathingConfig.TaskCompletionSkipRuleConfig.IsBoundaryTimeBasedOnServerTime ?? false
                ? ServerTimeHelper.GetServerTimeNow()
                : DateTimeOffset.Now;
        executionRecord.EndTime = DateTime.Now;
        ExecutionRecordStorage.SaveExecutionRecord(executionRecord);
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

    /// <summary>
    /// 清理 JsScriptSettingsObject 中不在 settings.json 定义的 Options 列表中的无效值
    /// </summary>
    private void CleanInvalidSettingsValues()
    {
        if (Project == null || JsScriptSettingsObject == null)
        {
            return;
        }

        try
        {
            // 加载 settings.json 中定义的配置项
            var settingItems = Project.Manifest.LoadSettingItems(Project.ProjectPath);
            if (settingItems.Count == 0)
            {
                return;
            }

            var settingsDict = JsScriptSettingsObject as IDictionary<string, object?>;
            if (settingsDict == null)
            {
                return;
            }

            // 遍历所有 multi-checkbox 类型的配置项
            foreach (var item in settingItems.Where(i => i.Type == "multi-checkbox"))
            {
                if (!settingsDict.ContainsKey(item.Name))
                {
                    continue;
                }

                // 获取当前保存的值
                var savedValue = settingsDict[item.Name];
                List<string>? checkedValues = null;

                if (savedValue is List<string> stringList)
                {
                    checkedValues = stringList;
                }
                else if (savedValue is List<object> objectList)
                {
                    checkedValues = objectList.Select(i => (string)i).ToList();
                    settingsDict[item.Name] = checkedValues;
                }

                // 清理不在 Options 列表中的无效值
                if (checkedValues != null && item.Options != null)
                {
                    checkedValues.RemoveAll(value => !item.Options.Contains(value));
                }
            }
        }
        catch (Exception ex)
        {
            // 清理失败不影响脚本执行，只记录日志
            TaskControl.Logger.LogDebug(ex, Lang.S["Gen_10257_ca6be5"]);
        }
    }
}

public class ScriptGroupProjectExtensions
{
    public static readonly Dictionary<string, string> TypeDescriptions = new()
    {
        { "Javascript", Lang.S["Script_010_86c033"] },
        { "KeyMouse", Lang.S["Script_012_ea035d"] },
        { "Pathing", Lang.S["Nav_MapPathing"] },
        { "Shell", "Shell" }
    };

    public static readonly Dictionary<string, string> StatusDescriptions = new()
    {
        { "Enabled", Lang.S["Settings_Enabled"] },
        { "Disabled", Lang.S["Gen_10256_710ad0"] }
    };

    public Dictionary<string, string> GetStatusDescriptions()
    {
        return StatusDescriptions;
    }

    public static readonly Dictionary<string, string> ScheduleDescriptions = new()
    {
        { "Daily", Lang.S["Gen_10255_ce2210"] },
        { "EveryTwoDays", Lang.S["Gen_10254_5762a8"] },
        { "Monday", Lang.S["Gen_10253_20f83c"] },
        { "Tuesday", Lang.S["Gen_10252_364cd4"] },
        { "Wednesday", Lang.S["Gen_10251_cbed94"] },
        { "Thursday", Lang.S["Gen_10250_2de240"] },
        { "Friday", Lang.S["Gen_10249_179940"] },
        { "Saturday", Lang.S["Gen_10248_c4ea6c"] },
        { "Sunday", Lang.S["Gen_10247_fe0b6a"] }
    };

    public Dictionary<string, string> GetScheduleDescriptions()
    {
        return ScheduleDescriptions;
    }
}
