namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

public class RetryException : System.Exception
{
    public RetryException() : base()
    {
    }

    public RetryException(string message) : base(message)
    {
    }
}