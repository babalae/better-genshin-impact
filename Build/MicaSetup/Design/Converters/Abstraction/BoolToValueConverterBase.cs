using System;
using System.Globalization;

namespace MicaSetup.Design.Converters;

public abstract class BoolToValueConverterBase<T, TConverter> : SingletonConverterBase<TConverter> where TConverter : new()
{
    public abstract T TrueValue { get; set; }

    public abstract T FalseValue { get; set; }

    public abstract bool IsInverted { get; set; }

    protected override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var returnValue = this.FalseValue;

        if (value is bool boolValue)
        {
            if (this.IsInverted)
            {
                returnValue = boolValue ? this.FalseValue : this.TrueValue;
            }
            else
            {
                returnValue = boolValue ? this.TrueValue : this.FalseValue;
            }
        }

        return returnValue!;
    }

    protected override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool returnValue = false;

        if (value != null)
        {
            if (this.IsInverted)
            {
                returnValue = value.Equals(this.FalseValue);
            }
            else
            {
                returnValue = value.Equals(this.TrueValue);
            }
        }

        return returnValue;
    }
}
