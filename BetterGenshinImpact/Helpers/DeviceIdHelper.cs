using System;
using DeviceId;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Helpers;

public class DeviceIdHelper
{
    private static readonly ILogger _logger = App.GetLogger<DeviceIdHelper>();
    private static readonly Lazy<string> _lazyDeviceId = new(InitializeDeviceId);
    
    public static string DeviceId => _lazyDeviceId.Value;
    
    private static string InitializeDeviceId()
    {
        try
        {
            return new DeviceIdBuilder()
                .OnWindows(windows => windows
                    .AddMacAddressFromWmi(excludeWireless: true, excludeNonPhysical: true)
                    .AddProcessorId()
                    .AddMotherboardSerialNumber()
                )
                .ToString();
        }
        catch (Exception e)
        {
            _logger.LogDebug(Lang.S["Gen_11895_98eb05"] + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" +
                             Environment.NewLine + e.Message);
            return string.Empty;
        }
    }
}