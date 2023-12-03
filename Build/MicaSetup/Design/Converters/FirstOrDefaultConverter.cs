using System;
using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(IEnumerable), typeof(object))]
public class FirstOrDefaultConverter : SingletonConverterBase<FirstOrDefaultConverter>
{
    protected override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable enumerable)
        {
            var enumerator = enumerable.GetEnumerator();
            {
                if (enumerator.MoveNext())
                {
                    return enumerator.Current;
                }
            }
        }

        return UnsetValue;
    }
}
