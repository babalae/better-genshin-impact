using System.Net.NetworkInformation;

namespace MicaSetup.Helper;

public static class ConnectivityHelper
{
    public static bool IsNetworkAvailable => NetworkInterface.GetIsNetworkAvailable();

    public static bool Ping(string hostNameOrAddress = null!)
    {
        try
        {
            using Ping ping = new();
            PingReply reply = ping.Send(hostNameOrAddress ?? "www.microsoft.com");
            return reply?.Status == IPStatus.Success;
        }
        catch
        {
        }
        return false;
    }
}
