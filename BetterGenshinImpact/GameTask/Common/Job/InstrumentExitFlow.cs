namespace BetterGenshinImpact.GameTask.Common.Job;

internal enum InstrumentExitScreen
{
    Other,
    World,
    InstrumentPage,
    ExitPrompt,
    BlockedPrompt,
    OrdinaryMenu
}

internal enum InstrumentExitAction
{
    NotApplicable,
    Close,
    Confirm,
    Wait,
    Complete,
    Stop
}

internal sealed class InstrumentExitFlow
{
    private bool _clickedClose;
    private bool _clickedConfirm;
    private bool _sawWorld;
    private int _worldFrames;
    private InstrumentExitAction _terminal;

    internal bool Started => _clickedClose || _clickedConfirm;

    internal InstrumentExitAction Observe(InstrumentExitScreen screen, bool sameWorldFrame = false)
    {
        if (_terminal is InstrumentExitAction.Stop or InstrumentExitAction.Complete)
            return _terminal;

        if (screen == InstrumentExitScreen.World)
        {
            if (Started && sameWorldFrame)
                return InstrumentExitAction.Wait;

            _sawWorld = true;
            return !Started || ++_worldFrames == 2
                ? _terminal = InstrumentExitAction.Complete
                : InstrumentExitAction.Wait;
        }

        _worldFrames = 0;
        switch (screen)
        {
            case InstrumentExitScreen.BlockedPrompt:
            case InstrumentExitScreen.InstrumentPage or InstrumentExitScreen.ExitPrompt when _sawWorld:
                return _terminal = InstrumentExitAction.Stop;
            case InstrumentExitScreen.InstrumentPage when !Started:
                _clickedClose = true;
                return InstrumentExitAction.Close;
            case InstrumentExitScreen.ExitPrompt when !_clickedConfirm:
                _clickedConfirm = true;
                return InstrumentExitAction.Confirm;
            default:
                return Started ? InstrumentExitAction.Wait : InstrumentExitAction.NotApplicable;
        }
    }
}
