using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

/// <summary>
/// Central service for managing application localization
/// </summary>
public class LocalizationService : ILocalizationService, IDisposable
{
    private readonly ILanguageManager _languageManager;
    private readonly IConfigService _configService;
    private readonly ILogger<LocalizationService> _logger;

    private string _currentLanguage = "zh-CN";
    private List<LanguageInfo> _availableLanguages = new();
    private Dictionary<string, string> _currentTranslations = new();
    private Dictionary<string, string> _fallbackTranslations = new();

    public LocalizationService(
        ILanguageManager languageManager,
        IConfigService configService,
        ILogger<LocalizationService> logger)
    {
        _languageManager = languageManager;
        _configService = configService;
        _logger = logger;
    }

    public string CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage != value)
            {
                var previousLanguage = _currentLanguage;
                _currentLanguage = value;
                OnPropertyChanged(nameof(CurrentLanguage));
                LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(previousLanguage, value));
            }
        }
    }

    public IEnumerable<LanguageInfo> AvailableLanguages => _availableLanguages;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing localization service");

            // Subscribe to language file changes for dynamic updates
            _languageManager.LanguageFilesChanged += OnLanguageFilesChanged;

            // Discover available languages with error recovery
            await InitializeLanguageDiscovery();

            // Load fallback translations with error recovery
            await InitializeFallbackTranslations();

            // Get saved language preference or use system default
            var savedLanguage = GetSavedLanguage();
            var targetLanguage = DetermineInitialLanguage(savedLanguage);

            await SetLanguageAsync(targetLanguage);

            _logger.LogInformation("Localization service initialized with language: {Language}", CurrentLanguage);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Critical failure during localization service initialization");
            await InitializeEmergencyFallback();
        }
    }

    /// <summary>
    /// Initializes language discovery with error recovery
    /// </summary>
    private async Task InitializeLanguageDiscovery()
    {
        try
        {
            var languages = await _languageManager.DiscoverLanguagesAsync();
            _availableLanguages = languages.ToList();

            if (_availableLanguages.Count == 0)
            {
                _logger.LogWarning("No language files discovered, creating minimal language list");
                // Create minimal language list with at least English
                _availableLanguages = new List<LanguageInfo>
                {
                    new LanguageInfo
                    {
                        Code = "en-US",
                        DisplayName = "English",
                        NativeName = "English",
                        FilePath = Path.Combine(_languageManager.LanguagesDirectory, "en-US.json"),
                        Version = "1.0.0"
                    }
                };
            }

            _logger.LogInformation("Found {Count} available languages", _availableLanguages.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover languages, using minimal fallback");
            // Create absolute minimal fallback
            _availableLanguages = new List<LanguageInfo>
            {
                new LanguageInfo
                {
                    Code = "en-US",
                    DisplayName = "English",
                    NativeName = "English",
                    FilePath = "fallback",
                    Version = "1.0.0"
                }
            };
        }
    }

    /// <summary>
    /// Initializes fallback translations with error recovery
    /// </summary>
    private async Task InitializeFallbackTranslations()
    {
        try
        {
            _fallbackTranslations = await _languageManager.LoadLanguageAsync("en-US");
            
            if (_fallbackTranslations.Count == 0)
            {
                _logger.LogWarning("No English translations loaded, creating minimal fallback translations");
                CreateMinimalFallbackTranslations();
            }
            else
            {
                _logger.LogInformation("Loaded {Count} fallback translations", _fallbackTranslations.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load English fallback translations, creating minimal set");
            CreateMinimalFallbackTranslations();
        }
    }

    /// <summary>
    /// Creates minimal fallback translations for critical UI elements
    /// </summary>
    private void CreateMinimalFallbackTranslations()
    {
        _fallbackTranslations = new Dictionary<string, string>
        {
            ["common.ok"] = "OK",
            ["common.cancel"] = "Cancel",
            ["common.save"] = "Save",
            ["common.close"] = "Close",
            ["common.error"] = "Error",
            ["common.warning"] = "Warning",
            ["common.information"] = "Information",
            ["settings.title"] = "Settings",
            ["settings.language"] = "Language",
            ["error.translation_load_failed"] = "Failed to load translations",
            ["error.language_not_available"] = "Language not available",
            ["error.file_corrupted"] = "Language file corrupted"
        };
        
        _logger.LogInformation("Created {Count} minimal fallback translations", _fallbackTranslations.Count);
    }

    /// <summary>
    /// Emergency fallback initialization when all else fails
    /// </summary>
    private async Task InitializeEmergencyFallback()
    {
        try
        {
            _logger.LogWarning("Initializing emergency fallback mode");
            
            // Set minimal state
            CurrentLanguage = "en-US";
            _availableLanguages = new List<LanguageInfo>
            {
                new LanguageInfo
                {
                    Code = "en-US",
                    DisplayName = "English (Emergency)",
                    NativeName = "English (Emergency)",
                    FilePath = "emergency",
                    Version = "1.0.0"
                }
            };
            
            CreateMinimalFallbackTranslations();
            _currentTranslations = new Dictionary<string, string>(_fallbackTranslations);
            
            _logger.LogWarning("Localization service initialized in emergency fallback mode");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Even emergency fallback initialization failed - localization service may not function properly");
            // Set absolute minimal state
            CurrentLanguage = "en-US";
            _availableLanguages = new List<LanguageInfo>();
            _fallbackTranslations = new Dictionary<string, string>();
            _currentTranslations = new Dictionary<string, string>();
        }
    }

    public string GetString(string key, params object[] args)
    {
        if (string.IsNullOrEmpty(key))
        {
            _logger.LogWarning("Attempted to get translation for empty or null key");
            return string.Empty;
        }

        try
        {
            // Try current language first
            if (_currentTranslations.TryGetValue(key, out var translation))
            {
                try
                {
                    return args != null && args.Length > 0 ? string.Format(translation, args) : translation;
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "Format error in translation for key: {Key}, translation: {Translation}", key, translation);
                    // Try fallback without formatting
                    return translation;
                }
            }

            // Fallback to English
            if (_fallbackTranslations.TryGetValue(key, out var fallbackTranslation))
            {
                _logger.LogDebug("Using fallback translation for key: {Key} (current language: {Language})", key, CurrentLanguage);
                try
                {
                    return args != null && args.Length > 0 ? string.Format(fallbackTranslation, args) : fallbackTranslation;
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "Format error in fallback translation for key: {Key}, translation: {Translation}", key, fallbackTranslation);
                    // Return unformatted fallback
                    return fallbackTranslation;
                }
            }

            // Log missing translation for monitoring and translation team
            LogMissingTranslation(key);
            
            // Return key with indicator if no translation found
            return $"[KEY_NOT_FOUND: {key}]";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting translation for key: {Key}", key);
            return $"[ERROR: {key}]";
        }
    }

    /// <summary>
    /// Logs missing translation keys for monitoring and translation team awareness
    /// </summary>
    private void LogMissingTranslation(string key)
    {
        // Log as warning for immediate attention
        _logger.LogWarning("Missing translation key: {Key} for language: {Language}", key, CurrentLanguage);
        
        // Also log structured data for potential automated collection
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TranslationKey"] = key,
            ["Language"] = CurrentLanguage,
            ["EventType"] = "MissingTranslation",
            ["Timestamp"] = DateTime.UtcNow
        });
        
        _logger.LogInformation("Translation gap detected - Key: {Key}, Language: {Language}", key, CurrentLanguage);
    }

    public async Task SetLanguageAsync(string languageCode)
    {
        if (string.IsNullOrEmpty(languageCode))
        {
            _logger.LogWarning("Attempted to set empty language code");
            return;
        }

        var originalLanguage = CurrentLanguage;
        
        try
        {
            _logger.LogInformation("Attempting to set language to: {LanguageCode}", languageCode);

            // Check if language is available
            var targetLanguage = _availableLanguages.FirstOrDefault(l => 
                l.Code.Equals(languageCode, StringComparison.OrdinalIgnoreCase));

            if (targetLanguage == null)
            {
                _logger.LogWarning("Language not available: {LanguageCode}, attempting fallback strategies", languageCode);
                
                // Try fallback strategies
                var fallbackLanguage = await AttemptLanguageFallback(languageCode);
                if (fallbackLanguage != null)
                {
                    languageCode = fallbackLanguage;
                    _logger.LogInformation("Using fallback language: {LanguageCode}", languageCode);
                }
                else
                {
                    _logger.LogWarning("All fallback strategies failed, using English");
                    languageCode = "en-US";
                }
            }

            // Load translations for the target language with error recovery
            var translations = await LoadLanguageWithErrorRecovery(languageCode);
            
            // Validate that we have some translations
            if (translations.Count == 0)
            {
                _logger.LogError("No translations available for {LanguageCode}, attempting emergency recovery", languageCode);
                
                // Emergency fallback - try to use fallback translations or create minimal set
                if (_fallbackTranslations.Count > 0)
                {
                    translations = new Dictionary<string, string>(_fallbackTranslations);
                    languageCode = "en-US";
                    _logger.LogWarning("Using fallback translations due to empty translation set");
                }
                else
                {
                    // Create absolute minimal translations
                    CreateMinimalFallbackTranslations();
                    translations = new Dictionary<string, string>(_fallbackTranslations);
                    languageCode = "en-US";
                    _logger.LogWarning("Created minimal translations due to complete translation failure");
                }
            }

            // Apply the language change
            _currentTranslations = translations;
            CurrentLanguage = languageCode;

            // Save language preference with error handling
            SaveLanguagePreference(languageCode);

            _logger.LogInformation("Language successfully changed to: {Language} ({Count} translations loaded)", 
                languageCode, translations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error setting language: {LanguageCode}, attempting recovery", languageCode);
            
            // Attempt to recover by reverting to original language or English
            await AttemptLanguageRecovery(originalLanguage, ex);
        }
    }

    /// <summary>
    /// Attempts various fallback strategies when a requested language is not available
    /// </summary>
    private async Task<string?> AttemptLanguageFallback(string requestedLanguageCode)
    {
        try
        {
            // Strategy 1: Try language without region (e.g., "en" instead of "en-US")
            var languageOnly = requestedLanguageCode.Split('-')[0];
            var matchingLanguage = _availableLanguages.FirstOrDefault(l => 
                l.Code.StartsWith(languageOnly, StringComparison.OrdinalIgnoreCase));
            
            if (matchingLanguage != null)
            {
                _logger.LogInformation("Found language match without region: {RequestedCode} -> {FoundCode}", 
                    requestedLanguageCode, matchingLanguage.Code);
                return matchingLanguage.Code;
            }

            // Strategy 2: Try common language variants
            var commonVariants = GetCommonLanguageVariants(requestedLanguageCode);
            foreach (var variant in commonVariants)
            {
                var variantLanguage = _availableLanguages.FirstOrDefault(l => 
                    l.Code.Equals(variant, StringComparison.OrdinalIgnoreCase));
                
                if (variantLanguage != null)
                {
                    _logger.LogInformation("Found language variant: {RequestedCode} -> {FoundCode}", 
                        requestedLanguageCode, variantLanguage.Code);
                    return variantLanguage.Code;
                }
            }

            // Strategy 3: Re-scan for languages in case files were added recently
            _logger.LogInformation("Re-scanning for languages in case new files were added");
            var freshLanguages = await _languageManager.DiscoverLanguagesAsync();
            var freshLanguagesList = freshLanguages.ToList();
            
            var foundInFresh = freshLanguagesList.FirstOrDefault(l => 
                l.Code.Equals(requestedLanguageCode, StringComparison.OrdinalIgnoreCase));
            
            if (foundInFresh != null)
            {
                _logger.LogInformation("Found language in fresh scan: {LanguageCode}", requestedLanguageCode);
                _availableLanguages = freshLanguagesList; // Update our cache
                OnPropertyChanged(nameof(AvailableLanguages));
                return requestedLanguageCode;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during language fallback attempt for: {LanguageCode}", requestedLanguageCode);
        }

        return null;
    }

    /// <summary>
    /// Gets common language variants for fallback attempts
    /// </summary>
    private string[] GetCommonLanguageVariants(string languageCode)
    {
        return languageCode.ToLowerInvariant() switch
        {
            "en" or "en-us" => new[] { "en-US", "en-GB", "en" },
            "zh" or "zh-cn" => new[] { "zh-CN", "zh-TW", "zh-HK", "zh" },
            "es" or "es-es" => new[] { "es-ES", "es-MX", "es-AR", "es" },
            "fr" or "fr-fr" => new[] { "fr-FR", "fr-CA", "fr" },
            "de" or "de-de" => new[] { "de-DE", "de-AT", "de-CH", "de" },
            "pt" or "pt-pt" => new[] { "pt-PT", "pt-BR", "pt" },
            _ => new[] { languageCode }
        };
    }

    /// <summary>
    /// Loads a language with comprehensive error recovery
    /// </summary>
    private async Task<Dictionary<string, string>> LoadLanguageWithErrorRecovery(string languageCode)
    {
        try
        {
            var translations = await _languageManager.LoadLanguageAsync(languageCode);
            
            if (translations.Count == 0 && !languageCode.Equals("en-US", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("No translations loaded for {LanguageCode}, trying fallback to English", languageCode);
                
                // Try to reload English fallback
                var fallbackTranslations = await _languageManager.LoadLanguageAsync("en-US");
                if (fallbackTranslations.Count > 0)
                {
                    return fallbackTranslations;
                }
                
                // If even English failed, use our cached fallback
                if (_fallbackTranslations.Count > 0)
                {
                    _logger.LogWarning("Using cached fallback translations");
                    return new Dictionary<string, string>(_fallbackTranslations);
                }
            }
            
            return translations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading language {LanguageCode}, using fallback", languageCode);
            
            // Return cached fallback or empty dictionary
            return _fallbackTranslations.Count > 0 
                ? new Dictionary<string, string>(_fallbackTranslations) 
                : new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Attempts to recover from a critical language setting error
    /// </summary>
    private async Task AttemptLanguageRecovery(string originalLanguage, Exception originalException)
    {
        try
        {
            _logger.LogWarning("Attempting language recovery, original language: {OriginalLanguage}", originalLanguage);
            
            // Try to revert to original language
            if (!string.IsNullOrEmpty(originalLanguage) && originalLanguage != CurrentLanguage)
            {
                try
                {
                    var originalTranslations = await _languageManager.LoadLanguageAsync(originalLanguage);
                    if (originalTranslations.Count > 0)
                    {
                        _currentTranslations = originalTranslations;
                        CurrentLanguage = originalLanguage;
                        _logger.LogInformation("Successfully reverted to original language: {Language}", originalLanguage);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to revert to original language: {Language}", originalLanguage);
                }
            }

            // Try to fall back to English
            try
            {
                var englishTranslations = await _languageManager.LoadLanguageAsync("en-US");
                if (englishTranslations.Count > 0)
                {
                    _currentTranslations = englishTranslations;
                    CurrentLanguage = "en-US";
                    _logger.LogInformation("Successfully fell back to English");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fall back to English");
            }

            // Use cached fallback translations
            if (_fallbackTranslations.Count > 0)
            {
                _currentTranslations = new Dictionary<string, string>(_fallbackTranslations);
                CurrentLanguage = "en-US";
                _logger.LogWarning("Using cached fallback translations for recovery");
                return;
            }

            // Create minimal emergency translations
            CreateMinimalFallbackTranslations();
            _currentTranslations = new Dictionary<string, string>(_fallbackTranslations);
            CurrentLanguage = "en-US";
            _logger.LogWarning("Created minimal emergency translations for recovery");
        }
        catch (Exception recoveryEx)
        {
            _logger.LogCritical(recoveryEx, "Language recovery failed completely. Original error: {OriginalError}", 
                originalException.Message);
            
            // Set absolute minimal state
            CurrentLanguage = "en-US";
            _currentTranslations = new Dictionary<string, string>();
            _fallbackTranslations = new Dictionary<string, string>();
        }
    }

    private string GetSavedLanguage()
    {
        try
        {
            var config = _configService.Get();
            return config.CommonConfig.Language ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get saved language preference");
            return string.Empty;
        }
    }

    private void SaveLanguagePreference(string languageCode)
    {
        try
        {
            var config = _configService.Get();
            config.CommonConfig.Language = languageCode;
            _configService.Save();
            _logger.LogDebug("Saved language preference: {Language}", languageCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save language preference");
        }
    }

    private string DetermineInitialLanguage(string savedLanguage)
    {
        // Use saved language if available and valid
        if (!string.IsNullOrEmpty(savedLanguage) && 
            _availableLanguages.Any(l => l.Code.Equals(savedLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            return savedLanguage;
        }

        // Try to use system language
        var systemLanguage = CultureInfo.CurrentUICulture.Name;
        if (_availableLanguages.Any(l => l.Code.Equals(systemLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Using system language: {Language}", systemLanguage);
            return systemLanguage;
        }

        // Try language without region (e.g., "en" instead of "en-US")
        var languageOnly = systemLanguage.Split('-')[0];
        var matchingLanguage = _availableLanguages.FirstOrDefault(l => 
            l.Code.StartsWith(languageOnly, StringComparison.OrdinalIgnoreCase));
        
        if (matchingLanguage != null)
        {
            _logger.LogInformation("Using closest matching language: {Language}", matchingLanguage.Code);
            return matchingLanguage.Code;
        }

        // Default to English
        _logger.LogInformation("Using default language: en-US");
        return "en-US";
    }

    /// <summary>
    /// Handles language file changes and updates available languages dynamically
    /// </summary>
    private async void OnLanguageFilesChanged(object? sender, LanguageFilesChangedEventArgs e)
    {
        try
        {
            _logger.LogInformation("Language files changed: {ChangeType} - {FilePath}", e.ChangeType, e.FilePath);

            // Re-discover available languages with error recovery
            await HandleLanguageDiscoveryChange();

            // Handle specific file change types with error recovery
            await HandleSpecificFileChange(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error handling language file change: {FilePath}", e.FilePath);
            
            // Attempt recovery to ensure the service remains functional
            await AttemptFileChangeRecovery(e, ex);
        }
    }

    /// <summary>
    /// Handles language discovery changes with error recovery
    /// </summary>
    private async Task HandleLanguageDiscoveryChange()
    {
        try
        {
            var languages = await _languageManager.DiscoverLanguagesAsync();
            var newLanguagesList = languages.ToList();

            // Check if the list actually changed
            if (!LanguageListsEqual(_availableLanguages, newLanguagesList))
            {
                _availableLanguages = newLanguagesList;
                OnPropertyChanged(nameof(AvailableLanguages));

                _logger.LogInformation("Available languages updated. New count: {Count}", _availableLanguages.Count);

                // Validate that we still have at least English available
                if (!_availableLanguages.Any(l => l.Code.Equals("en-US", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("English language no longer available after file change, adding fallback entry");
                    _availableLanguages.Add(new LanguageInfo
                    {
                        Code = "en-US",
                        DisplayName = "English (Fallback)",
                        NativeName = "English (Fallback)",
                        FilePath = "fallback",
                        Version = "1.0.0"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during language discovery update");
            
            // Ensure we maintain a minimal language list
            if (_availableLanguages.Count == 0)
            {
                _logger.LogWarning("Language list became empty, restoring minimal fallback");
                _availableLanguages = new List<LanguageInfo>
                {
                    new LanguageInfo
                    {
                        Code = "en-US",
                        DisplayName = "English (Recovery)",
                        NativeName = "English (Recovery)",
                        FilePath = "recovery",
                        Version = "1.0.0"
                    }
                };
                OnPropertyChanged(nameof(AvailableLanguages));
            }
        }
    }

    /// <summary>
    /// Handles specific file change types with comprehensive error recovery
    /// </summary>
    private async Task HandleSpecificFileChange(LanguageFilesChangedEventArgs e)
    {
        try
        {
            var changedFileName = Path.GetFileNameWithoutExtension(e.FilePath);
            
            switch (e.ChangeType)
            {
                case System.IO.WatcherChangeTypes.Deleted:
                    await HandleFileDeleted(changedFileName, e.FilePath);
                    break;
                    
                case System.IO.WatcherChangeTypes.Changed:
                    await HandleFileChanged(changedFileName, e.FilePath);
                    break;
                    
                case System.IO.WatcherChangeTypes.Created:
                    await HandleFileCreated(changedFileName, e.FilePath);
                    break;
                    
                case System.IO.WatcherChangeTypes.Renamed:
                    // Handled by separate rename event
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling specific file change: {ChangeType} - {FilePath}", e.ChangeType, e.FilePath);
        }
    }

    /// <summary>
    /// Handles file deletion with fallback mechanisms
    /// </summary>
    private async Task HandleFileDeleted(string deletedFileName, string filePath)
    {
        try
        {
            if (CurrentLanguage.Equals(deletedFileName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Current language file deleted: {Language}, attempting graceful fallback", CurrentLanguage);
                
                // Try to find an alternative language or fall back to English
                var fallbackLanguage = await FindBestFallbackLanguage(deletedFileName);
                await SetLanguageAsync(fallbackLanguage);
            }
            else if (deletedFileName.Equals("en-US", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("English fallback file deleted, creating emergency fallback translations");
                
                // Recreate minimal fallback translations
                CreateMinimalFallbackTranslations();
                
                // If current language is English, update current translations too
                if (CurrentLanguage.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                {
                    _currentTranslations = new Dictionary<string, string>(_fallbackTranslations);
                    LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(CurrentLanguage, CurrentLanguage));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file deletion: {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Handles file changes with validation and error recovery
    /// </summary>
    private async Task HandleFileChanged(string changedFileName, string filePath)
    {
        try
        {
            if (CurrentLanguage.Equals(changedFileName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Current language file modified: {Language}, reloading translations", CurrentLanguage);
                
                var newTranslations = await _languageManager.LoadLanguageAsync(CurrentLanguage);
                if (newTranslations.Count > 0)
                {
                    _currentTranslations = newTranslations;
                    LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(CurrentLanguage, CurrentLanguage));
                    _logger.LogInformation("Successfully reloaded {Count} translations for {Language}", newTranslations.Count, CurrentLanguage);
                }
                else
                {
                    _logger.LogWarning("Modified language file {Language} is empty or corrupted, keeping existing translations", CurrentLanguage);
                }
            }
            else if (changedFileName.Equals("en-US", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("English fallback file modified, reloading fallback translations");
                
                try
                {
                    var newFallbackTranslations = await _languageManager.LoadLanguageAsync("en-US");
                    if (newFallbackTranslations.Count > 0)
                    {
                        _fallbackTranslations = newFallbackTranslations;
                        _logger.LogInformation("Successfully reloaded {Count} fallback translations", newFallbackTranslations.Count);
                    }
                    else
                    {
                        _logger.LogWarning("Modified English file is empty, keeping existing fallback translations");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reloading English fallback file, keeping existing translations");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file change: {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Handles new file creation
    /// </summary>
    private async Task HandleFileCreated(string createdFileName, string filePath)
    {
        try
        {
            _logger.LogInformation("New language file created: {FileName}", createdFileName);
            
            // Validate the new file by attempting to load it
            var testTranslations = await _languageManager.LoadLanguageAsync(createdFileName);
            if (testTranslations.Count > 0)
            {
                _logger.LogInformation("New language file {FileName} validated successfully with {Count} translations", 
                    createdFileName, testTranslations.Count);
            }
            else
            {
                _logger.LogWarning("New language file {FileName} appears to be empty or invalid", createdFileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file creation: {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Finds the best fallback language when the current language is no longer available
    /// </summary>
    private async Task<string> FindBestFallbackLanguage(string unavailableLanguage)
    {
        try
        {
            // Try to find a similar language (same language family)
            var languagePrefix = unavailableLanguage.Split('-')[0];
            var similarLanguage = _availableLanguages.FirstOrDefault(l => 
                l.Code.StartsWith(languagePrefix, StringComparison.OrdinalIgnoreCase) && 
                !l.Code.Equals(unavailableLanguage, StringComparison.OrdinalIgnoreCase));
            
            if (similarLanguage != null)
            {
                _logger.LogInformation("Found similar language fallback: {UnavailableLanguage} -> {FallbackLanguage}", 
                    unavailableLanguage, similarLanguage.Code);
                return similarLanguage.Code;
            }

            // Try system language
            var systemLanguage = CultureInfo.CurrentUICulture.Name;
            if (_availableLanguages.Any(l => l.Code.Equals(systemLanguage, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogInformation("Using system language as fallback: {SystemLanguage}", systemLanguage);
                return systemLanguage;
            }

            // Default to English
            _logger.LogInformation("Using English as final fallback");
            return "en-US";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding fallback language, defaulting to English");
            return "en-US";
        }
    }

    /// <summary>
    /// Attempts recovery when file change handling fails
    /// </summary>
    private async Task AttemptFileChangeRecovery(LanguageFilesChangedEventArgs e, Exception originalException)
    {
        try
        {
            _logger.LogWarning("Attempting recovery from file change handling failure: {FilePath}", e.FilePath);
            
            // Ensure we still have a functional language service
            if (_currentTranslations.Count == 0)
            {
                _logger.LogWarning("Current translations lost, attempting to reload current language");
                
                try
                {
                    var recoveryTranslations = await _languageManager.LoadLanguageAsync(CurrentLanguage);
                    if (recoveryTranslations.Count > 0)
                    {
                        _currentTranslations = recoveryTranslations;
                        _logger.LogInformation("Successfully recovered current language translations");
                    }
                    else
                    {
                        // Fall back to cached fallback translations
                        if (_fallbackTranslations.Count > 0)
                        {
                            _currentTranslations = new Dictionary<string, string>(_fallbackTranslations);
                            CurrentLanguage = "en-US";
                            _logger.LogWarning("Recovered using cached fallback translations");
                        }
                        else
                        {
                            // Create emergency translations
                            CreateMinimalFallbackTranslations();
                            _currentTranslations = new Dictionary<string, string>(_fallbackTranslations);
                            CurrentLanguage = "en-US";
                            _logger.LogWarning("Created emergency translations for recovery");
                        }
                    }
                }
                catch (Exception recoveryEx)
                {
                    _logger.LogError(recoveryEx, "Recovery attempt failed, creating minimal emergency state");
                    
                    // Absolute minimal recovery
                    CreateMinimalFallbackTranslations();
                    _currentTranslations = new Dictionary<string, string>(_fallbackTranslations);
                    CurrentLanguage = "en-US";
                }
            }

            // Ensure we have at least one available language
            if (_availableLanguages.Count == 0)
            {
                _logger.LogWarning("No available languages after recovery, creating minimal list");
                _availableLanguages = new List<LanguageInfo>
                {
                    new LanguageInfo
                    {
                        Code = "en-US",
                        DisplayName = "English (Recovery)",
                        NativeName = "English (Recovery)",
                        FilePath = "recovery",
                        Version = "1.0.0"
                    }
                };
                OnPropertyChanged(nameof(AvailableLanguages));
            }
        }
        catch (Exception recoveryEx)
        {
            _logger.LogCritical(recoveryEx, "File change recovery failed completely. Original error: {OriginalError}", 
                originalException.Message);
        }
    }

    /// <summary>
    /// Compares two language lists to determine if they are equal
    /// </summary>
    private bool LanguageListsEqual(List<LanguageInfo> list1, List<LanguageInfo> list2)
    {
        if (list1.Count != list2.Count)
        {
            return false;
        }

        var codes1 = list1.Select(l => l.Code).OrderBy(c => c).ToList();
        var codes2 = list2.Select(l => l.Code).OrderBy(c => c).ToList();

        return codes1.SequenceEqual(codes2, StringComparer.OrdinalIgnoreCase);
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Disposes the localization service and cleans up event subscriptions
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (_languageManager != null)
            {
                _languageManager.LanguageFilesChanged -= OnLanguageFilesChanged;
            }
            _logger.LogDebug("LocalizationService disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing LocalizationService");
        }
    }
}