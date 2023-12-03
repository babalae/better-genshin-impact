using System;

namespace MicaSetup.Shell.Dialogs;

internal interface ICommonFileDialogIndexedControls
{
    event EventHandler SelectedIndexChanged;

    int SelectedIndex { get; set; }

    void RaiseSelectedIndexChangedEvent();
}
