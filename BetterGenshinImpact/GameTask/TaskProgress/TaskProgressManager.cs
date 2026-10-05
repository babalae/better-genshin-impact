using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.TaskProgress;

public class TaskProgressManager
{
    private static readonly string _configDir = Global.Absolute(@"log\task_progress");
    public static ILogger Logger { get; } = App.GetLogger<TaskProgressManager>();
    public static void SaveTaskProgress(TaskProgress taskProgress)
    {
        // 如果目录不存在，则创建
        if (!Directory.Exists(_configDir))
        {
            Directory.CreateDirectory(_configDir);
        }

        var file = Path.Combine(_configDir, $"{taskProgress.Name}.json");
        File.WriteAllText(file, taskProgress.ToJson());
    }

    public static List<TaskProgress> LoadAllTaskProgress()
    {
        // 确保目录存在
        if (!Directory.Exists(_configDir))
        {
            Directory.CreateDirectory(_configDir);
        }

        var result = new List<TaskProgress>();
        var now = DateTime.Now;

        // 匹配全数字文件名，形如：20250531081114.json
        var regex = new Regex(@"^\d{14}\.json$");
        var fileList = Directory.GetFiles(_configDir, "*.json")
            .Where(file => regex.IsMatch(Path.GetFileName(file))) // 筛选纯数字 JSON
            .Select(file => new FileInfo(file))
            .OrderByDescending(fi => fi.LastWriteTime)             // 最后修改时间倒序
            .ToList();
       
        foreach (var file in fileList.ToArray())
        {
            var fileName = file.Name;

            // 跳过非纯数字文件名
          //  if (!regex.IsMatch(fileName)) continue;

            var lastWrite = file.LastWriteTime;

            // 删除3天前未修改的文件
            if ((now - lastWrite).TotalDays > 3)
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex)
                {
                    Logger.LogInformation($"删除文件失败：{file} - {ex.Message}");
                }
                continue;
            }

            try
            {
                var json = File.ReadAllText(file.FullName);
                var progress =  JsonConvert.DeserializeObject<TaskProgress>(json);
                if (progress != null && progress.EndTime == null)
                    result.Add(progress);
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"读取文件失败：{file} - {ex.Message}");
            }
        }

        return result;
    }
    
public static void GenerNextProjectInfo(
    TaskProgress taskProgress,
    List<ScriptGroup> scriptGroups)
{
    if (scriptGroups.Count == 0)
    {
        Logger.LogWarning("无法计算下一个任务：配置组列表为空");
        return;
    }

    var orderedGroups = OrderGroupsByProgress(taskProgress, scriptGroups);
    var currentGroupIndex = 0;

    if (!string.IsNullOrEmpty(taskProgress.LastScriptGroupName))
    {
        currentGroupIndex = orderedGroups.FindIndex(g => g.Name == taskProgress.LastScriptGroupName);
        if (currentGroupIndex == -1)
        {
            Logger.LogWarning("无法计算下一个任务：找不到上次成功的配置组 {Group}", taskProgress.LastScriptGroupName);
            currentGroupIndex = orderedGroups.FindIndex(g => g.Name == taskProgress.CurrentScriptGroupName);
            if (currentGroupIndex == -1)
            {
                return;
            }

            TrySetNextProject(taskProgress, orderedGroups[currentGroupIndex], 0);
            return;
        }
    }

    var currentGroup = orderedGroups[currentGroupIndex];
    var currentProjectIndex = -1;
    if (taskProgress.LastSuccessScriptGroupProjectInfo != null)
    {
        currentProjectIndex = FindProjectIndex(currentGroup, taskProgress.LastSuccessScriptGroupProjectInfo);
        if (currentProjectIndex == -1)
        {
            Logger.LogWarning(
                "配置组 {Group} 中找不到上次成功项目 {Name}（{Folder}，index={Index}），改从该组后续或下一组继续",
                currentGroup.Name,
                taskProgress.LastSuccessScriptGroupProjectInfo.Name,
                taskProgress.LastSuccessScriptGroupProjectInfo.FolderName,
                taskProgress.LastSuccessScriptGroupProjectInfo.Index);

            var savedIndex = taskProgress.LastSuccessScriptGroupProjectInfo.Index;
            currentProjectIndex = savedIndex >= 0 && savedIndex < currentGroup.Projects.Count
                ? savedIndex
                : currentGroup.Projects.Count - 1;
        }
    }

    if (currentProjectIndex >= currentGroup.Projects.Count - 1)
    {
        if (!TrySetNextFromFollowingGroup(taskProgress, orderedGroups, currentGroupIndex))
        {
            Logger.LogWarning("无法计算下一个任务：{Group} 已是最后一项且没有后续配置组", currentGroup.Name);
        }

        return;
    }

    TrySetNextProject(taskProgress, currentGroup, currentProjectIndex + 1);
}

private static List<ScriptGroup> OrderGroupsByProgress(TaskProgress taskProgress, List<ScriptGroup> scriptGroups)
{
    if (taskProgress.ScriptGroupNames.Count == 0)
    {
        return scriptGroups;
    }

    var byName = scriptGroups
        .Where(g => taskProgress.ScriptGroupNames.Contains(g.Name))
        .ToDictionary(g => g.Name, StringComparer.Ordinal);
    var ordered = new List<ScriptGroup>();
    foreach (var name in taskProgress.ScriptGroupNames)
    {
        if (byName.TryGetValue(name, out var group))
        {
            ordered.Add(group);
        }
    }

    return ordered.Count > 0 ? ordered : scriptGroups;
}

private static int FindProjectIndex(ScriptGroup group, TaskProgress.ScriptGroupProjectInfo projectInfo)
{
    var projects = group.Projects.ToList();
    var exact = projects.FindIndex(p =>
        p.Name == projectInfo.Name &&
        p.FolderName == projectInfo.FolderName);
    if (exact >= 0)
    {
        return exact;
    }

    var folderMatches = projects
        .Select((p, i) => (p, i))
        .Where(x => x.p.FolderName == projectInfo.FolderName)
        .ToList();
    if (folderMatches.Count == 1)
    {
        return folderMatches[0].i;
    }

    if (folderMatches.Count > 1)
    {
        var nameInFolder = folderMatches.FindIndex(x => x.p.Name == projectInfo.Name);
        if (nameInFolder >= 0)
        {
            return folderMatches[nameInFolder].i;
        }
    }

    if (projectInfo.Index >= 0 && projectInfo.Index < projects.Count)
    {
        return projectInfo.Index;
    }

    return -1;
}

private static bool TrySetNextFromFollowingGroup(
    TaskProgress taskProgress,
    List<ScriptGroup> scriptGroups,
    int currentGroupIndex)
{
    for (var i = currentGroupIndex + 1; i < scriptGroups.Count; i++)
    {
        if (TrySetNextProject(taskProgress, scriptGroups[i], 0))
        {
            return true;
        }
    }

    if (!taskProgress.Loop)
    {
        return false;
    }

    for (var i = 0; i < currentGroupIndex; i++)
    {
        if (TrySetNextProject(taskProgress, scriptGroups[i], 0))
        {
            return true;
        }
    }

    return false;
}

private static bool TrySetNextProject(TaskProgress taskProgress, ScriptGroup group, int projectIndex)
{
    if (group.Projects == null || projectIndex < 0 || projectIndex >= group.Projects.Count)
    {
        return false;
    }

    var project = group.Projects[projectIndex];
    taskProgress.Next = new TaskProgress.Progress
    {
        GroupName = group.Name,
        Index = projectIndex,
        ProjectName = project.Name,
        FolderName = project.FolderName
    };
    return true;
}
}