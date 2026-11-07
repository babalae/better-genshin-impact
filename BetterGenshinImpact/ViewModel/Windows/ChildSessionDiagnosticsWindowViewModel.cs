using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _pendingRefreshTimer;

    [ObservableProperty]
    private string _statusText = "正在检查…";

    [ObservableProperty]
    private bool _isBusy;

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

    /// <summary>是否一切正常（没有任何需要处理或建议处理的项）。</summary>
    public bool IsAllGood => Warnings.Count == 0 && Notices.Count == 0;

    /// <summary>
    /// 构造函数：打开时先跑一次检查。termsrv.dll 的 sfc 校验在后台进行，
    /// 所以这里再安排一次延迟刷新，把校验结果取回来。
    /// </summary>
    public ChildSessionDiagnosticsWindowViewModel()
    {
        _pendingRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _pendingRefreshTimer.Tick += OnPendingRefreshTick;

        _ = RecheckAsync();
    }

    /// <summary>
    /// 重新检查一次环境。
    /// </summary>
    [RelayCommand]
    private async Task RecheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在检查…";

        try
        {
            // 检查本身是读注册表 + 扫任务，放到线程池，避免面板卡住。
            var issues = await Task.Run(() => ChildSessionEnvironmentCheck.CollectAll());

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

            StatusText = warningCount == 0 && noticeCount == 0
                ? "检查完成：没有发现问题。"
                : $"检查完成：{warningCount} 个需要处理，{noticeCount} 个建议处理。";
        }
        catch (Exception exception)
        {
            StatusText = $"检查失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        // termsrv.dll 的校验在后台跑，稍后再取一次结果。
        if (ChildSessionEnvironmentCheck.GetTermsrvState().Verified == false)
        {
            _pendingRefreshTimer.Stop();
            _pendingRefreshTimer.Start();
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

        var confirm = ThemedMessageBox.Question(
            $"即将修复：{item.Title}{Environment.NewLine}{Environment.NewLine}{item.FixHint}"
            + $"{Environment.NewLine}{Environment.NewLine}是否继续？",
            "确认修复",
            MessageBoxButton.YesNo,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        item.IsFixing = true;
        StatusText = $"正在修复：{item.Title}…";

        try
        {
            var result = await Task.Run(() => ChildSessionEnvironmentFixer.Fix(item.Id));
            StatusText = result.Message;

            if (result.Success)
            {
                ChildSessionEnvironmentCheck.InvalidateScanCache();
            }
        }
        catch (Exception exception)
        {
            StatusText = $"修复失败：{exception.Message}";
        }
        finally
        {
            item.IsFixing = false;
        }

        // 修复结果落在系统里，重新读一遍比「改本地列表」更可信。
        IsBusy = false;
        await RecheckAsync();
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

    /// <summary>
    /// 延迟刷新：只用来把 termsrv.dll 的后台校验结果取回来，跑一次就停。
    /// </summary>
    private void OnPendingRefreshTick(object? sender, EventArgs e)
    {
        _pendingRefreshTimer.Stop();
        if (ChildSessionEnvironmentCheck.GetTermsrvState().Verified)
        {
            _ = RecheckAsync();
        }
        else
        {
            _pendingRefreshTimer.Start();
        }
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
                                  + "没有可用备份时运行 sfc /scannow，可能需要 5~15 分钟。",
        _ => "这会修改系统设置。"
    };

    /// <summary>是否正在修复（用于禁用按钮）。</summary>
    [ObservableProperty]
    private bool _isFixing;
}
