using System.Diagnostics;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Simulator;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class KeyMouseRecordPageViewModel : ObservableObject, INavigationAware, IViewModel
{
    private string _macro = string.Empty;

    public void OnNavigatedTo()
    {
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    public void OnStartRecord()
    {
        GlobalKeyMouseRecord.Instance.StartRecord();
        // new DirectInputMonitor().Start();
        // Simulation.SendInput.Mouse.MoveMouseBy(500, 0);
    }

    [RelayCommand]
    public void OnStopRecord()
    {
        _macro = GlobalKeyMouseRecord.Instance.StopRecord();
        Debug.WriteLine("录制脚本结束:" + _macro);
    }

    [RelayCommand]
    public async Task OnStartPlay()
    {
        await KeyMouseMacroPlayer.PlayMacro(_macro);
        Debug.WriteLine("播放录制脚本结束");
    }

    [RelayCommand]
    public void OnStartCalibration()
    {
        new DirectInputCalibration().Start();
    }
}
