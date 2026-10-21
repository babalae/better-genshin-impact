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

/// <summary>一条龙可选账号准备项，始终编辑同一计划的账号绑定。</summary>
public partial class PuloniaOneDragonViewModel
{
    /// <summary>当前已保存的账号资料选择。</summary>
    public ObservableCollection<PuloniaGenshinAccountProfile> GenshinAccounts { get; } = [];
    /// <summary>多账号准备项默认隐藏。</summary>
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
            OnPropertyChanged(); AccountMessage = "账号绑定已更改，将随当前配置自动保存。";
        }
    }

    /// <summary>加载成功保存的账号目录，默认账号不要求用户完成设置。</summary>
    private async Task RefreshGenshinAccountsAsync()
    {
        var settings = await _store.LoadAccountSettingsAsync();
        GenshinAccounts.Clear(); foreach (var account in settings.Accounts) GenshinAccounts.Add(account);
        ApplyAccountPreparationVisibility(settings.MultiAccountEnabled);
        OnPropertyChanged(nameof(SelectedGenshinAccountId));
    }

    /// <summary>应用多账号步骤的显示状态；隐藏当前选中的准备项时回到首个普通任务。</summary>
    private void ApplyAccountPreparationVisibility(bool isVisible)
    {
        ShowAccountPreparation = isVisible;
        if (isVisible || !IsAccountPreparationSelected) return;
        IsAccountPreparationSelected = false;
        SelectedTask = TaskList.FirstOrDefault();
    }

    /// <summary>从一条龙“更多”菜单切换账号配置入口，并立即保存到全局账号设置。</summary>
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
            AccountMessage = isVisible ? "已显示多账号设置和切换账号步骤。" : "已隐藏多账号设置和切换账号步骤。";
        }
        catch (Exception ex)
        {
            // MenuItem 会先切换自身勾选状态；保存失败时主动刷新绑定以恢复已保存状态。
            OnPropertyChanged(nameof(ShowAccountPreparation));
            await ThemedMessageBox.ErrorAsync("切换账号配置显示状态失败：" + ex.Message, "一条龙");
        }
    }

    /// <summary>选中固定的账号准备项，任务表仍保持原来的八个默认任务。</summary>
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
