using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MicaSetup.Shell.Dialogs;

public sealed class CommonFileDialogControlCollection<T> : Collection<T> where T : DialogControl
{
    private readonly IDialogControlHost hostingDialog;

    internal CommonFileDialogControlCollection(IDialogControlHost host) => hostingDialog = host;

    public T this[string name]
    {
        get
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException(LocalizedMessages.DialogControlCollectionEmptyName, "name");
            }

            foreach (var control in base.Items)
            {
                CommonFileDialogGroupBox groupBox;
                if (control.Name == name)
                {
                    return control;
                }
                else if ((groupBox = (control as CommonFileDialogGroupBox)!) != null)
                {
                    foreach (T subControl in groupBox.Items)
                    {
                        if (subControl.Name == name) { return subControl; }
                    }
                }
            }
            return null!;
        }
    }

    internal DialogControl GetControlbyId(int id) => GetSubControlbyId(Items.Cast<DialogControl>(), id);

    internal DialogControl GetSubControlbyId(IEnumerable<DialogControl> controlCollection, int id)
    {
        if (controlCollection == null) { return null!; }

        foreach (var control in controlCollection)
        {
            if (control.Id == id) { return control; }

            var groupBox = control as CommonFileDialogGroupBox;
            if (groupBox != null)
            {
                var temp = GetSubControlbyId(groupBox.Items, id);
                if (temp != null) { return temp; }
            }
        }

        return null!;
    }

    protected override void InsertItem(int index, T control)
    {
        if (Items.Contains(control))
        {
            throw new InvalidOperationException(
                LocalizedMessages.DialogControlCollectionMoreThanOneControl);
        }
        if (control.HostingDialog != null)
        {
            throw new InvalidOperationException(
                LocalizedMessages.DialogControlCollectionRemoveControlFirst);
        }
        if (!hostingDialog.IsCollectionChangeAllowed())
        {
            throw new InvalidOperationException(
                LocalizedMessages.DialogControlCollectionModifyingControls);
        }
        if (control is CommonFileDialogMenuItem)
        {
            throw new InvalidOperationException(
                LocalizedMessages.DialogControlCollectionMenuItemControlsCannotBeAdded);
        }

        control.HostingDialog = hostingDialog;
        base.InsertItem(index, control);

        hostingDialog.ApplyCollectionChanged();
    }

    protected override void RemoveItem(int index) => throw new NotSupportedException(LocalizedMessages.DialogControlCollectionCannotRemoveControls);
}
