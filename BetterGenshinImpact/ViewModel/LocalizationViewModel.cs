using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// ViewModel for managing language selection and localization
/// </summary>
public partial class LocalizationViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<LocalizationViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<LanguageInfo> _availableLanguages = new();

    [ObservableProperty]
    private LanguageInfo? _selectedLanguage;

    [ObservableProperty]
    private bool _isLoading;

    public LocalizationViewModel(ILocalizationService localizationService, ILogger<LocalizationViewModel> logger)
    {
        _localizationService = localizationService;
        _logger = logger;

        // Subscribe to language change events
        _localizationService.LanguageChanged += OnLanguageChanged;
        
        // Subscribe to property changes to detect when available languages change
        _localizationService.PropertyChanged += OnLocalizationServicePropertyChanged;
    }

    /// <summary>
    /// Initializes the view model by loading available languages
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            _logger.LogInformation("Initializing LocalizationViewModel");

            // Load available languages
            var languages = _localizationService.AvailableLanguages.ToList();
            AvailableLanguages.Clear();
            foreach (var language in languages)
            {
                AvailableLanguages.Add(language);
                _logger.LogDebug("Added language: Code={Code}, DisplayName={DisplayName}, NativeName={NativeName}", 
                    language.Code, language.DisplayName, language.NativeName);
            }

            // Set the currently selected language
            var currentLanguageCode = _localizationService.CurrentLanguage;
            SelectedLanguage = AvailableLanguages.FirstOrDefault(l => 
                l.Code.Equals(currentLanguageCode, StringComparison.OrdinalIgnoreCase));

            _logger.LogInformation("LocalizationViewModel initialized with {Count} languages. Selected: {SelectedLanguage}", 
                AvailableLanguages.Count, SelectedLanguage?.DisplayName ?? "None");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize LocalizationViewModel");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Command to change the selected language
    /// </summary>
    [RelayCommand]
    private async Task ChangeLanguageAsync(LanguageInfo? language)
    {
        if (language == null || IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            _logger.LogInformation("Changing language to: {Language}", language.Code);

            await _localizationService.SetLanguageAsync(language.Code);

            _logger.LogInformation("Language changed successfully to: {Language}", language.Code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change language to: {Language}", language?.Code);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Handles language change events from the localization service
    /// </summary>
    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        // Update the selected language to match the service
        var newLanguage = AvailableLanguages.FirstOrDefault(l => 
            l.Code.Equals(e.NewLanguage, StringComparison.OrdinalIgnoreCase));
        
        if (newLanguage != null && !Equals(SelectedLanguage, newLanguage))
        {
            SelectedLanguage = newLanguage;
            _logger.LogDebug("Updated selected language to: {Language}", newLanguage.Code);
        }
    }

    /// <summary>
    /// Handles property change events from the localization service
    /// </summary>
    private async void OnLocalizationServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ILocalizationService.AvailableLanguages))
        {
            try
            {
                _logger.LogInformation("Available languages changed, refreshing language list");
                
                // Refresh the available languages list
                var languages = _localizationService.AvailableLanguages.ToList();
                
                // Update the collection on the UI thread
                if (Application.Current?.Dispatcher != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var currentSelection = SelectedLanguage;
                        
                        AvailableLanguages.Clear();
                        foreach (var language in languages)
                        {
                            AvailableLanguages.Add(language);
                        }
                        
                        // Try to maintain the current selection
                        if (currentSelection != null)
                        {
                            var newSelection = AvailableLanguages.FirstOrDefault(l => 
                                l.Code.Equals(currentSelection.Code, StringComparison.OrdinalIgnoreCase));
                            SelectedLanguage = newSelection;
                        }
                        
                        // If no selection or selection is invalid, select current language from service
                        if (SelectedLanguage == null)
                        {
                            var currentLanguageCode = _localizationService.CurrentLanguage;
                            SelectedLanguage = AvailableLanguages.FirstOrDefault(l => 
                                l.Code.Equals(currentLanguageCode, StringComparison.OrdinalIgnoreCase));
                        }
                        
                        _logger.LogInformation("Language list refreshed with {Count} languages", AvailableLanguages.Count);
                    });
                }
                else
                {
                    // Fallback for non-UI thread scenarios
                    var currentSelection = SelectedLanguage;
                    
                    AvailableLanguages.Clear();
                    foreach (var language in languages)
                    {
                        AvailableLanguages.Add(language);
                    }
                    
                    // Try to maintain the current selection
                    if (currentSelection != null)
                    {
                        var newSelection = AvailableLanguages.FirstOrDefault(l => 
                            l.Code.Equals(currentSelection.Code, StringComparison.OrdinalIgnoreCase));
                        SelectedLanguage = newSelection;
                    }
                    
                    // If no selection or selection is invalid, select current language from service
                    if (SelectedLanguage == null)
                    {
                        var currentLanguageCode = _localizationService.CurrentLanguage;
                        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => 
                            l.Code.Equals(currentLanguageCode, StringComparison.OrdinalIgnoreCase));
                    }
                    
                    _logger.LogInformation("Language list refreshed with {Count} languages", AvailableLanguages.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh available languages");
            }
        }
    }

    /// <summary>
    /// Gets the display text for a language (native name with fallback to display name)
    /// </summary>
    public static string GetLanguageDisplayText(LanguageInfo language)
    {
        if (language == null) return string.Empty;
        
        return !string.IsNullOrEmpty(language.NativeName) 
            ? language.NativeName 
            : language.DisplayName;
    }

    /// <summary>
    /// Disposes the view model and cleans up event subscriptions
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (_localizationService != null)
            {
                _localizationService.LanguageChanged -= OnLanguageChanged;
                _localizationService.PropertyChanged -= OnLocalizationServicePropertyChanged;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing LocalizationViewModel");
        }
    }
}