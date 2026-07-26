using System;

namespace BetterGenshinImpact.Service.ChildSession;

public sealed class ChildSessionConnectionFailedEventArgs(
    string message,
    int errorCode,
    int? extendedErrorCode = null) : EventArgs
{
    public string Message { get; } = message;

    public int ErrorCode { get; } = errorCode;

    public int? ExtendedErrorCode { get; } = extendedErrorCode;
}
