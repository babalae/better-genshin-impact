namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8618

public abstract class CommonFileDialogControl : DialogControl
{
    private bool enabled = true;

    private bool isAdded;

    private string textValue;

    private bool visible = true;

    protected CommonFileDialogControl()
    {
    }

    protected CommonFileDialogControl(string text)
        : base() => textValue = text;

    protected CommonFileDialogControl(string name, string text)
        : base(name) => textValue = text;

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (value == enabled) { return; }

            enabled = value;
            ApplyPropertyChange("Enabled");
        }
    }

    public virtual string Text
    {
        get => textValue;
        set
        {
            if (value != textValue)
            {
                textValue = value;
                ApplyPropertyChange("Text");
            }
        }
    }

    public bool Visible
    {
        get => visible;
        set
        {
            if (value == visible) { return; }

            visible = value;
            ApplyPropertyChange("Visible");
        }
    }

    internal bool IsAdded
    {
        get => isAdded;
        set => isAdded = value;
    }

    internal abstract void Attach(IFileDialogCustomize dialog);

    internal virtual void SyncUnmanagedProperties()
    {
        ApplyPropertyChange("Enabled");
        ApplyPropertyChange("Visible");
    }
}
