using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaSetup.Helper;
using System;
using System.IO;
using System.Windows;

namespace MicaSetup.ViewModels;

public partial class FinishViewModel : ObservableObject
{
    public FinishViewModel()
    {
    }

    [RelayCommand]
    public void Close()
    {
        if (UIDispatcherHelper.MainWindow is Window window)
        {
            SystemCommands.CloseWindow(window);
        }
    }

    [RelayCommand]
    public void Open()
    {
        if (UIDispatcherHelper.MainWindow is Window window)
        {
            try
            {
                FluentProcess.Create()
                    .FileName(Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName))
                    .WorkingDirectory(Option.Current.InstallLocation)
                    .UseShellExecute()
                    .Start()
                    .Forget();
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            SystemCommands.CloseWindow(window);
        }
    }
}
