# UI Verification Guide

This guide provides detailed instructions for verifying the UI consistency of the refactored TaskSettings modules.

## Purpose

The purpose of UI verification is to ensure that the refactored modules maintain the same visual appearance and behavior as the original implementation. This is critical for maintaining a consistent user experience.

## Verification Process

### 1. Visual Comparison

#### Setup
1. Take screenshots of the original TaskSettingsPage before refactoring
2. Take screenshots of the refactored TaskSettingsPage after implementation
3. Place both sets of screenshots side by side for comparison

#### Elements to Compare
For each module, verify the following visual elements:

- **Layout**: Overall arrangement of controls
- **Spacing**: Margins and padding between elements
- **Typography**: Font sizes, weights, and styles
- **Colors**: Background, foreground, and accent colors
- **Icons**: Correct icons are used and properly sized
- **Borders**: Border thickness and corner radius
- **Alignment**: Horizontal and vertical alignment of elements

### 2. Responsive Layout Testing

Test the UI at different window sizes to ensure responsive behavior:

1. Start with the window at full size
2. Gradually reduce the width and observe how elements reflow
3. Check for any elements that become cut off or misaligned
4. Verify that scrolling behavior works correctly

### 3. Interactive Element Testing

Verify that all interactive elements behave consistently:

- **CardExpanders**: Expand and collapse correctly
- **Buttons**: Proper hover and click effects
- **ComboBoxes**: Dropdown appears in the correct position
- **CheckBoxes**: Toggle state is visually clear
- **TextBoxes**: Focus and input behavior is consistent
- **NumberBoxes**: Increment/decrement controls work properly

### 4. Specific Module Verification

#### AutoGeniusInvocationTaskControl
- Verify strategy selection dropdown works correctly
- Check that the start button has the correct styling
- Ensure the help link is properly positioned

#### AutoWoodTaskControl
- Verify number input controls have consistent styling
- Check that round number and daily max count controls are aligned
- Ensure the start button and help link are properly positioned

#### AutoFightTaskControl
- Verify the nested CardExpanders work correctly
- Check that all checkboxes are properly aligned
- Ensure strategy selection works correctly

#### AutoDomainTaskControl
- Verify domain selection dropdown works correctly
- Check that round number control is properly styled
- Ensure artifact salvage settings are correctly displayed

#### Other Modules
- Apply similar verification steps to all remaining modules

## Reporting Issues

When reporting UI inconsistencies, include:

1. The specific module and element with the issue
2. A screenshot highlighting the problem
3. A description of how it differs from the original
4. The expected appearance or behavior

## Resolution Process

For each identified issue:

1. Determine the cause of the inconsistency
2. Make the necessary XAML adjustments
3. Re-verify after changes
4. Document the fix for future reference

## Conclusion

Thorough UI verification ensures that the refactored modules provide the same user experience as the original implementation while benefiting from improved code organization and maintainability.