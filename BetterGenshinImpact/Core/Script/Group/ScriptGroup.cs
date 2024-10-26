using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;

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
        return Newtonsoft.Json.JsonConvert.DeserializeObject<ScriptGroup>(json) ?? throw new Exception("解析配置组JSON配置失败");
    }
}
