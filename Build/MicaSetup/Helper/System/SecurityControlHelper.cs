using System;
using System.IO;
using System.Security.AccessControl;
using System.Windows;

namespace MicaSetup.Helper;

public static class SecurityControlHelper
{
    public static void AllowFullFileSecurity(string filePath)
    {
        if (!RuntimeHelper.IsElevated)
        {
            return;
        }

        try
        {
            FileInfo fileInfo = new(filePath);
            FileSecurity fileSecurity = fileInfo.GetAccessControl();
            fileSecurity.AddAccessRule(new FileSystemAccessRule("Everyone", FileSystemRights.FullControl, AccessControlType.Allow));
            fileSecurity.AddAccessRule(new FileSystemAccessRule("Users", FileSystemRights.FullControl, AccessControlType.Allow));
            fileInfo.SetAccessControl(fileSecurity);
        }
        catch (Exception e)
        {
            Logger.Error(e);
            MessageBox.Show("Allow Full File Security Error" + e.ToString());
        }
    }

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
            Logger.Error(e);
            MessageBox.Show("Allow Full Folder Security Error" + e.ToString());
        }
    }
}
