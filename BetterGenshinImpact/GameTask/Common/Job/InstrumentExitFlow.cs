namespace BetterGenshinImpact.GameTask.Common.Job;

internal enum InstrumentExitScreen
{
    Other,
    World,
    UnrelatedPrompt,
    InstrumentPage,
    ExitPrompt,
    BlockedPrompt
}

internal enum InstrumentExitAction
{
    Close,
    Confirm,
    Wait,
    Complete
}

internal sealed class InstrumentExitFlow
{
    private bool _clickedClose;
    private bool _clickedConfirm;
    private int _worldFrames;
    internal InstrumentExitAction Observe(InstrumentExitScreen screen, bool sameWorldFrame = false, bool worldReady = true)
    {
        if (screen == InstrumentExitScreen.World)
        {
            if (!worldReady) _worldFrames = 0;
            else if (!sameWorldFrame) _worldFrames++;
            return _worldFrames >= 2 ? InstrumentExitAction.Complete : InstrumentExitAction.Wait;
        }

        _worldFrames = 0;
        switch (screen)
        {
            case InstrumentExitScreen.InstrumentPage when !_clickedClose && !_clickedConfirm:
                _clickedClose = true;
                return InstrumentExitAction.Close;
            case InstrumentExitScreen.ExitPrompt when !_clickedConfirm:
                _clickedConfirm = true;
                return InstrumentExitAction.Confirm;
            default:
                return InstrumentExitAction.Wait;
        }
    }
}
