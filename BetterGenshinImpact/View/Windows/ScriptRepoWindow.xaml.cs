using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
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
    [ObservableProperty] private RepoChannel _selectedRepoChannel;

    public ScriptRepoWindow()
    {
        InitializeRepoChannels();
        InitializeComponent();
        DataContext = this;
    }

    private void InitializeRepoChannels()
    {
        _repoChannels = new ObservableCollection<RepoChannel>
        {
            new RepoChannel("CNB", "https://cnb.cool/bettergi/bettergi-scripts-list"),
            new RepoChannel("GitCode", "https://gitcode.com/huiyadanli/bettergi-scripts-list"),
            new RepoChannel("Gitee", "https://gitee.com/babalae/bettergi-scripts-list"),
            new RepoChannel("GitHub", "https://github.com/babalae/bettergi-scripts-list"),
        };
        SelectedRepoChannel = _repoChannels[0];
    }

    [RelayCommand]
    private async Task UpdateRepo()
    {
        try
        {
            // 使用选定渠道的URL进行更新
            string repoUrl = SelectedRepoChannel.Url;

            // 显示更新中提示
            Toast.Information("正在更新脚本仓库...");

            // 执行更新
            var (repoPath, updated) = await ScriptRepoUpdater.Instance.UpdateCenterRepoByGit(repoUrl);

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
            Toast.Error($"更新失败: {ex.Message}");
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