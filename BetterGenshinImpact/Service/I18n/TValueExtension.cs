using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace BetterGenshinImpact.Service.I18n;

/// <summary>
/// 翻译<b>数据绑定得到的值</b>，而不是 XAML 里的字面量：<see cref="TExtension"/> 的 Key 在编译期就确定，
/// 而 ComboBox 等控件的选项来自 ItemsSource，值只有运行时才知道，因此需要单独的扩展。
/// 用法：<c>&lt;TextBlock Text="{i18n:TValue}" /&gt;</c>（翻译 DataContext 本身，即字符串列表的每一项），
/// 或 <c>{i18n:TValue 属性名}</c>（翻译该属性的值）。
/// <para>
/// 只影响<b>显示</b>：SelectedItem / SelectedValue 以及写回配置的仍然是中文原文，
/// 因此配置文件和依赖中文字面量的业务逻辑都不受影响。
/// </para>
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TValueExtension : MarkupExtension
{
    private static readonly IMultiValueConverter TranslationConverter = new ValueToTranslationConverter();

    public TValueExtension()
    {
    }

    public TValueExtension(PropertyPath path)
    {
        Path = path;
    }

    /// <summary>
    /// 要翻译的属性路径。省略时翻译 DataContext 本身，对应“列表项就是字符串”这一最常见的情况。
    /// </summary>
    [ConstructorArgument("path")]
    public PropertyPath? Path { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var valueBinding = Path == null
            ? new Binding { Mode = BindingMode.OneWay }
            : new Binding { Path = Path, Mode = BindingMode.OneWay };

        if (IsInDesignMode(serviceProvider))
        {
            // 设计器显示未翻译的原值，和 TExtension 一样不依赖语言文件。
            return valueBinding.ProvideValue(serviceProvider);
        }

        // 第二路只为了在 ChangeLanguage 递增 Revision 时触发重新求值。
        var revisionBinding = new Binding(nameof(I18nService.Revision))
        {
            Source = I18nService.Instance,
            Mode = BindingMode.OneWay,
        };

        var multiBinding = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = TranslationConverter,
        };
        multiBinding.Bindings.Add(valueBinding);
        multiBinding.Bindings.Add(revisionBinding);

        return multiBinding.ProvideValue(serviceProvider);
    }

    private static bool IsInDesignMode(IServiceProvider serviceProvider)
    {
        if ((bool)DesignerProperties.IsInDesignModeProperty
            .GetMetadata(typeof(DependencyObject)).DefaultValue)
        {
            return true;
        }

        var provideValueTarget = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        return provideValueTarget?.TargetObject is DependencyObject dependencyObject
               && DesignerProperties.GetIsInDesignMode(dependencyObject);
    }

    private sealed class ValueToTranslationConverter : IMultiValueConverter
    {
        public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 0)
            {
                return null;
            }

            var value = values[0];

            // 绑定尚未取到值时原样返回，避免把 UnsetValue 当成 Key。
            if (value == DependencyProperty.UnsetValue || value == null)
            {
                return value;
            }

            // 非字符串项（数字、枚举、对象）不参与翻译。
            return value is string key ? I18nService.Instance.Translate(key) : value;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
