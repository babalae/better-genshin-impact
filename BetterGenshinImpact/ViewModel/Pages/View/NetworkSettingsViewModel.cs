using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Infrastructure.Network;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.View;

/// <summary>
/// 设置页“网络与代理”：代理模式、自定义代理地址与绕过列表、连接测试
/// </summary>
public partial class NetworkSettingsViewModel : ObservableObject
{
    /// <summary>
    /// 网络设置
    /// </summary>
    private readonly NetworkConfig _config;

    /// <summary>
    /// 连接测试
    /// </summary>
    private readonly NetworkDiagnostics _diagnostics;

    public NetworkSettingsViewModel(AllConfig config, NetworkDiagnostics diagnostics)
    {
        _config = config.NetworkConfig;
        _diagnostics = diagnostics;
        _proxyAddressText = _config.ProxyAddress;
        _bypassListText = _config.ProxyBypassList;
        if (IsCustomMode && !AppWebProxy.TryParseProxyAddress(_config.ProxyAddress, out _, out var error))
        {
            _proxyAddressError = error;
        }
    }

    /// <summary>
    /// 代理模式选项
    /// </summary>
    public IReadOnlyList<KeyValuePair<NetworkProxyMode, string>> ProxyModes { get; } =
    [
        new(NetworkProxyMode.System, "跟随系统代理"),
        new(NetworkProxyMode.None, "不使用代理"),
        new(NetworkProxyMode.Custom, "自定义代理"),
    ];

    /// <summary>
    /// 当前代理模式，修改后立即写入配置
    /// </summary>
    public NetworkProxyMode ProxyMode
    {
        get => _config.ProxyMode;
        set
        {
            if (_config.ProxyMode == value)
            {
                return;
            }

            _config.ProxyMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomMode));
            OnPropertyChanged(nameof(ModeText));
            OnPropertyChanged(nameof(ModeDescription));
            TestResults.Clear();
            if (value == NetworkProxyMode.Custom)
            {
                CommitProxyAddress();
            }
        }
    }

    /// <summary>
    /// 是否为自定义代理模式，决定是否显示地址和绕过列表
    /// </summary>
    public bool IsCustomMode => ProxyMode == NetworkProxyMode.Custom;

    /// <summary>
    /// 当前模式名称，显示在卡片标题右侧
    /// </summary>
    public string ModeText => ProxyModes.First(mode => mode.Key == ProxyMode).Value;

    /// <summary>
    /// 当前模式的说明
    /// </summary>
    public string ModeDescription => ProxyMode switch
    {
        NetworkProxyMode.None => "所有请求直接连接",
        NetworkProxyMode.Custom => "使用下方填写的代理地址",
        _ => "使用 Windows 设置中的代理",
    };

    /// <summary>
    /// 代理地址输入框的文字；校验通过后才写入配置
    /// </summary>
    [ObservableProperty]
    private string _proxyAddressText;

    /// <summary>
    /// 代理地址的错误提示；为 null 时不显示
    /// </summary>
    [ObservableProperty]
    private string? _proxyAddressError;

    /// <summary>
    /// 绕过列表输入框的文字
    /// </summary>
    [ObservableProperty]
    private string _bypassListText;

    /// <summary>
    /// 是否正在测试连接
    /// </summary>
    [ObservableProperty]
    private bool _isTesting;

    /// <summary>
    /// 连接测试结果
    /// </summary>
    public ObservableCollection<NetworkTestResult> TestResults { get; } = [];

    /// <summary>
    /// 提交代理地址：合法才写入配置；不合法时显示原因，配置保留上一次的合法值，避免输错一个字符就让所有请求失败
    /// </summary>
    [RelayCommand]
    private void CommitProxyAddress()
    {
        if (AppWebProxy.TryParseProxyAddress(ProxyAddressText, out var proxy, out var error))
        {
            _config.ProxyAddress = proxy!.ToString().TrimEnd('/');
            ProxyAddressText = _config.ProxyAddress;
            ProxyAddressError = null;
            return;
        }

        ProxyAddressError = string.IsNullOrWhiteSpace(ProxyAddressText) ? "请填写代理地址，填写前所有请求直接连接" : error;
    }

    /// <summary>
    /// 提交绕过列表
    /// </summary>
    [RelayCommand]
    private void CommitBypassList()
    {
        _config.ProxyBypassList = BypassListText.Trim();
    }

    /// <summary>
    /// 用当前设置测试连接；先提交输入框中尚未提交的修改
    /// </summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsCustomMode)
        {
            CommitProxyAddress();
            CommitBypassList();
        }

        IsTesting = true;
        TestResults.Clear();
        try
        {
            foreach (var result in await _diagnostics.TestAsync(CancellationToken.None))
            {
                TestResults.Add(result);
            }
        }
        finally
        {
            IsTesting = false;
        }
    }
}
