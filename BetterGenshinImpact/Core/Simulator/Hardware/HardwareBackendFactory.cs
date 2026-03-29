using BetterGenshinImpact.Core.Config;
using System;

namespace BetterGenshinImpact.Core.Simulator.Hardware;

internal sealed class HardwareBackendFactory
{
    public IHardwareKeyboardBackend CreateKeyboardBackend(HardwareInputConfig config)
    {
        if (string.Equals(config.KeyboardHardwareVendor, HardwareInputConfigValues.Makxd, StringComparison.OrdinalIgnoreCase))
        {
            return MakxdMakApiBackend.CreateKeyboard(config.KeyboardEffectiveComPort);
        }

        if (string.Equals(config.KeyboardFerrumApi, HardwareInputConfigValues.Dhz, StringComparison.OrdinalIgnoreCase))
        {
            return FerrumDhzApiBackend.CreateKeyboard(config.KeyboardFerrumNetIp, config.KeyboardFerrumNetPort, config.KeyboardFerrumNetUuid);
        }

        if (string.Equals(config.KeyboardFerrumApi, HardwareInputConfigValues.Net, StringComparison.OrdinalIgnoreCase))
        {
            return FerrumNetApiBackend.CreateKeyboard(config.KeyboardFerrumNetIp, config.KeyboardFerrumNetPort, config.KeyboardFerrumNetUuid);
        }

        return FerrumKmApiBackend.CreateKeyboard(config.KeyboardEffectiveComPort);
    }

    public IHardwareMouseBackend CreateMouseBackend(HardwareInputConfig config)
    {
        if (string.Equals(config.MouseHardwareVendor, HardwareInputConfigValues.Makcu, StringComparison.OrdinalIgnoreCase))
        {
            return MakcuKmApiBackend.CreateMouse(config.MouseEffectiveComPort);
        }

        if (string.Equals(config.MouseHardwareVendor, HardwareInputConfigValues.Makxd, StringComparison.OrdinalIgnoreCase))
        {
            return MakxdMakApiBackend.CreateMouse(config.MouseEffectiveComPort);
        }

        if (string.Equals(config.MouseFerrumApi, HardwareInputConfigValues.Dhz, StringComparison.OrdinalIgnoreCase))
        {
            return FerrumDhzApiBackend.CreateMouse(config.MouseFerrumNetIp, config.MouseFerrumNetPort, config.MouseFerrumNetUuid);
        }

        if (string.Equals(config.MouseFerrumApi, HardwareInputConfigValues.Net, StringComparison.OrdinalIgnoreCase))
        {
            return FerrumNetApiBackend.CreateMouse(config.MouseFerrumNetIp, config.MouseFerrumNetPort, config.MouseFerrumNetUuid);
        }

        return FerrumKmApiBackend.CreateMouse(config.MouseEffectiveComPort);
    }
}
