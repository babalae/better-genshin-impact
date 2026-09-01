using System;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

public class MapPositionNotRecognizedException : Exception
{
    public MapPositionNotRecognizedException(string message) : base(message)
    {
    }

    public MapPositionNotRecognizedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}