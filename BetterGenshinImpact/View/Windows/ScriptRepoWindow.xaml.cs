using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;

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
    private void UpdateRepo()
    {
        // 使用选定渠道的URL进行更新
        string repoUrl = SelectedRepoChannel.Url;
        // 在这里添加使用repoUrl更新仓库的逻辑
        // 例如: ScriptRepoUpdater.Instance.Update(repoUrl);
    }

    [RelayCommand]
    private void OpenLocalScriptRepo()
    {
        TaskContext.Instance().Config.ScriptConfig.ScriptRepoHintDotVisible = false;
        ScriptRepoUpdater.Instance.OpenLocalRepoInWebView();
    }
}