using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.Core.Script;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.ViewModel.Pages.Component;

namespace BetterGenshinImpact.ViewModel.Windows.GearTask;

/// <summary>
/// 地图追踪任务选择窗口ViewModel
/// </summary>
public partial class PathingTaskSelectionViewModel : ViewModel
{
    private readonly ILogger<PathingTaskSelectionViewModel> _logger = App.GetLogger<PathingTaskSelectionViewModel>();

    /// <summary>
    /// 地图追踪任务列表
    /// </summary>
    [ObservableProperty] private ObservableCollection<PathingTaskInfo> _pathingTasks = new();

    /// <summary>
    /// 过滤后的地图追踪任务列表
    /// </summary>
    [ObservableProperty] private ObservableCollection<PathingTaskInfo> _filteredPathingTasks = new();

    /// <summary>
    /// 当前选中的地图追踪任务
    /// </summary>
    [ObservableProperty] private PathingTaskInfo? _selectedTask;

    /// <summary>
    /// 搜索关键词
    /// </summary>
    [ObservableProperty] private string _searchKeyword = string.Empty;


    /// <summary>
    /// 右侧显示的内容
    /// </summary>
    [ObservableProperty] private string _displayContent = string.Empty;

    /// <summary>
    /// 右侧显示的内容类型（JSON或README）
    /// </summary>
    [ObservableProperty] private string _displayContentType = string.Empty;

    /// <summary>
    /// 任务导入方式：true=按组引用，false=逐个添加
    /// </summary>
    [ObservableProperty] private bool _isGroupImportMode = true;

    /// <summary>
    /// 选中目录下的任务数量
    /// </summary>
    [ObservableProperty] private int _selectedDirectoryTaskCount = 0;

    // /// <summary>
    // /// 图标字典
    // /// </summary>
    // private Dictionary<string, string> _iconDictionary = new();

    public PathingTaskSelectionViewModel()
    {
        // LoadIconDictionary();
        LoadPathingTasks();
    }

    public Action<GearTaskViewModel?>? OnTaskAdded { get; set; }

    // /// <summary>
    // /// 加载图标字典
    // /// </summary>
    // private void LoadIconDictionary()
    // {
    //     try
    //     {
    //         var jsonContent = ResourceHelper.GetString("pack://application:,,,/Resources/Json/icons.json");
    //         if (string.IsNullOrEmpty(jsonContent))
    //         {
    //             return;
    //         }
    //         var iconArray = JsonConvert.DeserializeObject<JArray>(jsonContent);
    //         if (iconArray != null)
    //         {
    //             foreach (var item in iconArray)
    //             {
    //                 var name = item["name"]?.ToString();
    //                 var link = item["link"]?.ToString();
    //                 if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(link))
    //                 {
    //                     _iconDictionary[name] = link;
    //                 }
    //             }
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogError(ex, "加载图标字典失败");
    //     }
    // }

    /// <summary>
    /// 加载地图追踪任务
    /// </summary>
    private void LoadPathingTasks()
    {
        try
        {
            PathingTasks.Clear();

            const string pathingRoot = "pathing";
            if (!ScriptRepoUpdater.Instance.DirectoryExistsInCenterRepo(pathingRoot))
            {
                _logger.LogWarning("地图追踪任务目录不存在: {PathingRoot}", pathingRoot);
                return;
            }

            // 加载 pathing 根目录下的直接子项
            LoadDirectChildrenFromRepo(pathingRoot, PathingTasks);
            FilterTasks();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载地图追踪任务失败");
        }
    }

    /// <summary>
    /// 从仓库加载直接子项（用于构建层级结构）
    /// </summary>
    private void LoadDirectChildrenFromRepo(string repoRelativePath, ObservableCollection<PathingTaskInfo> parentCollection)
    {
        try
        {
            var children = ScriptRepoUpdater.Instance.GetChildrenFromCenterRepo(repoRelativePath);
            foreach (var child in children)
            {
                var taskInfo = new PathingTaskInfo
                {
                    Name = child.Name,
                    FolderName = Path.GetFileNameWithoutExtension(child.Name),
                    FullPath = child.RelativePath,
                    IsDirectory = child.IsDirectory,
                    ParentPath = GetParentPath(child.RelativePath),
                    RelativePath = TrimPathingRoot(child.RelativePath).Replace('/', Path.DirectorySeparatorChar)
                };

                SetTaskIcon(taskInfo);

                if (taskInfo.IsDirectory)
                {
                    LoadDirectChildrenFromRepo(child.RelativePath, taskInfo.Children);
                }
                parentCollection.Add(taskInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载目录任务失败: {RepoRelativePath}", repoRelativePath);
        }
    }

    /// <summary>
    /// 设置任务图标
    /// </summary>
    private void SetTaskIcon(PathingTaskInfo taskInfo)
    {
        // var fileName = Path.GetFileNameWithoutExtension(taskInfo.Name);
        //
        // if (_iconDictionary.TryGetValue(fileName, out var iconUrl))
        // {
        //     taskInfo.IconUrl = iconUrl;
        //     taskInfo.UseSystemIcon = false;
        // }
        // else
        // {
        //     taskInfo.UseSystemIcon = true;
        // }
        taskInfo.UseSystemIcon = true;
    }

    /// <summary>
    /// 为显示加载README内容（按需加载）
    /// </summary>
    private void LoadReadmeContentForDisplay(PathingTaskInfo taskInfo)
    {
        try
        {
            if (taskInfo.IsDirectory && string.IsNullOrEmpty(taskInfo.ReadmeContent))
            {
                var readmePath = $"{NormalizeRepoPath(taskInfo.FullPath)}/README.md";
                taskInfo.ReadmeContent = ScriptRepoUpdater.Instance.ReadFileFromCenterRepo(readmePath) ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载README内容失败: {FullPath}", taskInfo.FullPath);
            taskInfo.ReadmeContent = "README加载失败";
        }
    }

    /// <summary>
    /// 为显示加载JSON内容（按需加载）
    /// </summary>
    private void LoadJsonContentForDisplay(PathingTaskInfo taskInfo)
    {
        try
        {
            if (!taskInfo.IsDirectory && taskInfo.FullPath.EndsWith(".json") && string.IsNullOrEmpty(taskInfo.JsonContent))
            {
                var jsonContent = ScriptRepoUpdater.Instance.ReadFileFromCenterRepo(taskInfo.FullPath);
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    taskInfo.JsonContent = "JSON文件不存在";
                    return;
                }

                // 格式化JSON
                var jsonObject = JsonConvert.DeserializeObject(jsonContent);
                taskInfo.JsonContent = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载JSON内容失败: {FullPath}", taskInfo.FullPath);
            taskInfo.JsonContent = "JSON格式错误";
        }
    }

    /// <summary>
    /// 过滤任务
    /// </summary>
    private void FilterTasks()
    {
        FilteredPathingTasks.Clear();

        foreach (var task in PathingTasks)
        {
            var filteredTask = FilterTaskRecursively(task);
            if (filteredTask != null)
            {
                FilteredPathingTasks.Add(filteredTask);
            }
        }
    }

    /// <summary>
    /// 递归过滤任务（支持搜索所有子节点）
    /// </summary>
    private PathingTaskInfo? FilterTaskRecursively(PathingTaskInfo task)
    {
        // 检查当前节点是否匹配搜索条件
        bool currentMatches = string.IsNullOrWhiteSpace(SearchKeyword) ||
                              task.Name.Contains(SearchKeyword, StringComparison.OrdinalIgnoreCase) ||
                              task.RelativePath.Contains(SearchKeyword, StringComparison.OrdinalIgnoreCase);

        // 始终显示文件和目录（默认展示到文件级别）
        bool modeMatches = true;

        // 创建新的任务对象用于显示
        var filteredTask = new PathingTaskInfo
        {
            Name = task.Name,
            FolderName = task.FolderName,
            FullPath = task.FullPath,
            IsDirectory = task.IsDirectory,
            JsonContent = task.JsonContent,
            ReadmeContent = task.ReadmeContent,
            IconUrl = task.IconUrl,
            UseSystemIcon = task.UseSystemIcon,
            ParentPath = task.ParentPath,
            RelativePath = task.RelativePath
        };

        // 递归处理子节点
        bool hasMatchingChildren = false;
        foreach (var child in task.Children)
        {
            var filteredChild = FilterTaskRecursively(child);
            if (filteredChild != null)
            {
                filteredTask.Children.Add(filteredChild);
                hasMatchingChildren = true;
            }
        }

        // 如果当前节点匹配条件且符合显示模式，或者有匹配的子节点，则返回该节点
        if ((currentMatches && modeMatches) || hasMatchingChildren)
        {
            return filteredTask;
        }

        return null;
    }

    /// <summary>
    /// 当选中任务改变时
    /// </summary>
    partial void OnSelectedTaskChanged(PathingTaskInfo? value)
    {
        if (value == null)
        {
            DisplayContent = string.Empty;
            DisplayContentType = string.Empty;
            SelectedDirectoryTaskCount = 0;
            return;
        }

        if (value.IsDirectory)
        {
            // 动态加载README内容
            LoadReadmeContentForDisplay(value);
            DisplayContent = value.ReadmeContent ?? string.Empty;
            DisplayContentType = "README";
            // 计算选中目录下的任务数量
            SelectedDirectoryTaskCount = CountTasksInDirectory(value);
        }
        else
        {
            // 动态加载JSON内容
            LoadJsonContentForDisplay(value);
            DisplayContent = value.JsonContent ?? string.Empty;
            DisplayContentType = "JSON";
            SelectedDirectoryTaskCount = 0;
        }
    }

    /// <summary>
    /// 当搜索关键词改变时
    /// </summary>
    partial void OnSearchKeywordChanged(string value)
    {
        FilterTasks();
    }

    /// <summary>
    /// 计算目录下的任务数量（递归计算所有子目录中的JSON文件）
    /// </summary>
    private int CountTasksInDirectory(PathingTaskInfo directory)
    {
        if (!directory.IsDirectory)
            return 0;

        try
        {
            return ScriptRepoUpdater.Instance.CountFilesInCenterRepo(directory.FullPath, ".json", recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算目录任务数量失败: {FullPath}", directory.FullPath);
            return 0;
        }
    }

    private static string NormalizeRepoPath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static string TrimPathingRoot(string repoRelativePath)
    {
        var normalized = NormalizeRepoPath(repoRelativePath);
        if (normalized.StartsWith("pathing/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized["pathing/".Length..];
        }

        return string.Equals(normalized, "pathing", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : normalized;
    }

    private static string GetParentPath(string repoRelativePath)
    {
        var normalized = NormalizeRepoPath(repoRelativePath);
        var idx = normalized.LastIndexOf('/');
        return idx > 0 ? normalized[..idx] : string.Empty;
    }

    /// <summary>
    /// 添加文件夹引用
    /// </summary>
    [RelayCommand]
    private void AddFolderTask()
    {
        if (SelectedTask?.IsDirectory == true)
        {
            // 按组引用：添加选中目录作为一个任务组
            var gearTaskViewModel = new GearTaskViewModel
            {
                Name = SelectedTask.Name,
                Path = @$"{{pathingRepoFolder}}\{SelectedTask.RelativePath}\",
                IsDirectory = true
            };

            // 触发添加事件或通过其他方式返回给调用方
            OnTaskAdded?.Invoke(gearTaskViewModel);
        }
    }

    /// <summary>
    /// 添加文件夹引用，保持目录结构
    /// </summary>
    [RelayCommand]
    private void AddFolderTasksWithStructure()
    {
        if (SelectedTask?.IsDirectory == true)
        {
            var task = GetAllFolderTasksInDirectoryWithStructure(SelectedTask);
            OnTaskAdded?.Invoke(task);
        }
    }

    /// <summary>
    /// 添加目录下所有文件，不要目录结构
    /// </summary>
    [RelayCommand]
    private void AddAllFileTasks()
    {
        if (SelectedTask?.IsDirectory == true)
        {
            var rootGearTaskViewModel = new GearTaskViewModel
            {
                Name = SelectedTask.Name,
                IsDirectory = true
            };
            
            // 逐个添加：添加目录下所有JSON文件作为独立任务
            var taskInfos = GetAllJsonFilesInDirectory(SelectedTask);
            foreach (var taskInfo in taskInfos)
            {
                var gearTaskViewModel = new GearTaskViewModel
                {
                    Name = Path.GetFileNameWithoutExtension(taskInfo.Name),
                    TaskType = "Pathing",
                    Path = @$"{{pathingRepoFolder}}\{taskInfo.RelativePath}\",
                    IsDirectory = false
                };
                rootGearTaskViewModel.Children.Add(gearTaskViewModel);
            }

            if (rootGearTaskViewModel.Children.Count > 0)
            {
                OnTaskAdded?.Invoke(rootGearTaskViewModel);
            }
        }
    }

    /// <summary>
    /// 保持目录结构添加所有目录下文件
    /// </summary>
    [RelayCommand]
    private void AddFileTasksWithStructure()
    {
        if (SelectedTask?.IsDirectory == true)
        {
            // 保持目录结构添加所有子任务
            var task = GetAllFileTasksInDirectoryWithStructure(SelectedTask);
            OnTaskAdded?.Invoke(task);
        }
    }


    /// <summary>
    /// 获取目录下所有JSON文件
    /// </summary>
    private List<PathingTaskInfo> GetAllJsonFilesInDirectory(PathingTaskInfo directory)
    {
        var jsonFiles = new List<PathingTaskInfo>();

        if (directory.Children is { Count: > 0 })
        {
            foreach (var child in directory.Children)
            {
                if (child.IsDirectory)
                {
                    jsonFiles.AddRange(GetAllJsonFilesInDirectory(child));
                }
                else if (child.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    jsonFiles.Add(child);
                }
            }
        }

        return jsonFiles;
    }

    /// <summary>
    /// 获取目录下所有json文件任务并保持结构
    /// </summary>
    private GearTaskViewModel? GetAllFileTasksInDirectoryWithStructure(PathingTaskInfo node)
    {
        if (node.IsDirectory)
        {
            // 添加子目录作为组
            var groupTask = new GearTaskViewModel
            {
                Name = node.Name,
                IsDirectory = true
            };

            foreach (var pathingTaskInfo in node.Children)
            {
                var gearTask = GetAllFileTasksInDirectoryWithStructure(pathingTaskInfo);
                if (gearTask != null)
                {
                    groupTask.Children.Add(gearTask);
                }
            }

            return groupTask;
        }
        else if (node.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            // 添加JSON文件作为任务
            var fileTask = new GearTaskViewModel
            {
                Name = Path.GetFileNameWithoutExtension(node.Name),
                TaskType = "Pathing",
                Path = @$"{{pathingRepoFolder}}\{node.RelativePath}\",
                IsDirectory = false
            };
            return fileTask;
        }

        return null;
    }


    /// <summary>
    /// 获取目录下所有文件夹任务并保持结构
    /// </summary>
    private GearTaskViewModel? GetAllFolderTasksInDirectoryWithStructure(PathingTaskInfo directory)
    {
        if (directory.Children is { Count: > 0 })
        {
            // 判断 directory 的子节点是否是文件
            var hasJsonFile = directory.Children.Any(grandChild => !grandChild.IsDirectory && grandChild.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            if (hasJsonFile)
            {
                // 添加子目录作为任务
                var groupTask = new GearTaskViewModel
                {
                    Name = directory.Name,
                    TaskType = "Pathing",
                    Path = @$"{{pathingRepoFolder}}\{directory.RelativePath}\",
                    IsDirectory = false
                };
                return groupTask;
            }
            else
            {
                // 添加子目录作为组
                var groupTask = new GearTaskViewModel
                {
                    Name = directory.Name,
                    IsDirectory = true
                };
                foreach (var pathingTaskInfo in directory.Children)
                {
                    var gearTask = GetAllFolderTasksInDirectoryWithStructure(pathingTaskInfo);
                    if (gearTask != null)
                    {
                        groupTask.Children.Add(gearTask);
                    }
                }
                return groupTask;
            }
        }
        else
        {
            return null;
        }
    }
}
