namespace MicaSetup.Shell.Dialogs;

public class ShellNonFileSystemFolder : ShellFolder
{
    internal ShellNonFileSystemFolder()
    {
    }

    internal ShellNonFileSystemFolder(IShellItem2 shellItem) => nativeShellItem = shellItem;
}
