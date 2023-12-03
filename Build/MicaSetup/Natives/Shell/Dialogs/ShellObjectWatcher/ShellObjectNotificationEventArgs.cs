using System;

namespace MicaSetup.Shell.Dialogs;

public class ShellObjectNotificationEventArgs : EventArgs
{
    public ShellObjectChangeTypes ChangeType { get; private set; }

    public bool FromSystemInterrupt { get; private set; }

    internal ShellObjectNotificationEventArgs(ChangeNotifyLock notifyLock)
    {
        ChangeType = notifyLock.ChangeType;
        FromSystemInterrupt = notifyLock.FromSystemInterrupt;
    }
}

public class ShellObjectChangedEventArgs : ShellObjectNotificationEventArgs
{
    public string Path { get; private set; }

    internal ShellObjectChangedEventArgs(ChangeNotifyLock notifyLock)
        : base(notifyLock) => Path = notifyLock.ItemName;
}

public class ShellObjectRenamedEventArgs : ShellObjectChangedEventArgs
{
    public string NewPath { get; private set; }

    internal ShellObjectRenamedEventArgs(ChangeNotifyLock notifyLock)
        : base(notifyLock) => NewPath = notifyLock.ItemName2;
}

public class SystemImageUpdatedEventArgs : ShellObjectNotificationEventArgs
{
    public int ImageIndex { get; private set; }

    internal SystemImageUpdatedEventArgs(ChangeNotifyLock notifyLock)
        : base(notifyLock) => ImageIndex = notifyLock.ImageIndex;
}
