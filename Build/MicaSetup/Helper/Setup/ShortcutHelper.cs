using System;
using System.IO;
using System.Runtime.InteropServices;
using File = System.IO.File;

namespace MicaSetup.Helper;

public static class ShortcutHelper
{
    public static void CreateShortcut(string directory, string shortcutName, string targetPath, string arguments = null!, string description = null!, string iconLocation = null!)
    {
        if (!Directory.Exists(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string shortcutPath = Path.Combine(directory, $"{shortcutName}.lnk");

        dynamic shell = null!;
        dynamic shortcut = null!;

        try
        {
            // Microsoft Visual C++ 2013 Redistributable
            shell = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")));
            shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.WindowStyle = 1;
            shortcut.Arguments = arguments;
            shortcut.Description = description;
            shortcut.IconLocation = string.IsNullOrWhiteSpace(iconLocation) ? targetPath : iconLocation;
            shortcut.Save();
        }
        finally
        {
            if (shortcut != null)
            {
                _ = Marshal.FinalReleaseComObject(shortcut);
            }
            if (shell != null)
            {
                _ = Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    public static void CreateShortcutOnDesktop(string shortcutName, string targetPath, string arguments = null!, string description = null!, string iconLocation = null!)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        CreateShortcut(desktop, shortcutName, targetPath, arguments, description, iconLocation);
    }

    public static void RemoveShortcutOnDesktop(string shortcutName)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string filePath = Path.Combine(desktop, $"{shortcutName}.lnk");

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    public static void CreateShortcutOnQuickLaunch(string shortcutName, string targetPath, string arguments = null!, string description = null!, string iconLocation = null!)
    {
        if (OsVersionHelper.IsWindows10_OrGreater)
        {
            string quickLaunchUserPinnedImplicitAppShortcutsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\ImplicitAppShortcuts");
            CreateShortcut(quickLaunchUserPinnedImplicitAppShortcutsPath, shortcutName, targetPath, arguments, description, iconLocation);
            ExplorerHelper.Refresh(quickLaunchUserPinnedImplicitAppShortcutsPath);

            string quickLaunchUserPinnedTaskBarPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            CreateShortcut(quickLaunchUserPinnedTaskBarPath, shortcutName, targetPath, arguments, description, iconLocation);
            ExplorerHelper.Refresh(quickLaunchUserPinnedTaskBarPath);
        }
        else
        {
            dynamic shell = null!;

            try
            {
                // Microsoft Visual C++ 2013 Redistributable
                shell = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")));
                string quickLaunchPath = shell.SpecialFolders.Item("Quick Launch");

                if (string.IsNullOrWhiteSpace(quickLaunchPath))
                {
                    quickLaunchPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch");
                }
                CreateShortcut(quickLaunchPath, shortcutName, targetPath, arguments, description, iconLocation);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {
                if (shell != null)
                {
                    _ = Marshal.FinalReleaseComObject(shell);
                }
            }
        }
    }

    public static void RemoveShortcutOnQuickLaunch(string shortcutName)
    {
        if (OsVersionHelper.IsWindows10_OrGreater)
        {
            string quickLaunchUserPinnedImplicitAppShortcutsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\ImplicitAppShortcuts");
            string quickLaunchUserPinnedImplicitAppShortcutsLnkPath = Path.Combine(quickLaunchUserPinnedImplicitAppShortcutsPath, $"{shortcutName}.lnk");

            if (File.Exists(quickLaunchUserPinnedImplicitAppShortcutsLnkPath))
            {
                File.Delete(quickLaunchUserPinnedImplicitAppShortcutsLnkPath);
            }
            ExplorerHelper.Refresh(quickLaunchUserPinnedImplicitAppShortcutsPath);

            string quickLaunchUserPinnedTaskBarPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            string quickLaunchUserPinnedTaskBarLnkPath = Path.Combine(quickLaunchUserPinnedTaskBarPath, $"{shortcutName}.lnk");

            if (File.Exists(quickLaunchUserPinnedTaskBarLnkPath))
            {
                File.Delete(quickLaunchUserPinnedTaskBarLnkPath);
            }
            ExplorerHelper.Refresh(quickLaunchUserPinnedTaskBarPath);
        }
        else
        {
            dynamic shell = null!;

            try
            {
                // Microsoft Visual C++ 2013 Redistributable
                shell = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")));
                string quickLaunchPath = shell.SpecialFolders.Item("Quick Launch");

                if (string.IsNullOrWhiteSpace(quickLaunchPath))
                {
                    quickLaunchPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch");
                }
                string quickLaunchLnkPath = Path.Combine(quickLaunchPath, $"{shortcutName}.lnk");

                if (File.Exists(quickLaunchLnkPath))
                {
                    File.Delete(quickLaunchLnkPath);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {
                if (shell != null)
                {
                    _ = Marshal.FinalReleaseComObject(shell);
                }
            }
        }
    }
}
