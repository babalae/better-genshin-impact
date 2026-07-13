using System;
using System.Windows.Markup;

namespace BetterGenshinImpact.Markup;

[MarkupExtensionReturnType(typeof(object))]
public class ServiceLocatorExtension : MarkupExtension
{
    public Type Type { get; set; } = null!;

    public ServiceLocatorExtension()
    {
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(Type);
        return App.GetService(Type)!;
    }
}
