using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Markup;

namespace MicaSetup.Shell.Dialogs;

[ContentProperty("Items")]
public class CommonFileDialogComboBox : CommonFileDialogProminentControl, ICommonFileDialogIndexedControls
{
    private readonly Collection<CommonFileDialogComboBoxItem> items = new Collection<CommonFileDialogComboBoxItem>();
    private int selectedIndex = -1;

    public CommonFileDialogComboBox()
    {
    }

    public CommonFileDialogComboBox(string name)
        : base(name, string.Empty)
    {
    }

    public event EventHandler SelectedIndexChanged = delegate { };

    public Collection<CommonFileDialogComboBoxItem> Items => items;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2201:DoNotRaiseReservedExceptionTypes")]
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (selectedIndex == value)
                return;

            if (HostingDialog == null)
            {
                selectedIndex = value;
                return;
            }

            if (value >= 0 && value < items.Count)
            {
                selectedIndex = value;
                ApplyPropertyChange("SelectedIndex");
            }
            else
            {
                throw new IndexOutOfRangeException(LocalizedMessages.ComboBoxIndexOutsideBounds);
            }
        }
    }

    void ICommonFileDialogIndexedControls.RaiseSelectedIndexChangedEvent()
    {
        if (Enabled)
            SelectedIndexChanged(this, EventArgs.Empty);
    }

    internal override void Attach(IFileDialogCustomize dialog)
    {
        Debug.Assert(dialog != null, "CommonFileDialogComboBox.Attach: dialog parameter can not be null");

        dialog!.AddComboBox(Id);

        for (var index = 0; index < items.Count; index++)
            dialog.AddControlItem(Id, index, items[index].Text);

        if (selectedIndex >= 0 && selectedIndex < items.Count)
        {
            dialog.SetSelectedControlItem(Id, selectedIndex);
        }
        else if (selectedIndex != -1)
        {
            throw new IndexOutOfRangeException(LocalizedMessages.ComboBoxIndexOutsideBounds);
        }

        if (IsProminent)
            dialog.MakeProminent(Id);

        SyncUnmanagedProperties();
    }
}

public class CommonFileDialogComboBoxItem
{
    private string text = string.Empty;

    public CommonFileDialogComboBoxItem()
    {
    }

    public CommonFileDialogComboBoxItem(string text) => this.text = text;

    public string Text
    {
        get => text;
        set => text = value;
    }
}
