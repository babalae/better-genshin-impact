using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.View.Windows;

[ObservableObject]
public partial class ScriptRepoWindow
{
    // Update channel class
    public class RepoChannel
    {
        public string Name { get; set; }
        public string Url { get; set; }

        public RepoChannel(string name, string url)
        {
            Name = name;
            Url = url;
        }
    }

    // Channel list
    private ObservableCollection<RepoChannel> _repoChannels;
    public ObservableCollection<RepoChannel> RepoChannels => _repoChannels;

    // Selected channel
    [ObservableProperty] private RepoChannel? _selectedRepoChannel;

    // Control whether repository address is read-only
    [ObservableProperty] private bool _isRepoUrlReadOnly = true;

    // Progress-related observable properties
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private int _updateProgressValue;
    [ObservableProperty] private string _updateProgressText = "准备更新...";
    [ObservableProperty] private ScriptConfig _config = TaskContext.Instance().Config.ScriptConfig;

    public ScriptRepoWindow()
    {
        InitializeRepoChannels();
        InitializeComponent();
        DataContext = this;
        Config.PropertyChanged += OnConfigPropertyChanged;
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        //OnSelectedRepoChannelChanged
        if (e.PropertyName == nameof(SelectedRepoChannel))
        {
            OnSelectedRepoChannelChanged();
        }
    }

    ~ScriptRepoWindow()
    {
        Config.PropertyChanged -= OnConfigPropertyChanged;
        PropertyChanged -= OnPropertyChanged;
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptConfig.SelectedRepoUrl))
        {
            OnConfigSelectedRepoUrlChanged();
        }
    }

    private void InitializeRepoChannels()
    {
        _repoChannels = new ObservableCollection<RepoChannel>
        {
            new("CNB", "https://cnb.cool/bettergi/bettergi-scripts-list"),
            new("GitCode", "https://gitcode.com/huiyadanli/bettergi-scripts-list"),
            // Currently unavailable
            // new("Gitee", "https://gitee.com/babalae/bettergi-scripts-list"),
            new("GitHub", "https://github.com/babalae/bettergi-scripts-list"),
            new(App.GetService<ILocalizationService>().GetString("scriptRepo.custom"), "https://example.com/custom-repo")
        };

        if (string.IsNullOrEmpty(Config.SelectedRepoUrl))
        {
            // Select the first channel by default
            SelectedRepoChannel = _repoChannels[0];
            Config.SelectedRepoUrl = SelectedRepoChannel.Url;
        }
        else
        {
            // Try to find the corresponding channel based on the URL in the configuration
            OnConfigSelectedRepoUrlChanged();
        }
    }

    // Config.SelectedRepoUrl change
    private void OnConfigSelectedRepoUrlChanged()
    {
        // If the URL in the configuration doesn't match the currently selected channel, update the selected channel
        if (string.IsNullOrEmpty(SelectedRepoChannel?.Url) || SelectedRepoChannel.Url != Config.SelectedRepoUrl)
        {
            var customText = App.GetService<ILocalizationService>().GetString("scriptRepo.custom");
            SelectedRepoChannel = _repoChannels.FirstOrDefault(c => c.Url == Config.SelectedRepoUrl) ??
                                  _repoChannels.FirstOrDefault(c => c.Name == customText) ?? _repoChannels[0];
        }
    }

    private void OnSelectedRepoChannelChanged()
    {
        if (SelectedRepoChannel is null)
        {
            return;
        }

        // Update repository address read-only status
        var customText = App.GetService<ILocalizationService>().GetString("scriptRepo.custom");
        IsRepoUrlReadOnly = SelectedRepoChannel.Name != customText;

        // Update selected repository URL in configuration
        if (SelectedRepoChannel.Name != customText)
        {
            // If not a custom channel, directly use the selected channel's URL
            Config.SelectedRepoUrl = SelectedRepoChannel.Url;
        }
    }

    [RelayCommand]
    private async Task UpdateRepo()
    {
        var localizationService = App.GetService<ILocalizationService>();
        
        if (SelectedRepoChannel is null)
        {
            Toast.Warning(localizationService.GetString("toast.selectRepoChannel"));
            return;
        }
        try
        {
            // Use the selected channel's URL for update
            string repoUrl = SelectedRepoChannel.Url;

            // Show updating notification
            Toast.Information(localizationService.GetString("toast.updatingRepo"));

            // Set progress display
            IsUpdating = true;
            UpdateProgressValue = 0;
            UpdateProgressText = App.GetService<ILocalizationService>().GetString("scriptRepo.preparingUpdate");
            // Execute update (repoPath, updated)
            var (_, updated) = await ScriptRepoUpdater.Instance.UpdateCenterRepoByGit(repoUrl,
                (path, steps, totalSteps) =>
                {
                    // Update progress display
                    double progressPercentage = totalSteps > 0 ? Math.Min(100, (double)steps / totalSteps * 100) : 0;
                    UpdateProgressValue = (int)progressPercentage;
                    UpdateProgressText = $"{path}";
                });


            // Update result notification
            if (updated)
            {
                Toast.Success(localizationService.GetString("toast.repoUpdateSuccess"));
            }
            else
            {
                Toast.Success(localizationService.GetString("toast.repoAlreadyLatest"));
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ErrorAsync(localizationService.GetString("dialog.updateFailed", ex.Message));
        }
        finally
        {
            // Hide progress bar
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private void OpenLocalScriptRepo()
    {
        TaskContext.Instance().Config.ScriptConfig.ScriptRepoHintDotVisible = false;
        ScriptRepoUpdater.Instance.OpenLocalRepoInWebView();
        Close();
    }

    [RelayCommand]
    private async Task ResetRepo()
    {
        var localizationService = App.GetService<ILocalizationService>();
        
        if (IsUpdating)
        {
            Toast.Warning(localizationService.GetString("toast.waitForUpdateComplete"));
            return;
        }

        // Add confirmation dialog
        var result = await MessageBox.ShowAsync(
            localizationService.GetString("dialog.confirmResetRepo"),
            localizationService.GetString("dialog.confirmReset"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                if (Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
                {
                    DirectoryHelper.DeleteReadOnlyDirectory(ScriptRepoUpdater.CenterRepoPath);
                    Toast.Success(localizationService.GetString("toast.repoResetSuccess"));
                }
                else
                {
                    Toast.Information(localizationService.GetString("toast.repoNotExist"));
                }
            }
            catch (Exception ex)
            {
                Toast.Error(localizationService.GetString("toast.resetFailed", ex.Message));
            }
        }
    }
}
