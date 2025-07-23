using System;
using System.IO;
using System.Security.AccessControl;
using System.Windows;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Helpers;

public static class SecurityControlHelper
{

    public static void AllowFullFolderSecurity(string dirPath)
    {
        if (!RuntimeHelper.IsElevated)
        {
            return;
        }

        try
        {
            DirectoryInfo dir = new(dirPath);
            DirectorySecurity dirSecurity = dir.GetAccessControl(AccessControlSections.All);
            InheritanceFlags inherits = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            FileSystemAccessRule everyoneFileSystemAccessRule = new("Everyone", FileSystemRights.FullControl, inherits, PropagationFlags.None, AccessControlType.Allow);
            FileSystemAccessRule usersFileSystemAccessRule = new("Users", FileSystemRights.FullControl, inherits, PropagationFlags.None, AccessControlType.Allow);
            dirSecurity.ModifyAccessRule(AccessControlModification.Add, everyoneFileSystemAccessRule, out _);
            dirSecurity.ModifyAccessRule(AccessControlModification.Add, usersFileSystemAccessRule, out _);
            dir.SetAccessControl(dirSecurity);
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogError("首次运行自动初始化按键绑定异常：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
            var localizationService = App.GetService<ILocalizationService>();
            MessageBox.Show(localizationService.GetString("dialog.securityError") + e.Message);
        }
    }
}
