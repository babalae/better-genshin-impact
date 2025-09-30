using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.View.Windows;

[ObservableObject]
public partial class ScriptRepoWindow
{
    // 转换器类
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : value;
        }
    }

    // 更新渠道类
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

    // 渠道列表
    private ObservableCollection<RepoChannel> _repoChannels;
    public ObservableCollection<RepoChannel> RepoChannels => _repoChannels;

    // 选中的渠道
    [ObservableProperty] private RepoChannel? _selectedRepoChannel;

    // 控制仓库地址是否只读
    [ObservableProperty] private bool _isRepoUrlReadOnly = true;

    // 添加进度相关的可观察属性
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private int _updateProgressValue;
    [ObservableProperty] private string _updateProgressText = "准备更新，请耐心等待...";
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
            new("GitHub", "https://github.com/babalae/bettergi-scripts-list"),
            new("自定义", "https://example.com/custom-repo")
        };

        if (string.IsNullOrEmpty(Config.SelectedRepoUrl))
        {
            // 默认选中第一个渠道
            SelectedRepoChannel = _repoChannels[0];
            Config.SelectedRepoUrl = SelectedRepoChannel.Url;
        }
        else
        {
            // 尝试根据配置中的URL找到对应的渠道
            OnConfigSelectedRepoUrlChanged();
        }
    }

    // Config.SelectedRepoUrl 变化
    private void OnConfigSelectedRepoUrlChanged()
    {
        if (string.IsNullOrEmpty(Config.SelectedRepoUrl))
        {
            SelectedRepoChannel = _repoChannels[0];
            Config.SelectedRepoUrl = SelectedRepoChannel.Url;
            return;
        }

        // 尝试找到匹配的预定义渠道
        var matchedChannel = _repoChannels.FirstOrDefault(c => 
            c.Name != "自定义" && c.Url == Config.SelectedRepoUrl);
        
        if (matchedChannel != null)
        {
            SelectedRepoChannel = matchedChannel;
        }
        else
        {
            // 没有匹配的预定义渠道，选择自定义渠道
            var customChannel = _repoChannels.FirstOrDefault(c => c.Name == "自定义");
            if (customChannel != null)
            {
                SelectedRepoChannel = customChannel;
            }
            else
            {
                SelectedRepoChannel = _repoChannels[0];
            }
        }
    }

    private void OnSelectedRepoChannelChanged()
    {
        if (SelectedRepoChannel is null)
        {
            return;
        }

        // 更新仓库地址只读状态
        IsRepoUrlReadOnly = SelectedRepoChannel.Name != "自定义";

        if (SelectedRepoChannel.Name != "自定义")
        {
            // 如果不是自定义渠道，使用选中渠道的URL
            Config.SelectedRepoUrl = SelectedRepoChannel.Url;
        }
    }

    [RelayCommand]
    private async Task UpdateRepo()
    {
        if (SelectedRepoChannel is null)
        {
            Toast.Warning("请选择一个脚本仓库更新渠道。");
            return;
        }

        // 确定要使用的URL
        string repoUrl;
        if (SelectedRepoChannel.Name == "自定义")
        {
            // 使用配置中的自定义URL
            repoUrl = Config.SelectedRepoUrl;
            
            // 验证自定义URL
            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                Toast.Warning("请输入自定义仓库URL。");
                return;
            }
            
            if (repoUrl == "https://example.com/custom-repo")
            {
                Toast.Warning("请修改默认的自定义URL为有效的仓库地址。");
                return;
            }
        }
        else
        {
            // 使用预定义渠道的URL
            repoUrl = SelectedRepoChannel.Url;
        }

        if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out _))
        {
            Toast.Warning("请输入有效的URL地址。");
            return;
        }

        try
        {
            // 显示更新中提示
            Toast.Information("正在更新脚本仓库，请耐心等待...");

            // 设置进度显示
            IsUpdating = true;
            UpdateProgressValue = 0;
            UpdateProgressText = "准备更新，请耐心等待...";
            
            // 执行更新
            var (_, updated) = await ScriptRepoUpdater.Instance.UpdateCenterRepoByGit(repoUrl,
                (path, steps, totalSteps) =>
                {
                    double progressPercentage = totalSteps > 0 ? Math.Min(100, (double)steps / totalSteps * 100) : 0;
                    UpdateProgressValue = (int)progressPercentage;
                    UpdateProgressText = $"{path}";
                });

            // 更新结果提示
            if (updated)
            {
                Toast.Success("脚本仓库更新成功，有新内容");
            }
            else
            {
                Toast.Success("脚本仓库已是最新");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ErrorAsync($"更新失败，可尝试重置仓库后重新更新。失败原因：{ex.Message}");
        }
        finally
        {
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
        if (IsUpdating)
        {
            Toast.Warning("请等待当前更新完成后再进行重置操作。");
            return;
        }

        // 添加确认对话框
        var result = await MessageBox.ShowAsync(
            "确定要重置脚本仓库吗？无法正常更新时候可以使用本功能，重置后请重新更新脚本仓库。",
            "确认重置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                if (Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
                {
                    DirectoryHelper.DeleteReadOnlyDirectory(ScriptRepoUpdater.CenterRepoPath);
                    Toast.Success("脚本仓库已重置，请重新更新脚本仓库。");
                }
                else
                {
                    Toast.Information("脚本仓库不存在，无需重置");
                }
            }
            catch (Exception ex)
            {
                Toast.Error($"重置失败: {ex.Message}");
            }
        }
    }
}