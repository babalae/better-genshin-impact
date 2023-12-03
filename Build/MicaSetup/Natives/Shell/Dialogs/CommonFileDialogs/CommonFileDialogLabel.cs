using System.Diagnostics;

namespace MicaSetup.Shell.Dialogs;

public class CommonFileDialogLabel : CommonFileDialogControl
{
    public CommonFileDialogLabel()
    {
    }

    public CommonFileDialogLabel(string text) : base(text)
    {
    }

    public CommonFileDialogLabel(string name, string text) : base(name, text)
    {
    }

    internal override void Attach(IFileDialogCustomize dialog)
    {
        Debug.Assert(dialog != null, "CommonFileDialog.Attach: dialog parameter can not be null");

        dialog!.AddText(Id, Text);

        SyncUnmanagedProperties();
    }
}
