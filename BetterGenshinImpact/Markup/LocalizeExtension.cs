using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.Markup;

/// <summary>
/// XAML markup extension for localization that provides reactive binding to localized strings
/// </summary>
[MarkupExtensionReturnType(typeof(BindingExpression))]
public class LocalizeExtension : MarkupExtension
{
    /// <summary>
    /// The translation key to look up
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Optional formatting arguments for the localized string
    /// </summary>
    public object[]? Args { get; set; }

    /// <summary>
    /// Constructor for XAML usage without parameters
    /// </summary>
    public LocalizeExtension()
    {
    }

    /// <summary>
    /// Constructor for XAML usage with key parameter
    /// </summary>
    /// <param name="key">The translation key</param>
    public LocalizeExtension(string key)
    {
        Key = key;
    }

    /// <summary>
    /// Provides the value for the XAML binding
    /// </summary>
    /// <param name="serviceProvider">The service provider from XAML</param>
    /// <returns>A binding expression that updates when the language changes</returns>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return "[EMPTY_KEY]";
        }

        // For design-time support, return a placeholder
        if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
        {
            return $"[{Key}]";
        }

        try
        {
            // Get the localization service
            var localizationService = App.GetService<ILocalizationService>();
            if (localizationService == null)
            {
                return $"[SERVICE_NOT_FOUND: {Key}]";
            }

            // Create a binding to the localization service
            var binding = new Binding
            {
                Source = new LocalizationProxy(localizationService, Key, Args),
                Path = new PropertyPath(nameof(LocalizationProxy.LocalizedValue)),
                Mode = BindingMode.OneWay
            };

            // If we have a target object, create the binding expression
            if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target &&
                target.TargetObject is DependencyObject targetObject &&
                target.TargetProperty is DependencyProperty targetProperty)
            {
                return binding.ProvideValue(serviceProvider);
            }

            // Fallback for cases where we can't create a binding
            return localizationService.GetString(Key, Args ?? Array.Empty<object>());
        }
        catch (Exception ex)
        {
            // Log error and return error indicator
            System.Diagnostics.Debug.WriteLine($"LocalizeExtension error: {ex.Message}");
            return $"[LOCALIZE_ERROR: {Key}]";
        }
    }
}

/// <summary>
/// Proxy class that provides a bindable property for localized values
/// </summary>
public class LocalizationProxy : INotifyPropertyChanged
{
    private readonly ILocalizationService _localizationService;
    private readonly string _key;
    private readonly object[]? _args;

    public LocalizationProxy(ILocalizationService localizationService, string key, object[]? args)
    {
        _localizationService = localizationService;
        _key = key;
        _args = args;

        // Subscribe to language change events
        _localizationService.LanguageChanged += OnLanguageChanged;
        _localizationService.PropertyChanged += OnServicePropertyChanged;
    }

    /// <summary>
    /// Gets the localized value for the current language
    /// </summary>
    public string LocalizedValue
    {
        get
        {
            try
            {
                return _localizationService.GetString(_key, _args ?? Array.Empty<object>());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalizationProxy error: {ex.Message}");
                return $"[PROXY_ERROR: {_key}]";
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        // Notify that the localized value has changed
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalizedValue)));
    }

    private void OnServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // If the current language property changed, update our value
        if (e.PropertyName == nameof(ILocalizationService.CurrentLanguage))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalizedValue)));
        }
    }
}