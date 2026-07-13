using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace BetterGenshinImpact.Helpers;
public static class CultureHelper
{
    public static string WithCultureGet(this IStringLocalizer stringLocalizer, CultureInfo? culture, string text)
    {
        if (culture == null)
        {
            return text;
        }
        else
        {
            using var _ = Use(culture);
            return stringLocalizer[text];
        }
    }

    public static IDisposable Use([NotNull] string culture, string? uiCulture = null)
    {
        ArgumentNullException.ThrowIfNull(culture, nameof(culture));

        return Use(
            new CultureInfo(culture),
            uiCulture == null
                ? null
                : new CultureInfo(uiCulture)
        );
    }

    /// <summary>
    /// WithCulture被微软移除了，这是个替代方法，用于临时切换到某个CurrentCulture、CurrentUICulture
    /// </summary>
    /// <param name="culture"></param>
    /// <param name="uiCulture"></param>
    /// <returns></returns>
    public static IDisposable Use([NotNull] CultureInfo culture, CultureInfo? uiCulture = null)
    {
        ArgumentNullException.ThrowIfNull(culture, nameof(culture));

        var currentCulture = CultureInfo.CurrentCulture;
        var currentUiCulture = CultureInfo.CurrentUICulture;

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture ?? culture;

        return new DisposeAction<ValueTuple<CultureInfo, CultureInfo>>(static (state) =>
        {
            var (currentCulture, currentUiCulture) = state;
            CultureInfo.CurrentCulture = currentCulture;
            CultureInfo.CurrentUICulture = currentUiCulture;
        }, (currentCulture, currentUiCulture));
    }

    public class DisposeAction<T> : IDisposable
    {
        private readonly Action<T> _action;

        private readonly T? _parameter;

        public DisposeAction(Action<T> action, T parameter)
        {
            ArgumentNullException.ThrowIfNull(action, nameof(action));

            _action = action;
            _parameter = parameter;
        }

        public void Dispose()
        {
            if (_parameter != null)
            {
                _action(_parameter);
            }
        }
    }
}
