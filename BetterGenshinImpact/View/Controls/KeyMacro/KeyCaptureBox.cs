using BetterGenshinImpact.GameTask.KeyMacro.Script;
using System.Windows;
using System.Windows.Input;
using Vanara.PInvoke;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace BetterGenshinImpact.View.Controls.KeyMacro;

/// <summary>
/// 捕获单个键盘键的输入框：获得焦点后按下任意键即完成捕获，显示易读名称，绑定值为按键宏脚本里的按键名。
/// 鼠标键由鼠标类步骤选择，这里不捕获
/// </summary>
public class KeyCaptureBox : TextBox
{
    /// <summary>
    /// 捕获到的按键名（规范名称，例如 e、lshift），空字符串表示未设置
    /// </summary>
    public static readonly DependencyProperty KeyNameProperty = DependencyProperty.Register(
        nameof(KeyName),
        typeof(string),
        typeof(KeyCaptureBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((KeyCaptureBox)d).UpdateText()));

    /// <summary>
    /// 捕获到的按键名
    /// </summary>
    public string KeyName
    {
        get => (string)GetValue(KeyNameProperty);
        set => SetValue(KeyNameProperty, value);
    }

    /// <summary>
    /// 创建捕获框
    /// </summary>
    public KeyCaptureBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        // 清空按钮会直接改写显示文字，与捕获逻辑冲突
        ClearButtonEnabled = false;
        if (ContextMenu is not null)
        {
            ContextMenu.Visibility = Visibility.Collapsed;
        }

        UpdateText();
    }

    /// <summary>
    /// 获得焦点时提示按键
    /// </summary>
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = "请按键…";
    }

    /// <summary>
    /// 失去焦点时恢复显示当前按键
    /// </summary>
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        UpdateText();
    }

    /// <summary>
    /// 捕获按键。Tab、Esc 等也作为普通按键捕获，不移动焦点
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };
        if (key == Key.None)
        {
            return;
        }

        var vk = (User32.VK)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
        {
            return;
        }

        KeyName = KeyMacroKeys.GetName(vk);
        UpdateText();
        // 捕获后把焦点交还给外层，避免下一次按键再次覆盖
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    /// <summary>
    /// 显示当前按键的易读名称
    /// </summary>
    private void UpdateText()
    {
        if (IsKeyboardFocused)
        {
            return;
        }

        Text = string.IsNullOrEmpty(KeyName)
            ? "点击后按键"
            : KeyMacroKeys.TryParseKey(KeyName, out var vk) ? KeyMacroKeys.GetDisplayName(vk) : KeyName;
    }
}
