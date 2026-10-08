using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>统一目录选择、ZIP 选择与拖放入口，校验成功后立即添加本地来源。</summary>
public partial class PuloniaLocalRepositoryDialogViewModel : ViewModel
{
    /// <summary>复用仓库索引校验和共享 Repos ZIP 导入流程。</summary>
    private readonly PuloniaRepositoryManagementService _service;
    /// <summary>正在校验、导入或注册，阻止重复输入和窗口关闭。</summary>
    [ObservableProperty] private bool _isBusy;
    /// <summary>拖放目标高亮，由文件拖放 Behavior 回传。</summary>
    [ObservableProperty] private bool _isDraggingOver;
    /// <summary>本次处理的源路径，失败时保留以便用户定位。</summary>
    [ObservableProperty] private string _sourcePath = string.Empty;
    /// <summary>面向用户展示的拒绝或添加失败原因。</summary>
    [ObservableProperty] private string _errorMessage = string.Empty;
    /// <summary>当前校验或导入阶段。</summary>
    [ObservableProperty] private string _progressText = string.Empty;
    /// <summary>共享 ZIP 导入器提供的完成百分比。</summary>
    [ObservableProperty] private int _progressValue;
    /// <summary>索引校验期间使用不确定进度，导入开始后切换为百分比。</summary>
    [ObservableProperty] private bool _isProgressIndeterminate;
    /// <summary>添加成功后交回管理窗口的稳定仓库身份。</summary>
    [ObservableProperty] private ScriptRepositoryRegistration? _addedRepository;

    /// <summary>空闲时接受路径、选择器和取消动作。</summary>
    public bool CanEdit => !IsBusy;

    /// <summary>建立独立添加状态，打开窗口时不读取或注册任何来源。</summary>
    public PuloniaLocalRepositoryDialogViewModel(PuloniaRepositoryManagementService service) => _service = service;

    /// <summary>处理开始后关闭所有输入，并清除拖入高亮。</summary>
    partial void OnIsBusyChanged(bool value)
    {
        if (value) IsDraggingOver = false;
        OnPropertyChanged(nameof(CanEdit));
        AddSourceCommand.NotifyCanExecuteChanged();
        SelectDirectoryCommand.NotifyCanExecuteChanged();
        SelectZipCommand.NotifyCanExecuteChanged();
        RejectDropCommand.NotifyCanExecuteChanged();
    }

    /// <summary>只有尚未添加且空闲的窗口可以接受新来源。</summary>
    private bool CanAcceptSource() => !IsBusy && AddedRepository is null;

    /// <summary>选择包含 repo.json 的本地根目录，通过校验后直接注册，不加入自动更新目标。</summary>
    [RelayCommand(CanExecute = nameof(CanAcceptSource))]
    private async Task SelectDirectoryAsync()
    {
        var dialog = new Wpf.Ui.Violeta.Win32.OpenFolderDialog
        { Description = "选择包含 repo.json 索引的本地仓库根目录", UseDescriptionForTitle = true };
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
        var accepted = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(new WindowInteropHelper(owner).Handle);
        if (accepted == true) await AddSourceAsync(dialog.SelectedPath);
    }

    /// <summary>选择离线 ZIP，校验 repo.json 后通过老导入器解压到共享 Repos 目录并注册。</summary>
    [RelayCommand(CanExecute = nameof(CanAcceptSource))]
    private async Task SelectZipAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择脚本仓库 ZIP 压缩包", Filter = "ZIP 压缩包 (*.zip)|*.zip",
            Multiselect = false, CheckFileExists = true
        };
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
        if (dialog.ShowDialog(owner) == true) await AddSourceAsync(dialog.FileName);
    }

    /// <summary>选择和拖放共用立即添加流程，单次失败保留窗口并允许重新输入。</summary>
    [RelayCommand(CanExecute = nameof(CanAcceptSource))]
    private async Task AddSourceAsync(string? path)
    {
        if (!CanAcceptSource()) return;
        SourcePath = path ?? string.Empty;
        ErrorMessage = string.Empty;
        ProgressText = "正在识别本地来源…";
        ProgressValue = 0;
        IsProgressIndeterminate = true;
        IsBusy = true;
        ScriptRepositoryRegistration? added = null;
        try
        {
            // Progress 捕获窗口 UI 上下文，后台导入回调不能直接修改绑定属性。
            var progress = new Progress<(int Value, string Text)>(state =>
            {
                if (!IsBusy) return;
                ProgressValue = state.Value;
                ProgressText = state.Text;
                IsProgressIndeterminate = state.Value == 0;
            });
            added = await _service.AddLocalSourceAsync(SourcePath,
                (value, text) => ((IProgress<(int, string)>)progress).Report((value, text)));
        }
        catch (Exception ex) { ErrorMessage = "添加本地仓库失败：" + ex.Message; }
        finally { IsBusy = false; }
        // ZIP 写入开始后等待解压和注册完整结束，再让窗口返回成功结果。
        AddedRepository = added;
    }

    /// <summary>显示拖放 Behavior 的输入错误，不产生注册或磁盘写入。</summary>
    [RelayCommand(CanExecute = nameof(CanAcceptSource))]
    private void RejectDrop(string message) => ErrorMessage = message;
}
