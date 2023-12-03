using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaSetup.Design.Controls;
using MicaSetup.Helper;
using System;
using System.IO;
using System.Windows;

namespace MicaSetup.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private bool keepMyData = Option.Current.KeepMyData;

    partial void OnKeepMyDataChanged(bool value)
    {
        Option.Current.KeepMyData = value;
        if (!value)
        {
            _ = MessageBoxX.Info(UIDispatcherHelper.MainWindow, Mui("NotKeepMyDataTips", Mui("KeepMyDataTips")));
        }
    }

    public MainViewModel()
    {
    }

    [RelayCommand]
    private void StartUninstall()
    {
        try
        {
            UninstallDataInfo uinfo = PrepareUninstallPathHelper.GetPrepareUninstallPath(Option.Current.KeyName);

            Option.Current.InstallLocation = uinfo.InstallLocation;
            if (!FileWritableHelper.CheckWritable(Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName)))
            {
                _ = MessageBoxX.Info(UIDispatcherHelper.MainWindow, Mui("LockedTipsAndExitTry", Option.Current.ExeName));
                return;
            }
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }

        Routing.GoToNext();
    }

    [RelayCommand]
    private void CancelUninstall()
    {
        SystemCommands.CloseWindow(UIDispatcherHelper.MainWindow);
    }
}
