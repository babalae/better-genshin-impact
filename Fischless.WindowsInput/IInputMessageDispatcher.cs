using Vanara.PInvoke;

namespace Fischless.WindowsInput;

internal interface IInputMessageDispatcher
{
    public void DispatchInput(User32.INPUT[] inputs);
}
