using System;
using System.Diagnostics;

namespace MicaSetup.Shell.Dialogs;

public abstract class DialogControl
{
    private string name = null!;

    protected DialogControl()
    {
    }

    protected DialogControl(string name) : this() => Name = name;

    public IDialogControlHost HostingDialog { get; set; } = null!;

    public int Id { get; private set; }

    public string Name
    {
        get => name;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(LocalizedMessages.DialogControlNameCannotBeEmpty);
            }

            if (!string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException(LocalizedMessages.DialogControlsCannotBeRenamed);
            }

            name = value;
        }
    }

    public override bool Equals(object obj)
    {
        if (obj is DialogControl control)
            return (Id == control.Id);

        return false;
    }

    public override int GetHashCode()
    {
        if (Name == null)
        {
            return ToString().GetHashCode();
        }

        return Name.GetHashCode();
    }

    protected void ApplyPropertyChange(string propName)
    {
        Debug.Assert(!string.IsNullOrEmpty(propName), "Property changed was not specified");
        HostingDialog?.ApplyControlPropertyChange(propName, this);
    }

    protected void CheckPropertyChangeAllowed(string propName)
    {
        Debug.Assert(!string.IsNullOrEmpty(propName), "Property to change was not specified");
        HostingDialog?.IsControlPropertyChangeAllowed(propName, this);
    }
}
