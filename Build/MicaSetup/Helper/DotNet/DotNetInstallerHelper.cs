using System;
using System.ComponentModel;
using System.IO;

namespace MicaSetup.Helper;

public static class DotNetInstallerHelper
{
    public static DotNetInstallInfo GetInfo(Version version, bool offline = false)
    {
        DotNetInstallInfo info = new()
        {
            Offline = offline,
            Version = version,
            SDKsUrl = "https://dotnet.microsoft.com/en-us/download/visual-studio-sdks",
        };

        if (version == new Version(4, 8, 1))
        {
            info.Name = $".NET Framework 4.8.1";
            info.FileName = offline switch
            {
                true => "ndp481-x86-x64-allos-enu.exe",
                false => "ndp481-web.exe",
            };
            info.TempFilePath = Path.Combine();
            info.Arguments = " /q /norestart /ChainingPackage FullX64Bootstrapper";
            info.DownloadUrl = offline switch
            {
                true => "https://download.visualstudio.microsoft.com/download/pr/6f083c7e-bd40-44d4-9e3f-ffba71ec8b09/3951fd5af6098f2c7e8ff5c331a0679c/ndp481-x86-x64-allos-enu.exe",
                false => "https://download.visualstudio.microsoft.com/download/pr/6f083c7e-bd40-44d4-9e3f-ffba71ec8b09/d05099507287c103a91bb68994498bde/ndp481-web.exe",
            };
            info.ThankYouUrl = "https://dotnet.microsoft.com/en-us/download/dotnet-framework/thank-you/net481-web-installer";
            info.ReleaseNoteUrl = "https://devblogs.microsoft.com/dotnet/announcing-dotnet-framework-481";
        }
        else if (version == new Version(4, 8))
        {
            info.Name = $".NET Framework 4.8";
            info.FileName = offline switch
            {
                true => "ndp48-x86-x64-allos-enu.exe",
                false => "ndp48-web.exe",
            };
            info.TempFilePath = Path.Combine(SpecialPathHelper.TempPath.SureDirectoryExists(), info.FileName);
            info.Arguments = " /q /norestart /ChainingPackage FullX64Bootstrapper";
            info.DownloadUrl = offline switch
            {
                true => "https://download.visualstudio.microsoft.com/download/pr/2d6bb6b2-226a-4baa-bdec-798822606ff1/8494001c276a4b96804cde7829c04d7f/ndp48-x86-x64-allos-enu.exe",
                false => "https://download.visualstudio.microsoft.com/download/pr/2d6bb6b2-226a-4baa-bdec-798822606ff1/9b7b8746971ed51a1770ae4293618187/ndp48-web.exe",
            };
            info.ThankYouUrl = "https://dotnet.microsoft.com/en-us/download/dotnet-framework/thank-you/net48-web-installer";
            info.ReleaseNoteUrl = "https://devblogs.microsoft.com/dotnet/announcing-the-net-framework-4-8";
        }
        else
        {
            throw new NotImplementedException();
        }
        return info;
    }

    public static bool Download(DotNetInstallInfo info, InstallerProgressChangedEventHandler callback = null!)
    {
        _ = info.Version ?? throw new NotImplementedException();
        _ = info.DownloadUrl ?? throw new NotImplementedException();
        _ = info.FileName ?? throw new NotImplementedException();

        Logger.Info($"[DotNetInstaller] Download .NET Framework {info.Version} from '{info.DownloadUrl}' and save to '{info.TempFilePath}'.");

        if (!ConnectivityHelper.IsNetworkAvailable || !ConnectivityHelper.Ping())
        {
            throw new Exception($"{Mui("NetworkUnavailableTips")}");
        }
        callback?.Invoke(ProgressType.Download, new ProgressChangedEventArgs(0, null!));
        return SimpleDownloadHelper.DownloadFile(info.DownloadUrl, info.TempFilePath, (s, e) => callback?.Invoke(ProgressType.Download, e));
    }

    public static bool Install(DotNetInstallInfo info, InstallerProgressChangedEventHandler callback = null!)
    {
        _ = info.Version ?? throw new NotImplementedException();
        _ = info.FileName ?? throw new NotImplementedException();
        _ = info.Arguments ?? throw new NotImplementedException();

        Logger.Info($"[DotNetInstaller] Install .NET Framework {info.Version} from '{info.TempFilePath}'.");

        callback?.Invoke(ProgressType.Install, new ProgressChangedEventArgs(-1, null!));
        int exitCode = FluentProcess.Create()
            .FileName(info.TempFilePath)
            .Arguments(info.Arguments)
            .Start()
            .WaitForExit()
            .ExitCode;
        bool ret = exitCode == 0;

        if (ret)
        {
            callback?.Invoke(ProgressType.Install, new ProgressChangedEventArgs(100, null!));
        }
        else
        {
            Logger.Error($"[DotNetInstaller] Install .NET Framework {info.Version} exit with {exitCode}.");
        }
        return ret;
    }
}

public class DotNetInstallInfo
{
    public string Name { get; set; } = null!;
    public Version Version { get; set; } = null!;
    public string SDKsUrl { get; set; } = null!;
    public string ThankYouUrl { get; set; } = null!;
    public string DownloadUrl { get; set; } = null!;
    public string ReleaseNoteUrl { get; set; } = null!;
    public bool Offline { get; set; } = false;
    public string FileName { get; set; } = null!;
    public string TempFilePath { get; set; } = null!;
    public string Arguments { get; set; } = null!;
}

public delegate void InstallerProgressChangedEventHandler(ProgressType type, ProgressChangedEventArgs e);

public enum ProgressType
{
    Download,
    Install,
}
