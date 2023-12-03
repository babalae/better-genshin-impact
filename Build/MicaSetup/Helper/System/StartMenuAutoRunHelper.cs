using System;
using System.IO;
using System.Windows;

namespace MicaSetup.Helper;

public static class StartMenuAutoRunHelper
{
    public static string StartupFolder => Environment.GetEnvironmentVariable("windir") + @"\..\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup\";

    public static void Enable(string shortcutName, string targetPath, string arguments = null!)
    {
        try
        {
            if (Directory.Exists(StartupFolder))
            {
                ShortcutHelper.CreateShortcut(StartupFolder, shortcutName, targetPath, arguments);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e);
            MessageBox.Show("Create Startup ShortCut error" + "See detail following" + e.ToString());
        }
    }

    public static void Disable(string shortcutName)
    {
        try
        {
            string lnk = StartupFolder + shortcutName + ".lnk";

            if (File.Exists(lnk))
            {
                File.Delete(lnk);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }
    }

    public static void SetEnabled(bool enable, string shortcutName, string targetPath, string arguments = null!)
    {
        if (enable)
        {
            Enable(shortcutName, targetPath, arguments);
        }
        else
        {
            Disable(shortcutName);
        }
    }
}
