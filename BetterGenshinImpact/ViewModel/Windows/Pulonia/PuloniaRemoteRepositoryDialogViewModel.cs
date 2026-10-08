using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>独立维护远程仓库添加草稿，只在用户确认后创建真实仓库注册。</summary>
public partial class PuloniaRemoteRepositoryDialogViewModel : ViewModel
{
    /// <summary>已有仓库的注册存储，添加过程不调用远程同步。</summary>
    private readonly ScriptRepositoryStore _repositories;
    /// <summary>新仓库的展示名称。</summary>
    [ObservableProperty] private string _repositoryName = "第三方脚本仓库";
    /// <summary>用户填写的 Git 远程地址。</summary>
    [ObservableProperty] private string _remoteUrl = string.Empty;
    /// <summary>首次同步将使用的分支。</summary>
    [ObservableProperty] private string _branch = "release";
    /// <summary>名称、地址和分支的即时校验结果，空字符串表示有效。</summary>
    [ObservableProperty] private string _validationMessage = string.Empty;
    /// <summary>注册失败时展示的原因，不弹出第二层错误窗口。</summary>
    [ObservableProperty] private string _errorMessage = string.Empty;
    /// <summary>正在保存注册，阻止重复提交或关闭窗口。</summary>
    [ObservableProperty] private bool _isBusy;
    /// <summary>确认成功后交回管理窗口的真实仓库，草稿不会赋给此属性。</summary>
    [ObservableProperty] private ScriptRepositoryRegistration? _addedRepository;

    /// <summary>空闲时允许填写或取消。</summary>
    public bool CanEdit => !IsBusy;

    /// <summary>建立草稿并立即校验默认值，不读写仓库注册表。</summary>
    public PuloniaRemoteRepositoryDialogViewModel(ScriptRepositoryStore repositories)
    {
        _repositories = repositories;
        Validate();
    }

    /// <summary>名称修改后重新校验确认按钮。</summary>
    partial void OnRepositoryNameChanged(string value) => Validate();
    /// <summary>远程地址修改后重新校验确认按钮。</summary>
    partial void OnRemoteUrlChanged(string value) => Validate();
    /// <summary>分支修改后重新校验确认按钮。</summary>
    partial void OnBranchChanged(string value) => Validate();
    /// <summary>保存期间禁用表单和确认、取消按钮。</summary>
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        AddCommand.NotifyCanExecuteChanged();
    }

    /// <summary>复用存储层 Git 地址和分支校验，避免界面和实际保存规则不一致。</summary>
    private void Validate()
    {
        ErrorMessage = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(RepositoryName)) throw new ArgumentException("仓库名称不能为空。");
            ScriptRepositoryStore.ValidateRemote(RemoteUrl, Branch);
            ValidationMessage = string.Empty;
        }
        catch (ArgumentException ex) { ValidationMessage = ex.Message; }
        AddCommand.NotifyCanExecuteChanged();
    }

    /// <summary>有效草稿只能在空闲且尚未添加时提交。</summary>
    private bool CanAdd() => !IsBusy && AddedRepository is null && string.IsNullOrEmpty(ValidationMessage);

    /// <summary>用户确认后创建注册；成功结果触发窗口关闭，不建立或同步本机副本。</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        if (!CanAdd()) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        ScriptRepositoryRegistration? added = null;
        try { added = await _repositories.AddRemoteRepositoryAsync(RepositoryName, RemoteUrl, Branch); }
        catch (Exception ex) { ErrorMessage = "添加远程仓库失败：" + ex.Message; }
        finally { IsBusy = false; }
        // 先解除忙碌状态，再通知窗口接受结果，关闭时不会中断注册。
        AddedRepository = added;
        AddCommand.NotifyCanExecuteChanged();
    }
}
