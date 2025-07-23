# Task Settings Module Test Suite

This directory contains a comprehensive test suite for verifying the refactored TaskSettings modules.

## Quick Start

To run all tests, execute the following batch file:

```
RunTaskSettingsTests.bat
```

## Available Tests

### Functionality Tests
- **TaskSettingsModuleTests.ps1** - Verifies that all modules have the required controls, data bindings, and commands.
- **Run with:** `RunTaskSettingsModuleTests.bat`

### UI Consistency Tests
- **UIConsistencyTests.ps1** - Checks that UI styling is consistent across all modules.
- **UIScreenshotGenerator.ps1** - Creates a checklist for manual UI verification.
- **Run with:** `RunUIConsistencyTests.bat` or `GenerateUIScreenshots.bat`

### Data Binding and Localization Tests
- **DataBindingLocalizationTests.ps1** - Verifies data binding and localization implementation.
- **DetailedDataBindingTests.ps1** - Performs detailed data binding verification.
- **HardcodedStringChecker.ps1** - Identifies potential hardcoded strings.
- **Run with:** `RunDataBindingTests.bat`, `RunDetailedDataBindingTests.bat`, or `CheckHardcodedStrings.bat`

## Documentation

- **TaskSettingsTestDocumentation.md** - Main test documentation
- **UIVerificationGuide.md** - Guide for UI consistency verification
- **LocalizationVerificationGuide.md** - Guide for localization verification
- **TaskSettingsTestSummary.md** - Summary of test results

## Test Results

Test results are saved in the `Results` directory with timestamps for tracking.

## Adding New Tests

To add a new test:

1. Create a new PowerShell script in this directory
2. Add a corresponding batch file for easy execution
3. Update this README with information about the new test

## Best Practices

When testing TaskSettings modules:

1. Always verify against the requirements document
2. Check both functionality and appearance
3. Ensure data binding paths are correct
4. Verify localization is implemented properly
5. Document any issues found during testing