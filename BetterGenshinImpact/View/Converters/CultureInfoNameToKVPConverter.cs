using BetterGenshinImpact;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Converters;

[ValueConversion(typeof(KeyValuePair<string, string>), typeof(string))]
class CultureInfoNameToKVPConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return null;
        }

        string? cultureInfoName = value as string;
        if (cultureInfoName is null)
        {
            throw new ArgumentException("Value must be a CultureInfoName");
        }

        CultureInfo.CurrentUICulture = new CultureInfo(cultureInfoName);
        var stringLocalizer = App.GetService<IStringLocalizer<CultureInfoNameToKVPConverter>>() ?? throw new NullReferenceException();

        return new KeyValuePair<string, string>(cultureInfoName, stringLocalizer["简体中文"].ToString());
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return null;
        }

        var kvp = (KeyValuePair<string, string>)value;
        return kvp.Key;
    }
}
