using System;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Video.obs;

public class ObsLogFileProcessor
{
    private static readonly string ObsLogPath = Global.Absolute(@"video\bin\OBS-Studio-31.0.0-Windows\config\obs-studio\logs");

    public static FileInfo? GetLogFilePath()
    {
        // 找到目录中最新的日志文件
        if (!Directory.Exists(ObsLogPath))
        {
            TaskControl.Logger.LogError("OBS日志目录不存在");
            return null;
        }

        var files = Directory.GetFiles(ObsLogPath, "*.txt");
        if (files.Length == 0)
        {
            TaskControl.Logger.LogError("OBS日志文件不存在");
            return null;
        }

        FileInfo? latestFile = null;
        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            if (latestFile == null || fileInfo.LastWriteTime > latestFile.LastWriteTime)
            {
                latestFile = fileInfo;
            }
        }

        TaskControl.Logger.LogDebug("当前最新的OBS日志文件:{Path}", latestFile?.FullName);
        return latestFile;
    }

    public static async Task<DateTime?> ProcessLog(DateTime? beforeStartTime)
    {
        var logFile = GetLogFilePath();
        if (logFile == null)
        {
            return null;
        }
        
        // 以共享的方式读取日志文件
        string content;
        try
        {
            await using var fileStream = new FileStream(logFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);
            content = await reader.ReadToEndAsync();
        }
        catch (IOException e)
        {
            TaskControl.Logger.LogError("OBS日志文件读取失败:{Msg}", e.Message);
            return null;
        }

        // 从下网上找到最近一次 = Recording Start = 关键词的行
        var lines = content.Split('\n');
        string? lastLine = null;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].Contains("= Recording Start ="))
            {
                if (beforeStartTime != null)
                {
                    var timeStr = lines[i].Substring(0, 12);
                    if (DateTime.TryParse(timeStr, out var time))
                    {
                        if (time >= beforeStartTime)
                        {
                            lastLine = lines[i];
                            return time;
                        }
                    }
                }
                else
                {
                    lastLine = lines[i];
                    break;
                }
            }
        }

        if (lastLine == null)
        {
            TaskControl.Logger.LogDebug("未找到OBS日志中录制开始关键词");
            return null;
        }

        // 内容应该是 16:19:40.665: ==== Recording Start ===============================================
        var timeStr2 = lastLine.Substring(0, 12);
        // 16:19:40.665 转换到datetime
        if (DateTime.TryParse(timeStr2, out var time2))
        {
            return time2;
        }

        return null;
    }

    public static void FindStartTime(string fileName, DateTime beforeStartTime)
    {
        Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(1000);
                var time = await ProcessLog(beforeStartTime);
                if (time != null)
                {
                    var t = time.Value;
                    TaskControl.Logger.LogInformation("OBS实际录制开始时间（来自log）:{Time}", t.ToString("yyyy-MM-dd HH:mm:s.ffff"));
                    var folderPath = Global.Absolute($@"User\KeyMouseScript\{fileName}\");
                    await File.WriteAllTextAsync(Path.Combine(folderPath, "videoStartTime.txt"), (t.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalNanoseconds.ToString("F0"));
                    break;
                }
                else
                {
                    TaskControl.Logger.LogDebug("未找到OBS实际录制开始时间，等待1s后重试");
                }
            }
        });
    }
}