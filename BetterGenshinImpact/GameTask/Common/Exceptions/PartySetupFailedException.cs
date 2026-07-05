namespace BetterGenshinImpact.GameTask.Common.Exceptions;

public class PartySetupFailedException : System.Exception
{
    public PartySetupFailedException(string message) : base(message)
    {
    }

    public PartySetupFailedException(string message, System.Exception innerException) : base(message, innerException)
    {
    }
}
