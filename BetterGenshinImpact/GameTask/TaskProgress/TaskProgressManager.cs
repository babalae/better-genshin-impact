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
    var currentGroupIndex = 0;
    var currentProjectIndex = -1;
    /*if (taskProgress.LastSuccessScriptGroupProjectInfo == null)
        return ;*/

    if (taskProgress.LastScriptGroupName!=null)
    {
        currentGroupIndex = scriptGroups.FindIndex(g => g.Name == taskProgress.LastScriptGroupName);
        if (currentGroupIndex == -1)
            return ;
    }
    
    var currentGroup = scriptGroups[currentGroupIndex];
    var isLastInGroup = false;
    if (taskProgress.LastSuccessScriptGroupProjectInfo!=null)
    {
        
        var currentProjectInfo = taskProgress.LastSuccessScriptGroupProjectInfo;

        currentProjectIndex = currentGroup.Projects.ToList().FindIndex(p =>
            p.Name == currentProjectInfo.Name &&
            p.FolderName == currentProjectInfo.FolderName);

        if (currentProjectIndex == -1)
            return ;

        isLastInGroup = currentProjectIndex == currentGroup.Projects.Count - 1;
    }

    //bool isIncomplete = currentProjectInfo.EndTime == null;

    if (isLastInGroup)
    {
        // 向后查找下一个非空组
        for (int i = currentGroupIndex + 1; i < scriptGroups.Count; i++)
        {
            var group = scriptGroups[i];
            if (group.Projects != null && group.Projects.Any())
            {
                var project = group.Projects.First();

                taskProgress.Next=new TaskProgress.Progress
                {
                    GroupName = group.Name,
                    Index = 0,
                    ProjectName = project.Name,
                    FolderName = project.FolderName
                };
                return;
            }
        }

        // 循环从开头查找直到当前组之前
        if (taskProgress.Loop)
        {
            for (int i = 0; i < currentGroupIndex; i++)
            {
                var group = scriptGroups[i];
                if (group.Projects != null && group.Projects.Any())
                {
                    var project = group.Projects.First();
                    taskProgress.Next = new TaskProgress.Progress
                    {
                        GroupName = group.Name,
                        Index = 0,
                        ProjectName = project.Name,
                        FolderName = project.FolderName
                    };
                    return;
                }
            }
        }

        return ;
    }
    else
    {
        //取成功执行的下一个任务
        currentProjectIndex++;
    }



    // 返回当前项目
    var currentProject = currentGroup.Projects[currentProjectIndex];
    taskProgress.Next = new TaskProgress.Progress
    {
        GroupName = currentGroup.Name,
        Index = currentProjectIndex,
        ProjectName = currentProject.Name,
        FolderName = currentProject.FolderName
    };
}
}