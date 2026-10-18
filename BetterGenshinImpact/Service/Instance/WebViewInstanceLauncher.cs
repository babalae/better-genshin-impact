using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace BetterGenshinImpact.Service.Instance;

/// <summary>
/// 从 Primary 启动云原神网页版实例：同一 Windows Session 内另起一个 BetterGI 进程，
/// 参数为 <c>--instance webview --instance-name &lt;实例名&gt; start</c>。
/// 新进程继承当前进程的权限，启动后通过根管道登记到 Primary，并自动打开宿主窗口等待绑定
/// </summary>
public sealed class WebViewInstanceLauncher(
    WebViewInstanceStore store,
    IConfigService configService,
    ILogger<WebViewInstanceLauncher> logger)
{
    /// <summary>
    /// 启动指定实例。实例不存在或已在运行时抛出 <see cref="InvalidOperationException"/>
    /// </summary>
    public void Launch(string name)
    {
        if (!WebViewInstanceStore.TryNormalizeName(name, out var normalized, out var error))
        {
            throw new ArgumentException(error, nameof(name));
        }

        if (!store.Exists(normalized))
        {
            throw new InvalidOperationException($"实例「{normalized}」不存在，请先新建");
        }

        if (WebViewInstanceStore.IsRunning(normalized))
        {
            throw new InvalidOperationException($"实例「{normalized}」已在运行");
        }

        // 所有实例共用同一个 config.json，启动前先写入防抖窗口内尚未落盘的配置改动
        configService.Flush();

        var startInfo = CreateStartInfo(normalized);
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("启动网页版实例进程失败");
        logger.LogInformation("已启动云原神网页版实例「{Name}」，进程 {ProcessId}", normalized, process.Id);
    }

    private static ProcessStartInfo CreateStartInfo(string name)
    {
        var processPath = Path.GetFullPath(Environment.ProcessPath
                                           ?? throw new InvalidOperationException("无法取得 BetterGI 程序路径。"));
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
        };

        // 通过 dotnet BetterGI.dll 运行时，入口改为 dotnet，参数前加入口程序集路径
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                throw new InvalidOperationException("无法取得 BetterGI 入口程序集路径。");
            }

            startInfo.ArgumentList.Add(Path.GetFullPath(entryAssemblyPath));
        }

        startInfo.ArgumentList.Add(CommandLineOptions.InstanceArgument);
        startInfo.ArgumentList.Add("webview");
        startInfo.ArgumentList.Add(CommandLineOptions.InstanceNameArgument);
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add("start");
        return startInfo;
    }
}
