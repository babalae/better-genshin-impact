using BetterGenshinImpact.ViewModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>
/// 独立任务页面统一使用的启动与实例管理按钮。
/// 控件只负责展示状态，任务操作仍由 ViewModel 命令完成。
/// </summary>
public sealed class SoloTaskButton : Button
{
    /// <summary>
    /// 创建独立任务按钮，并使用 WPF 绑定把状态对象投影到基础按钮属性。
    /// </summary>
    public SoloTaskButton()
    {
        // 派生控件不会自动命中以 WPF-UI Button 类型为键的隐式样式，需显式引用基类样式。
        SetResourceReference(StyleProperty, typeof(Button));
        SetBinding(ContentProperty, new Binding($"{nameof(State)}.{nameof(SoloTaskButtonState.ButtonText)}")
        {
            RelativeSource = RelativeSource.Self,
            FallbackValue = "启动",
            TargetNullValue = "启动"
        });
        SetBinding(IsEnabledProperty, new Binding($"{nameof(State)}.{nameof(SoloTaskButtonState.CanInteract)}")
        {
            RelativeSource = RelativeSource.Self,
            FallbackValue = false
        });
        SetBinding(CommandProperty, new Binding(nameof(ActionCommand))
        {
            RelativeSource = RelativeSource.Self
        });
        SetBinding(CommandParameterProperty, new Binding($"{nameof(State)}.{nameof(SoloTaskButtonState.TaskId)}")
        {
            RelativeSource = RelativeSource.Self
        });
    }

    /// <summary>
    /// 按钮展示状态依赖属性。
    /// </summary>
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(SoloTaskButtonState),
        typeof(SoloTaskButton),
        new PropertyMetadata(null));

    /// <summary>
    /// 统一管理命令依赖属性。
    /// </summary>
    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand),
        typeof(ICommand),
        typeof(SoloTaskButton),
        new PropertyMetadata(null));

    /// <summary>
    /// 当前按钮对应的展示状态。
    /// </summary>
    public SoloTaskButtonState? State
    {
        get => (SoloTaskButtonState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>
    /// 接收独立任务标识（字符串）参数的统一管理命令。
    /// </summary>
    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

}
