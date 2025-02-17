using System;
using System.IO.Pipes;

namespace BetterGenshinImpact.Hutao;

internal static class NamedPipeClientStreamExtension
{
    public static bool TryConnectOnce(this NamedPipeClientStream clientStream)
    {
        if (clientStream.IsConnected)
        {
            return true;
        }

        try
        {
            clientStream.Connect(TimeSpan.Zero);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}