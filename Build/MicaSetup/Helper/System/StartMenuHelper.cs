using MicaSetup.Natives;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace MicaSetup.Helper;

public static class StartMenuHelper
{
    public static void AddToRecent(string targetPath)
    {
        Shell32.SHAddToRecentDocs(SHARD.SHARD_PATHW, targetPath);
        if (Marshal.GetLastWin32Error() != 0)
        {
            throw new Win32Exception(nameof(Shell32.SHAddToRecentDocs));
        }
    }

    public static void CreateStartMenuFolder(string folderName, string targetPath, bool isCreateUninst = true)
    {
        string startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs");
        string startMenuFolderPath = Path.Combine(startMenuPath, folderName);

        if (!Directory.Exists(startMenuFolderPath))
        {
            Directory.CreateDirectory(startMenuFolderPath);
        }
        ShortcutHelper.CreateShortcut(startMenuFolderPath, folderName, targetPath);
        AddToRecent(Path.Combine(startMenuFolderPath, $"{folderName}.lnk"));

        if (isCreateUninst)
        {
            string uninstTargetPath = Path.Combine($"{new FileInfo(targetPath).Directory.FullName}", "Uninst.exe");
            ShortcutHelper.CreateShortcut(startMenuFolderPath, $"Uninstall_{folderName}", uninstTargetPath);
        }
    }

    public static void RemoveStartMenuFolder(string folderName)
    {
        string startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs");
        string startMenuFolderPath = Path.Combine(startMenuPath, folderName);

        if (Directory.Exists(startMenuFolderPath))
        {
            Directory.Delete(startMenuFolderPath, true);
        }
    }
}
