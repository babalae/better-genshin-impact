# Task Settings Module Test Documentation

This document provides instructions for running the comprehensive test suite for the refactored Task Settings modules.

## Overview

The test suite consists of three main components:

1. **Functionality Tests** - Verify that all modules have the required controls, data bindings, and commands.
2. **UI Consistency Tests** - Ensure that the UI styling is consistent across all modules.
3. **Data Binding and Localization Tests** - Validate that data binding and localization are implemented correctly.

## Prerequisites

- Windows PowerShell 5.1 or later
- Access to the BetterGenshinImpact project files

## Running the Tests

### Option 1: Run the Complete Test Suite

To run all tests at once, execute the main test runner script:

```powershell
.\Test\BetterGenshinImpact.Test\RunTaskSettingsTests.ps1
```

This will run all test scripts and generate log files in the `Test/BetterGenshinImpact.Test/Results` directory.

### Option 2: Run Individual Test Scripts

You can also run each test script individually:

```powershell
# Run functionality tests
.\Test\BetterGenshinImpact.Test\TaskSettingsModuleTests.ps1

# Run UI consistency tests
.\Test\BetterGenshinImpact.Test\UIConsistencyTests.ps1

# Run data binding and localization tests
.\Test\BetterGenshinImpact.Test\DataBindingLocalizationTests.ps1
```

## Test Details

### Functionality Tests

The functionality tests verify that each module:

- Has the required XAML and code-behind files
- Contains all necessary UI controls (CardExpander, Buttons, etc.)
- Implements the correct data bindings
- Uses the appropriate commands

These tests ensure that the refactored modules maintain all the functionality of the original implementation.

### UI Consistency Tests

The UI consistency tests check that each module:

- Uses consistent margins and padding
- Applies the same styling to common elements
- Maintains consistent spacing between controls
- Uses the correct CardExpander configuration

These tests help ensure that the refactored UI looks identical to the original design.

### Data Binding and Localization Tests

These tests verify that each module:

- Correctly sets up data binding to the ViewModel
- Properly implements localization using the {local:Localize} markup extension
- Declares all required namespaces
- Uses command bindings appropriately

## Interpreting Test Results

- **[PASS]** - The test passed successfully
- **[WARN]** - The test found a potential issue that should be reviewed
- **[FAIL]** - The test failed and requires attention

## Manual Verification

In addition to the automated tests, the following manual checks should be performed:

1. **Visual Inspection** - Compare the refactored UI with screenshots of the original UI
2. **Functional Testing** - Test each module's functionality in the running application
3. **Responsive Layout** - Verify that the UI adapts correctly when resizing the window

## Troubleshooting

If tests fail, check the following:

1. Ensure all required files are in the correct locations
2. Verify that namespace declarations match the project structure
3. Check that data binding paths are correct
4. Confirm that all commands are properly implemented in the ViewModel

## Reporting Issues

Document any issues found during testing, including:

- The specific module and test that failed
- The expected vs. actual behavior
- Any error messages or logs
- Steps to reproduce the issue

## Conclusion

These tests help ensure that the refactored Task Settings modules maintain full compatibility with the original implementation while improving code maintainability and organization.