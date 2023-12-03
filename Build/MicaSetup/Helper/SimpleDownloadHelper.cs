using System;
using System.Net;

namespace MicaSetup.Helper;

public static class SimpleDownloadHelper
{
    public static bool DownloadFile(string address, string fileName, DownloadProgressChangedEventHandler callback = null!)
    {
        try
        {
            using WebClient client = new();
            client.DownloadProgressChanged += (sender, e) =>
            {
                Logger.Debug($"[DownloadFile] {address} saved to '{fileName}', {e.ProgressPercentage}% completed.");
                callback?.Invoke(sender, e);
            };

            client.DownloadFile(address, fileName);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }
        return false;
    }
}
