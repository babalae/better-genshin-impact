using System;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 背景跟随主题的消息对话框，兼容旧 MessageBox API
/// </summary>
public partial class ThemedMessageBox : FluentWindow
{
    private TaskCompletionSource<MessageBoxResult>? _taskCompletionSource;
    private MessageBoxButton _buttonType;

    /// <summary>
    /// 消息框图标类型
    /// </summary>
    public enum MessageBoxIcon
    {
        None,
        Information,
        Warning,
        Error,
        Question,
        Success
    }

    /// <summary>
    /// 初始化自定义消息对话框
    /// </summary>
    private ThemedMessageBox()
    {
        InitializeComponent();

        // 注册事件
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowHelper.TryApplySystemBackdrop(this);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // 根据按钮类型返回正确的关闭结果
        var result = _buttonType switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.None
        };
        _taskCompletionSource?.TrySetResult(result);
    }

    /// <summary>
    /// 显示对话框并等待结果
    /// </summary>
    public Task<MessageBoxResult> ShowDialogAsync()
    {
        _taskCompletionSource = new TaskCompletionSource<MessageBoxResult>();
        ShowDialog();
        return _taskCompletionSource.Task;
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        // 根据按钮类型返回正确的主按钮结果
        var result = _buttonType switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => MessageBoxResult.OK,
            MessageBoxButton.YesNo => MessageBoxResult.Yes,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
            _ => MessageBoxResult.OK
        };
        _taskCompletionSource?.TrySetResult(result);
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        // 根据按钮类型返回正确的次按钮结果
        var result = _buttonType switch
        {
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.No,
            _ => MessageBoxResult.Cancel
        };
        _taskCompletionSource?.TrySetResult(result);
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // 关闭按钮仅在 YesNoCancel 时显示，始终返回 Cancel
        _taskCompletionSource?.TrySetResult(MessageBoxResult.Cancel);
        Close();
    }

    /// <summary>
    /// 显示自定义消息框
    /// </summary>
    /// <param name="content">消息内容</param>
    /// <param name="title">标题</param>
    /// <param name="button">按钮类型</param>
    /// <param name="icon">图标类型</param>
    /// <param name="defaultResult">默认结果</param>
    /// <param name="owner">父窗口</param>
    /// <returns>用户选择的结果</returns>
    public static async Task<MessageBoxResult> ShowAsync(
        string content,
        string title = "提示",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxIcon icon = MessageBoxIcon.Information,
        MessageBoxResult defaultResult = MessageBoxResult.None,
        Window? owner = null)
    {
        var messageBox = new ThemedMessageBox
        {
            Title = title,
            Owner = owner ?? Application.Current.MainWindow
        };

        // 设置消息内容
        messageBox.MessageTextBlock.Text = content;

        // 设置图标
        SetIcon(messageBox, icon);

        // 设置按钮并保存按钮类型
        messageBox._buttonType = button;
        SetButtons(messageBox, button);
        var result = await messageBox.ShowDialogAsync();
        if (result == MessageBoxResult.None)
        {
            return defaultResult;
        }
        return result;
    }

    public static MessageBoxResult Show(string message, string title, MessageBoxButton button, MessageBoxIcon icon, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return ShowAsync(message, title, button, icon, defaultResult).GetAwaiter().GetResult();
        }
        else
        {
            return Application.Current.Dispatcher.Invoke(() =>
                ShowAsync(message, title, button, icon, defaultResult).GetAwaiter().GetResult());
        }
    }

    /// <summary>
    /// 设置图标
    /// </summary>
    private static void SetIcon(ThemedMessageBox messageBox, MessageBoxIcon icon)
    {
        if (icon == MessageBoxIcon.None) { messageBox.MessageIcon.Visibility = Visibility.Collapsed; return; }
        messageBox.MessageIcon.Symbol = icon switch
        {
            MessageBoxIcon.Information => SymbolRegular.Info24,
            MessageBoxIcon.Warning => SymbolRegular.Warning24,
            MessageBoxIcon.Error => SymbolRegular.ErrorCircle24,
            MessageBoxIcon.Question => SymbolRegular.QuestionCircle24,
            MessageBoxIcon.Success => SymbolRegular.CheckmarkCircle24,
            _ => SymbolRegular.Info24
        };
        var colorKey = icon switch
        {
            MessageBoxIcon.Warning => "SystemFillColorAttentionBrush",
            MessageBoxIcon.Error => "SystemFillColorCriticalBrush",
            MessageBoxIcon.Success => "SystemFillColorSuccessBrush",
            _ => "SystemFillColorCautionBrush"
        };
        messageBox.MessageIcon.Foreground = (System.Windows.Media.Brush)Application.Current.Resources[colorKey];
    }

    private static void SetButtons(ThemedMessageBox messageBox, MessageBoxButton button)
    {
        switch (button)
        {
            case MessageBoxButton.OK:
                messageBox.PrimaryButton.Content = "确定";
                messageBox.PrimaryButton.Visibility = Visibility.Visible;
                break;

            case MessageBoxButton.OKCancel:
                messageBox.PrimaryButton.Content = "确定";
                messageBox.PrimaryButton.Visibility = Visibility.Visible;
                messageBox.SecondaryButton.Content = "取消";
                messageBox.SecondaryButton.Visibility = Visibility.Visible;
                break;

            case MessageBoxButton.YesNo:
                messageBox.PrimaryButton.Content = "是";
                messageBox.PrimaryButton.Visibility = Visibility.Visible;
                messageBox.SecondaryButton.Content = "否";
                messageBox.SecondaryButton.Visibility = Visibility.Visible;
                break;

            case MessageBoxButton.YesNoCancel:
                messageBox.PrimaryButton.Content = "是";
                messageBox.PrimaryButton.Visibility = Visibility.Visible;
                messageBox.SecondaryButton.Content = "否";
                messageBox.SecondaryButton.Visibility = Visibility.Visible;
                messageBox.CloseButton.Content = "取消";
                messageBox.CloseButton.Visibility = Visibility.Visible;
                break;
        }
    }

    #region Error 方法

    public static void Error(string message, string title = "错误") =>
        Application.Current.Dispatcher.InvokeAsync(async () => await ShowAsync(message, title, MessageBoxButton.OK, MessageBoxIcon.Error));

    public static MessageBoxResult Error(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return ShowAsync(message, title, button, MessageBoxIcon.Error, defaultResult).GetAwaiter().GetResult();
        }
        else
        {
            return Application.Current.Dispatcher.Invoke(() =>
                ShowAsync(message, title, button, MessageBoxIcon.Error, defaultResult).GetAwaiter().GetResult());
        }
    }

    public static Task<MessageBoxResult> ErrorAsync(string message, string title = "错误") =>
        ShowAsync(message, title, MessageBoxButton.OK, MessageBoxIcon.Error);

    public static Task<MessageBoxResult> ErrorAsync(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowAsync(message, title, button, MessageBoxIcon.Error, defaultResult);

    #endregion

    #region Warning 方法

    public static void Warning(string message, string title = "警告") =>
        Application.Current.Dispatcher.InvokeAsync(async () => await ShowAsync(message, title, MessageBoxButton.OK, MessageBoxIcon.Warning));

    public static MessageBoxResult Warning(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return ShowAsync(message, title, button, MessageBoxIcon.Warning, defaultResult).GetAwaiter().GetResult();
        }
        else
        {
            return Application.Current.Dispatcher.Invoke(() =>
                ShowAsync(message, title, button, MessageBoxIcon.Warning, defaultResult).GetAwaiter().GetResult());
        }
    }

    public static Task<MessageBoxResult> WarningAsync(string message, string title = "警告") =>
        ShowAsync(message, title, MessageBoxButton.OK, MessageBoxIcon.Warning);

    public static Task<MessageBoxResult> WarningAsync(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowAsync(message, title, button, MessageBoxIcon.Warning, defaultResult);

    #endregion

    #region Information 方法

    public static void Information(string message, string title = "信息") =>
        Application.Current.Dispatcher.InvokeAsync(async () => await ShowAsync(message, title, MessageBoxButton.OK, MessageBoxIcon.Information));

    public static MessageBoxResult Information(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return ShowAsync(message, title, button, MessageBoxIcon.Information, defaultResult).GetAwaiter().GetResult();
        }
        else
        {
            return Application.Current.Dispatcher.Invoke(() =>
                ShowAsync(message, title, button, MessageBoxIcon.Information, defaultResult).GetAwaiter().GetResult());
        }
    }

    public static Task<MessageBoxResult> InformationAsync(string message, string title = "信息") =>
        ShowAsync(message, title, MessageBoxButton.OK, MessageBoxIcon.Information);

    public static Task<MessageBoxResult> InformationAsync(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowAsync(message, title, button, MessageBoxIcon.Information, defaultResult);

    #endregion

    #region Success 方法

    public static void Success(string message, string title = "成功") =>
        Application.Current.Dispatcher.InvokeAsync(async () => await ShowAsync(message, title, MessageBoxButton.OK, MessageBoxIcon.Success));

    public static MessageBoxResult Success(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return ShowAsync(message, title, button, MessageBoxIcon.Success, defaultResult).GetAwaiter().GetResult();
        }
        else
        {
            return Application.Current.Dispatcher.Invoke(() =>
                ShowAsync(message, title, button, MessageBoxIcon.Success, defaultResult).GetAwaiter().GetResult());
        }
    }

    public static Task<MessageBoxResult> SuccessAsync(string message, string title = "成功") =>
        ShowAsync(message, title, MessageBoxButton.OK, MessageBoxIcon.Success);

    public static Task<MessageBoxResult> SuccessAsync(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowAsync(message, title, button, MessageBoxIcon.Success, defaultResult);

    #endregion

    #region Question 方法

    public static MessageBoxResult Question(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return ShowAsync(message, title, button, MessageBoxIcon.Question, defaultResult).GetAwaiter().GetResult();
        }
        else
        {
            return Application.Current.Dispatcher.Invoke(() =>
                ShowAsync(message, title, button, MessageBoxIcon.Question, defaultResult).GetAwaiter().GetResult());
        }
    }

    public static Task<MessageBoxResult> QuestionAsync(string message, string title = "确认") =>
        ShowAsync(message, title, MessageBoxButton.YesNo, MessageBoxIcon.Question);

    public static Task<MessageBoxResult> QuestionAsync(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowAsync(message, title, button, MessageBoxIcon.Question, defaultResult);

    #endregion
}
