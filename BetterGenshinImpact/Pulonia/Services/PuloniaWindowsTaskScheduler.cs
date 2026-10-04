using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>仅管理当前安装目录、当前登录用户的一个 Windows 调度唤起任务。</summary>
public sealed class PuloniaWindowsTaskScheduler
{
    /// <summary>本安装独占的任务名，不删除其他安装或用户创建的任务。</summary>
    public string TaskName { get; } = GetTaskName();

    /// <summary>在计算本安装身份后立即释放 Windows 用户令牌。</summary>
    private static string GetTaskName()
    {
        using var user = WindowsIdentity.GetCurrent();
        return "BetterGI-Pulonia-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(AppContext.BaseDirectory).ToUpperInvariant() + user.User?.Value)))[..16];
    }

    /// <summary>创建可测试的任务 XML；只在交互登录会话运行，不保存用户密码。</summary>
    public static string BuildXml(string executable, string userSid, DateTimeOffset nextUtc, bool wake)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement E(string name, object? content) => new(ns + name, content);
        return new XElement(ns + "Task", new XAttribute("version", "1.2"),
            E("RegistrationInfo", E("Description", "BetterGI Pulonia：到点检查已保存的触发器；锁屏时不操作游戏。")),
            E("Triggers", new object[]
            {
                E("TimeTrigger", new object[] { E("StartBoundary", nextUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)), E("Enabled", true) }),
                E("LogonTrigger", new object[] { E("Enabled", true), E("UserId", userSid) })
            }),
            E("Principals", new XElement(ns + "Principal", new XAttribute("id", "CurrentUser"),
                E("UserId", userSid), E("LogonType", "InteractiveToken"), E("RunLevel", "HighestAvailable"))),
            E("Settings", new object[]
            {
                E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", false),
                E("StopIfGoingOnBatteries", false), E("StartWhenAvailable", true),
                E("Enabled", true), E("WakeToRun", wake), E("ExecutionTimeLimit", "PT0S")
            }),
            new XElement(ns + "Actions", new XAttribute("Context", "CurrentUser"),
                E("Exec", new object[] { E("Command", executable), E("Arguments", "--pulonia-dispatch"), E("WorkingDirectory", Path.GetDirectoryName(executable)) })))
            .ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>同步下次唤起或撤销本安装拥有的任务；COM 引用无论成功失败都释放。</summary>
    public void Synchronize(DateTimeOffset? nextUtc, bool wake)
    {
        object? scheduler = null, folder = null, registered = null;
        try
        {
            scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new InvalidOperationException("Windows 任务计划服务不可用。"));
            dynamic service = scheduler!;
            service.Connect();
            folder = service.GetFolder("\\");
            dynamic root = folder!;
            if (nextUtc is null)
            {
                try { root.DeleteTask(TaskName, 0); }
                catch (COMException ex) when ((uint)ex.HResult == 0x80070002) { }
                return;
            }
            using var user = WindowsIdentity.GetCurrent();
            var sid = user.User?.Value
                ?? throw new InvalidOperationException("不能确认当前交互用户。");
            registered = root.RegisterTask(TaskName, BuildXml(Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "BetterGI.exe"), sid, nextUtc.Value, wake),
                6, sid, null, 3, null);
        }
        finally
        {
            foreach (var value in new[] { registered, folder, scheduler })
                if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
    }
}
