using System.Windows.Markup;

namespace MicaSetup.Shell.Dialogs;

[ContentProperty("Items")]
public abstract class CommonFileDialogProminentControl : CommonFileDialogControl
{
    private bool isProminent;

    protected CommonFileDialogProminentControl()
    {
    }

    protected CommonFileDialogProminentControl(string text) : base(text)
    {
    }

    protected CommonFileDialogProminentControl(string name, string text) : base(name, text)
    {
    }

    public bool IsProminent
    {
        get => isProminent;
        set => isProminent = value;
    }
}
