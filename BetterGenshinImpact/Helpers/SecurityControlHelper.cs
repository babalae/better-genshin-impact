using BetterGenshinImpact.Helpers;
﻿using System;
using System.IO;
using System.Security.AccessControl;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.View.Windows;
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
            TaskControl.Logger.LogError(Lang.S["Gen_11915_d8e122"] + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
            ThemedMessageBox.Warning(Lang.S["Gen_11914_634dc2"] + e.Message);
        }
    }
}
