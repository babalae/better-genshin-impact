using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BetterGenshinImpact.ViewModel.Pages.View;

/// <summary>
/// 单个目标连接测试的界面状态，用于决定结果文字的颜色。
/// </summary>
public enum ProxyConnectionTestState
{
    Idle,
    Running,
    Succeeded,
    Failed
}

/// <summary>
/// 设置页中「网络代理」分组的视图模型，负责代理地址草稿、保存校验与连接测试。
/// </summary>
public partial class NetworkSettingsViewModel : ObservableObject, IDisposable
{
    private readonly NetworkConfig _config;
    private readonly ProxyService _proxyService;
    private bool _updating;
    private bool _disposed;
    private bool _isAddressDirty;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private string _proxyUrl;

    [ObservableProperty]
    private string _validationError = string.Empty;

    [ObservableProperty]
    private string _githubTestResult = string.Empty;

    [ObservableProperty]
    private string _cnbTestResult = string.Empty;

    [ObservableProperty]
    private ProxyConnectionTestState _githubTestState;

    [ObservableProperty]
    private ProxyConnectionTestState _cnbTestState;

    [ObservableProperty]
    private bool _isTestRunning;

    public bool HasTestResult => !string.IsNullOrEmpty(GithubTestResult) || !string.IsNullOrEmpty(CnbTestResult);

    public NetworkSettingsViewModel(IConfigService configService, ProxyService proxyService)
    {
        _config = configService.Get().NetworkConfig;
        _proxyService = proxyService;
        _proxyUrl = _config.ProxyUrl;
        _enabled = _config.Enabled;
        _proxyService.SettingsChanged += OnSettingsChanged;
        I18nService.Instance.PropertyChanged += OnLanguageChanged;
        RefreshValidationState();
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_updating)
        {
            return;
        }

        if (value && !TryGetDraftAddress(out _))
        {
            _updating = true;
            Enabled = false;
            _updating = false;
            return;
        }

        if (value)
        {
            SaveAddress();
        }

        _config.Enabled = value;
        RefreshValidationState();
    }

    partial void OnProxyUrlChanged(string value)
    {
        _isAddressDirty = !string.Equals(value, _config.ProxyUrl, StringComparison.Ordinal);
    }

    partial void OnGithubTestResultChanged(string value) => OnPropertyChanged(nameof(HasTestResult));

    partial void OnCnbTestResultChanged(string value) => OnPropertyChanged(nameof(HasTestResult));

    [RelayCommand]
    private void SaveAddress()
    {
        if (!TryGetDraftAddress(out var address))
        {
            return;
        }

        ProxyUrl = ProxyService.FormatAddress(address!);
        _isAddressDirty = false;
        _config.ProxyUrl = ProxyUrl;
        RefreshValidationState();
    }

    private bool TryGetDraftAddress(out Uri? address)
    {
        var valid = ProxyService.TryParseAddress(ProxyUrl, out address);
        ValidationError = valid ? string.Empty : InvalidAddressMessage();
        return valid;
    }

    private static string InvalidAddressMessage() =>
        I18nService.Instance.Translate("代理地址无效，请填写带端口的 HTTP、HTTPS 或 SOCKS5 地址。");

    private void RefreshValidationState()
    {
        if (_proxyService.HasInvalidConfiguration)
        {
            ValidationError = InvalidAddressMessage();
        }
        else if (!_isAddressDirty)
        {
            ValidationError = string.Empty;
        }
    }

    /// <summary>
    /// 重新读取配置，保留用户尚未保存的地址草稿。
    /// </summary>
    public void RefreshFromConfig()
    {
        _updating = true;
        Enabled = _config.Enabled;
        if (!_isAddressDirty)
        {
            ProxyUrl = _config.ProxyUrl;
        }

        _updating = false;
        RefreshValidationState();
    }

    /// <summary>
    /// 离开设置页时取消仍在进行的连接测试。
    /// </summary>
    public void CancelPendingTest() => TestConnectionCommand.Cancel();

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (!TryGetDraftAddress(out var address))
        {
            return;
        }

        IsTestRunning = true;
        GithubTestState = CnbTestState = ProxyConnectionTestState.Running;
        GithubTestResult = CnbTestResult = I18nService.Instance.Translate("正在测试连接…");
        try
        {
            await Task.WhenAll(
                TestTargetAsync(address!, "https://api.github.com",
                    (text, state) => { GithubTestResult = text; GithubTestState = state; }, cancellationToken),
                TestTargetAsync(address!, "https://cnb.cool",
                    (text, state) => { CnbTestResult = text; CnbTestState = state; }, cancellationToken));
        }
        catch (Exception ex)
        {
            GithubTestResult = CnbTestResult = string.Format(I18nService.Instance.Translate("连接测试失败：{0}"), ex.Message);
            GithubTestState = CnbTestState = ProxyConnectionTestState.Failed;
        }
        finally
        {
            IsTestRunning = false;
        }
    }

    private static async Task TestTargetAsync(Uri address, string target,
        Action<string, ProxyConnectionTestState> update, CancellationToken cancellationToken)
    {
        var result = await ProxyConnectionTester.TestAsync(address, new Uri(target), cancellationToken);
        if (result.Canceled)
        {
            // 取消后不保留半截结果，界面只呈现成功或失败。
            update(string.Empty, ProxyConnectionTestState.Idle);
        }
        else if (result.TimedOut)
        {
            update(string.Format(I18nService.Instance.Translate("连接失败：{0}"),
                I18nService.Instance.Translate("请求超时")), ProxyConnectionTestState.Failed);
        }
        else if (result.StatusCode.HasValue)
        {
            update(string.Format(I18nService.Instance.Translate("连接成功（{0} ms）"), result.ElapsedMilliseconds),
                ProxyConnectionTestState.Succeeded);
        }
        else
        {
            update(string.Format(I18nService.Instance.Translate("连接失败：{0}"), result.Error),
                ProxyConnectionTestState.Failed);
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => RunOnUiThread(RefreshFromConfig);

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => RunOnUiThread(() =>
    {
        if (!string.IsNullOrEmpty(ValidationError))
        {
            ValidationError = InvalidAddressMessage();
        }
    });

    private void RunOnUiThread(Action action)
    {
        if (_disposed)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.InvokeAsync(() => { if (!_disposed) action(); });
        }
    }

    public void Dispose()
    {
        _disposed = true;
        CancelPendingTest();
        _proxyService.SettingsChanged -= OnSettingsChanged;
        I18nService.Instance.PropertyChanged -= OnLanguageChanged;
    }
}
