using System.Windows;
using Property = System.Windows.DependencyProperty;

namespace MicaSetup.Design.Converters;

#pragma warning disable WPF0011
#pragma warning disable WPF0015

internal static class PropertyHelper
{
    public static Property Create<T, TParent>(string name, T defaultValue) =>
        Property.Register(name, typeof(T), typeof(TParent), new PropertyMetadata(defaultValue));

    public static Property Create<T, TParent>(string name) => Create<T, TParent>(name, default!);
}
