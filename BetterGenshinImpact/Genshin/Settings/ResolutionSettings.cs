using System;

namespace BetterGenshinImpact.Genshin.Settings;

public class ResolutionSettings
{
    protected string? height_name = null;
    protected string? width_name = null;
    protected string? fullscreen_name = null;

    public int Height { get; protected set; }
    public int Width { get; protected set; }
    public bool FullScreen { get; protected set; }

    public ResolutionSettings()
    {
        if (GenshinRegistry.GetRegistryKey() is not { } hk)
        {
            return;
        }

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

        Height = Convert.ToInt32(hk.GetValue(height_name));
        Width = Convert.ToInt32(hk.GetValue(width_name));
        FullScreen = Convert.ToInt32(hk.GetValue(fullscreen_name)) == 1;
    }
}
