using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Service.ChildSession;

internal static class ChildSessionProcessLauncher
{
    private const int TaskActionExecute = 0;
    private const int TaskCreate = 2;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskRunLevelHighest = 1;
    private const int TaskRunUseSessionId = 0x4;

    internal static Task LaunchBetterGiAsync(uint childSessionId)
    {
        var startInfo = CreateBetterGiStartInfo();
        return LaunchElevatedAsync(
            childSessionId,
            startInfo.ExecutablePath,
            startInfo.Arguments,
            startInfo.WorkingDirectory);
    }

    internal static Task LaunchElevatedAsync(uint childSessionId, string executablePath)
    {
        var fullPath = ValidateExecutablePath(executablePath);
        return LaunchElevatedAsync(
            childSessionId,
            fullPath,
            string.Empty,
            Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory);
    }

    private static Task LaunchElevatedAsync(
        uint childSessionId,
        string executablePath,
        string arguments,
        string workingDirectory)
    {
        return Task.Run(() =>
            LaunchWithTemporaryTask(
                childSessionId,
                executablePath,
                arguments,
                workingDirectory));
    }

    private static ProcessLaunchInfo CreateBetterGiStartInfo()
    {
        var currentProcessPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法取得 BetterGI 程序路径。");
        var fullProcessPath = Path.GetFullPath(currentProcessPath);

        // 通过 dotnet BetterGI.dll 启动时，计划任务也需要使用 dotnet 作为入口。
        if (string.Equals(
                Path.GetFileNameWithoutExtension(fullProcessPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                throw new InvalidOperationException("无法取得 BetterGI 入口程序集路径。");
            }

            return new ProcessLaunchInfo(
                fullProcessPath,
                $"{QuoteArgument(Path.GetFullPath(entryAssemblyPath))} {CommandLineOptions.ChildSessionArgument}",
                AppContext.BaseDirectory);
        }

        return new ProcessLaunchInfo(
            ValidateExecutablePath(fullProcessPath),
            CommandLineOptions.ChildSessionArgument,
            AppContext.BaseDirectory);
    }

    private static void LaunchWithTemporaryTask(
        uint childSessionId,
        string executablePath,
        string arguments,
        string workingDirectory)
    {
        var actualChildSessionId = ChildSessionNativeMethods.TryGetChildSessionId();
        if (actualChildSessionId != childSessionId)
        {
            throw new InvalidOperationException(
                $"目标 Child Session 已发生变化。请求会话为 {childSessionId}，当前会话为 "
                + (actualChildSessionId?.ToString(CultureInfo.InvariantCulture) ?? "无")
                + "。");
        }

        var schedulerType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("当前 Windows 未提供任务计划程序 COM 服务。");
        var taskName = $"BetterGI-ChildSession-ElevatedLaunch-{Guid.NewGuid():N}";
        string accountName;
        using (var currentIdentity = WindowsIdentity.GetCurrent())
        {
            accountName = currentIdentity.Name;
        }

        object? schedulerObject = null;
        object? rootFolderObject = null;
        object? taskDefinitionObject = null;
        object? actionObject = null;
        object? registeredTaskObject = null;
        object? runningTaskObject = null;
        var taskRegistered = false;

        try
        {
            schedulerObject = Activator.CreateInstance(schedulerType)
                ?? throw new InvalidOperationException("无法创建任务计划程序 COM 对象。");
            dynamic scheduler = schedulerObject;
            scheduler.Connect();

            rootFolderObject = scheduler.GetFolder("\\");
            dynamic rootFolder = rootFolderObject;
            taskDefinitionObject = scheduler.NewTask(0);
            dynamic taskDefinition = taskDefinitionObject;

            taskDefinition.RegistrationInfo.Author = "BetterGI";
            taskDefinition.RegistrationInfo.Description =
                $"临时启动 {Path.GetFileName(executablePath)} 到 Child Session {childSessionId}";

            taskDefinition.Settings.Enabled = true;
            taskDefinition.Settings.Hidden = true;
            taskDefinition.Settings.AllowDemandStart = true;
            taskDefinition.Settings.DisallowStartIfOnBatteries = false;
            taskDefinition.Settings.StopIfGoingOnBatteries = false;
            taskDefinition.Settings.ExecutionTimeLimit = "PT0S";

            taskDefinition.Principal.UserId = accountName;
            taskDefinition.Principal.LogonType = TaskLogonInteractiveToken;
            taskDefinition.Principal.RunLevel = TaskRunLevelHighest;

            actionObject = taskDefinition.Actions.Create(TaskActionExecute);
            dynamic action = actionObject;
            action.Path = executablePath;
            action.Arguments = arguments;
            action.WorkingDirectory = workingDirectory;

            registeredTaskObject = rootFolder.RegisterTaskDefinition(
                taskName,
                taskDefinition,
                TaskCreate,
                accountName,
                null,
                TaskLogonInteractiveToken,
                null);
            taskRegistered = true;

            dynamic registeredTask = registeredTaskObject;
            runningTaskObject = registeredTask.RunEx(
                null,
                TaskRunUseSessionId,
                checked((int)childSessionId),
                null);

            if (runningTaskObject is null)
            {
                throw new InvalidOperationException("任务计划程序没有返回运行实例。");
            }
        }
        finally
        {
            if (taskRegistered && rootFolderObject is not null)
            {
                try
                {
                    dynamic rootFolder = rootFolderObject;
                    rootFolder.DeleteTask(taskName, 0);
                }
                catch (COMException)
                {
                    // 临时任务启动成功后，清理失败不应中断已经启动的目标程序。
                }
            }

            ReleaseComObject(runningTaskObject);
            ReleaseComObject(registeredTaskObject);
            ReleaseComObject(actionObject);
            ReleaseComObject(taskDefinitionObject);
            ReleaseComObject(rootFolderObject);
            ReleaseComObject(schedulerObject);
        }
    }

    private static string ValidateExecutablePath(string executablePath)
    {
        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("要启动的程序不存在。", fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("当前只允许选择 .exe 程序。", nameof(executablePath));
        }

        return fullPath;
    }

    private static string QuoteArgument(string argument)
    {
        return $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private sealed record ProcessLaunchInfo(
        string ExecutablePath,
        string Arguments,
        string WorkingDirectory);
}
