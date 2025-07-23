# Localization Verification Guide

This guide provides detailed instructions for verifying the localization implementation in the refactored TaskSettings modules.

## Purpose

The purpose of localization verification is to ensure that all text elements in the refactored modules are properly localized using the application's localization system, allowing for seamless language switching.

## Localization System Overview

BetterGenshinImpact uses a custom localization system with the following key components:

1. **Localize Markup Extension**: Used in XAML as `{local:Localize Key=key.name}`
2. **Localization Service**: Backend service that provides localized strings
3. **Language Files**: JSON files containing localized text for different languages

## Verification Process

### 1. Static Text Verification

For each module, verify that:

- All visible text uses the `{local:Localize Key=key.name}` markup extension
- No hardcoded strings are used for user-visible text
- All localization keys are valid and exist in the language files

### 2. Dynamic Text Verification

For text that is set programmatically:

- Verify that the `ILocalizationService.GetString()` method is used
- Check that localized strings are properly bound to UI elements
- Ensure that string formatting preserves localization

### 3. Language Switching Test

Test the modules with different language settings:

1. Change the application language
2. Verify that all text elements update correctly
3. Check for any text that remains in the original language
4. Verify that text layout adapts to different language lengths

### 4. Specific Elements to Check

#### Headers and Titles
- Module titles in CardExpander headers
- Section headings within modules

#### Control Labels
- Button text
- Checkbox labels
- ComboBox labels and items
- TextBox placeholders

#### Messages and Help Text
- Tooltips
- Help links
- Status messages

### 5. Localization Key Naming Convention

Verify that localization keys follow the established naming convention:

- `task.modulename` for module titles
- `task.modulename.setting` for specific settings
- `common.action` for common actions (start, stop, help)

## Testing Tools

### Manual Testing

1. **Visual Inspection**: Review each module in the UI
2. **Language Switching**: Test with different language settings
3. **XAML Review**: Check for proper use of localization markup

### Automated Testing

Use the provided PowerShell scripts to:

1. Scan XAML files for hardcoded strings
2. Verify that all expected localization keys are used
3. Check for consistency in localization implementation

## Reporting Issues

When reporting localization issues, include:

1. The specific module and element with the issue
2. The current text and whether it's hardcoded or using an incorrect key
3. The expected localization key that should be used
4. Screenshots showing the issue in different languages

## Resolution Process

For each identified issue:

1. Replace hardcoded strings with localization markup
2. Add missing localization keys to language files
3. Fix incorrect key references
4. Re-verify after changes

## Conclusion

Thorough localization verification ensures that the refactored modules maintain the application's multilingual support, providing a consistent experience for users of all languages.