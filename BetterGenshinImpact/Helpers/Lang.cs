using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// JSON-based localization manager for UI strings.
/// Usage in XAML: Text="{Binding [KeyName], Source={x:Static helpers:Lang.S}}"
/// </summary>
public sealed class Lang : INotifyPropertyChanged
{
    private static readonly Lazy<Lang> _lazy = new(() => new Lang());

    /// <summary>
    /// Singleton instance for XAML binding
    /// </summary>
    public static Lang S => _lazy.Value;

    private Dictionary<string, string> _strings = new();
    private string _currentCulture = "zh-Hans";

    public event PropertyChangedEventHandler? PropertyChanged;

    private Lang()
    {
    }

    /// <summary>
    /// Indexer for XAML binding: {Binding [Key], Source={x:Static helpers:Lang.S}}
    /// Returns the localized string for the given key, or the key itself if not found.
    /// The setter is intentionally a no-op to allow WPF TwoWay bindings without errors.
    /// </summary>
    public string this[string key]
    {
        get => _strings.TryGetValue(key, out var val) ? val : key;
        set { } // no-op setter to satisfy WPF TwoWay binding requirements
    }

    /// <summary>
    /// Current culture name (e.g. "zh-Hans", "en", "fr")
    /// </summary>
    public string CurrentCulture => _currentCulture;

    /// <summary>
    /// Load language strings from a JSON file in the Lang folder.
    /// </summary>
    /// <param name="cultureName">Culture name, e.g. "zh-Hans", "en", "fr"</param>
    public void Load(string cultureName)
    {
        _currentCulture = cultureName;

        var langDir = Path.Combine(AppContext.BaseDirectory, "User", "Lang");
        var filePath = Path.Combine(langDir, $"{cultureName}.json");

        if (!File.Exists(filePath))
        {
            // Fallback to zh-Hans
            filePath = Path.Combine(langDir, "zh-Hans.json");
        }

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
            catch
            {
                _strings = new();
            }
        }
        else
        {
            _strings = new();
        }

        // Notify all bindings that strings have changed
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
