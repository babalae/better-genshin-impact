namespace MicaSetup.Shell.Dialogs;

public interface IDialogControlHost
{
    void ApplyCollectionChanged();

    void ApplyControlPropertyChange(string propertyName, DialogControl control);

    bool IsCollectionChangeAllowed();

    bool IsControlPropertyChangeAllowed(string propertyName, DialogControl control);
}
