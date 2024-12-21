namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception; // TODO: change this namespace to BetterGenshinImpact.GameTask.Common.Exception

public class RetryNoCountException : System.Exception
{
    public RetryNoCountException() : base()
    {
    }

    public RetryNoCountException(string message) : base(message)
    {
    }
}
