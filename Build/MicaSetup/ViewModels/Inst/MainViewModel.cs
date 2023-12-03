using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaSetup.Design.Controls;
using MicaSetup.Helper;
using MicaSetup.Services;
using MicaSetup.Shell.Dialogs;
using SharpCompress.Readers;
using System;
using System.IO;
using DialogResult = System.Windows.Forms.DialogResult;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace MicaSetup.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string installPath = PrepareInstallPathHelper.GetPrepareInstallPath(Option.Current.KeyName, Option.Current.UseInstallPathPreferX86);

    partial void OnInstallPathChanged(string value)
    {
        try
        {
            value = Path.Combine(value).TrimEnd('\\', '/');
            if (value.EndsWith(":"))
            {
                value += Path.DirectorySeparatorChar;
            }
            availableFreeSpaceLong = DriveInfoHelper.GetAvailableFreeSpace(value);
            AvailableFreeSpace = availableFreeSpaceLong.ToFreeSpaceString();
            Option.Current.InstallLocation = (value?.EndsWith(Option.Current.KeyName, StringComparison.OrdinalIgnoreCase) ?? false) ? value : Path.Combine(value, Option.Current.KeyName);
            Logger.Debug($"[InstallLocation] {Option.Current.InstallLocation}");
            IsIllegalPath = false;
        }
        catch
        {
            IsIllegalPath = true;
        }
    }

    [ObservableProperty]
    private string requestedFreeSpace = null!;

    private long requestedFreeSpaceLong = default;

    [ObservableProperty]
    private string availableFreeSpace = null!;

    private long availableFreeSpaceLong = default;

    [ObservableProperty]
    private bool isIllegalPath = false;

    [ObservableProperty]
    private string licenseInfo = null!;

    [ObservableProperty]
    private bool licenseShown = false;

    [ObservableProperty]
    private bool licenseRead = true;

    partial void OnLicenseReadChanged(bool value)
    {
        CanStart = value;
    }

    [ObservableProperty]
    private bool canStart = true;

    [ObservableProperty]
    private bool isElevated = RuntimeHelper.IsElevated;

    [ObservableProperty] 
    private bool isCustomizeVisiableAutoRun = Option.Current.IsCustomizeVisiableAutoRun;

    [ObservableProperty]
    private bool autoRun = Option.Current.IsCreateAsAutoRun;

    partial void OnAutoRunChanged(bool value)
    {
        Option.Current.IsCreateAsAutoRun = value;
    }

    [ObservableProperty]
    private bool desktopShortcut = Option.Current.IsCreateDesktopShortcut;

    partial void OnDesktopShortcutChanged(bool value)
    {
        Option.Current.IsCreateDesktopShortcut = value;
    }

    public MainViewModel()
    {
        LicenseInfo = ResourceHelper.GetString(ServiceManager.GetService<IMuiLanguageService>().GetLicenseUriString());
        using Stream archiveStream = ResourceHelper.GetStream("pack://application:,,,/MicaSetup;component/Resources/Setups/publish.7z");

        ReaderOptions readerOptions = new()
        {
            LookForHeader = true,
            Password = string.IsNullOrEmpty(Option.Current.UnpackingPassword) ? null! : Option.Current.UnpackingPassword,
        };

        requestedFreeSpaceLong = ArchiveFileHelper.TotalUncompressSize(archiveStream, readerOptions) + (Option.Current.IsCreateUninst ? 2048000 : 0);
        RequestedFreeSpace = requestedFreeSpaceLong.ToFreeSpaceString();
        availableFreeSpaceLong = DriveInfoHelper.GetAvailableFreeSpace(installPath);
        AvailableFreeSpace = availableFreeSpaceLong.ToFreeSpaceString();
    }

    [ObservableProperty]
    private bool installPathShown = false;

    [RelayCommand]
    private void ShowOrHideInstallPath()
    {
        InstallPathShown = !InstallPathShown;
    }

    [RelayCommand]
    private void ShowOrHideLincenseInfo()
    {
        LicenseShown = !LicenseShown;
    }

    [RelayCommand]
    private void SelectFolder()
    {
        if (Option.Current.UseFolderPickerPreferClassic)
        {
            using FolderBrowserDialog dialog = new()
            {
                ShowNewFolderButton = true,
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string selectedFolder = dialog.SelectedPath;
                Option.Current.InstallLocation = InstallPath = selectedFolder;
            }
        }
        else
        {
            using CommonOpenFileDialog dialog = new()
            {
                IsFolderPicker = true,
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                string selectedFolder = dialog.FileName;
                Option.Current.InstallLocation = InstallPath = selectedFolder;
            }
        }
    }

    [RelayCommand]
    private void StartInstall()
    {
        OnInstallPathChanged(InstallPath);

        if (IsIllegalPath)
        {
            _ = MessageBoxX.Info(UIDispatcherHelper.MainWindow, Mui("IllegalPathTips"));
            return;
        }

        try
        {
            if (requestedFreeSpaceLong >= availableFreeSpaceLong)
            {
                _ = MessageBoxX.Info(UIDispatcherHelper.MainWindow, Mui("AvailableFreeSpaceInsufficientTips"));
                return;
            }
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }

        try
        {
            if (!FileWritableHelper.CheckWritable(Path.Combine(InstallPath, Option.Current.ExeName)))
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
}
