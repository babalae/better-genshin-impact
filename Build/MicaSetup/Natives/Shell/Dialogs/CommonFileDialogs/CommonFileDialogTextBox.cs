using System.Diagnostics;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8618

public class CommonFileDialogTextBox : CommonFileDialogControl
{
    private IFileDialogCustomize customizedDialog;

    public CommonFileDialogTextBox() : base(string.Empty)
    {
    }

    public CommonFileDialogTextBox(string text) : base(text)
    {
    }

    public CommonFileDialogTextBox(string name, string text) : base(name, text)
    {
    }

    public override string Text
    {
        get
        {
            if (!Closed)
            {
                SyncValue();
            }

            return base.Text;
        }

        set
        {
            if (customizedDialog != null)
            {
                customizedDialog.SetEditBoxText(Id, value);
            }

            base.Text = value;
        }
    }

    internal bool Closed { get; set; }

    internal override void Attach(IFileDialogCustomize dialog)
    {
        Debug.Assert(dialog != null, "CommonFileDialogTextBox.Attach: dialog parameter can not be null");

        dialog!.AddEditBox(Id, Text);

        customizedDialog = dialog;

        SyncUnmanagedProperties();

        Closed = false;
    }

    internal void SyncValue()
    {
        if (customizedDialog != null)
        {
            customizedDialog.GetEditBoxText(Id, out var textValue);

            base.Text = textValue;
        }
    }
}
