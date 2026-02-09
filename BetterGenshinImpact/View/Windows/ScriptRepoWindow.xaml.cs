using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Helpers.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meziantou.Framework.Win32;
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
using System.Windows.Data;
using System.Globalization;
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
    [ObservableProperty] private string _updateProgressText = Lang.S["View_12190_ecc674"];
    [ObservableProperty] private ScriptConfig _config = TaskContext.Instance().Config.ScriptConfig;

    // Git 凭据相关属性
    private const string GitCredentialAppName = "BetterGenshinImpact.GitCredentials";

    [ObservableProperty] private string _gitUsername = "";
    [ObservableProperty] private string _gitToken = "";

    // 在线更新相关属性
    [ObservableProperty] private string _onlineDownloadUrl = "";

    // 获取当前仓库URL（用于界面显示）
    public string CurrentRepoUrl
    {
        get
        {
            if (SelectedRepoChannel == null)
            {
                return "";
            }
            return SelectedRepoChannel.Name == Lang.S["View_12191_f1d4ff"] ? Config.CustomRepoUrl : SelectedRepoChannel.Url;
        }
    }

    public ScriptRepoWindow()
    {
        InitializeRepoChannels();
        LoadCredentialsFromManager();
        InitializeComponent();
        DataContext = this;
        Config.PropertyChanged += OnConfigPropertyChanged;
        PropertyChanged += OnPropertyChanged;

        // 设置 PasswordBox 的初始值
        Loaded += (s, e) => GitTokenPasswordBox.Password = GitToken;

        SourceInitialized += (s, e) =>
        {
            // 应用系统背景
            WindowHelper.TryApplySystemBackdrop(this);

            // 设置仓库地址的只读状态
            IsRepoUrlReadOnly = SelectedRepoChannel == null || SelectedRepoChannel.Name != Lang.S["View_12191_f1d4ff"];
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
        // 监听 GitUsername 和 GitToken 变化，保存到凭据管理器
        else if (e.PropertyName == nameof(GitUsername) || e.PropertyName == nameof(GitToken))
        {
            SaveCredentialsToManager();
        }
    }

    /// <summary>
    /// 从 Windows 凭据管理器加载 Git 凭据
    /// </summary>
    private void LoadCredentialsFromManager()
    {
        var credential = CredentialManagerHelper.ReadCredential(GitCredentialAppName);
        GitUsername = credential?.UserName ?? "";
        GitToken = credential?.Password ?? "";
    }

    /// <summary>
    /// 保存 Git 凭据到 Windows 凭据管理器
    /// </summary>
    private void SaveCredentialsToManager()
    {
        CredentialManagerHelper.SaveCredential(
            GitCredentialAppName,
            GitUsername,
            GitToken,
            "Git credentials for BetterGenshinImpact script repository",
            CredentialPersistence.LocalMachine);
    }

    ~ScriptRepoWindow()
    {
        Config.PropertyChanged -= OnConfigPropertyChanged;
        PropertyChanged -= OnPropertyChanged;
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 监听CustomRepoUrl变化，通知界面更新显示
        if (e.PropertyName == nameof(ScriptConfig.CustomRepoUrl))
        {
            OnPropertyChanged(nameof(CurrentRepoUrl));
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
            new("GitHub", "https://github.com/babalae/bettergi-scripts-list"),
            new(Lang.S["View_12191_f1d4ff"], "https://example.com/custom-repo")
        };

        // 根据配置中保存的渠道名称恢复选择
        if (string.IsNullOrEmpty(Config.SelectedChannelName))
        {
            // 默认选中第一个渠道
            SelectedRepoChannel = _repoChannels[0];
            Config.SelectedChannelName = SelectedRepoChannel.Name;
        }
        else
        {
            // 根据保存的渠道名称找到对应的渠道
            var savedChannel = _repoChannels.FirstOrDefault(c => c.Name == Config.SelectedChannelName);
            SelectedRepoChannel = savedChannel ?? _repoChannels[0];

            // 如果找不到保存的渠道，更新配置为默认渠道
            if (savedChannel == null)
            {
                Config.SelectedChannelName = _repoChannels[0].Name;
            }
        }
    }

    private void OnSelectedRepoChannelChanged()
    {
        if (SelectedRepoChannel is null)
        {
            return;
        }

        // 保存选择的渠道名称
        Config.SelectedChannelName = SelectedRepoChannel.Name;

        // 更新仓库地址只读状态
        IsRepoUrlReadOnly = SelectedRepoChannel.Name != Lang.S["View_12191_f1d4ff"];

        // 通知界面更新CurrentRepoUrl
        OnPropertyChanged(nameof(CurrentRepoUrl));
    }

    [RelayCommand]
    private async Task UpdateRepo()
    {
        if (SelectedRepoChannel is null)
        {
            Toast.Warning(Lang.S["ScriptRepo_SelectChannel"]);
            return;
        }

        // 获取当前仓库URL
        string repoUrl = CurrentRepoUrl;

        // 验证URL
        if (string.IsNullOrWhiteSpace(repoUrl))
        {
            Toast.Warning(Lang.S["ScriptRepo_EnterCustomUrl"]);
            return;
        }

        if (repoUrl == "https://example.com/custom-repo")
        {
            Toast.Warning(Lang.S["ScriptRepo_ModifyDefaultUrl"]);
            return;
        }

        if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out _))
        {
            Toast.Warning(Lang.S["ScriptRepo_EnterValidUrl"]);
            return;
        }

        try
        {
            // 显示更新中提示
            Toast.Information(Lang.S["ScriptRepo_Updating"]);

            // 设置进度显示
            IsUpdating = true;
            UpdateProgressValue = 0;
            UpdateProgressText = Lang.S["View_12190_ecc674"];

            // 执行更新
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
                Toast.Success(Lang.S["ScriptRepo_UpdateSuccess"]);
            }
            else
            {
                Toast.Success(Lang.S["ScriptRepo_AlreadyLatest"]);
            }
        }
        catch (Exception ex)
        {
            await ThemedMessageBox.ErrorAsync($"{Lang.S["View_12189_624ea5"]});
        }
        finally
        {
            // 隐藏进度条
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private async Task OpenLocalScriptRepo()
    {
        // 检查是否需要提示用户更新仓库
        var shouldContinue = await CheckAndPromptRepoUpdate();
        if (shouldContinue)
        {
            TaskContext.Instance().Config.ScriptConfig.ScriptRepoHintDotVisible = false;
            ScriptRepoUpdater.Instance.OpenLocalRepoInWebView();
            Close();
        }
    }

    /// <summary>
    /// 检查仓库更新时间并提示用户
    /// </summary>
    /// <returns>是否继续打开仓库（true: 继续打开, false: 取消操作）</returns>
    private async Task<bool> CheckAndPromptRepoUpdate()
    {
        TimeSpan timeSinceUpdate;
        try
        {
            // 检查仓库文件夹是否存在
            if (!Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
            {
                return true;
            }

            // 查找 repo.json 文件
            var repoJsonPath = Directory.GetFiles(ScriptRepoUpdater.CenterRepoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
            if (repoJsonPath == null || !File.Exists(repoJsonPath))
            {
                return true;
            }

            // 获取 repo.json 文件的最后修改时间
            var repoJsonFile = new FileInfo(repoJsonPath);
            DateTime lastUpdateTime = repoJsonFile.LastWriteTime;

            // 检查是否超过 30 天
            timeSinceUpdate = DateTime.Now - lastUpdateTime;
            if (timeSinceUpdate.TotalDays <= 30)
            {
                return true;
            }
        }
        catch
        {
            // 出现异常时，继续打开仓库
            return true;
        }

        // 提示用户更新
        var dialog = new RepoUpdateDialog((int)timeSinceUpdate.TotalDays);
        var result = await dialog.ShowDialogAsync();

        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            // 用户选择"立即更新"
            await UpdateRepo();
            return false;
        }
        else if (result == Wpf.Ui.Controls.MessageBoxResult.Secondary)
        {
            // 用户选择"直接打开"
            return true;
        }
        else
        {
            // 用户关闭对话框（点击 X 或按 ESC）
            return false;
        }
    }

    [RelayCommand]
    private async Task ResetRepo()
    {
        if (IsUpdating)
        {
            Toast.Warning(Lang.S["ScriptRepo_WaitUpdateComplete"]);
            return;
        }

        // 添加确认对话框
        var result = await ThemedMessageBox.ShowAsync(
            Lang.S["ScriptRepo_ResetConfirm"],
            Lang.S["ScriptRepo_ResetConfirmTitle"],
            MessageBoxButton.YesNo,
            ThemedMessageBox.MessageBoxIcon.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                if (Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
                {
                    DirectoryHelper.DeleteReadOnlyDirectory(ScriptRepoUpdater.CenterRepoPath);
                    Toast.Success(Lang.S["ScriptRepo_ResetSuccess"]);
                }
                else
                {
                    Toast.Information(Lang.S["ScriptRepo_NoNeedReset"]);
                }
            }
            catch (Exception ex)
            {
                Toast.Error($"{Lang.S["View_12188_f9c2c3"]});
            }
        }
    }

    /*
    [RelayCommand]
    private async Task DownloadOnlineRepo()
    {
        if (string.IsNullOrWhiteSpace(OnlineDownloadUrl))
        {
            Toast.Warning(Lang.S["ScriptRepo_EnterValidDownloadUrl"]);
            return;
        }

        if (IsUpdating)
        {
            Toast.Warning(Lang.S["ScriptRepo_WaitOperationComplete"]);
            return;
        }

        try
        {
            IsUpdating = true;
            UpdateProgressValue = 0;
            UpdateProgressText = Lang.S["View_12187_d0a174"];

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

            UpdateProgressText = Lang.S["View_12186_5445d5"];
            UpdateProgressValue = 50;

            // 导入下载的zip文件
            await ImportZipFile(tempZipPath);

            // 清理临时文件
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }

            Toast.Success(Lang.S["ScriptRepo_DownloadSuccess"]);
        }
        catch (Exception ex)
        {
            Toast.Error($"{Lang.S["View_12185_7154d8"]});
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
            Toast.Warning(Lang.S["ScriptRepo_WaitImportComplete"]);
            return;
        }

        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = Lang.S["View_12184_dba2c5"],
                Filter = Lang.S["View_12183_b899ab"],
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                IsUpdating = true;
                UpdateProgressValue = 0;
                UpdateProgressText = Lang.S["View_12182_66eaa7"];

                await ImportZipFile(openFileDialog.FileName);
                Toast.Success(Lang.S["ScriptRepo_ImportSuccess"]);
            }
        }
        catch (Exception ex)
        {
            Toast.Error($"{Lang.S["View_12181_b9a363"]});
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
                    UpdateProgressText = Lang.S["View_12180_937a96"];
                });

                var tempUnzipDir = Path.Combine(tempPath, "importZipFile");

                // 先删除临时目录
                DirectoryHelper.DeleteReadOnlyDirectory(tempPath);

                // 创建目标目录
                Directory.CreateDirectory(tempUnzipDir);

                Dispatcher.Invoke(() =>
                {
                    UpdateProgressValue = 10;
                    UpdateProgressText = Lang.S["View_12179_3ab163"];
                });

                // 阶段2: 解压zip文件 (10-50%)
                ZipFile.ExtractToDirectory(zipFilePath, tempUnzipDir, true);

                Dispatcher.Invoke(() =>
                {
                    UpdateProgressValue = 50;
                    UpdateProgressText = Lang.S["View_12178_cb48d9"];
                });

                // 阶段3: 查找并验证 repo.json (50-60%)
                var repoJsonPath = Directory.GetFiles(tempUnzipDir, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
                if (repoJsonPath == null)
                {
                    throw new FileNotFoundException(Lang.S["View_12177_07b9ec"]);
                }

                var repoDir = Path.GetDirectoryName(repoJsonPath)!;

                Dispatcher.Invoke(() =>
                {
                    UpdateProgressValue = 60;
                    UpdateProgressText = Lang.S["View_12176_82e834"];
                });

                // 阶段4: 删除旧的目标目录 (60-70%)
                if (Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
                {
                    DirectoryHelper.DeleteReadOnlyDirectory(ScriptRepoUpdater.CenterRepoPath);
                }

                Dispatcher.Invoke(() =>
                {
                    UpdateProgressValue = 70;
                    UpdateProgressText = Lang.S["View_12175_64f921"];
                });

                // 阶段5: 复制新目录 (70-95%)
                DirectoryHelper.CopyDirectory(repoDir, ScriptRepoUpdater.CenterRepoPath);

                Dispatcher.Invoke(() =>
                {
                    UpdateProgressValue = 95;
                    UpdateProgressText = Lang.S["View_12174_ec3b0e"];
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
            UpdateProgressText = Lang.S["View_12173_8edcee"];
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
            ThemedMessageBox.Warning($"{Lang.S["View_12172_efc9c6"]}, "错误");
        }
        e.Handled = true;
    }

    /// <summary>
    /// 处理 PasswordBox 的密码变化事件
    /// </summary>
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox passwordBox)
        {
            // 更新 GitToken 属性，触发自动保存到凭据管理器
            GitToken = passwordBox.Password;
        }
    }
}