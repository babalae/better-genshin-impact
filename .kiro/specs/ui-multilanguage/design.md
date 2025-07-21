# Design Document

## Overview

The multi-language system will be implemented using a combination of WPF's built-in localization capabilities and a custom localization service. The design leverages .NET's Microsoft.Extensions.Localization framework already included in the project, combined with a custom markup extension for XAML binding and automatic language file discovery.

The system will use JSON-based language files for easy translation management, stored in a dedicated `Languages` directory. A centralized `LocalizationService` will handle language switching, file discovery, and provide localized strings throughout the application.

## Architecture

### Core Components

1. **LocalizationService** - Central service managing language switching and string retrieval
2. **LocalizeExtension** - XAML markup extension for binding localized strings
3. **LanguageManager** - Handles language file discovery and loading
4. **LocalizationViewModel** - ViewModel for language selection UI
5. **Language Files** - JSON files containing translations

### Technology Stack

- **Microsoft.Extensions.Localization** - Already included in the project for localization infrastructure
- **System.Text.Json** - For parsing JSON language files
- **WPF Data Binding** - For automatic UI updates when language changes
- **INotifyPropertyChanged** - For reactive language switching

## Components and Interfaces

### 1. LocalizationService

```csharp
public interface ILocalizationService : INotifyPropertyChanged
{
    string CurrentLanguage { get; }
    IEnumerable<LanguageInfo> AvailableLanguages { get; }
    string GetString(string key, params object[] args);
    Task SetLanguageAsync(string languageCode);
    event EventHandler<LanguageChangedEventArgs> LanguageChanged;
}

public class LocalizationService : ILocalizationService
{
    // Implementation handles language switching and string retrieval
}
```

### 2. XAML Markup Extension

```csharp
public class LocalizeExtension : MarkupExtension, INotifyPropertyChanged
{
    public string Key { get; set; }
    public object[] Args { get; set; }
    
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Returns binding that updates when language changes
    }
}
```

### 3. Language Manager

```csharp
public interface ILanguageManager
{
    Task<IEnumerable<LanguageInfo>> DiscoverLanguagesAsync();
    Task<Dictionary<string, string>> LoadLanguageAsync(string languageCode);
}

public class LanguageManager : ILanguageManager
{
    // Handles file discovery and loading
}
```

### 4. Language Information Model

```csharp
public class LanguageInfo
{
    public string Code { get; set; }        // e.g., "en-US", "zh-CN"
    public string DisplayName { get; set; }  // e.g., "English", "中文"
    public string NativeName { get; set; }   // e.g., "English", "中文"
    public string FilePath { get; set; }     // Path to language file
}
```

## Data Models

### Language File Structure

Language files will be stored as JSON in the `Languages` directory with the following structure:

```json
{
  "metadata": {
    "code": "en-US",
    "displayName": "English",
    "nativeName": "English",
    "version": "1.0.0"
  },
  "strings": {
    "common.ok": "OK",
    "common.cancel": "Cancel",
    "common.save": "Save",
    "settings.title": "Settings",
    "settings.language": "Language",
    "autofight.title": "Auto Fight",
    "autofight.enabled": "Enable Auto Fight",
    "macro.title": "Macro Settings",
    "macro.priority": "Default Combat Macro Priority",
    "trigger.title": "Trigger Settings"
  }
}
```

### Directory Structure

```
Languages/
├── en-US.json          # English (default)
├── zh-CN.json          # Simplified Chinese
├── zh-TW.json          # Traditional Chinese
├── ja-JP.json          # Japanese
├── ko-KR.json          # Korean
└── ...                 # Additional languages
```

### File Naming Convention

- Files named using standard language codes (RFC 5646)
- Format: `{language}-{region}.json` (e.g., `en-US.json`, `zh-CN.json`)
- Fallback to language-only codes when region not specified (e.g., `en.json`)

## Error Handling

### Missing Translation Keys

1. **Primary Strategy**: Return key name with prefix (e.g., `[KEY_NOT_FOUND: settings.title]`)
2. **Fallback Strategy**: Attempt to load from default language (English)
3. **Logging**: Log missing keys for translation team awareness

### Missing Language Files

1. **Graceful Degradation**: Continue with available languages
2. **Default Language**: Always ensure English is available as fallback
3. **User Notification**: Inform user if selected language becomes unavailable

### Malformed JSON

1. **Validation**: Validate JSON structure on load
2. **Error Recovery**: Skip malformed files and log errors
3. **Fallback**: Use default language if current language file is corrupted

## Testing Strategy

### Unit Tests

1. **LocalizationService Tests**
   - Language switching functionality
   - String retrieval with parameters
   - Event firing on language change

2. **LanguageManager Tests**
   - File discovery accuracy
   - JSON parsing correctness
   - Error handling for malformed files

3. **LocalizeExtension Tests**
   - XAML binding functionality
   - Property change notifications
   - Parameter formatting

### Integration Tests

1. **End-to-End Language Switching**
   - UI updates across all pages
   - Settings persistence
   - Application restart behavior

2. **File System Integration**
   - Language file discovery
   - Dynamic file addition/removal
   - File watching for development

### UI Tests

1. **Visual Validation**
   - Text rendering in different languages
   - Layout adjustments for longer text
   - Font support for various character sets

2. **User Experience**
   - Language selection workflow
   - Immediate UI feedback
   - Settings persistence

## Implementation Phases

### Phase 1: Core Infrastructure
- Implement LocalizationService and LanguageManager
- Create base language files (English, Chinese)
- Set up dependency injection

### Phase 2: XAML Integration
- Implement LocalizeExtension markup extension
- Create language selection UI
- Update key pages with localization

### Phase 3: Comprehensive Localization
- Extract all hardcoded strings
- Create complete language files
- Add additional language support

### Phase 4: Polish and Testing
- Comprehensive testing
- Performance optimization
- Documentation and translation guidelines

## Integration Points

### Dependency Injection

The localization services will be registered in `App.xaml.cs`:

```csharp
services.AddSingleton<ILanguageManager, LanguageManager>();
services.AddSingleton<ILocalizationService, LocalizationService>();
```

### Settings Integration

Language preference will be stored in the existing configuration system:

```csharp
public class CommonConfig
{
    public string Language { get; set; } = "en-US";
    // ... existing properties
}
```

### XAML Usage

Pages will use the new markup extension:

```xml
<ui:TextBlock Text="{local:Localize Key=settings.title}" />
<ui:Button Content="{local:Localize Key=common.save}" />
```

## Performance Considerations

1. **Lazy Loading**: Load language files only when needed
2. **Caching**: Cache loaded translations in memory
3. **Async Operations**: Use async/await for file operations
4. **Memory Management**: Dispose of unused language data

## Security Considerations

1. **File Validation**: Validate JSON structure and content
2. **Path Security**: Ensure language files are loaded from expected directory only
3. **Input Sanitization**: Sanitize user-provided translation parameters