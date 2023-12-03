using System;
using System.IO;

namespace MicaSetup.Helper;

public static class TempPathForkHelper
{
    public const string ForkedCli = "forked";

    public static void Fork()
    {
        if (!CommandLineHelper.Has(ForkedCli))
        {
            string tempPath = SpecialPathHelper.TempPath;

            if (!Directory.Exists(tempPath))
            {
                _ = Directory.CreateDirectory(tempPath);
            }
            string forkedFilePath = Path.Combine(tempPath, $"{(Option.Current.IsUninst ? "Uninst" : "Setup")}.exe");

            try
            {
                string domainFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName);
                File.Copy(domainFilePath, forkedFilePath);
                Logger.Info($"[UseTempPathFork] Copy domain file from '{domainFilePath}' to '{forkedFilePath}'");
                RuntimeHelper.RestartAsElevated(forkedFilePath, tempPath, $"{RuntimeHelper.ReArguments()} /{ForkedCli}");
            }
            catch (Exception e)
            {
                Logger.Fatal(e);
            }
        }
    }

    public static void Clean()
    {
        if (CommandLineHelper.Has(ForkedCli))
        {
            string tempPath = AppDomain.CurrentDomain.BaseDirectory;
            string filePath = Path.Combine(tempPath, $"{(Option.Current.IsUninst ? "Uninst" : "Setup")}.exe");

            _ = UninstallHelper.DeleteDelayUntilReboot(tempPath);
            try
            {
                FluentProcess.Create()
                    .FileName("powershell.exe")
                    .Arguments(
                        $"""
                            Start-Sleep -s 3;
                            Remove-Item "{filePath}";
                            Remove-Item "{tempPath}";
                        """)
                    .UseShellExecute(false)
                    .CreateNoWindow()
                    .Start()
                    .Forget();
            }
            catch
            {
            }
        }
    }
}
