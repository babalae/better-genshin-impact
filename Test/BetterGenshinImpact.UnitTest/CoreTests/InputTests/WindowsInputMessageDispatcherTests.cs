using Fischless.WindowsInput;

namespace BetterGenshinImpact.UnitTest.CoreTests.InputTests;

public class WindowsInputMessageDispatcherTests
{
    [Fact]
    public void FailureMessageContainsNativeDiagnosticsAndUipiCaveat()
    {
        var message = WindowsInputMessageDispatcher.CreateFailureMessage(
            requested: 3,
            sent: 0,
            errorCode: 5,
            errorMessage: "Access is denied.",
            processId: 1234,
            sessionId: 7);

        Assert.Contains("requested=3", message);
        Assert.Contains("sent=0", message);
        Assert.Contains("win32Error=5", message);
        Assert.Contains("Access is denied.", message);
        Assert.Contains("pid=1234", message);
        Assert.Contains("sessionId=7", message);
        Assert.Contains("UIPI", message);
        Assert.Contains("GetLastError", message);
    }
}
