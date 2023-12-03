namespace MicaSetup.Shell.Dialogs;

internal static class DialogsDefaults
{
    internal const int IdealWidth = 0;

    internal const int ProgressBarMaximumValue = 100;
    internal const int ProgressBarMinimumValue = 0;
    internal const int ProgressBarStartingValue = 0;
    internal static string Caption => LocalizedMessages.DialogDefaultCaption;
    internal static string Content => LocalizedMessages.DialogDefaultContent;
    internal static string MainInstruction => LocalizedMessages.DialogDefaultMainInstruction;
}
