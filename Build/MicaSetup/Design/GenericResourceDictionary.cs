using System;
using System.Windows;

namespace MicaSetup.Design;

public sealed class GenericResourceDictionary : ResourceDictionary
{
    public GenericResourceDictionary()
    {
        Source = new Uri($"pack://application:,,,/MicaSetup;component/Resources/Generic.xaml");
    }
}
