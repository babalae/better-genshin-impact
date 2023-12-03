using System.ComponentModel;

namespace MicaSetup.Shell.Dialogs;

public class CommonFileDialogFolderChangeEventArgs : CancelEventArgs
{
    public CommonFileDialogFolderChangeEventArgs(string folder) => Folder = folder;

    public string Folder { get; set; }
}
