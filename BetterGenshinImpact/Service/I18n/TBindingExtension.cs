using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace BetterGenshinImpact.Service.I18n;

/// <summary>
/// 把绑定得到的中文数据值翻译后展示，例如：Text="{i18n:TBinding}" 或 Text="{i18n:TBinding Name}"。
/// 适用于保存在配置中、参与逻辑比较、不能直接改成译文的数据值；只影响展示，不回写。
/// 语言切换时通过监听 <see cref="I18nService.Revision"/> 重新取值。
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TBindingExtension : MarkupExtension
{
    private static readonly IMultiValueConverter TranslationConverter = new ValueToTranslationConverter();

    /// <summary>
    /// 绑定当前数据上下文本身。
    /// </summary>
    public TBindingExtension()
    {
    }

    /// <summary>
    /// 绑定当前数据上下文中的指定路径。
    /// </summary>
    /// <param name="path">绑定路径。</param>
    public TBindingExtension(string path)
    {
        Path = path;
    }

    /// <summary>
    /// 绑定路径；为空时绑定数据上下文本身。
    /// </summary>
    [ConstructorArgument("path")]
    public string? Path { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // 第一个绑定取数据值，第二个绑定监听语言版本号，二者任一变化都会重新翻译。
        var multiBinding = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = TranslationConverter,
        };
        multiBinding.Bindings.Add(string.IsNullOrEmpty(Path) ? new Binding() : new Binding(Path));
        multiBinding.Bindings.Add(new Binding(nameof(I18nService.Revision))
        {
            Source = I18nService.Instance,
            Mode = BindingMode.OneWay,
        });

        return multiBinding.ProvideValue(serviceProvider);
    }

    /// <summary>
    /// 将字符串数据值翻译为当前界面语言；非字符串值原样转为文本。
    /// </summary>
    private sealed class ValueToTranslationConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return values.Length > 0 && values[0] is string text
                ? I18nService.Instance.Translate(text)
                : values.Length > 0 ? values[0]?.ToString() ?? string.Empty : string.Empty;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
