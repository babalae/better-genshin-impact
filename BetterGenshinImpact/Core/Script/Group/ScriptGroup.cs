using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;
namespace BetterGenshinImpact.Core.Script.Group;

/// <summary>
/// 调度器 配置组
/// </summary>
public partial class ScriptGroup : ObservableObject
{
    public int Index { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private ScriptGroupConfig _config = new();

    [ObservableProperty]
    private ObservableCollection<ScriptGroupProject> _projects = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public bool NextFlag
    {
        get => _nextFlag;
        set => SetProperty(ref _nextFlag, value);
    }
    private bool _nextFlag;

    public ScriptGroup()
    {
        Projects.CollectionChanged += ProjectsCollectionChanged;
    }

    private void ProjectsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Projects));
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, ConfigService.JsonOptions);
    }

    public static ScriptGroup FromJson(string json)
    {
        var group = JsonConvert.DeserializeObject<ScriptGroup>(json) ?? throw new Exception("解析配置组JSON配置失败");
        ResetGroupInfo(group);
        return group;
    }

    public static void ResetGroupInfo(ScriptGroup group)
    {
        foreach (var project in group.Projects)
        {
            project.GroupInfo = group;
        }
    }

    public void AddProject(ScriptGroupProject project)
    {
        project.GroupInfo = this;
        Projects.Add(project);
    }
}
