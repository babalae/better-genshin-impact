using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using System.Reflection;

namespace BetterGenshinImpact.UnitTest.AutoPathing;

internal static class TestConfigEnvironment
{
    private static readonly object SyncRoot = new();

    public static void EnsureInitialized()
    {
        if (ConfigService.Config != null)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (ConfigService.Config != null)
            {
                return;
            }

            var property = typeof(ConfigService).GetProperty(
                nameof(ConfigService.Config),
                BindingFlags.Public | BindingFlags.Static);
            property?.SetValue(null, new AllConfig());
        }
    }
}
