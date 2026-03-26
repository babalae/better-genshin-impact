using BetterGenshinImpact.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Core.Config;

public static class HardwareInputConfigValues
{
    public const string Virtual = "Virtual";
    public const string Hardware = "Hardware";
    public const string Makcu = "Makcu";
    public const string Ferrum = "Ferrum";
}

[Serializable]
public partial class HardwareInputConfig : ObservableObject
{
    [ObservableProperty]
    private string _keyboardOutputMode = HardwareInputConfigValues.Virtual;

    [ObservableProperty]
    private string _keyboardHardwareVendor = HardwareInputConfigValues.Ferrum;

    [ObservableProperty]
    private bool _keyboardAutoDetectComPort = true;

    [ObservableProperty]
    private string _keyboardComPort = string.Empty;

    [ObservableProperty]
    private string _mouseOutputMode = HardwareInputConfigValues.Virtual;

    [ObservableProperty]
    private string _mouseHardwareVendor = HardwareInputConfigValues.Makcu;

    [ObservableProperty]
    private bool _mouseAutoDetectComPort = true;

    [ObservableProperty]
    private string _mouseComPort = string.Empty;

    [JsonIgnore]
    public bool IsKeyboardHardware => string.Equals(KeyboardOutputMode, HardwareInputConfigValues.Hardware, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsMouseHardware => string.Equals(MouseOutputMode, HardwareInputConfigValues.Hardware, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string KeyboardDetectedComPort => HardwarePortDetector.ResolvePort(KeyboardHardwareVendor);

    [JsonIgnore]
    public string MouseDetectedComPort => HardwarePortDetector.ResolvePort(MouseHardwareVendor);

    [JsonIgnore]
    public string KeyboardEffectiveComPort => KeyboardAutoDetectComPort ? KeyboardDetectedComPort : KeyboardComPort.Trim();

    [JsonIgnore]
    public string MouseEffectiveComPort => MouseAutoDetectComPort ? MouseDetectedComPort : MouseComPort.Trim();

    public void RefreshDetectedPorts()
    {
        OnPropertyChanged(nameof(KeyboardDetectedComPort));
        OnPropertyChanged(nameof(MouseDetectedComPort));
        OnPropertyChanged(nameof(KeyboardEffectiveComPort));
        OnPropertyChanged(nameof(MouseEffectiveComPort));
    }

    partial void OnKeyboardOutputModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsKeyboardHardware));
    }

    partial void OnKeyboardHardwareVendorChanged(string value)
    {
        RefreshDetectedPorts();
    }

    partial void OnKeyboardAutoDetectComPortChanged(bool value)
    {
        OnPropertyChanged(nameof(KeyboardEffectiveComPort));
    }

    partial void OnKeyboardComPortChanged(string value)
    {
        OnPropertyChanged(nameof(KeyboardEffectiveComPort));
    }

    partial void OnMouseOutputModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsMouseHardware));
    }

    partial void OnMouseHardwareVendorChanged(string value)
    {
        RefreshDetectedPorts();
    }

    partial void OnMouseAutoDetectComPortChanged(bool value)
    {
        OnPropertyChanged(nameof(MouseEffectiveComPort));
    }

    partial void OnMouseComPortChanged(string value)
    {
        OnPropertyChanged(nameof(MouseEffectiveComPort));
    }
}
