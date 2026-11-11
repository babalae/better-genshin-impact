using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 账户页：顶部是本实例当前账户（修改立即保存），左侧是账户列表，右侧是所选账户的详情（概况、登录资料、路线 CD、首领领奖、狛荷屋）。
/// 概况与登录资料是草稿，“保存”一次保存整个目录（带凭据草稿、检查修订）；删除账户确认后立即保存。
/// 路线 CD 与首领领奖是所选账户的账户进度，不属于草稿，见 PuloniaAccountsPageViewModel.Progress.cs；
/// 读取 UID、采集图片与测试切换等游戏操作见 PuloniaAccountsPageViewModel.Game.cs；
/// “狛荷屋”页签跳转到狛荷屋页面，见 PuloniaAccountsPageViewModel.AccountRecords.cs。
/// </summary>
public partial class PuloniaAccountsPageViewModel : ViewModel
{
    /// <summary>只有一个账户时的页首说明，文字按统筹设计 9.3 节。</summary>
    private const string SingleAccountHeader = "只有一个账号时不需要任何设置。有多个账号时，在这里添加账户，并在标题栏选择每个实例当前运行的账户。";

    /// <summary>有多个账户时的页首说明。</summary>
    private const string MultipleAccountsHeader = "为每个账户填写 UID，并在标题栏或本页顶部选择每个实例当前运行的账户；需要 BetterGI 自动切换时，再填写登录资料。";

    /// <summary>账户目录、实例当前账户与删除检查的应用服务。</summary>
    private readonly IPuloniaAccountService _accounts;

    /// <summary>账户图片的存储入口（导入图片时新建文件）。</summary>
    private readonly PuloniaTaskStore _store;

    /// <summary>页面所在的 UI 线程；账户服务的事件可能来自后台线程。</summary>
    private readonly Dispatcher _dispatcher;

    /// <summary>上次读取或保存的目录修订，保存时据此检查冲突。</summary>
    private long _revision;

    /// <summary>上次读取或保存的账户顺序，用于判断是否调整过顺序。</summary>
    private IReadOnlyList<string> _savedOrder = [];

    /// <summary>是否已经成功读取过账户目录。</summary>
    private bool _loaded;

    /// <summary>页面是否可见；只在可见时响应账户变化事件。</summary>
    private bool _isActive;

    /// <summary>本实例当前账户 ID；“未确定”时为 null。</summary>
    private string? _currentAccountId;

    /// <summary>最近一次保存成功时的凭据或关联数据警告；没有警告时为 null，删除账户后的说明据此补充。</summary>
    private string? _lastSaveWarning;

    /// <summary>账户目录草稿，顺序即界面显示顺序。</summary>
    public ObservableCollection<PuloniaAccountItemViewModel> Accounts { get; } = [];

    /// <summary>顶部“本实例当前账户”的选项：已保存的账户。</summary>
    public ObservableCollection<PuloniaAccountChoiceViewModel> CurrentAccountOptions { get; } = [];

    /// <summary>当前查看与编辑的账户。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestSwitchHint))]
    private PuloniaAccountItemViewModel? _selectedAccount;

    /// <summary>右侧详情当前页签：0 为概况，1 为登录资料，2 为路线 CD，3 为首领领奖，4 为狛荷屋。</summary>
    [ObservableProperty] private int _selectedDetailTabIndex;

    /// <summary>最近一次操作的结果说明。</summary>
    [ObservableProperty] private string _message = "";

    /// <summary>读写或游戏操作进行中，期间禁止编辑和其他操作。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isBusy;

    /// <summary>
    /// 建立账户页视图模型；构造过程不读写文件。
    /// </summary>
    /// <param name="accounts">账户应用服务</param>
    /// <param name="store">Pulonia 存储，用于新建账户图片</param>
    /// <param name="coordinator">游戏输入租约，用于读取 UID、采集图片和测试切换</param>
    /// <param name="switcher">账户切换、UID 读取与图片采集</param>
    /// <param name="session">编辑会话：路线 CD 与首领领奖页签读取计划与账户进度</param>
    /// <param name="accountRecordsPage">狛荷屋页视图模型，“打开狛荷屋”在导航前记下要选中的 UID；测试直接构造时为空</param>
    /// <param name="navigation">页面导航，用于跳转到狛荷屋页、任务计划页与新一条龙页；测试直接构造时为空</param>
    /// <param name="runOverview">跨实例运行视图，概况页读取账户最近一次运行；为空时不显示</param>
    public PuloniaAccountsPageViewModel(IPuloniaAccountService accounts, PuloniaTaskStore store,
        PuloniaGameTaskCoordinator coordinator, IPuloniaGenshinAccountSwitcher switcher, PuloniaPlanEditingSession session,
        AccountRecordsPageViewModel? accountRecordsPage = null, INavigationService? navigation = null,
        PuloniaRunOverview? runOverview = null)
    {
        _accounts = accounts;
        _store = store;
        _coordinator = coordinator;
        _switcher = switcher;
        _accountRecordsPage = accountRecordsPage;
        _navigation = navigation;
        _session = session;
        _runOverview = runOverview;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        // 路线 CD 页签固定为所选账户；选中账户之前列表为空
        RouteCooldowns = new PuloniaRouteCooldownOverviewViewModel(session);
        RouteCooldowns.SetFixedAccount(null);
        BossRewards = new PuloniaBossRewardOverviewViewModel(session);
        Accounts.CollectionChanged += (_, _) => OnAccountListChanged();
    }

    /// <summary>是否允许编辑。</summary>
    public bool CanEdit => !IsBusy;

    /// <summary>只有一个账户时收起列表，直接显示该账户的详情。</summary>
    public bool IsSingleAccount => Accounts.Count <= 1;

    /// <summary>是否显示左侧账户列表。</summary>
    public bool ShowAccountList => !IsSingleAccount;

    /// <summary>页首说明：只有一个账户与有多个账户时不同。</summary>
    public string HeaderText => IsSingleAccount ? SingleAccountHeader : MultipleAccountsHeader;

    /// <summary>本实例的名称，如“主实例”“云原神 · 小号a”。</summary>
    public string CurrentInstanceName => PuloniaInstanceKey.DescribeDirectory(PuloniaInstanceKey.ToDirectoryName(_accounts.CurrentInstanceKey));

    /// <summary>顶部当前账户的说明文字。</summary>
    public string CurrentInstanceText => CurrentInstanceName + " · 本实例的游戏现在登录的账户；修改立即保存，下一次运行开始时生效。";

    /// <summary>本实例是否是云原神实例；云原神实例不能切换账户。</summary>
    public bool IsCloudInstance => _accounts.CurrentInstanceKey.StartsWith(PuloniaInstanceKey.WebViewPrefix, StringComparison.Ordinal);

    /// <summary>本实例当前账户 ID；从界面写入时立即保存。下拉框重建选项时写回的 null 被忽略，不能据此改为“未确定”。</summary>
    public string? CurrentAccountId
    {
        get => _currentAccountId;
        set
        {
            if (value is null || value == _currentAccountId)
                return;
            _ = ChangeCurrentAccountAsync(value);
        }
    }

    /// <summary>本实例当前账户为“未确定”时的提示；已确定时为 null。</summary>
    public string? CurrentAccountHint => _loaded && _currentAccountId is null
        ? "本实例当前账户未确定，跟随当前账户的运行会进入待处理。请选择游戏中现在登录的账户。"
        : null;

    /// <summary>是否显示当前账户提示。</summary>
    public bool HasCurrentAccountHint => CurrentAccountHint is not null;

    /// <summary>草稿是否与已保存的目录不同：调整了顺序、新增了账户或修改了任一账户。</summary>
    public bool HasUnsavedChanges => !Accounts.Select(account => account.Id).SequenceEqual(_savedOrder)
                                     || Accounts.Any(account => account.IsDirty);

    /// <summary>服务器时区选项，与触发器使用同一组取值。</summary>
    public IReadOnlyDictionary<PuloniaTaskServerTimeZone, string> ServerTimeZones { get; } = new Dictionary<PuloniaTaskServerTimeZone, string>
    {
        [PuloniaTaskServerTimeZone.UtcPlus8] = "国服 / 亚服 / 港澳台（UTC+8）",
        [PuloniaTaskServerTimeZone.Europe] = "欧服（UTC+1）",
        [PuloniaTaskServerTimeZone.America] = "美服（UTC-5）"
    };

    /// <summary>登录方式的中文选项。</summary>
    public IReadOnlyDictionary<PuloniaGenshinAccountLoginMode, string> LoginModes { get; } = new Dictionary<PuloniaGenshinAccountLoginMode, string>
    {
        [PuloniaGenshinAccountLoginMode.Automatic] = "自动：图片 → 列表文字 → 可选密码",
        [PuloniaGenshinAccountLoginMode.Image] = "账户图片",
        [PuloniaGenshinAccountLoginMode.ListText] = "列表文字",
        [PuloniaGenshinAccountLoginMode.Password] = "账号密码"
    };

    /// <summary>进入页面：订阅账户变化并重新读取；有未保存的修改时保留草稿，只刷新当前账户。</summary>
    public override async Task OnNavigatedToAsync()
    {
        if (!_isActive)
        {
            _isActive = true;
            _accounts.AccountsChanged += OnAccountsChanged;
            _accounts.CurrentAccountChanged += OnCurrentAccountChanged;
        }
        await LoadAsync(force: false);
        // 其他页面的“查看路线 CD”在导航前记下的请求：账户列表就绪后选中账户并切到“路线 CD”
        ApplyRouteCooldownRequest();
        // 概况的最近一次运行与指定的计划可能已在其他页面或实例中变化
        await RefreshOverviewAsync();
    }

    /// <summary>离开页面：停止响应账户变化；草稿保留到下次进入。</summary>
    public override Task OnNavigatedFromAsync()
    {
        if (_isActive)
        {
            _isActive = false;
            _accounts.AccountsChanged -= OnAccountsChanged;
            _accounts.CurrentAccountChanged -= OnCurrentAccountChanged;
        }
        return base.OnNavigatedFromAsync();
    }

    /// <summary>页面 Loaded 是唯一的进入入口，通过 Behaviors 接入。</summary>
    [RelayCommand]
    private Task OpenPageAsync() => OnNavigatedToAsync();

    /// <summary>页面 Unloaded 是唯一的离开入口，通过 Behaviors 接入。</summary>
    [RelayCommand]
    private Task ClosePageAsync() => OnNavigatedFromAsync();

    /// <summary>
    /// 读取账户目录、本实例当前账户和各实例的当前账户。
    /// </summary>
    /// <param name="force">为 true 时放弃草稿，用磁盘内容替换；为 false 时有未保存的修改则保留草稿</param>
    internal async Task LoadAsync(bool force)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            await LoadCoreAsync(force);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>读取的实际步骤；调用方负责忙碌状态。</summary>
    /// <param name="force">是否放弃草稿</param>
    private async Task LoadCoreAsync(bool force)
    {
        try
        {
            // 1. 账户目录：修订未变时保留现有草稿；有未保存的修改时不替换，避免丢失用户输入
            var settings = await _accounts.LoadAccountsAsync();
            if (force || !_loaded || (!HasUnsavedChanges && settings.Revision != _revision))
                ApplyDirectory(settings);
            else if (HasUnsavedChanges && settings.Revision != _revision)
                Message = "账户目录已在其他地方修改。本页有未保存的修改，保存时会提示冲突；可以先“放弃修改”再查看最新内容。";
            _loaded = true;
            // 2. 本实例当前账户与各实例的当前账户
            await RefreshCurrentAccountsCoreAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Message = "账户读取失败：" + ex.Message;
        }
    }

    /// <summary>用读取或保存得到的目录重建草稿列表，尽量保留原来的选中账户。</summary>
    /// <param name="settings">已保存的账户目录</param>
    private void ApplyDirectory(PuloniaGenshinAccountSettings settings)
    {
        var selectedId = SelectedAccount?.Id;
        foreach (var item in Accounts)
        {
            item.PropertyChanged -= OnAccountPropertyChanged;
            item.ClearCredentialDraft();
        }
        Accounts.Clear();
        foreach (var account in settings.Accounts)
            Accounts.Add(Attach(new PuloniaAccountItemViewModel(account)));
        _revision = settings.Revision;
        _savedOrder = settings.Accounts.Select(account => account.Id).ToArray();
        RebuildCurrentAccountOptions(settings.Accounts);
        SelectedAccount = Accounts.FirstOrDefault(item => item.Id == selectedId) ?? Accounts.FirstOrDefault();
        NotifyDirtyChanged();
    }

    /// <summary>订阅草稿变化，用于更新未保存状态与命令。</summary>
    /// <param name="item">账户草稿</param>
    private PuloniaAccountItemViewModel Attach(PuloniaAccountItemViewModel item)
    {
        item.PropertyChanged += OnAccountPropertyChanged;
        return item;
    }

    /// <summary>按已保存的账户重建顶部当前账户的选项，并重新同步下拉框的选中值。</summary>
    /// <param name="saved">已保存的账户</param>
    private void RebuildCurrentAccountOptions(IEnumerable<PuloniaGenshinAccountProfile> saved)
    {
        CurrentAccountOptions.Clear();
        foreach (var account in saved)
            CurrentAccountOptions.Add(new PuloniaAccountChoiceViewModel(account, account.Id == _currentAccountId));
        // 重建选项时下拉框会写回 null（被忽略），这里重新通知以恢复选中项。
        OnPropertyChanged(nameof(CurrentAccountId));
    }

    /// <summary>读取本实例当前账户与各实例的当前账户；失败时在状态栏说明。</summary>
    private async Task RefreshCurrentAccountsAsync()
    {
        try { await RefreshCurrentAccountsCoreAsync(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Message = "当前账户读取失败：" + ex.Message; }
    }

    /// <summary>
    /// 读取本实例当前账户，并按各实例设置标出每个账户是哪些实例的当前账户。
    /// </summary>
    private async Task RefreshCurrentAccountsCoreAsync()
    {
        SetCurrentAccountIdSilently(await _accounts.GetCurrentAccountIdAsync(_accounts.CurrentInstanceKey));
        var instances = await _accounts.ListInstanceSettingsAsync();
        var currentDirectory = PuloniaInstanceKey.ToDirectoryName(_accounts.CurrentInstanceKey);
        // 主实例、桌面分身在前，云原神实例按名称排列；本实例另加标注
        var names = instances.Items.Where(entry => entry.Settings.CurrentAccountId is not null)
            .OrderBy(entry => InstanceOrder(entry.DirectoryName)).ThenBy(entry => entry.DirectoryName, StringComparer.Ordinal)
            .GroupBy(entry => entry.Settings.CurrentAccountId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => string.Join("、", group.Select(entry =>
                PuloniaInstanceKey.DescribeDirectory(entry.DirectoryName) + (entry.DirectoryName == currentDirectory ? "（本实例）" : ""))),
                StringComparer.Ordinal);
        foreach (var item in Accounts)
        {
            item.InstancesText = names.GetValueOrDefault(item.Id) ?? "";
            // 账户占用（任一实例中正在使用该账户的运行）：新增尚未保存的账户没有占用文件
            item.RunningText = item.Saved is not null && _accounts.GetAccountOccupant(item.Id) is { } occupant
                ? "运行中 · " + (string.IsNullOrEmpty(occupant.InstanceKey) ? "另一个实例" : PuloniaInstanceKey.Describe(occupant.InstanceKey))
                : "";
        }
        if (instances.Failures.Count > 0)
            Message = "有实例设置文件无法读取，“作为哪些实例的当前账户”可能不完整：" + string.Join("；", instances.Failures);
    }

    /// <summary>实例目录的显示顺序：主实例、桌面分身、云原神实例。</summary>
    /// <param name="directoryName">实例目录名</param>
    private static int InstanceOrder(string directoryName) => directoryName switch
    {
        PuloniaInstanceKey.Primary => 0,
        PuloniaInstanceKey.ChildSession => 1,
        _ => 2
    };

    /// <summary>更新本实例当前账户的显示，不触发保存。</summary>
    /// <param name="accountId">当前账户 ID；“未确定”时为 null</param>
    private void SetCurrentAccountIdSilently(string? accountId)
    {
        _currentAccountId = accountId;
        foreach (var option in CurrentAccountOptions)
            option.IsCurrent = option.Id == accountId;
        OnPropertyChanged(nameof(CurrentAccountId));
        OnPropertyChanged(nameof(CurrentAccountHint));
        OnPropertyChanged(nameof(HasCurrentAccountHint));
    }

    /// <summary>
    /// 修改本实例当前账户并立即保存；失败时恢复原来的显示。
    /// </summary>
    /// <param name="accountId">新的当前账户 ID</param>
    private async Task ChangeCurrentAccountAsync(string accountId)
    {
        var previous = _currentAccountId;
        SetCurrentAccountIdSilently(accountId);
        try
        {
            await _accounts.SetCurrentAccountAsync(_accounts.CurrentInstanceKey, accountId);
            var name = CurrentAccountOptions.FirstOrDefault(option => option.Id == accountId)?.Name ?? accountId;
            Message = $"本实例当前账户已改为“{name}”，下一次运行开始时生效。";
            // 各实例的当前账户随之变化；页面不可见时没有订阅事件，这里直接刷新
            if (!_isActive)
                await RefreshCurrentAccountsAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetCurrentAccountIdSilently(previous);
            Message = "未能修改本实例当前账户：" + ex.Message;
        }
    }

    /// <summary>添加一份尚未保存的账户：名称不与已有账户重复，服务器时区取通用配置。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddAccount()
    {
        var names = Accounts.Select(item => item.Name.Trim()).ToHashSet(StringComparer.Ordinal);
        var name = "新账户";
        for (var index = 2; names.Contains(name); index++)
            name = $"新账户 {index}";
        var account = new PuloniaGenshinAccountProfile { Name = name, ServerTimeZone = PuloniaServerTime.GetDefaultServerTimeZone() };
        var item = Attach(new PuloniaAccountItemViewModel(account, isSaved: false));
        Accounts.Add(item);
        SelectedAccount = item;
        SelectedDetailTabIndex = 0;
        Message = "已新增账户草稿：填写名称和完整 UID（可以从当前游戏读取），选择服务器后点“保存”。";
    }

    /// <summary>所选账户能否上移。</summary>
    private bool CanMoveUp() => CanEdit && SelectedAccount is { } account && Accounts.IndexOf(account) > 0;

    /// <summary>把所选账户上移一位，保存后生效。</summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => MoveSelected(-1);

    /// <summary>所选账户能否下移。</summary>
    private bool CanMoveDown() => CanEdit && SelectedAccount is { } account && Accounts.IndexOf(account) is var index
                                  && index >= 0 && index < Accounts.Count - 1;

    /// <summary>把所选账户下移一位，保存后生效。</summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => MoveSelected(1);

    /// <summary>移动所选账户并保持选中。</summary>
    /// <param name="offset">移动的位数，负数为上移</param>
    private void MoveSelected(int offset)
    {
        if (SelectedAccount is not { } account)
            return;
        var index = Accounts.IndexOf(account);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Accounts.Count)
            return;
        Accounts.Move(index, target);
        SelectedAccount = account;
        Message = "已调整账户顺序，点“保存”后生效。";
    }

    /// <summary>是否选中了可编辑的账户。</summary>
    private bool CanEditSelected() => CanEdit && SelectedAccount is not null;

    /// <summary>为所选账户改名，保存后生效。</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private void RenameAccount()
    {
        if (SelectedAccount is not { } account)
            return;
        var name = PromptDialog.Prompt("请输入新的账户名称，名称不能与其他账户重复。", "账户改名", account.Name).Trim();
        if (string.IsNullOrWhiteSpace(name) || name == account.Name.Trim())
            return;
        account.Name = name;
        Message = $"账户已改名为“{name}”，点“保存”后生效。";
    }

    /// <summary>所选账户能否删除：目录中只剩一个账户时不能删除。</summary>
    private bool CanDeleteAccount() => CanEditSelected() && Accounts.Count > 1;

    /// <summary>
    /// 删除所选账户：先检查是否仍被计划指定或是某个实例的当前账户，有阻碍时说明原因；
    /// 可删时确认，然后立即保存目录（一并删除凭据和不再被引用的账户图片）。尚未保存的新账户直接从草稿移除。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteAccount))]
    private async Task DeleteAccountAsync()
    {
        if (SelectedAccount is not { } account || Accounts.Count <= 1 || IsBusy)
            return;
        // 1. 尚未保存的新账户不会被任何计划或实例引用，直接从草稿移除
        if (account.Saved is null)
        {
            RemoveFromDraft(account);
            Message = $"已移除尚未保存的账户“{account.Name}”。";
            return;
        }
        IsBusy = true;
        try
        {
            // 2. 检查已保存的引用；有阻碍时说明原因，不删除
            if (await _accounts.CheckAccountRemovalAsync(account.Id) is { } blocker)
            {
                Message = "不能删除账户：" + blocker;
                await ThemedMessageBox.WarningAsync(blocker, "不能删除账户");
                return;
            }
            // 3. 确认后从草稿移除并立即保存整个目录；保存失败时恢复原位置
            var othersDirty = Accounts.Where(item => !ReferenceEquals(item, account)).Any(item => item.IsDirty)
                              || !Accounts.Where(item => !ReferenceEquals(item, account)).Select(item => item.Id)
                                  .SequenceEqual(_savedOrder.Where(id => id != account.Id));
            // 列出这个账户实际会被删除的数据（统筹设计 4.1）；读取失败时退回类别说明
            string dataText;
            try
            {
                var items = (await _accounts.DescribeAccountRemovalAsync(account.Id)).Describe();
                dataText = items.Count == 0
                    ? "这个账户没有保存登录凭据、账户图片、路线 CD 完成记录、首领领奖次数或狛荷屋记录。"
                    : "将一并删除：\n" + string.Join("\n", items.Select(item => "• " + item));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                dataText = "将一并删除它保存的登录凭据和账户图片，以及该账户的路线 CD 完成记录、首领领奖次数和狛荷屋记录"
                           + $"（未能读取具体数据：{ex.Message}）。";
            }
            var confirm = $"删除账户“{account.Name}”？\n\n{dataText}\n\n删除后不能撤销。"
                          + (othersDirty ? "\n本页其他未保存的修改会一并保存。" : "");
            if (await ThemedMessageBox.ShowAsync(confirm, "删除账户", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            var index = Accounts.IndexOf(account);
            RemoveFromDraft(account);
            if (await SaveCoreAsync())
            {
                // 凭据或狛荷屋记录等关联数据未能处理时，保留保存结果中的说明
                Message = _lastSaveWarning is { } warning
                    ? $"已删除账户“{account.Name}”，但{warning}"
                    : $"已删除账户“{account.Name}”。";
                return;
            }
            // 保存失败（例如修订冲突后放弃了重新读取）：账户仍在目录中，放回原位置
            if (!Accounts.Any(item => item.Id == account.Id))
            {
                Accounts.Insert(Math.Min(index, Accounts.Count), Attach(account));
                SelectedAccount = account;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Message = "未删除：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>从草稿中移除账户并选中相邻账户。</summary>
    /// <param name="account">要移除的账户草稿</param>
    private void RemoveFromDraft(PuloniaAccountItemViewModel account)
    {
        var index = Accounts.IndexOf(account);
        account.PropertyChanged -= OnAccountPropertyChanged;
        account.ClearCredentialDraft();
        Accounts.Remove(account);
        SelectedAccount = Accounts.Count == 0 ? null : Accounts[Math.Clamp(index, 0, Accounts.Count - 1)];
    }

    /// <summary>是否有可保存的修改。</summary>
    private bool CanSave() => CanEdit && HasUnsavedChanges;

    /// <summary>保存整个账户目录与本次填写的凭据。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try { await SaveCoreAsync(); }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 校验后保存整个目录；存储层先保存目录，成功后再写入本次填写的凭据（目标名按账户固定、同名覆盖），
    /// 并删除被移除账户或改用无密码登录方式的账户的旧凭据。修订冲突时提示重新读取。调用方负责忙碌状态。
    /// </summary>
    /// <returns>是否保存成功</returns>
    private async Task<bool> SaveCoreAsync()
    {
        var settings = new PuloniaGenshinAccountSettings
        {
            Revision = _revision,
            Accounts = Accounts.Select(item => item.ToProfile()).ToList()
        };
        var credentials = Accounts.Where(item => item.HasCredentialDraft)
            .ToDictionary(item => item.Id, item => (item.LoginUsername, item.LoginPassword), StringComparer.Ordinal);
        _lastSaveWarning = null;
        try
        {
            var result = await _accounts.SaveAccountsAsync(settings, credentials);
            _revision = result.Settings.Revision;
            _savedOrder = result.Settings.Accounts.Select(account => account.Id).ToArray();
            // 凭据引用由存储层按草稿和登录方式确定；凭据未全部写入时保留密码草稿，用户可直接再次保存重试。
            var keepCredentials = result.CredentialError is not null;
            foreach (var item in Accounts)
                item.MarkSaved(result.Settings.Accounts.Single(saved => saved.Id == item.Id), keepCredentials);
            RebuildCurrentAccountOptions(result.Settings.Accounts);
            NotifyDirtyChanged();
            // 凭据写入失败与狛荷屋记录改名、删除失败都不回滚目录，一并显示在消息里
            var warnings = new[] { result.CredentialError, result.DataWarning }.OfType<string>().ToArray();
            _lastSaveWarning = warnings.Length > 0 ? string.Join("；", warnings) : null;
            Message = _lastSaveWarning is { } warning ? "账户目录已保存，但" + warning : "账户目录已保存。";
            // 新增的账户保存后才有最近一次运行与指定的计划
            _ = RefreshOverviewAsync();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!await IsRevisionConflictAsync())
            {
                Message = "未保存：" + ex.Message;
                return false;
            }
            // 修订冲突：其他实例或页面已保存账户目录，本页草稿基于旧内容，不能覆盖
            Message = "未保存：账户目录已被其他实例或页面修改。";
            if (await ThemedMessageBox.ShowAsync("账户目录已被其他实例或页面修改，本页的修改不能直接保存。\n\n要重新读取最新的账户目录吗？本页未保存的修改会丢失。",
                    "账户目录已变更", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
            {
                await LoadCoreAsync(force: true);
                Message = "已重新读取最新的账户目录，请重新修改后保存。";
            }
            return false;
        }
    }

    /// <summary>保存失败后判断是否因为修订冲突：磁盘上的修订与本页草稿的基准不同。</summary>
    private async Task<bool> IsRevisionConflictAsync()
    {
        try { return (await _accounts.LoadAccountsAsync()).Revision != _revision; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return false; }
    }

    /// <summary>是否可以放弃修改。</summary>
    private bool CanDiscardChanges() => CanEdit && HasUnsavedChanges;

    /// <summary>确认后放弃本页未保存的修改，重新读取账户目录。</summary>
    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
    private async Task DiscardChangesAsync()
    {
        if (await ThemedMessageBox.QuestionAsync("放弃本页未保存的修改，并重新读取账户目录？", "放弃修改") != MessageBoxResult.Yes)
            return;
        await LoadAsync(force: true);
        Message = "已放弃未保存的修改。";
    }

    /// <summary>账户目录在本进程保存后重新读取（只在页面可见且空闲时）；修订未变时保留草稿。</summary>
    private void OnAccountsChanged(object? sender, EventArgs e)
        => _dispatcher.InvokeAsync(() =>
        {
            if (_isActive && !IsBusy)
                _ = LoadAsync(force: false);
        });

    /// <summary>任一实例的当前账户在本进程被修改后刷新顶部与列表中的当前账户。</summary>
    private void OnCurrentAccountChanged(object? sender, PuloniaCurrentAccountChangedEventArgs e)
        => _dispatcher.InvokeAsync(() =>
        {
            if (_isActive)
                _ = RefreshCurrentAccountsAsync();
        });

    /// <summary>草稿属性变化后更新未保存状态与命令。</summary>
    private void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PuloniaAccountItemViewModel.IsDirty) or null)
            NotifyDirtyChanged();
        if (ReferenceEquals(sender, SelectedAccount) && e.PropertyName is nameof(PuloniaAccountItemViewModel.Uid) or null)
            OnPropertyChanged(nameof(TestSwitchHint));
    }

    /// <summary>账户增删或调整顺序后更新列表显示方式、页首说明与命令。</summary>
    private void OnAccountListChanged()
    {
        OnPropertyChanged(nameof(IsSingleAccount));
        OnPropertyChanged(nameof(ShowAccountList));
        OnPropertyChanged(nameof(HeaderText));
        NotifyDirtyChanged();
    }

    /// <summary>未保存状态变化后通知界面与命令。</summary>
    private void NotifyDirtyChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        NotifyCommands();
    }

    /// <summary>选中账户变化后更新命令，路线 CD 与首领领奖页签随之改为该账户，概况重新读取最近一次运行与指定的计划。</summary>
    partial void OnSelectedAccountChanged(PuloniaAccountItemViewModel? value)
    {
        NotifyCommands();
        SyncProgressAccount(value);
        _ = RefreshOverviewAsync();
    }

    /// <summary>忙碌状态变化后更新命令。</summary>
    partial void OnIsBusyChanged(bool value) => NotifyCommands();

    /// <summary>通知源生成命令重新计算可执行状态。</summary>
    private void NotifyCommands()
    {
        AddAccountCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        RenameAccountCommand.NotifyCanExecuteChanged();
        DeleteAccountCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
        NotifyGameCommands();
        NotifyAccountRecordsCommands();
    }
}
