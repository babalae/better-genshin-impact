using System.Windows;
using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : BoolToValueConverter<Visibility>
{
    public BoolToVisibilityConverter()
    {
        this.TrueValue = Visibility.Visible;
        this.FalseValue = Visibility.Collapsed;
    }
}
