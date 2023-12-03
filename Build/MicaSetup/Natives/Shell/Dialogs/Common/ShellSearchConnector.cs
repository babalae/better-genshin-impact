using MicaSetup.Helper;

namespace MicaSetup.Shell.Dialogs;

public sealed class ShellSearchConnector : ShellSearchCollection
{
    internal ShellSearchConnector() => OsVersionHelper.ThrowIfNotWin7();

    internal ShellSearchConnector(IShellItem2 shellItem)
        : this() => nativeShellItem = shellItem;

    public new static bool IsPlatformSupported =>
            OsVersionHelper.IsWindows7_OrGreater;
}
