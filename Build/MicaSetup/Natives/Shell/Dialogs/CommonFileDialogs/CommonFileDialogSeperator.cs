using System.Diagnostics;

namespace MicaSetup.Shell.Dialogs;

public class CommonFileDialogSeparator : CommonFileDialogControl
{
    internal override void Attach(IFileDialogCustomize dialog)
    {
        Debug.Assert(dialog != null, "CommonFileDialogSeparator.Attach: dialog parameter can not be null");

        dialog!.AddSeparator(Id);

        SyncUnmanagedProperties();
    }
}
