using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Service.ChildSession;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 桌面分身「诊断与修复」窗口的视图模型。
///
/// 面板只负责展示与触发：检查在 <see cref="ChildSessionEnvironmentCheck"/>，
/// 修复在 <see cref="ChildSessionEnvironmentFixer"/>，这里不直接碰注册表。
/// </summary>
public partial class ChildSessionDiagnosticsWindowViewModel : ObservableObject
{
    /// <summary>等待 termsrv.dll 校验出结论的上限。sfc 单文件校验通常 1~3 秒。</summary>
    private static readonly TimeSpan TermsrvWaitTimeout = TimeSpan.FromSeconds(20);

    /// <summary>上一次检测是否抛出了异常。失败时不要把空列表当成「一切正常」。</summary>
    private bool _checkFailed;

    /// <summary>是否已经安排了「等后台校验结束再刷一次」，避免重复挂等待。</summary>
    private bool _refreshAfterVerificationPending;

    /// <summary>
    /// 确认框是否开着。
    ///
    /// 模态对话框只是挡住输入，消息照泵：后台补刷会在这期间把列表清空重建，
    /// 用户点「是」之后修的是一条已经不在列表里的旧项。这里做个闸门，让补刷等一等。
    /// </summary>
    private bool _confirmDialogOpen;

    /// <summary>
    /// 补刷被确认框的超时闸门挡掉了，需要等框关掉后补一次。
    ///
    /// 只有这一种情况要补：其余时候补刷要么还在排队（框一关自己就走），
    /// 要么早就刷完了——无条件补刷会让结果区闪一下并把用户展开的行全折叠。
    /// </summary>
    private bool _refreshAbandonedByDialog;

    [ObservableProperty]
    private string _statusText = "正在检测…";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// 最近一次修复的结论。
    ///
    /// 单独占一行，不和 <see cref="StatusText"/> 抢：修完会立刻重新检测，
    /// 状态栏马上变成「检查完成：…」，修复失败的原因就再也看不到了。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastFixResult))]
    private string _lastFixResult = string.Empty;

    /// <summary>是否有可展示的修复结论。</summary>
    public bool HasLastFixResult => !string.IsNullOrWhiteSpace(LastFixResult);

    /// <summary>
    /// 是否还在检测。检测期间不展示任何结论：termsrv.dll 的校验在后台跑，
    /// 先按旧结论渲染会亮出一条「文件不一致」，等校验完成又撤掉，看起来像假警报。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResults))]
    private bool _isChecking = true;

    /// <summary>检测结束后才展示结论区域。</summary>
    public bool ShowResults => !IsChecking;

    /// <summary>需要处理的检查项（会阻塞或明显影响启动）。</summary>
    public ObservableCollection<ChildSessionDiagnosticItem> Warnings { get; } = [];

    /// <summary>建议处理的检查项（不影响启动，但会让修复失效或影响使用）。</summary>
    public ObservableCollection<ChildSessionDiagnosticItem> Notices { get; } = [];

    /// <summary>当前没有触发的检查项，附上实际值，便于区分「没问题」和「没检查」。</summary>
    public ObservableCollection<ChildSessionDiagnosticItem> NormalItems { get; } = [];

    /// <summary>是否有需要处理的项。</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>是否有建议处理的项。</summary>
    public bool HasNotices => Notices.Count > 0;

    /// <summary>是否有未触发的项。</summary>
    public bool HasNormalItems => NormalItems.Count > 0;

    /// <summary>是否一切正常（检测已结束且成功，没有任何需要处理或建议处理的项）。</summary>
    public bool IsAllGood => !IsChecking && !_checkFailed && Warnings.Count == 0 && Notices.Count == 0;

    partial void OnIsCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAllGood));
    }

    /// <summary>
    /// 构造函数：打开时先跑一次检查。
    /// </summary>
    public ChildSessionDiagnosticsWindowViewModel()
    {
        // 打开窗口走缓存路径：五分钟内的结论直接复用，不必每次开面板都重跑一趟 sfc。
        _ = RecheckCoreAsync(forceVerify: false);
    }

    /// <summary>
    /// 「重新检测」按钮：用户主动要求重查，缓存一律作废。
    ///
    /// 尤其是 termsrv.dll 的完整性结论会缓存 5 分钟——用户如果在别处把文件修好了，
    /// 不清缓存的话再点一次还是旧警告，还带着一个点了也修不动的「修复」按钮。
    /// </summary>
    [RelayCommand]
    private Task RecheckAsync() => RecheckCoreAsync(forceVerify: true);

    /// <summary>
    /// 重新检查一次环境。检测全部出结论后才把结果交给界面。
    /// </summary>
    /// <param name="forceVerify">
    /// true 表示用户主动点「重新检测」：先清缓存，强制重新校验 termsrv.dll。
    /// false 用于内部刷新（修复后、后台校验结束后）：那些时刻缓存刚被校验本身写过，
    /// 再清一次只会多跑一趟 sfc。
    /// </param>
    private async Task RecheckCoreAsync(bool forceVerify)
    {
        if (IsBusy)
        {
            return;
        }

        // 这次重检本身就是一次刷新：把「等框关了再补」的意图消费掉，免得它一直挂在那里。
        _refreshAbandonedByDialog = false;

        if (forceVerify)
        {
            ChildSessionEnvironmentCheck.InvalidateScanCache();
        }

        IsBusy = true;
        IsChecking = true;
        _checkFailed = false;
        StatusText = "正在检测…";

        try
        {
            // 检查本身是读注册表 + 扫任务，放到线程池，避免面板卡住。
            var issues = await Task.Run(() => ChildSessionEnvironmentCheck.CollectAll());

            // termsrv.dll 只能靠 sfc 校验，几秒钟才出结论。这里等它跑完再渲染：
            // 否则会先按上一次的结论亮一条「与系统组件库不一致」，等新结论回来又撤掉，
            // 用户看到的就是一次莫名其妙的假警报。等不到（超时或没提权）就按「未验证」渲染，
            // 由检查项自己决定不出结论，而不是拿旧值凑数。
            if (ChildSessionEnvironmentCheck.IsElevated())
            {
                if (!ChildSessionEnvironmentCheck.GetTermsrvState().Verified)
                {
                    var deadline = DateTime.UtcNow + TermsrvWaitTimeout;
                    var noticeShown = false;

                    while (ChildSessionEnvironmentCheck.IsTermsrvVerifying
                           && DateTime.UtcNow < deadline)
                    {
                        if (!noticeShown)
                        {
                            noticeShown = true;
                            StatusText = "正在校验系统组件库（termsrv.dll）…";
                        }

                        await Task.Delay(200);
                    }
                }

                // 不管有没有等过都重收一次：校验可能恰好在「收集之后、判断之前」完成，
                // 那份结论里就缺了完整性这一项，而这时界面已经没有机会再刷新了。
                issues = await Task.Run(() => ChildSessionEnvironmentCheck.CollectAll());
            }

            Warnings.Clear();
            Notices.Clear();
            NormalItems.Clear();

            var warningCount = 0;
            var noticeCount = 0;

            foreach (var issue in issues)
            {
                var item = new ChildSessionDiagnosticItem(issue);

                switch (issue.Severity)
                {
                    case ChildSessionIssueSeverity.Warning:
                        Warnings.Add(item);
                        warningCount++;
                        break;
                    case ChildSessionIssueSeverity.Notice:
                        Notices.Add(item);
                        noticeCount++;
                        break;
                    default:
                        NormalItems.Add(item);
                        break;
                }
            }

            OnPropertyChanged(nameof(HasWarnings));
            OnPropertyChanged(nameof(HasNotices));
            OnPropertyChanged(nameof(HasNormalItems));
            OnPropertyChanged(nameof(IsAllGood));

            var status = warningCount == 0 && noticeCount == 0
                ? "检查完成：没有发现问题。"
                : $"检查完成：{warningCount} 个需要处理，{noticeCount} 个建议处理。";

            // 没验证过 termsrv.dll 时别把话说满：非管理员跑 sfc 只会返回 1，
            // 这一项按「不下结论」处理——那就得在状态栏说清楚，别让用户以为全查过了。
            if (!ChildSessionEnvironmentCheck.GetTermsrvState().Verified)
            {
                status += ChildSessionEnvironmentCheck.IsElevated()
                    ? "（termsrv.dll 完整性本次未能校验，可重新检测）"
                    : "（未以管理员身份运行，termsrv.dll 完整性未校验）";
            }

            StatusText = status;

            // 等超时了、但后台校验还在跑：先照实渲染「暂时无法验证」，等它出结果再自动刷一次。
            // 否则那个「未验证」会一直挂着，除非用户自己想起来点「重新检测」。
            if (ChildSessionEnvironmentCheck.IsElevated()
                && ChildSessionEnvironmentCheck.IsTermsrvVerifying)
            {
                _ = RefreshAfterVerificationAsync();
            }
        }
        catch (Exception exception)
        {
            _checkFailed = true;
            OnPropertyChanged(nameof(IsAllGood));
            StatusText = $"检查失败：{exception.Message}";
        }
        finally
        {
            IsChecking = false;
            IsBusy = false;
        }
    }

    /// <summary>
    /// 后台校验结束后补刷一次结论。
    ///
    /// 面板最多等 <see cref="TermsrvWaitTimeout"/>，sfc 偶尔会超过；那之后校验结果不会再有人
    /// 渲染，面板上就留着一句「暂时无法验证」。这里等它跑完补一次，只补一次：
    /// 修复期间（IsBusy）不刷——修复流程结束时会自己重新检查；已经在检测也不重复进。
    /// </summary>
    private async Task RefreshAfterVerificationAsync()
    {
        if (_refreshAfterVerificationPending)
        {
            return;
        }

        _refreshAfterVerificationPending = true;

        try
        {
            // 上限比单次 sfc 的超时（3 分钟）短一点，卡住就不等了，别在后台一直转。
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
            while (ChildSessionEnvironmentCheck.IsTermsrvVerifying && DateTime.UtcNow < deadline)
            {
                // 这里没有 ConfigureAwait(false)：延时后要回到 UI 线程继续，才碰得到界面状态。
                await Task.Delay(500);
            }

            // 确认框开着就先别动列表：等它关掉再决定刷不刷。
            // 这也顺带覆盖了「用户正在确认修复时，补刷把这一项从列表里换掉」的情况。
            var dialogDeadline = DateTime.UtcNow + TimeSpan.FromMinutes(5);
            while (_confirmDialogOpen && DateTime.UtcNow < dialogDeadline)
            {
                await Task.Delay(200);
            }

            if (_confirmDialogOpen)
            {
                // 等超时了框还开着：这次补刷直接放弃。宁可不刷，也不能在用户确认期间把列表换掉。
                // 关框之后要么走修复流程（它自己会重检），要么由取消分支补这一次。
                _refreshAbandonedByDialog = true;
                return;
            }

            if (ChildSessionEnvironmentCheck.IsTermsrvVerifying || IsBusy || IsChecking)
            {
                return;
            }

            // 校验刚写进缓存，这里再清一次只会立刻又跑一遍 sfc。
            await RecheckCoreAsync(forceVerify: false);
        }
        finally
        {
            _refreshAfterVerificationPending = false;
        }
    }

    /// <summary>
    /// 修复一条检查项，完成后自动重新检查。
    /// </summary>
    /// <param name="item">要修复的项。</param>
    [RelayCommand]
    private async Task FixAsync(ChildSessionDiagnosticItem? item)
    {
        if (item is null || item.IsFixing || IsBusy)
        {
            return;
        }

        var confirm = MessageBoxResult.No;
        _confirmDialogOpen = true;
        try
        {
            confirm = ThemedMessageBox.Question(
                $"即将修复：{item.Title}{Environment.NewLine}{Environment.NewLine}{item.FixHint}"
                + $"{Environment.NewLine}{Environment.NewLine}是否继续？",
                "确认修复",
                MessageBoxButton.YesNo,
                MessageBoxResult.No);
        }
        finally
        {
            _confirmDialogOpen = false;
        }

        if (confirm != MessageBoxResult.Yes)
        {
            // 只在补刷确实被框挡掉（超时放弃）时才补。排队中的那轮框一关自己就会继续，
            // 无条件补反而会让结果区闪一下、把用户展开的行全折叠回默认状态。
            // 不再要求「校验已经结束」：补刷自己会等 sfc 出结论再渲染。
            // 否则校验还在跑时会跳过，标志留着没人消费，面板就一直是「未验证」。
            if (_refreshAbandonedByDialog
                && ChildSessionEnvironmentCheck.IsElevated()
                && !IsBusy)
            {
                _refreshAbandonedByDialog = false;
                _ = RefreshAfterVerificationAsync();
            }

            return;
        }

        // 用户确认修复：结束时会自己重检，别再补一次。
        _refreshAbandonedByDialog = false;

        item.IsFixing = true;

        // 修复期间禁掉「重新检测」：两者并发会互相覆盖对方的结论。
        IsBusy = true;
        StatusText = $"正在修复：{item.Title}…";

        try
        {
            var result = await Task.Run(() => ChildSessionEnvironmentFixer.Fix(item.Id));
            StatusText = result.Message;
            LastFixResult = result.Message;

            // 只在成功时作废缓存。失败分支里有一类是「已在新窗口启动 sfc /scannow」：
            // 那时清缓存会让下面的重检立刻起一个 sfc /verifyfile，两个 sfc 撞在一起，
            // 而 SfcGate 只管得住我们自己起的进程，管不到那个独立窗口。
            if (result.Success)
            {
                ChildSessionEnvironmentCheck.InvalidateScanCache();
            }
        }
        catch (Exception exception)
        {
            StatusText = $"修复失败：{exception.Message}";
            LastFixResult = StatusText;
        }
        finally
        {
            item.IsFixing = false;
            IsBusy = false;
        }

        // 修复结果落在系统里，重新读一遍比「改本地列表」更可信。
        // 缓存刚才已作废，这里不必再清一次。
        await RecheckCoreAsync(forceVerify: false);
    }

    /// <summary>
    /// 把当前环境结论复制到剪贴板，便于反馈问题时贴给开发者。
    /// </summary>
    [RelayCommand]
    private void CopyEnvironment()
    {
        var builder = new StringBuilder();
        builder.AppendLine("桌面分身环境检查");
        builder.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"管理员：{ChildSessionEnvironmentCheck.IsElevated()}");
        builder.AppendLine();

        AppendSection(builder, "需要处理", Warnings);
        AppendSection(builder, "建议处理", Notices);
        AppendSection(builder, "未触发", NormalItems);

        try
        {
            Clipboard.SetText(builder.ToString());
            StatusText = "环境信息已复制到剪贴板。";
        }
        catch (Exception exception)
        {
            StatusText = $"复制失败：{exception.Message}";
        }
    }

    /// <summary>
    /// 按分区把结论写进文本。
    /// </summary>
    private static void AppendSection(
        StringBuilder builder,
        string title,
        ObservableCollection<ChildSessionDiagnosticItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        builder.AppendLine($"【{title}】");
        foreach (var item in items)
        {
            builder.AppendLine($"- {item.Title}：{item.Summary}");
        }

        builder.AppendLine();
    }
}

/// <summary>
/// 面板里的一行检查项。
/// </summary>
public partial class ChildSessionDiagnosticItem : ObservableObject
{
    /// <summary>
    /// 用一条检查结论构造。
    /// </summary>
    /// <param name="issue">检查结论。</param>
    internal ChildSessionDiagnosticItem(ChildSessionEnvironmentCheck.Issue issue)
    {
        Id = issue.Id;
        Title = issue.Title;
        Summary = issue.Summary;
        Detail = issue.Detail;
        CanAutoFix = issue.CanAutoFix;
        Severity = issue.Severity;

        Icon = issue.Severity switch
        {
            ChildSessionIssueSeverity.Warning => SymbolRegular.ErrorCircle24,
            ChildSessionIssueSeverity.Notice => SymbolRegular.Info24,
            _ => SymbolRegular.CheckmarkCircle24
        };
    }

    /// <summary>检查项标识。</summary>
    public string Id { get; }

    /// <summary>标题。</summary>
    public string Title { get; }

    /// <summary>一句话结论。</summary>
    public string Summary { get; }

    /// <summary>展开后的完整说明。</summary>
    public string Detail { get; }

    /// <summary>是否提供一键修复。</summary>
    public bool CanAutoFix { get; }

    /// <summary>严重程度。</summary>
    public ChildSessionIssueSeverity Severity { get; }

    /// <summary>列表左侧的图标。</summary>
    public SymbolRegular Icon { get; }

    /// <summary>修复前给用户的提示语（不同项的副作用不同）。</summary>
    public string FixHint => Id switch
    {
        "rdp-disabled" => "这会打开本机的远程桌面监听并重启远程桌面服务。",
        "rdp-wrapper" => "这会把 TermService 还原成系统原生的 termsrv.dll，并清理相关开机任务；"
                         + "还原后多用户并发远程桌面的能力会失效。",
        "termservice-disabled" => "这会把远程桌面服务的启动类型从「禁用」改回「手动」并启动它。",
        "termservice-stopped" => "这会启动远程桌面服务。",
        "termservice-dll-type" => "这会把 ServiceDll 写回原生路径与正确类型，然后重启服务。",
        "termservice-key-broken" => "这会恢复 TermService 服务键的默认值（依赖只追加），然后重启服务。",
        "hello-only" => "这会关闭「仅允许 Windows Hello 登录」。改完需要你自己注销或重启才会生效。",
        "rdp-tool-persistence" => "这会导出备份后删除这些开机任务与自启项。",
        "termsrv-dll-tampered" => "这会先用工具留下的备份还原 termsrv.dll（还原后会复核，不通过自动回滚）；"
                                  + "没有可用备份时，在服务停止的状态下用 sfc 只修这一个文件，几秒到几十秒、"
                                  + "当场生效、不需要重启；连它都修不好才会弹出命令行窗口跑完整扫描（5~15 分钟）。",
        _ => "这会修改系统设置。"
    };

    /// <summary>是否正在修复（用于禁用按钮）。</summary>
    [ObservableProperty]
    private bool _isFixing;
}
