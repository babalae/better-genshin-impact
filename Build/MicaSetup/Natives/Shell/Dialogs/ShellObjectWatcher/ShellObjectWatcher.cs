using System;
using System.ComponentModel;
using System.Threading;

namespace MicaSetup.Shell.Dialogs;

public class ShellObjectWatcher : IDisposable
{
    private readonly ShellObject _shellObject;
    private readonly bool _recursive;

    private readonly ChangeNotifyEventManager _manager = new();
    private readonly nint _listenerHandle;
    private readonly uint _message;

    private uint _registrationId;
    private volatile bool _running;

    private readonly SynchronizationContext _context = SynchronizationContext.Current;

    public ShellObjectWatcher(ShellObject shellObject, bool recursive)
    {
        if (_context == null)
        {
            _context = new SynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_context);
        }

        _shellObject = shellObject ?? throw new ArgumentNullException("shellObject");
        _recursive = recursive;

        var result = MessageListenerFilter.Register(OnWindowMessageReceived);
        _listenerHandle = result.WindowHandle;
        _message = result.Message;
    }

    public bool Running
    {
        get => _running;
        private set => _running = value;
    }

    public void Start()
    {
        if (Running) { return; }

        var entry = new SHChangeNotifyEntry
        {
            recursively = _recursive,

            pIdl = _shellObject.PIDL
        };

        _registrationId = Shell32.SHChangeNotifyRegister(
            _listenerHandle,
            ShellChangeNotifyEventSource.ShellLevel | ShellChangeNotifyEventSource.InterruptLevel | ShellChangeNotifyEventSource.NewDelivery,
             _manager.RegisteredTypes,
            _message,
            1,
            ref entry);

        if (_registrationId == 0)
        {
            throw new Win32Exception(LocalizedMessages.ShellObjectWatcherRegisterFailed);
        }

        Running = true;
    }

    public void Stop()
    {
        if (!Running) { return; }
        if (_registrationId > 0)
        {
            Shell32.SHChangeNotifyDeregister(_registrationId);
            _registrationId = 0;
        }
        Running = false;
    }

    private void OnWindowMessageReceived(WindowMessageEventArgs e)
    {
        if (e.Message.Msg == _message)
        {
            _context.Send(x => ProcessChangeNotificationEvent(e), null);
        }
    }

    private void ThrowIfRunning()
    {
        if (Running)
        {
            throw new InvalidOperationException(LocalizedMessages.ShellObjectWatcherUnableToChangeEvents);
        }
    }

    protected virtual void ProcessChangeNotificationEvent(WindowMessageEventArgs e)
    {
        if (!Running) { return; }
        if (e == null) { throw new ArgumentNullException("e"); }

        var notifyLock = new ChangeNotifyLock(e.Message);
        ShellObjectNotificationEventArgs args = notifyLock.ChangeType switch
        {
            ShellObjectChangeTypes.DirectoryRename or ShellObjectChangeTypes.ItemRename => new ShellObjectRenamedEventArgs(notifyLock),
            ShellObjectChangeTypes.SystemImageUpdate => new SystemImageUpdatedEventArgs(notifyLock),
            _ => new ShellObjectChangedEventArgs(notifyLock),
        };
        _manager.Invoke(this, notifyLock.ChangeType, args);
    }

    public event EventHandler<ShellObjectNotificationEventArgs> AllEvents
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.AllEventsMask, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.AllEventsMask, value);
        }
    }

    public event EventHandler<ShellObjectNotificationEventArgs> GlobalEvents
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.GlobalEventsMask, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.GlobalEventsMask, value);
        }
    }

    public event EventHandler<ShellObjectNotificationEventArgs> DiskEvents
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.DiskEventsMask, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.DiskEventsMask, value);
        }
    }

    public event EventHandler<ShellObjectRenamedEventArgs> ItemRenamed
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.ItemRename, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.ItemRename, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> ItemCreated
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.ItemCreate, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.ItemCreate, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> ItemDeleted
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.ItemDelete, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.ItemDelete, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> Updated
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.Update, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.Update, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> DirectoryUpdated
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.DirectoryContentsUpdate, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.DirectoryContentsUpdate, value);
        }
    }

    public event EventHandler<ShellObjectRenamedEventArgs> DirectoryRenamed
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.DirectoryRename, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.DirectoryRename, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> DirectoryCreated
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.DirectoryCreate, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.DirectoryCreate, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> DirectoryDeleted
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.DirectoryDelete, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.DirectoryDelete, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> MediaInserted
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.MediaInsert, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.MediaInsert, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> MediaRemoved
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.MediaRemove, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.MediaRemove, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> DriveAdded
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.DriveAdd, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.DriveAdd, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> DriveRemoved
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.DriveRemove, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.DriveRemove, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> FolderNetworkShared
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.NetShare, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.NetShare, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> FolderNetworkUnshared
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.NetUnshare, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.NetUnshare, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> ServerDisconnected
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.ServerDisconnect, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.ServerDisconnect, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> SystemImageChanged
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.SystemImageUpdate, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.SystemImageUpdate, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> FreeSpaceChanged
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.FreeSpace, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.FreeSpace, value);
        }
    }

    public event EventHandler<ShellObjectChangedEventArgs> FileTypeAssociationChanged
    {
        add
        {
            ThrowIfRunning();
            _manager.Register(ShellObjectChangeTypes.AssociationChange, value);
        }
        remove
        {
            ThrowIfRunning();
            _manager.Unregister(ShellObjectChangeTypes.AssociationChange, value);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        Stop();
        _manager.UnregisterAll();

        if (_listenerHandle != 0)
        {
            MessageListenerFilter.Unregister(_listenerHandle, _message);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~ShellObjectWatcher()
    {
        Dispose(false);
    }
}
