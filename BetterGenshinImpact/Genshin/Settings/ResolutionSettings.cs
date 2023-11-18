using System;
using Microsoft.Win32;

namespace BetterGenshinImpact.Genshin.Settings;

internal class ResolutionSettings
{
    protected string? height_name = null;
    protected string? width_name = null;
    protected string? fullscreen_name = null;

    public int Height { get; protected set; }
    public int Width { get; protected set; }
    public bool FullScreen { get; protected set; }

    public ResolutionSettings()
    {
        using RegistryKey hk = GenshinRegistry.GetRegistryKey();
        string[] names = hk.GetValueNames();

        foreach (string name in names)
        {
            if (name.Contains("Width"))
            {
                width_name = name;
            }
            if (name.Contains("Height"))
            {
                height_name = name;
            }
            if (name.Contains("Fullscreen"))
            {
                fullscreen_name = name;
            }
        }
        Read();
    }

    private void Read()
    {
        using RegistryKey hk = GenshinRegistry.GetRegistryKey();

        Height = Convert.ToInt32(hk.GetValue(height_name));
        Width = Convert.ToInt32(hk.GetValue(width_name));
        FullScreen = Convert.ToInt32(hk.GetValue(fullscreen_name)) == 1;
    }
}
