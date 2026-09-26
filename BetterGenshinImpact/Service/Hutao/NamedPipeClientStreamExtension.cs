using System;
using System.IO;
using System.IO.Pipes;

namespace BetterGenshinImpact.Service.Hutao;

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
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
        {
            return false;
        }
    }
}
