using BetterGenshinImpact.Core.Simulator.Hardware;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace BetterGenshinImpact.ViewModel.Pages;

public sealed partial class HardwareInputMonitorItemViewModel(string label) : ObservableObject
{
    private static readonly Brush NoneBackground = CreateBrush(0xF3, 0xF4, 0xF6, 0xCC);
    private static readonly Brush NoneBorder = CreateBrush(0x9C, 0xA3, 0xAF);
    private static readonly Brush NoneForeground = CreateBrush(0x6B, 0x72, 0x80);
    private static readonly Brush PhysicalBackground = CreateBrush(0xDB, 0xEA, 0xFE, 0xCC);
    private static readonly Brush PhysicalBorder = CreateBrush(0x25, 0x63, 0xEB);
    private static readonly Brush PhysicalForeground = CreateBrush(0x1D, 0x4E, 0xD8);
    private static readonly Brush HardwareBackground = CreateBrush(0xD1, 0xFA, 0xE5, 0xCC);
    private static readonly Brush HardwareBorder = CreateBrush(0x05, 0x96, 0x69);
    private static readonly Brush HardwareForeground = CreateBrush(0x04, 0x78, 0x57);
    private static readonly Brush BothBackground = CreateBrush(0xFE, 0xF3, 0xC7, 0xCC);
    private static readonly Brush BothBorder = CreateBrush(0xD9, 0x77, 0x06);
    private static readonly Brush BothForeground = CreateBrush(0xB4, 0x53, 0x09);

    public string Label { get; } = label;

    [ObservableProperty]
    private int _stateCode;

    [ObservableProperty]
    private string _stateText = "未触发";

    [ObservableProperty]
    private Brush _stateBackground = NoneBackground;

    [ObservableProperty]
    private Brush _stateBorder = NoneBorder;

    [ObservableProperty]
    private Brush _stateForeground = NoneForeground;

    internal void ApplyState(HardwareInputState state)
    {
        switch (state)
        {
            case HardwareInputState.Physical:
                StateCode = 1;
                StateText = "物理";
                StateBackground = PhysicalBackground;
                StateBorder = PhysicalBorder;
                StateForeground = PhysicalForeground;
                break;
            case HardwareInputState.Hardware:
                StateCode = 2;
                StateText = "硬体";
                StateBackground = HardwareBackground;
                StateBorder = HardwareBorder;
                StateForeground = HardwareForeground;
                break;
            case HardwareInputState.Both:
                StateCode = 3;
                StateText = "同时";
                StateBackground = BothBackground;
                StateBorder = BothBorder;
                StateForeground = BothForeground;
                break;
            default:
                StateCode = 0;
                StateText = "未触发";
                StateBackground = NoneBackground;
                StateBorder = NoneBorder;
                StateForeground = NoneForeground;
                break;
        }
    }

    private static Brush CreateBrush(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
