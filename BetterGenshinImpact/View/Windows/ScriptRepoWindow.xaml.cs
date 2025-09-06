using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
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
    [ObservableProperty] private RepoChannel? _selectedRepoChannel;

    // 控制仓库地址是否只读
    [ObservableProperty] private bool _isRepoUrlReadOnly = true;

    // 添加进度相关的可观察属性
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private int _updateProgressValue;
    [ObservableProperty] private string _updateProgressText = "准备更新，请耐心等待...";
    [ObservableProperty] private ScriptConfig _config = TaskContext.Instance().Config.ScriptConfig;

    // 在线更新相关属性
    [ObservableProperty] private string _onlineDownloadUrl = "";

    public ScriptRepoWindow()
    {
        InitializeRepoChannels();
        InitializeComponent();
        DataContext = this;
        Config.PropertyChanged += OnConfigPropertyChanged;
        PropertyChanged += OnPropertyChanged;
        SourceInitialized += (s, e) =>
        {
            // 应用系统背景
            WindowHelper.TryApplySystemBackdrop(this);
        };
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        //OnSelectedRepoChannelChanged
        if (e.PropertyName == nameof(SelectedRepoChannel))
        {
            OnSelectedRepoChannelChanged();
        }
        // 监听IsUpdating变化以调整窗口高度
        else if (e.PropertyName == nameof(IsUpdating))
        {
            OnIsUpdatingChanged();
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

    private void OnIsUpdatingChanged()
    {
        // 当IsUpdating状态变化时，强制重新计算窗口大小
        Dispatcher.BeginInvoke(() =>
        {
            InvalidateMeasure();
            UpdateLayout();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void InitializeRepoChannels()
    {
        _repoChannels = new ObservableCollection<RepoChannel>
        {
            new("CNB", "https://cnb.cool/bettergi/bettergi-scripts-list"),
            new("GitCode", "https://gitcode.com/huiyadanli/bettergi-scripts-list"),
            // 暂时无法使用
            // new("Gitee", "https://gitee.com/babalae/bettergi-scripts-list"),
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
        // 如果配置中的URL与当前选中渠道不一致，更新选中渠道
        if (string.IsNullOrEmpty(SelectedRepoChannel?.Url) || SelectedRepoChannel.Url != Config.SelectedRepoUrl)
        {
            SelectedRepoChannel = _repoChannels.FirstOrDefault(c => c.Url == Config.SelectedRepoUrl) ??
                                  _repoChannels.FirstOrDefault(c => c.Name == "自定义") ?? _repoChannels[0];
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

        // 更新配置中的选中仓库URL
        if (SelectedRepoChannel.Name != "自定义")
        {
            // 如果不是自定义渠道，直接使用选中渠道的URL
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
        try
        {
            // 使用选定渠道的URL进行更新
            string repoUrl = SelectedRepoChannel.Url;

            // 显示更新中提示
            Toast.Information("正在更新脚本仓库，请耐心等待...");

            // 设置进度显示
            IsUpdating = true;
            UpdateProgressValue = 0;
            UpdateProgressText = "准备更新，请耐心等待...";
            // 执行更新  (repoPath, updated) 
            var (_, updated) = await ScriptRepoUpdater.Instance.UpdateCenterRepoByGit(repoUrl,
                (path, steps, totalSteps) =>
                {
                    // 更新进度显示
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
            await MessageBox.ErrorAsync($"更新失败，可尝试重置仓库后重新更新。失败原因：: {ex.Message}");
        }
        finally
        {
            // 隐藏进度条
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

    /*
    [RelayCommand]
    private async Task DownloadOnlineRepo()
    {
        if (string.IsNullOrWhiteSpace(OnlineDownloadUrl))
        {
            Toast.Warning("请输入有效的下载地址。");
            return;
        }

        if (IsUpdating)
        {
            Toast.Warning("请等待当前操作完成后再进行下载。");
            return;
        }

        try
        {
            IsUpdating = true;
            UpdateProgressValue = 0;
            UpdateProgressText = "正在下载脚本仓库...";

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            // 下载文件
            var response = await httpClient.GetAsync(OnlineDownloadUrl);
            response.EnsureSuccessStatusCode();

            var tempZipPath = Path.Combine(Path.GetTempPath(), $"script_repo_{DateTime.Now:yyyyMMddHHmmss}.zip");
            await using (var fileStream = File.Create(tempZipPath))
            {
                await response.Content.CopyToAsync(fileStream);
            }

            UpdateProgressText = "正在解压并导入...";
            UpdateProgressValue = 50;

            // 导入下载的zip文件
            await ImportZipFile(tempZipPath);

            // 清理临时文件
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }

            Toast.Success("在线脚本仓库下载并导入成功！");
        }
        catch (Exception ex)
        {
            Toast.Error($"下载失败: {ex.Message}");
        }
        finally
        {
            IsUpdating = false;
        }
    }*/

    [RelayCommand]
    private async Task ImportLocalScriptsRepoZip()
    {
        if (IsUpdating)
        {
            Toast.Warning("请等待当前操作完成后再进行导入。");
            return;
        }

        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "选择脚本仓库压缩包",
                Filter = "压缩包文件 (*.zip)|*.zip",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                IsUpdating = true;
                UpdateProgressValue = 0;
                UpdateProgressText = "正在导入脚本仓库，请耐心等待...";

                await ImportZipFile(openFileDialog.FileName);
                Toast.Success("脚本仓库导入成功！");
            }
        }
        catch (Exception ex)
        {
            Toast.Error($"导入失败: {ex.Message}");
        }
        finally
        {
            IsUpdating = false;
        }
    }

    private async Task ImportZipFile(string zipFilePath)
    {
        await Task.Run(() =>
        {
            var tempPath = ScriptRepoUpdater.ReposTempPath;
            try
            {
                // 阶段1: 准备工作 (0-10%)
                Dispatcher.Invoke(() =>
                {
                    UpdateProgressValue = 0;
                    UpdateProgressText = "正在准备导入环境...";
                });

                var tempUnzipDir = Path.Combine(tempPath, "importZipFile");

                // 先删除临时目录
                DirectoryHelper.DeleteReadOnlyDirectory(tempPath);

                // 创建目标目录
                Directory.CreateDirectory(tempUnzipDir);

                Dispatcher.Invoke(() =>
                {
                    UpdateProgressValue = 10;
                    UpdateProgressText = "准备完成，开始解压文件...";
                });

                // 阶段2: 解压zip文件 (10-50%)
                ZipFile.ExtractToDirectory(zipFilePath, tempUnzipDir, true);

                Dispatcher.Invoke(() =>
                {
                    UpdateProgressValue = 50;
                    UpdateProgressText = "文件解压完成，正在验证仓库结构...";
                });

                // 阶段3: 查找并验证 repo.json (50-60%)
                var repoJsonPath = Directory.GetFiles(tempUnzipDir, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
                if (repoJsonPath == null)
                {
                    throw new FileNotFoundException("未找到 repo.json 文件，导入失败。");
                }

                var repoDir = Path.GetDirectoryName(repoJsonPath)!;

                Dispatcher.Invoke(() =>
                {
                    UpdateProgressValue = 60;
                    UpdateProgressText = "仓库结构验证通过，正在清理旧数据...";
                });

                // 阶段4: 删除旧的目标目录 (60-70%)
                if (Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
                {
                    DirectoryHelper.DeleteReadOnlyDirectory(ScriptRepoUpdater.CenterRepoPath);
                }

                Dispatcher.Invoke(() =>
                {
                    UpdateProgressValue = 70;
                    UpdateProgressText = "旧数据清理完成，正在复制新仓库...";
                });

                // 阶段5: 复制新目录 (70-95%)
                DirectoryHelper.CopyDirectory(repoDir, ScriptRepoUpdater.CenterRepoPath);

                Dispatcher.Invoke(() =>
                {
                    UpdateProgressValue = 95;
                    UpdateProgressText = "仓库复制完成，正在清理临时文件...";
                });
            }
            finally
            {
                // 阶段6: 清理临时文件 (95-100%)
                DirectoryHelper.DeleteReadOnlyDirectory(tempPath);
            }

        });

        // 最终完成
        Dispatcher.Invoke(() =>
        {
            UpdateProgressValue = 100;
            UpdateProgressText = "导入完成";
        });
    }

    /// <summary>
    /// 处理超链接点击事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开链接: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        e.Handled = true;
    }
}