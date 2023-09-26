using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Vision.Recognition.Helper;

public class Log
{
    public static void LogInformation(string? message, params object?[] args)
    {
        VisionContext.Instance().Log?.LogInformation(message, args);
    }

    public static void LogWarning(string? message, params object?[] args)
    {
        VisionContext.Instance().Log?.LogWarning(message, args);
    }

    public static void LogError(string? message, params object?[] args)
    {
        VisionContext.Instance().Log?.LogError(message, args);
    }
}
