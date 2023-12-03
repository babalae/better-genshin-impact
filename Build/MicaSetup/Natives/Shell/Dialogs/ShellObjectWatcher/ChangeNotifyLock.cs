using System;
using System.Diagnostics;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8618

internal class ChangeNotifyLock
{
    private readonly uint _event = 0;

    internal ChangeNotifyLock(Message message)
    {
        var lockId = Shell32.SHChangeNotification_Lock(
                message.WParam, (int)message.LParam, out var pidl, out _event);
        try
        {
            Trace.TraceInformation("Message: {0}", (ShellObjectChangeTypes)_event);

            var notifyStruct = pidl.MarshalAs<ShellNotifyStruct>();

            var guid = new Guid(ShellIIDGuid.IShellItem2);
            if (notifyStruct.item1 != 0 &&
                (((ShellObjectChangeTypes)_event) & ShellObjectChangeTypes.SystemImageUpdate) == ShellObjectChangeTypes.None)
            {
                if (CoreErrorHelper.Succeeded(Shell32.SHCreateItemFromIDList(
                    notifyStruct.item1, ref guid, out var nativeShellItem)))
                {
                    nativeShellItem.GetDisplayName(ShellItemDesignNameOptions.FileSystemPath,
                        out var name);
                    ItemName = name;

                    Trace.TraceInformation("Item1: {0}", ItemName);
                }
            }
            else
            {
                ImageIndex = (int)notifyStruct.item1;
            }

            if (notifyStruct.item2 != 0)
            {
                if (CoreErrorHelper.Succeeded(Shell32.SHCreateItemFromIDList(
                    notifyStruct.item2, ref guid, out var nativeShellItem)))
                {
                    nativeShellItem.GetDisplayName(ShellItemDesignNameOptions.FileSystemPath,
                        out var name);
                    ItemName2 = name;

                    Trace.TraceInformation("Item2: {0}", ItemName2);
                }
            }
        }
        finally
        {
            if (lockId != 0)
            {
                Shell32.SHChangeNotification_Unlock(lockId);
            }
        }
    }

    public bool FromSystemInterrupt => ((ShellObjectChangeTypes)_event & ShellObjectChangeTypes.FromInterrupt)
                != ShellObjectChangeTypes.None;

    public int ImageIndex { get; private set; }
    public string ItemName { get; private set; }
    public string ItemName2 { get; private set; }

    public ShellObjectChangeTypes ChangeType => (ShellObjectChangeTypes)_event;
}
