using System;
using System.Diagnostics;

namespace MicaSetup.Shell.Dialogs;

public class CommonFileDialogCheckBox : CommonFileDialogProminentControl
{
    private bool isChecked;

    public CommonFileDialogCheckBox()
    {
    }

    public CommonFileDialogCheckBox(string text) : base(text)
    {
    }

    public CommonFileDialogCheckBox(string name, string text) : base(name, text)
    {
    }

    public CommonFileDialogCheckBox(string text, bool isChecked)
        : base(text) => this.isChecked = isChecked;

    public CommonFileDialogCheckBox(string name, string text, bool isChecked)
        : base(name, text) => this.isChecked = isChecked;

    public event EventHandler CheckedChanged = delegate { };

    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (isChecked != value)
            {
                isChecked = value;
                ApplyPropertyChange("IsChecked");
            }
        }
    }

    internal override void Attach(IFileDialogCustomize dialog)
    {
        Debug.Assert(dialog != null, "CommonFileDialogCheckBox.Attach: dialog parameter can not be null");

        dialog!.AddCheckButton(Id, Text, isChecked);

        if (IsProminent) { dialog.MakeProminent(Id); }

        ApplyPropertyChange("IsChecked");

        SyncUnmanagedProperties();
    }

    internal void RaiseCheckedChangedEvent()
    {
        if (Enabled)
        {
            CheckedChanged(this, EventArgs.Empty);
        }
    }
}
