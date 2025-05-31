using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.GameTask.TaskProgress;

public partial class TaskProgress : ObservableObject
{

    [ObservableProperty] private List<string> _scriptGroupNames = new();

    [ObservableProperty] private string? _scriptGroupName;

    [ObservableProperty] private ScriptGroupProjectInfo? _currentScriptGroupProjectInfo;

    [ObservableProperty] private string _name = DateTime.Now.ToString("yyyyMMddHHmmss");
    [ObservableProperty] private DateTime startTime = DateTime.Now;
    [ObservableProperty] private DateTime? endTime = null;
    //进度信息，如果next不为空，则从next执行
    [ObservableProperty][JsonIgnore] private  Progress? _next;
    public partial class Progress : ObservableObject
    {
        [ObservableProperty] private string _groupName  = string.Empty;
        [ObservableProperty] private int _index  = 0;
        [ObservableProperty] private string _projectName = string.Empty;
        [ObservableProperty] private string _folderName  = string.Empty;
    }
    public partial class ScriptGroupProjectInfo : ObservableObject
    {
        [ObservableProperty] private bool _taskEnd  = false;
        [ObservableProperty] private int _index  = 0;
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _folderName  = string.Empty;
        [ObservableProperty] private DateTime startTime = DateTime.Now;
        [ObservableProperty] private DateTime? endTime = null;
    }
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, ConfigService.JsonOptions);
    }
}
