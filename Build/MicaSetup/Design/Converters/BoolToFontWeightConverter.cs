using System.Windows;
using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(bool), typeof(FontWeight))]
public class BoolToFontWeightConverter : BoolToValueConverter<FontWeight>
{
    public BoolToFontWeightConverter()
    {
        this.TrueValue = FontWeights.Bold;
        this.FalseValue = FontWeights.Normal;
    }
}
