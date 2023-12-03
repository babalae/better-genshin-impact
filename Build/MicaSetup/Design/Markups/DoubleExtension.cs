using System;
using System.Windows.Markup;

namespace MicaSetup.Design.Markups;

[MarkupExtensionReturnType(typeof(double))]
public sealed class DoubleExtension : MarkupExtension
{
    public double Value { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Value;
    }
}
