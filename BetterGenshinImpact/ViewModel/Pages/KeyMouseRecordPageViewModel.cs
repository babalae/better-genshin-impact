using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class KeyMouseRecordPageViewModel : ObservableObject, INavigationAware, IViewModel
{
    private string _macro = string.Empty;
    private readonly string scriptPath = Global.Absolute(@"User\KeyMouseScript");

    [ObservableProperty]
    private ObservableCollection<KeyMouseScriptItem> _scriptItems = [];

    public KeyMouseRecordPageViewModel()
    {
    }

    private void InitScriptListViewData()
    {
        _scriptItems.Clear();
        var fileInfos = LoadScriptFiles(scriptPath);
        foreach (var f in fileInfos)
        {
            _scriptItems.Add(new KeyMouseScriptItem
            {
                Name = f.Name,
                CreateTime = f.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
    }

    private List<FileInfo> LoadScriptFiles(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var files = Directory.GetFiles(folder, "*.*",
            SearchOption.AllDirectories);

        return files.Select(file => new FileInfo(file)).ToList();
    }

    public void OnNavigatedTo()
    {
        InitScriptListViewData();
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
    public void OnOpenScriptFolder()
    {
        Process.Start("explorer.exe", scriptPath);
    }
}
