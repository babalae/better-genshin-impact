using System;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Service.ChildSession;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 桌面分身自动登录设置。
///
/// 开关是「点一下就生效」的，没有单独保存按钮；密码另有自己的保存动作，
/// 因为它要先用系统的登录接口校验一次，校验失败不该静默落盘。
/// 账户固定为当前 Windows 账户——桌面分身只支持当前账户登录。
/// </summary>
public partial class ChildSessionAutoLoginWindow : FluentWindow
{
    private readonly ChildSessionService _childSessionService;
    private bool _loading;
    private int _verifyRequestId;

    public ChildSessionAutoLoginWindow(ChildSessionService childSessionService)
    {
        _childSessionService = childSessionService;
        InitializeComponent();

        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        Closed += (_, _) => _verifyRequestId++;

        _loading = true;
        AccountText.Text = $"当前账户：{_childSessionService.AutoLoginAccountName}";
        AutoLoginSwitch.IsChecked = _childSessionService.AutoLoginEnabled;
        _loading = false;

        RefreshPasswordState();
    }

    private void OnAutoLoginSwitchChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var enabled = AutoLoginSwitch.IsChecked == true;
        try
        {
            _childSessionService.AutoLoginEnabled = enabled;

            if (enabled && !_childSessionService.HasStoredAutoLoginPassword)
            {
                ShowStatus("自动登录已开启，但还没有保存密码，连接时仍会要求输入。", InfoBarSeverity.Warning);
                return;
            }

            ShowStatus(
                enabled ? "已开启自动登录。" : "已关闭自动登录。",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus(exception.GetBaseException().Message, InfoBarSeverity.Error);
            RestoreSwitchState();
        }
    }

    private async void OnSavePasswordClicked(object sender, RoutedEventArgs e)
    {
        var password = PasswordInput.Password;
        if (string.IsNullOrEmpty(password))
        {
            ShowStatus("请输入要保存的 Windows 登录密码。", InfoBarSeverity.Error);
            return;
        }

        // 校验走 LogonUser，微软账户还要联网比对，可能要一两秒。
        // 放到工作线程上跑，否则整个窗口（以及同一 UI 线程上的主窗口）都会卡住。
        SaveButton.IsEnabled = false;
        ClearButton.IsEnabled = false;
        ShowStatus("正在校验密码…", InfoBarSeverity.Informational);

        // 校验是异步的：期间窗口可能被关掉、或者用户又点了别的操作。
        // 记一个请求号，回来时对不上就整段丢弃，避免用旧结果去改当前的设置。
        var requestId = ++_verifyRequestId;

        try
        {
            var (result, errorCode) = await Task.Run(() =>
            {
                var outcome = _childSessionService.VerifyAutoLoginPassword(password, out var code);
                return (outcome, code);
            });

            if (requestId != _verifyRequestId || !IsLoaded)
            {
                return;
            }

            if (result != ChildSessionAutoLoginService.PasswordCheckResult.Valid)
            {
                ShowStatus(DescribeVerifyFailure(result, errorCode), InfoBarSeverity.Error);
                return;
            }

            if (!_childSessionService.SaveAutoLoginPassword(password))
            {
                ShowStatus("密码加密失败，未保存。请确认 BetterGI 以当前 Windows 账户运行。", InfoBarSeverity.Error);
                return;
            }

            PasswordInput.Clear();

            // 按开关此刻的状态落定：等待校验期间用户可能已经把开关关掉了，
            // 这时候不能再替他打开。
            _childSessionService.AutoLoginEnabled = AutoLoginSwitch.IsChecked == true;
            RestoreSwitchState();

            RefreshPasswordState();

            // 开关没开时别说「会自动填写」：密码存下来了，但下次连接不会用它。
            ShowStatus(
                _childSessionService.AutoLoginEnabled
                    ? "已保存。下次连接桌面分身时会自动填写凭据。"
                    : "密码已保存。打开「启动自动登录」后才会自动填写。",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus(exception.GetBaseException().Message, InfoBarSeverity.Error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
            RefreshPasswordState();
        }
    }

    private void OnClearPasswordClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _childSessionService.ClearAutoLoginPassword();
            PasswordInput.Clear();
            RefreshPasswordState();
            ShowStatus("已清除当前账户保存的密码。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus(exception.GetBaseException().Message, InfoBarSeverity.Error);
        }
    }

    private void RestoreSwitchState()
    {
        _loading = true;
        AutoLoginSwitch.IsChecked = _childSessionService.AutoLoginEnabled;
        _loading = false;
    }

    private void RefreshPasswordState()
    {
        var hasPassword = _childSessionService.HasStoredAutoLoginPassword;

        // 一行里把两件事说完：校验方式 + 当前账户有没有存过密码。
        PasswordHintText.Text =
            "保存前会先用系统的登录接口校验一次。当前账户"
            + (hasPassword ? "已保存密码。" : "还没有保存密码。");

        ClearButton.IsEnabled = hasPassword;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private static string DescribeVerifyFailure(
        ChildSessionAutoLoginService.PasswordCheckResult result,
        int errorCode)
    {
        return result switch
        {
            ChildSessionAutoLoginService.PasswordCheckResult.InvalidPassword =>
                "密码不正确，请确认输入的是当前 Windows 账户的密码。",
            ChildSessionAutoLoginService.PasswordCheckResult.AccountLocked =>
                "账户已被锁定（连续输入错误次数过多），请等锁定期结束后再试，或请管理员解锁。",
            ChildSessionAutoLoginService.PasswordCheckResult.AccountDisabled =>
                "账户已被禁用，无法使用密码登录。",
            ChildSessionAutoLoginService.PasswordCheckResult.PasswordExpired =>
                "密码已过期，请先修改密码。",
            ChildSessionAutoLoginService.PasswordCheckResult.PasswordMustChange =>
                "系统要求先修改密码才能登录，请先修改密码。",
            ChildSessionAutoLoginService.PasswordCheckResult.AccountRestricted =>
                "账户受到登录限制（例如空密码、登录时段限制），无法直接使用密码登录。",
            _ => $"无法校验密码（错误码 {errorCode}）。请确认 BetterGI 以管理员权限运行。"
        };
    }
}
