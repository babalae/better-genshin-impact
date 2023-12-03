using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Markup;

namespace MicaSetup.Shell.Dialogs;

[ContentProperty("Items")]
public class CommonFileDialogRadioButtonList : CommonFileDialogControl, ICommonFileDialogIndexedControls
{
    private readonly Collection<CommonFileDialogRadioButtonListItem> items = new Collection<CommonFileDialogRadioButtonListItem>();
    private int selectedIndex = -1;

    public CommonFileDialogRadioButtonList()
    {
    }

    public CommonFileDialogRadioButtonList(string name) : base(name, string.Empty)
    {
    }

    public event EventHandler SelectedIndexChanged = delegate { };

    public Collection<CommonFileDialogRadioButtonListItem> Items => items;

    [SuppressMessage("Microsoft.Usage", "CA2201:")]
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (selectedIndex == value) { return; }

            if (HostingDialog == null)
            {
                selectedIndex = value;
            }
            else if (value >= 0 && value < items.Count)
            {
                selectedIndex = value;
                ApplyPropertyChange("SelectedIndex");
            }
            else
            {
                throw new IndexOutOfRangeException(LocalizedMessages.RadioButtonListIndexOutOfBounds);
            }
        }
    }

    void ICommonFileDialogIndexedControls.RaiseSelectedIndexChangedEvent()
    {
        if (Enabled) { SelectedIndexChanged(this, EventArgs.Empty); }
    }

    internal override void Attach(IFileDialogCustomize dialog)
    {
        Debug.Assert(dialog != null, "CommonFileDialogRadioButtonList.Attach: dialog parameter can not be null");

        dialog!.AddRadioButtonList(Id);

        for (var index = 0; index < items.Count; index++)
        {
            dialog.AddControlItem(Id, index, items[index].Text);
        }

        if (selectedIndex >= 0 && selectedIndex < items.Count)
        {
            dialog.SetSelectedControlItem(Id, selectedIndex);
        }
        else if (selectedIndex != -1)
        {
            throw new IndexOutOfRangeException(LocalizedMessages.RadioButtonListIndexOutOfBounds);
        }

        SyncUnmanagedProperties();
    }
}

public class CommonFileDialogRadioButtonListItem
{
    public CommonFileDialogRadioButtonListItem() : this(string.Empty)
    {
    }

    public CommonFileDialogRadioButtonListItem(string text) => Text = text;

    public string Text { get; set; }
}
