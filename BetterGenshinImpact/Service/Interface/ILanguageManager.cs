using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.Service.Interface;

/// <summary>
/// Event arguments for language files change events
/// </summary>
public class LanguageFilesChangedEventArgs : EventArgs
{
    public WatcherChangeTypes ChangeType { get; }
    public string FilePath { get; }

    public LanguageFilesChangedEventArgs(WatcherChangeTypes changeType, string filePath)
    {
        ChangeType = changeType;
        FilePath = filePath;
    }
}

/// <summary>
/// Service for managing language files and discovery
/// </summary>
public interface ILanguageManager : IDisposable
{
    /// <summary>
    /// Discovers all available language files
    /// </summary>
    /// <returns>A collection of available languages</returns>
    Task<IEnumerable<LanguageInfo>> DiscoverLanguagesAsync();

    /// <summary>
    /// Loads a language file and returns the translations
    /// </summary>
    /// <param name="languageCode">The language code to load</param>
    /// <returns>A dictionary of translation keys and values</returns>
    Task<Dictionary<string, string>> LoadLanguageAsync(string languageCode);

    /// <summary>
    /// Gets the path to the languages directory
    /// </summary>
    string LanguagesDirectory { get; }

    /// <summary>
    /// Event fired when language files are added, removed, or modified
    /// </summary>
    event EventHandler<LanguageFilesChangedEventArgs> LanguageFilesChanged;
}