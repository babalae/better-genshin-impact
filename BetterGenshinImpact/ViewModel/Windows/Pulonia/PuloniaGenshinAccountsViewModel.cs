using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers.Win32;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meziantou.Framework.Win32;
using Microsoft.Win32;
using OpenCvSharp;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>独立的原神账号管理草稿，只有明确保存才更新账号目录。</summary>
public partial class PuloniaGenshinAccountsViewModel : ObservableObject, IDisposable
{
    /// <summary>复用原子账号存储。</summary>
    private readonly PuloniaTaskStore _store;
    /// <summary>持有采集过程的游戏输入。</summary>
    private readonly PuloniaGameTaskCoordinator _coordinator;
    /// <summary>原生账号切换与图片采集。</summary>
    private readonly IPuloniaGenshinAccountSwitcher _switcher;
    /// <summary>窗口关闭时取消尚在采集的操作。</summary>
    private readonly CancellationTokenSource _closed = new();
    /// <summary>密码草稿只保留在内存，账号目录只保存凭据引用。</summary>
    private readonly Dictionary<string, (string Username, string Password)> _credentialDrafts = [];
    /// <summary>上次读入或成功保存的目录修订。</summary>
    private long _revision;
    /// <summary>窗口是否正在释放。</summary>
    private bool _disposed;
    /// <summary>可选账号，包括不可删除的默认账号。</summary>
    public ObservableCollection<PuloniaGenshinAccountProfile> Accounts { get; } = [];
    /// <summary>载入账号草稿时保留的一条龙账号准备项设置；该设置改由一条龙页面编辑。</summary>
    private bool _multiAccountEnabled;
    /// <summary>当前编辑的账号。</summary>
    [ObservableProperty] private PuloniaGenshinAccountProfile? _selectedAccount;
    /// <summary>账号名称草稿。</summary>
    [ObservableProperty] private string _accountName = "";
    /// <summary>完整 UID 草稿。</summary>
    [ObservableProperty] private string _uid = "";
    /// <summary>登录方式草稿。</summary>
    [ObservableProperty] private PuloniaGenshinAccountLoginMode _loginMode;
    /// <summary>列表显示的脱敏账号标识。</summary>
    [ObservableProperty] private string _listText = "";
    /// <summary>登录用户名草稿，不写入账号目录。</summary>
    [ObservableProperty] private string _loginUsername = "";
    /// <summary>登录密码草稿，不写入账号目录。</summary>
    [ObservableProperty] private string _loginPassword = "";
    /// <summary>图片相对路径草稿。</summary>
    [ObservableProperty] private string? _imagePath;
    /// <summary>保存、导入或采集操作结果。</summary>
    [ObservableProperty] private string _message = "默认账号使用当前登录状态，无需填写任何登录资料。";
    /// <summary>操作期间阻止并发切换和编辑。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit))] private bool _isBusy;
    /// <summary>是否允许修改资料。</summary>
    public bool CanEdit => !IsBusy;
    /// <summary>默认账号不能填写目标 UID 或登录资料。</summary>
    public bool CanEditAccount => SelectedAccount is { IsDefault: false };
    /// <summary>登录方式的中文选项。</summary>
    public IReadOnlyDictionary<PuloniaGenshinAccountLoginMode, string> LoginModes { get; } = new Dictionary<PuloniaGenshinAccountLoginMode, string>
    {
        [PuloniaGenshinAccountLoginMode.Automatic] = "自动：图片 → 列表文字 → 可选密码",
        [PuloniaGenshinAccountLoginMode.Image] = "账号图片", [PuloniaGenshinAccountLoginMode.ListText] = "列表文字",
        [PuloniaGenshinAccountLoginMode.Password] = "账号密码"
    };

    /// <summary>建立只在本窗口使用的编辑草稿。</summary>
    public PuloniaGenshinAccountsViewModel(PuloniaTaskStore store, PuloniaGameTaskCoordinator coordinator,
        IPuloniaGenshinAccountSwitcher switcher)
    { _store = store; _coordinator = coordinator; _switcher = switcher; }

    /// <summary>读取目录，不隐式创建账号或进行游戏操作。</summary>
    public async Task LoadAsync()
    {
        var settings = await _store.LoadAccountSettingsAsync(_closed.Token);
        _revision = settings.Revision; _multiAccountEnabled = settings.MultiAccountEnabled;
        foreach (var account in settings.Accounts) Accounts.Add(account);
        SelectedAccount = Accounts.FirstOrDefault();
    }

    /// <summary>切换账号编辑时保留未提交草稿，密码不回读到表单。</summary>
    partial void OnSelectedAccountChanged(PuloniaGenshinAccountProfile? oldValue, PuloniaGenshinAccountProfile? newValue)
    {
        CaptureDraft(oldValue);
        AccountName = newValue?.Name ?? ""; Uid = newValue?.Uid ?? "";
        LoginMode = newValue?.LoginMode ?? PuloniaGenshinAccountLoginMode.Automatic;
        ListText = newValue?.ListText ?? ""; ImagePath = newValue?.ImagePath;
        var credentials = newValue is not null ? _credentialDrafts.GetValueOrDefault(newValue.Id) : default;
        LoginUsername = credentials.Username ?? ""; LoginPassword = credentials.Password ?? "";
        OnPropertyChanged(nameof(CanEditAccount));
    }

    /// <summary>将表单内容保存到内存模型，默认账号始终保持免配置。</summary>
    private void CaptureDraft(PuloniaGenshinAccountProfile? account)
    {
        if (account is null || account.IsDefault) return;
        account.Name = AccountName.Trim(); account.Uid = Uid.Trim(); account.LoginMode = LoginMode;
        account.ListText = ListText.Trim(); account.ImagePath = ImagePath;
        if (LoginUsername.Length > 0 || LoginPassword.Length > 0)
            _credentialDrafts[account.Id] = (LoginUsername, LoginPassword);
        else _credentialDrafts.Remove(account.Id);
    }

    /// <summary>添加一份尚未写盘的账号资料。</summary>
    [RelayCommand] private void AddAccount()
    {
        if (IsBusy) return;
        var account = new PuloniaGenshinAccountProfile(); Accounts.Add(account); SelectedAccount = account;
    }

    /// <summary>拒绝移除仍由计划引用的资料；历史凭据和图片继续保留供快照续跑。</summary>
    [RelayCommand] private async Task RemoveAccountAsync()
    {
        if (SelectedAccount is not { IsDefault: false } account || IsBusy) return;
        IsBusy = true;
        try
        {
            if ((await _store.ListPlansAsync(_closed.Token)).Any(plan => plan.Accounts.Any(binding => binding.AccountId == account.Id)))
                throw new InvalidOperationException("仍有计划绑定此账号，请先将这些计划改为其他账号。");
            Accounts.Remove(account); SelectedAccount = Accounts.FirstOrDefault(); _credentialDrafts.Remove(account.Id);
            Message = "账号已从草稿移除，保存后生效；历史运行的图片与凭据保留。";
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>导入 1080p 登录列表中的账号标识图片，验证可解码后保存为独立 PNG。</summary>
    [RelayCommand] private async Task ImportImageAsync()
    {
        if (!CanEditAccount || IsBusy) return;
        var picker = new OpenFileDialog { Filter = "账号图片|*.png;*.jpg;*.jpeg", Title = "选择 1080p 登录列表中的账号标识裁剪图" };
        if (picker.ShowDialog() != true) return;
        IsBusy = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(picker.FileName, _closed.Token);
            using var image = Cv2.ImDecode(bytes, ImreadModes.Color);
            if (image.Empty() || image.Width > 1920 || image.Height > 1080)
                throw new InvalidOperationException("请选择可解码的 1080p 账号标识裁剪图片，或使用自动采集。");
            var relative = Path.Combine("account-images", Guid.NewGuid().ToString("N") + ".png");
            var path = _store.GetAccountImagePath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, image.ImEncode(".png"), _closed.Token);
            ImagePath = relative; Message = "图片已导入，保存账号目录后生效。";
        }
        catch (Exception ex) { Message = "图片未导入：" + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>采集前确认会进入登录界面，过程共用游戏任务输入租约。</summary>
    [RelayCommand] private async Task CaptureImageAsync()
    {
        if (!CanEditAccount || IsBusy) return;
        if (await ThemedMessageBox.ShowAsync("将从当前已登录账号识别 UID，退出到登录界面采集账号标识，再返回游戏。请确认当前登录的是要保存的账号。",
                "采集账号图片", System.Windows.MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Question,
                System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes) return;
        IsBusy = true;
        try
        {
            using var lease = await _coordinator.AcquireAsync(_closed.Token);
            var result = await _switcher.CaptureCurrentAccountAsync(lease.Runtime, _closed.Token);
            Uid = result.Uid; ImagePath = result.ImagePath;
            Message = "采集完成，已返回游戏。保存账号目录后生效。";
        }
        catch (Exception ex) { Message = "采集未完成：" + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>校验后保存整个目录；新凭据使用独立版本引用，不改变正在执行的旧快照。</summary>
    [RelayCommand] private async Task SaveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            CaptureDraft(SelectedAccount);
            var settings = new PuloniaGenshinAccountSettings { Revision = _revision, MultiAccountEnabled = _multiAccountEnabled, Accounts = Accounts.ToList() };
            PuloniaTaskStore.ValidateAccountSettings(settings);
            foreach (var (id, credentials) in _credentialDrafts)
            {
                if (string.IsNullOrWhiteSpace(credentials.Username) || string.IsNullOrEmpty(credentials.Password))
                    throw new InvalidOperationException("保存登录凭据时必须同时填写账号和密码；无需更新凭据时请留空。");
                var account = settings.Accounts.Single(a => a.Id == id);
                account.CredentialReference = "BetterGenshinImpact.Pulonia.Account." + Guid.NewGuid().ToString("N");
                CredentialManagerHelper.SaveCredential(account.CredentialReference, credentials.Username, credentials.Password,
                    "Pulonia 原神登录资料", CredentialPersistence.LocalMachine);
            }
            var saved = await _store.SaveAccountSettingsAsync(settings, _closed.Token);
            _revision = saved.Revision; _credentialDrafts.Clear(); LoginPassword = ""; LoginUsername = "";
            Message = "账号目录已保存。";
        }
        catch (Exception ex) { Message = "未保存：" + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>关闭时只取消本窗口的采集等待，不取消其他任务，也不释放仍被操作使用的令牌源。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _closed.Cancel();
        _credentialDrafts.Clear(); LoginPassword = ""; LoginUsername = "";
        // 异步命令可能仍在退出输入租约，令牌源在命令完全结束后释放。
        Task.WhenAll(CaptureImageCommand.ExecutionTask ?? Task.CompletedTask, SaveCommand.ExecutionTask ?? Task.CompletedTask,
            ImportImageCommand.ExecutionTask ?? Task.CompletedTask, RemoveAccountCommand.ExecutionTask ?? Task.CompletedTask)
            .ContinueWith(completed => { _ = completed.Exception; _closed.Dispose(); }, TaskScheduler.Default);
    }
}
