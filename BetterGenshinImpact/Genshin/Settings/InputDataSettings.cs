using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Genshin.Settings;

public class InputDataSettings
{
    public InputDataConfig? data = null;
    public double MouseSensitivity => data!.MouseSensitivity;
    public MouseFocusSenseIndex MouseFocusSenseIndex => (MouseFocusSenseIndex)data!.MouseFocusSenseIndex;
    public MouseFocusSenseIndex MouseFocusSenseIndexY => (MouseFocusSenseIndex)data!.MouseFocusSenseIndexY;

    public InputDataSettings(MainJson data)
    {
        Load(data);
    }

    public void Load(MainJson data)
    {
        try
        {
            if (string.IsNullOrEmpty(data.InputData))
            {
                return;
            }

            this.data = JsonSerializer.Deserialize<InputDataConfig>(data.InputData);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.ToString());
        }
    }
}

public class InputDataConfig
{
    [JsonPropertyName("mouseSensitivity")]
    public double MouseSensitivity { get; set; }

    [JsonPropertyName("mouseFocusSenseIndex")]
    public int MouseFocusSenseIndex { get; set; }

    [JsonPropertyName("mouseFocusSenseIndexY")]
    public int MouseFocusSenseIndexY { get; set; }
}

public enum MouseFocusSenseIndex
{
    Level1,
    Level2,
    Level3,
    Level4,
    Level5,
}
