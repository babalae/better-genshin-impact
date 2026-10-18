using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Microsoft.Xaml.Behaviors;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 约束 NumberBox 的数值范围：为空时回填 <see cref="EmptyValue"/>，低于下限时归位到 <see cref="MinimumValue"/>。
/// 校正时同步 Value、显示文本与绑定源。WPF-UI 的 NumberBox 存在三点行为需要这里补齐：
/// 一是输入 0、负数等越界内容时框架直接拒绝该次输入，既不更新数值也不刷新显示，输入框会停留在非法文本；
/// 二是数值未变化的校正不会刷新显示文本，只写 Value 会出现“值已回填、输入框仍显示 0 或空白”的错觉；
/// 三是其失焦校验不会把校正结果写回绑定源，清空后必须显式更新源，否则配置值停留在旧值。
/// </summary>
public sealed class NumberBoxValueConstraintBehavior : Behavior<NumberBox>
{
    public static readonly DependencyProperty EmptyValueProperty =
        DependencyProperty.Register(
            nameof(EmptyValue),
            typeof(double),
            typeof(NumberBoxValueConstraintBehavior),
            new PropertyMetadata(0d));

    public static readonly DependencyProperty MinimumValueProperty =
        DependencyProperty.Register(
            nameof(MinimumValue),
            typeof(double),
            typeof(NumberBoxValueConstraintBehavior),
            new PropertyMetadata(double.NegativeInfinity));

    private bool _coercing;

    /// <summary>
    /// 输入为空时回填的数值。
    /// </summary>
    public double EmptyValue
    {
        get => (double)GetValue(EmptyValueProperty);
        set => SetValue(EmptyValueProperty, value);
    }

    /// <summary>
    /// 允许的最小数值；低于该值会被归位。
    /// </summary>
    public double MinimumValue
    {
        get => (double)GetValue(MinimumValueProperty);
        set => SetValue(MinimumValueProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.ValueChanged += OnValueChanged;
        AssociatedObject.LostFocus += OnLostFocus;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.ValueChanged -= OnValueChanged;
        AssociatedObject.LostFocus -= OnLostFocus;
        base.OnDetaching();
    }

    private void OnValueChanged(object sender, NumberBoxValueChangedEventArgs e)
    {
        Coerce(e.NewValue);
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        // 输入 0、负数等非法内容时，NumberBox 会拒绝这次输入且不刷新显示，
        // 也不会触发 ValueChanged；失焦时补一次校正，避免输入框停留在非法文本上。
        Coerce(ParseText());
    }

    private double? ParseText()
    {
        return double.TryParse(
            AssociatedObject.Text?.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private void Coerce(double? value)
    {
        if (_coercing)
        {
            return;
        }

        var isEmpty = value is null;
        if (!isEmpty && value >= MinimumValue)
        {
            return;
        }

        double target = isEmpty ? EmptyValue : MinimumValue;

        _coercing = true;
        try
        {
            // 显示文本始终校正，避免数值未变化时输入框残留 0、负数或空白。
            if (value != target)
            {
                AssociatedObject.SetCurrentValue(NumberBox.ValueProperty, target);
            }

            AssociatedObject.Text = target.ToString(CultureInfo.InvariantCulture);
            AssociatedObject.GetBindingExpression(NumberBox.ValueProperty)?.UpdateSource();
        }
        finally
        {
            _coercing = false;
        }
    }
}
