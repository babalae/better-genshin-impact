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
    private MessageBoxResult _result;
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
    /// 初始化主题色消息对话框
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
        // 如果没有明确设置结果，根据按钮类型返回默认的关闭结果
        if (_result == MessageBoxResult.None)
        {
            _result = _buttonType switch
            {
                MessageBoxButton.OK => MessageBoxResult.OK,
                MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
                MessageBoxButton.YesNo => MessageBoxResult.No,
                MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
                _ => MessageBoxResult.None
            };
        }
    }

    /// <summary>
    /// 显示对话框并返回结果
    /// </summary>
    private MessageBoxResult ShowDialogWithResult()
    {
        _result = MessageBoxResult.None;
        ShowDialog();
        return _result;
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        // 根据按钮类型返回正确的主按钮结果
        _result = _buttonType switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => MessageBoxResult.OK,
            MessageBoxButton.YesNo => MessageBoxResult.Yes,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
            _ => MessageBoxResult.OK
        };
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        // 根据按钮类型返回正确的次按钮结果
        _result = _buttonType switch
        {
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.No,
            _ => MessageBoxResult.Cancel
        };
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // 关闭按钮仅在 YesNoCancel 时显示，始终返回 Cancel
        _result = MessageBoxResult.Cancel;
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
    /// <remarks>
    /// 此方法必须在 UI 线程上调用。它会阻塞调用线程直到用户关闭对话框。
    /// 对话框使用 ShowDialog() 显示，内部会创建嵌套消息循环来处理用户交互。
    /// 如果需要从非 UI 线程调用，请使用 ShowAsync 方法。
    /// </remarks>
    public static MessageBoxResult Show(
        string content,
        string title = "提示",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxIcon icon = MessageBoxIcon.Information,
        MessageBoxResult defaultResult = MessageBoxResult.None,
        Window? owner = null)
    {
        var messageBox = new ThemedMessageBox
        {
            Title = title
        };

        // 设置父窗口，需要防止将自己设置为父窗口，以及处理 MainWindow 未初始化的情况
        if (owner != null && owner != messageBox)
        {
            messageBox.Owner = owner;
        }
        else if (Application.Current?.MainWindow != null && Application.Current.MainWindow != messageBox)
        {
            messageBox.Owner = Application.Current.MainWindow;
        }

        // 设置消息内容
        messageBox.MessageTextBlock.Text = content;

        // 设置图标
        SetIcon(messageBox, icon);

        // 设置按钮并保存按钮类型
        messageBox._buttonType = button;
        SetButtons(messageBox, button);

        var result = messageBox.ShowDialogWithResult();
        return result == MessageBoxResult.None ? defaultResult : result;
    }

    /// <summary>
    /// 异步显示主题色消息框
    /// </summary>
    /// <param name="content">消息内容</param>
    /// <param name="title">标题</param>
    /// <param name="button">按钮类型</param>
    /// <param name="icon">图标类型</param>
    /// <param name="defaultResult">默认结果</param>
    /// <param name="owner">父窗口</param>
    /// <returns>用户选择结果的 Task</returns>
    /// <remarks>
    /// 此方法可以从任何线程安全调用。它会将对话框的显示调度到 UI 线程，
    /// 并返回一个 Task 以便调用者可以 await 等待用户响应。
    /// 推荐在异步上下文中使用此方法以避免阻塞调用线程。
    /// </remarks>
    public static Task<MessageBoxResult> ShowAsync(
        string content,
        string title = "提示",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxIcon icon = MessageBoxIcon.Information,
        MessageBoxResult defaultResult = MessageBoxResult.None,
        Window? owner = null)
    {
        return Application.Current.Dispatcher.InvokeAsync(() =>
            Show(content, title, button, icon, defaultResult, owner)).Task;
    }

    /// <summary>
    /// 设置图标
    /// </summary>
    private static void SetIcon(ThemedMessageBox messageBox, MessageBoxIcon icon)
    {
        if (icon == MessageBoxIcon.None)
        {
            messageBox.MessageIcon.Visibility = Visibility.Collapsed;
            messageBox.TitleBar.Icon = null;
            return;
        }

        var symbol = icon switch
        {
            MessageBoxIcon.Information => SymbolRegular.Info24,
            MessageBoxIcon.Warning => SymbolRegular.Warning24,
            MessageBoxIcon.Error => SymbolRegular.ErrorCircle24,
            MessageBoxIcon.Question => SymbolRegular.QuestionCircle24,
            MessageBoxIcon.Success => SymbolRegular.CheckmarkCircle24,
            _ => SymbolRegular.Info24
        };

        messageBox.MessageIcon.Symbol = symbol;
        messageBox.TitleBar.Icon = new SymbolIcon(symbol);

        var colorKey = icon switch
        {
            MessageBoxIcon.Information => "SystemFillColorAttentionBrush",
            MessageBoxIcon.Warning => "SystemFillColorCautionBrush",
            MessageBoxIcon.Error => "SystemFillColorCriticalBrush",
            MessageBoxIcon.Question => "SystemFillColorNeutralBrush",
            MessageBoxIcon.Success => "SystemFillColorSuccessBrush",
            _ => "SystemFillColorAttentionBrush"
        };

        if (Application.Current != null)
        {
            var brush = Application.Current.TryFindResource(colorKey) as System.Windows.Media.Brush;
            if (brush != null)
            {
                messageBox.MessageIcon.Foreground = brush;
                messageBox.TitleBar.Icon.Foreground = brush;
            }
        }
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

    /// <summary>
    /// 显示错误消息框（同步，阻塞调用）
    /// </summary>
    public static void Error(string message, string title = "错误") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxIcon.Error);

    public static MessageBoxResult Error(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Show(message, title, button, MessageBoxIcon.Error, defaultResult);

    public static Task<MessageBoxResult> ErrorAsync(string message, string title = "错误") =>
        ShowAsync(message, title, MessageBoxButton.OK, MessageBoxIcon.Error);

    public static Task<MessageBoxResult> ErrorAsync(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowAsync(message, title, button, MessageBoxIcon.Error, defaultResult);

    #endregion

    #region Warning 方法

    /// <summary>
    /// 显示警告消息框（同步，阻塞调用）
    /// </summary>
    public static void Warning(string message, string title = "警告") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxIcon.Warning);

    public static MessageBoxResult Warning(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Show(message, title, button, MessageBoxIcon.Warning, defaultResult);

    public static Task<MessageBoxResult> WarningAsync(string message, string title = "警告") =>
        ShowAsync(message, title, MessageBoxButton.OK, MessageBoxIcon.Warning);

    public static Task<MessageBoxResult> WarningAsync(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowAsync(message, title, button, MessageBoxIcon.Warning, defaultResult);

    #endregion

    #region Information 方法

    /// <summary>
    /// 显示信息消息框（同步，阻塞调用）
    /// </summary>
    public static void Information(string message, string title = "信息") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxIcon.Information);

    public static MessageBoxResult Information(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Show(message, title, button, MessageBoxIcon.Information, defaultResult);

    public static Task<MessageBoxResult> InformationAsync(string message, string title = "信息") =>
        ShowAsync(message, title, MessageBoxButton.OK, MessageBoxIcon.Information);

    public static Task<MessageBoxResult> InformationAsync(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowAsync(message, title, button, MessageBoxIcon.Information, defaultResult);

    #endregion

    #region Success 方法

    /// <summary>
    /// 显示成功消息框（同步，阻塞调用）
    /// </summary>
    public static void Success(string message, string title = "成功") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxIcon.Success);

    public static MessageBoxResult Success(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Show(message, title, button, MessageBoxIcon.Success, defaultResult);

    public static Task<MessageBoxResult> SuccessAsync(string message, string title = "成功") =>
        ShowAsync(message, title, MessageBoxButton.OK, MessageBoxIcon.Success);

    public static Task<MessageBoxResult> SuccessAsync(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowAsync(message, title, button, MessageBoxIcon.Success, defaultResult);

    #endregion

    #region Question 方法

    public static MessageBoxResult Question(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Show(message, title, button, MessageBoxIcon.Question, defaultResult);

    public static Task<MessageBoxResult> QuestionAsync(string message, string title = "确认") =>
        ShowAsync(message, title, MessageBoxButton.YesNo, MessageBoxIcon.Question);

    public static Task<MessageBoxResult> QuestionAsync(string message, string title, MessageBoxButton button, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowAsync(message, title, button, MessageBoxIcon.Question, defaultResult);

    #endregion
}
