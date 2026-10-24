using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.View.Windows.Pulonia;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 一条龙可选账号准备项，始终编辑同一计划的账号绑定。
/// “显示账号切换步骤”只控制清单中准备项的显示；绑定非默认账号时，配置下拉框旁常驻显示运行账号。
/// </summary>
public partial class PuloniaOneDragonViewModel
{
    /// <summary>当前已保存的账号资料选择。</summary>
    public ObservableCollection<PuloniaGenshinAccountProfile> GenshinAccounts { get; } = [];
    /// <summary>清单中的账号准备项默认隐藏；不影响账号绑定是否生效。</summary>
    [ObservableProperty] private bool _showAccountPreparation;
    /// <summary>是否正在查看固定首位的系统准备项。</summary>
    [ObservableProperty] private bool _isAccountPreparationSelected;
    /// <summary>账号绑定保存或读取的反馈。</summary>
    [ObservableProperty] private string _accountMessage = "";
    /// <summary>当前配置的绑定身份，缺省为默认账号。</summary>
    public string SelectedGenshinAccountId
    {
        get => SelectedConfiguration is { } document ? PuloniaTaskService.ResolveOneDragonAccountId(document.Plan) : PuloniaGenshinAccountProfile.DefaultId;
        set
        {
            if (SelectedConfiguration is not { } document || !GenshinAccounts.Any(account => account.Id == value)
                || value == SelectedGenshinAccountId) return;
            // 账号变更保留同一账号已有的预设选择，不把登录资料写入计划。
            var binding = document.Plan.Accounts.FirstOrDefault(account => account.AccountId == value)
                          ?? new PuloniaTaskAccountBinding { AccountId = value };
            document.ApplyMutation(() => { binding.Enabled = true; document.Plan.Accounts = [binding]; }, document.SelectedNode);
            NotifyAccountBindingChanged();
            AccountMessage = "账号绑定已更改，将随当前配置自动保存。";
        }
    }

    /// <summary>当前配置是否绑定了非默认账号；运行时会切换账号，因此始终在配置下拉框旁提示。</summary>
    public bool HasRunAccount => TryGetBoundAccountId() is var id && id != PuloniaGenshinAccountProfile.DefaultId;

    /// <summary>配置下拉框旁显示的运行账号；账号资料缺失或绑定无效时明确说明。</summary>
    public string RunAccountText
    {
        get
        {
            if (TryGetBoundAccountId() is not { } id)
                return "运行账号：绑定无效，请重新选择";
            if (id == PuloniaGenshinAccountProfile.DefaultId)
                return "运行账号：当前登录账号";
            return GenshinAccounts.FirstOrDefault(account => account.Id == id) is { } account
                ? "运行账号：" + account.Name
                : "运行账号：账号资料已删除，请重新选择";
        }
    }

    /// <summary>读取当前配置绑定的账号；绑定不合法（多个账号）时返回 null，不让显示属性抛出异常。</summary>
    private string? TryGetBoundAccountId()
    {
        try { return SelectedGenshinAccountId; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>账号绑定、配置切换、撤销或账号目录刷新后同步所有账号相关显示。</summary>
    private void NotifyAccountBindingChanged()
    {
        OnPropertyChanged(nameof(SelectedGenshinAccountId));
        OnPropertyChanged(nameof(HasRunAccount));
        OnPropertyChanged(nameof(RunAccountText));
    }

    /// <summary>加载成功保存的账号目录，默认账号不要求用户完成设置。</summary>
    private async Task RefreshGenshinAccountsAsync()
    {
        var settings = await _store.LoadAccountSettingsAsync();
        GenshinAccounts.Clear(); foreach (var account in settings.Accounts) GenshinAccounts.Add(account);
        ApplyAccountPreparationVisibility(settings.MultiAccountEnabled);
        NotifyAccountBindingChanged();
    }

    /// <summary>应用多账号步骤的显示状态；隐藏当前选中的准备项时回到首个普通任务。</summary>
    private void ApplyAccountPreparationVisibility(bool isVisible)
    {
        ShowAccountPreparation = isVisible;
        if (isVisible || !IsAccountPreparationSelected) return;
        IsAccountPreparationSelected = false;
        SelectedTask = TaskList.FirstOrDefault();
    }

    /// <summary>从一条龙“更多”菜单切换清单中账号准备项的显示，并立即保存到全局账号设置；账号绑定不受影响。</summary>
    [RelayCommand]
    private async Task ToggleAccountPreparationAsync()
    {
        var isVisible = !ShowAccountPreparation;
        try
        {
            // 每次基于最新账号目录修改显示标记，避免覆盖账号管理窗口已保存的资料。
            var settings = await _store.LoadAccountSettingsAsync();
            settings.MultiAccountEnabled = isVisible;
            await _store.SaveAccountSettingsAsync(settings);
            ApplyAccountPreparationVisibility(isVisible);
            AccountMessage = isVisible
                ? "已在清单中显示“切换账号”步骤。"
                : "已隐藏清单中的“切换账号”步骤；绑定的运行账号仍然生效，并显示在配置旁。";
        }
        catch (Exception ex)
        {
            // MenuItem 会先切换自身勾选状态；保存失败时主动刷新绑定以恢复已保存状态。
            OnPropertyChanged(nameof(ShowAccountPreparation));
            await ThemedMessageBox.ErrorAsync("切换账号步骤显示失败：" + ex.Message, "一条龙");
        }
    }

    /// <summary>选中固定的账号准备项，任务表仍保持原来的八个默认任务；运行账号提示也从这里进入。</summary>
    [RelayCommand] private void SelectAccountPreparation()
    { SelectedTask = null; IsAccountPreparationSelected = true; }

    /// <summary>打开独立账号管理，成功保存的设置在关闭后重新读取。</summary>
    [RelayCommand] private async Task ManageGenshinAccountsAsync()
    {
        try
        {
            using var viewModel = new PuloniaGenshinAccountsViewModel(_store,
                App.GetService<PuloniaGameTaskCoordinator>()!, App.GetService<IPuloniaGenshinAccountSwitcher>()!);
            await viewModel.LoadAsync();
            new PuloniaGenshinAccountsWindow(viewModel) { Owner = Application.Current.MainWindow }.ShowDialog();
            await RefreshGenshinAccountsAsync();
        }
        catch (Exception ex) { await ThemedMessageBox.ErrorAsync("账号管理未完成：" + ex.Message, "账号管理"); }
    }
}
