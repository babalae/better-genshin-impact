using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.Service.Interface;

/// <summary>
/// Event arguments for language change events
/// </summary>
public class LanguageChangedEventArgs : EventArgs
{
    public string PreviousLanguage { get; }
    public string NewLanguage { get; }

    public LanguageChangedEventArgs(string previousLanguage, string newLanguage)
    {
        PreviousLanguage = previousLanguage;
        NewLanguage = newLanguage;
    }
}

/// <summary>
/// Service for managing application localization
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>
    /// Gets the current language code
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Gets all available languages
    /// </summary>
    IEnumerable<LanguageInfo> AvailableLanguages { get; }

    /// <summary>
    /// Gets a localized string by key
    /// </summary>
    /// <param name="key">The translation key</param>
    /// <param name="args">Optional formatting arguments</param>
    /// <returns>The localized string</returns>
    string GetString(string key, params object[] args);

    /// <summary>
    /// Sets the current language
    /// </summary>
    /// <param name="languageCode">The language code to set</param>
    /// <returns>A task representing the async operation</returns>
    Task SetLanguageAsync(string languageCode);

    /// <summary>
    /// Initializes the localization service
    /// </summary>
    /// <returns>A task representing the async operation</returns>
    Task InitializeAsync();

    /// <summary>
    /// Event fired when the language changes
    /// </summary>
    event EventHandler<LanguageChangedEventArgs> LanguageChanged;
}