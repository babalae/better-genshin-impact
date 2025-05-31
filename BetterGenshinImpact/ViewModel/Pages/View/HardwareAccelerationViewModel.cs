using System;
using System.Diagnostics;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.View;

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

public partial class HardwareAccelerationViewModel : ObservableObject, IViewModel
{
    public HardwareAccelerationConfig Config { get; set; }
    public BgiOnnxFactory Status { get; set; }
    [ObservableProperty]
    private InferenceDeviceType[] _inferenceDeviceTypes = Enum.GetValues<InferenceDeviceType>();
    [ObservableProperty]
    private string _providerTypesText;
    public HardwareAccelerationViewModel()
    {
        Config = TaskContext.Instance().Config.HardwareAccelerationConfig;
        Status = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>();
        _providerTypesText = string.Join(",", Status.ProviderTypes);
    }
    [RelayCommand]
    public void OnOpenCacheFolder()
    {
        Process.Start("explorer.exe", Global.Absolute(BgiOnnxModel.ModelCacheRelativePath));
    }
}