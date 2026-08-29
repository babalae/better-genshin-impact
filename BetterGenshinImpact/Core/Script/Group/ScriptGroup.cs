using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text;
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
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

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

    public void WriteToFileAtomically(string filePath)
    {
        var json = ToJson();
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new ArgumentException("配置组文件路径无效", nameof(filePath));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, json, Utf8WithoutBom);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
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
