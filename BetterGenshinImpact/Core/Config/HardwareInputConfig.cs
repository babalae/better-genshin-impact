using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Core.Simulator.Hardware;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Core.Config;

public static class HardwareInputConfigValues
{
    public const string Virtual = "Virtual";
    public const string Hardware = "Hardware";
    public const string Makcu = "Makcu";
    public const string Makxd = "Makxd";
    public const string Ferrum = "Ferrum";
    public const string Km = "KM";
    public const string Dhz = "DHZ";
    public const string Net = "NET";
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
    private string _keyboardFerrumApi = HardwareInputConfigValues.Km;

    [ObservableProperty]
    private string _keyboardFerrumNetIp = string.Empty;

    [ObservableProperty]
    private string _keyboardFerrumNetPort = string.Empty;

    [ObservableProperty]
    private string _keyboardFerrumNetUuid = string.Empty;

    [ObservableProperty]
    private string _mouseOutputMode = HardwareInputConfigValues.Virtual;

    [ObservableProperty]
    private string _mouseHardwareVendor = HardwareInputConfigValues.Makcu;

    [ObservableProperty]
    private bool _mouseAutoDetectComPort = true;

    [ObservableProperty]
    private string _mouseComPort = string.Empty;

    [ObservableProperty]
    private string _mouseFerrumApi = HardwareInputConfigValues.Km;

    [ObservableProperty]
    private string _mouseFerrumNetIp = string.Empty;

    [ObservableProperty]
    private string _mouseFerrumNetPort = string.Empty;

    [ObservableProperty]
    private string _mouseFerrumNetUuid = string.Empty;

    [JsonIgnore]
    public bool IsKeyboardHardware => string.Equals(KeyboardOutputMode, HardwareInputConfigValues.Hardware, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsMouseHardware => string.Equals(MouseOutputMode, HardwareInputConfigValues.Hardware, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsKeyboardFerrumSelected => string.Equals(KeyboardHardwareVendor, HardwareInputConfigValues.Ferrum, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsMouseFerrumSelected => string.Equals(MouseHardwareVendor, HardwareInputConfigValues.Ferrum, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsKeyboardFerrumNetworkApi =>
        IsKeyboardFerrumSelected &&
        !string.Equals(KeyboardFerrumApi, HardwareInputConfigValues.Km, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsKeyboardFerrumDhzApi =>
        IsKeyboardFerrumSelected &&
        string.Equals(KeyboardFerrumApi, HardwareInputConfigValues.Dhz, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsMouseFerrumNetworkApi =>
        IsMouseFerrumSelected &&
        !string.Equals(MouseFerrumApi, HardwareInputConfigValues.Km, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsMouseFerrumDhzApi =>
        IsMouseFerrumSelected &&
        string.Equals(MouseFerrumApi, HardwareInputConfigValues.Dhz, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string KeyboardDetectedComPort => HardwarePortDetector.ResolvePort(KeyboardHardwareVendor);

    [JsonIgnore]
    public string MouseDetectedComPort => HardwarePortDetector.ResolvePort(MouseHardwareVendor);

    [JsonIgnore]
    public string KeyboardEffectiveComPort => KeyboardAutoDetectComPort
        ? HardwarePortDetector.NormalizePortName(KeyboardDetectedComPort)
        : HardwarePortDetector.NormalizePortName(KeyboardComPort);

    [JsonIgnore]
    public string MouseEffectiveComPort => MouseAutoDetectComPort
        ? HardwarePortDetector.NormalizePortName(MouseDetectedComPort)
        : HardwarePortDetector.NormalizePortName(MouseComPort);

    [JsonIgnore]
    public string KeyboardHardwareVidPid => HardwarePortDetector.ResolveVidPid(KeyboardEffectiveComPort);

    [JsonIgnore]
    public string MouseHardwareVidPid => HardwarePortDetector.ResolveVidPid(MouseEffectiveComPort);

    [JsonIgnore]
    public string KeyboardHardwareBaudRate => IsKeyboardFerrumNetworkApi
        ? "UDP"
        : HardwareInputRouter.Instance.GetKeyboardConnectedBaudRateText() ?? HardwarePortDetector.GetBaudRateText(KeyboardHardwareVendor);

    [JsonIgnore]
    public string MouseHardwareBaudRate => IsMouseFerrumNetworkApi
        ? "UDP"
        : HardwareInputRouter.Instance.GetMouseConnectedBaudRateText() ?? HardwarePortDetector.GetBaudRateText(MouseHardwareVendor);

    [JsonIgnore]
    public string KeyboardFerrumCredentialLabel => IsKeyboardFerrumDhzApi ? "Ferrum 密钥" : "Ferrum UUID";

    [JsonIgnore]
    public string KeyboardFerrumCredentialHint => IsKeyboardFerrumDhzApi ? "DHZ 使用的加密密钥" : "NET 使用的设备 UUID";

    [JsonIgnore]
    public string KeyboardFerrumCredentialDisplayName => IsKeyboardFerrumDhzApi ? "Key" : "UUID";

    [JsonIgnore]
    public string MouseFerrumCredentialLabel => IsMouseFerrumDhzApi ? "Ferrum 密钥" : "Ferrum UUID";

    [JsonIgnore]
    public string MouseFerrumCredentialHint => IsMouseFerrumDhzApi ? "DHZ 使用的加密密钥" : "NET 使用的设备 UUID";

    [JsonIgnore]
    public string MouseFerrumCredentialDisplayName => IsMouseFerrumDhzApi ? "Key" : "UUID";

    [JsonIgnore]
    public string KeyboardHardwareInfo => IsKeyboardFerrumNetworkApi
        ? $"Protocol: {KeyboardFerrumApi}    IP: {KeyboardFerrumNetIp.Trim()}    Port: {KeyboardFerrumNetPort.Trim()}    {KeyboardFerrumCredentialDisplayName}: {KeyboardFerrumNetUuid.Trim()}"
        : $"VID/PID: {KeyboardHardwareVidPid}    Baud: {KeyboardHardwareBaudRate}";

    [JsonIgnore]
    public string MouseHardwareInfo => IsMouseFerrumNetworkApi
        ? $"Protocol: {MouseFerrumApi}    IP: {MouseFerrumNetIp.Trim()}    Port: {MouseFerrumNetPort.Trim()}    {MouseFerrumCredentialDisplayName}: {MouseFerrumNetUuid.Trim()}"
        : $"VID/PID: {MouseHardwareVidPid}    Baud: {MouseHardwareBaudRate}";

    [JsonIgnore]
    public string KeyboardHardwareTestInfo => IsKeyboardFerrumNetworkApi
        ? KeyboardHardwareInfo
        : $"COM: {KeyboardEffectiveComPort}    {KeyboardHardwareInfo}";

    [JsonIgnore]
    public string MouseHardwareTestInfo => IsMouseFerrumNetworkApi
        ? MouseHardwareInfo
        : $"COM: {MouseEffectiveComPort}    {MouseHardwareInfo}";

    public void RefreshDetectedPorts()
    {
        OnPropertyChanged(nameof(KeyboardDetectedComPort));
        OnPropertyChanged(nameof(MouseDetectedComPort));
        OnPropertyChanged(nameof(KeyboardEffectiveComPort));
        OnPropertyChanged(nameof(MouseEffectiveComPort));
        OnPropertyChanged(nameof(KeyboardHardwareVidPid));
        OnPropertyChanged(nameof(MouseHardwareVidPid));
        OnPropertyChanged(nameof(KeyboardHardwareBaudRate));
        OnPropertyChanged(nameof(MouseHardwareBaudRate));
        OnPropertyChanged(nameof(KeyboardHardwareInfo));
        OnPropertyChanged(nameof(MouseHardwareInfo));
        OnPropertyChanged(nameof(KeyboardHardwareTestInfo));
        OnPropertyChanged(nameof(MouseHardwareTestInfo));
        OnPropertyChanged(nameof(IsKeyboardFerrumSelected));
        OnPropertyChanged(nameof(IsMouseFerrumSelected));
        OnPropertyChanged(nameof(IsKeyboardFerrumNetworkApi));
        OnPropertyChanged(nameof(IsKeyboardFerrumDhzApi));
        OnPropertyChanged(nameof(IsMouseFerrumNetworkApi));
        OnPropertyChanged(nameof(IsMouseFerrumDhzApi));
        OnPropertyChanged(nameof(KeyboardFerrumCredentialLabel));
        OnPropertyChanged(nameof(KeyboardFerrumCredentialHint));
        OnPropertyChanged(nameof(KeyboardFerrumCredentialDisplayName));
        OnPropertyChanged(nameof(MouseFerrumCredentialLabel));
        OnPropertyChanged(nameof(MouseFerrumCredentialHint));
        OnPropertyChanged(nameof(MouseFerrumCredentialDisplayName));
    }

    partial void OnKeyboardOutputModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsKeyboardHardware));
        OnPropertyChanged(nameof(IsKeyboardFerrumSelected));
        OnPropertyChanged(nameof(IsKeyboardFerrumNetworkApi));
        OnPropertyChanged(nameof(IsKeyboardFerrumDhzApi));
    }

    partial void OnKeyboardHardwareVendorChanged(string value)
    {
        RefreshDetectedPorts();
    }

    partial void OnKeyboardFerrumApiChanged(string value)
    {
        OnPropertyChanged(nameof(IsKeyboardFerrumNetworkApi));
        OnPropertyChanged(nameof(IsKeyboardFerrumDhzApi));
        OnPropertyChanged(nameof(KeyboardHardwareBaudRate));
        OnPropertyChanged(nameof(KeyboardHardwareInfo));
        OnPropertyChanged(nameof(KeyboardHardwareTestInfo));
        OnPropertyChanged(nameof(KeyboardFerrumCredentialLabel));
        OnPropertyChanged(nameof(KeyboardFerrumCredentialHint));
        OnPropertyChanged(nameof(KeyboardFerrumCredentialDisplayName));
    }

    partial void OnKeyboardFerrumNetIpChanged(string value)
    {
        OnPropertyChanged(nameof(KeyboardHardwareInfo));
        OnPropertyChanged(nameof(KeyboardHardwareTestInfo));
    }

    partial void OnKeyboardFerrumNetPortChanged(string value)
    {
        OnPropertyChanged(nameof(KeyboardHardwareInfo));
        OnPropertyChanged(nameof(KeyboardHardwareTestInfo));
    }

    partial void OnKeyboardFerrumNetUuidChanged(string value)
    {
        OnPropertyChanged(nameof(KeyboardHardwareInfo));
        OnPropertyChanged(nameof(KeyboardHardwareTestInfo));
    }

    partial void OnKeyboardAutoDetectComPortChanged(bool value)
    {
        OnPropertyChanged(nameof(KeyboardEffectiveComPort));
        OnPropertyChanged(nameof(KeyboardHardwareVidPid));
        OnPropertyChanged(nameof(KeyboardHardwareInfo));
        OnPropertyChanged(nameof(KeyboardHardwareTestInfo));
    }

    partial void OnKeyboardComPortChanged(string value)
    {
        OnPropertyChanged(nameof(KeyboardEffectiveComPort));
        OnPropertyChanged(nameof(KeyboardHardwareVidPid));
        OnPropertyChanged(nameof(KeyboardHardwareInfo));
        OnPropertyChanged(nameof(KeyboardHardwareTestInfo));
    }

    partial void OnMouseOutputModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsMouseHardware));
        OnPropertyChanged(nameof(IsMouseFerrumSelected));
        OnPropertyChanged(nameof(IsMouseFerrumNetworkApi));
        OnPropertyChanged(nameof(IsMouseFerrumDhzApi));
    }

    partial void OnMouseHardwareVendorChanged(string value)
    {
        RefreshDetectedPorts();
    }

    partial void OnMouseFerrumApiChanged(string value)
    {
        OnPropertyChanged(nameof(IsMouseFerrumNetworkApi));
        OnPropertyChanged(nameof(IsMouseFerrumDhzApi));
        OnPropertyChanged(nameof(MouseHardwareBaudRate));
        OnPropertyChanged(nameof(MouseHardwareInfo));
        OnPropertyChanged(nameof(MouseHardwareTestInfo));
        OnPropertyChanged(nameof(MouseFerrumCredentialLabel));
        OnPropertyChanged(nameof(MouseFerrumCredentialHint));
        OnPropertyChanged(nameof(MouseFerrumCredentialDisplayName));
    }

    partial void OnMouseFerrumNetIpChanged(string value)
    {
        OnPropertyChanged(nameof(MouseHardwareInfo));
        OnPropertyChanged(nameof(MouseHardwareTestInfo));
    }

    partial void OnMouseFerrumNetPortChanged(string value)
    {
        OnPropertyChanged(nameof(MouseHardwareInfo));
        OnPropertyChanged(nameof(MouseHardwareTestInfo));
    }

    partial void OnMouseFerrumNetUuidChanged(string value)
    {
        OnPropertyChanged(nameof(MouseHardwareInfo));
        OnPropertyChanged(nameof(MouseHardwareTestInfo));
    }

    partial void OnMouseAutoDetectComPortChanged(bool value)
    {
        OnPropertyChanged(nameof(MouseEffectiveComPort));
        OnPropertyChanged(nameof(MouseHardwareVidPid));
        OnPropertyChanged(nameof(MouseHardwareInfo));
        OnPropertyChanged(nameof(MouseHardwareTestInfo));
    }

    partial void OnMouseComPortChanged(string value)
    {
        OnPropertyChanged(nameof(MouseEffectiveComPort));
        OnPropertyChanged(nameof(MouseHardwareVidPid));
        OnPropertyChanged(nameof(MouseHardwareInfo));
        OnPropertyChanged(nameof(MouseHardwareTestInfo));
    }
}
