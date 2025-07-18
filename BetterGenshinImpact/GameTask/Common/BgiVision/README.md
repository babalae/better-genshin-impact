# BGI Playwright-Inspired API

This document describes the new Playwright-inspired API for Better Genshin Impact automation tasks. The API provides a simple, elegant, and powerful way to interact with the game UI using automatic retry mechanisms and fluent syntax.

## Overview

The BGI Playwright API consists of several key components:

- **BgiPage**: Main entry point for automation, similar to Playwright's Page class
- **BgiLocator**: Used to find elements on the screen with automatic retry
- **BgiElement**: Represents a found element with interaction methods
- **BgiUI**: Provides preset locators for common UI elements
- **BgiPageExtensions**: Additional utility methods for common operations

## Key Features

- **Automatic Retry**: All operations include built-in retry logic with configurable timeouts
- **Fluent API**: Method chaining for readable and maintainable code
- **Template Matching**: Find elements using image templates
- **OCR Integration**: Find elements by text content
- **Color Detection**: Find elements by color ranges
- **Drag & Drop**: Support for complex drag and drop operations
- **Screenshot Support**: Take screenshots of the entire screen or specific regions
- **Multiple Element Waiting**: Wait for any or all elements to appear
- **Preset UI Elements**: Pre-configured locators for common Genshin Impact UI elements

## Basic Usage

### Creating a Page

```csharp
using var page = BgiUI.NewPage(cancellationToken);
```

### Finding Elements

```csharp
// By template image
var confirmButton = page.Locator("confirm_button.png");

// By text (OCR)
var startButton = page.GetByText("开始");

// Using preset UI elements
var paimonMenu = BgiUI.PaimonMenu(page);
var whiteConfirmButton = BgiUI.WhiteConfirmButton(page);
```

### Interacting with Elements

```csharp
// Wait for element and click
await confirmButton.WaitAndClick(timeout: 5000);

// Click if visible (no waiting)
await confirmButton.ClickIfVisible();

// Click repeatedly until element disappears
await confirmButton.ClickUntilGone(timeout: 10000);

// Get text content
var text = await element.GetTextAsync();
```

### Keyboard and Mouse Operations

```csharp
// Press keys
page.PressKey(VirtualKeyCode.ESCAPE);

// Hold key for duration
await page.HoldKey(VirtualKeyCode.W, duration: 2000);

// Type text
page.Type("Hello World");

// Click at coordinates
await page.ClickAt(100, 200);

// Drag and drop
await page.DragFromTo(100, 200, 300, 400);
```

## Advanced Usage

### Waiting for Multiple Elements

```csharp
var buttons = new[]
{
    BgiUI.WhiteConfirmButton(page),
    BgiUI.BlackConfirmButton(page),
    BgiUI.OnlineYesButton(page)
};

// Wait for any button to appear
var buttonIndex = await page.WaitForAny(buttons, timeout: 10000);

// Wait for all buttons to appear
var allVisible = await page.WaitForAll(buttons, timeout: 10000);
```

### Color-Based Detection

```csharp
// Define color range (HSV)
var lowerRed = new Scalar(0, 120, 70);
var upperRed = new Scalar(10, 255, 255);

// Check if color exists
if (page.HasColor(lowerRed, upperRed))
{
    // Handle low health
}

// Wait for color to appear/disappear
await page.WaitForColor(lowerRed, upperRed, timeout: 5000);
await page.WaitForColorGone(lowerRed, upperRed, timeout: 5000);
```

### Region-Based Operations

```csharp
// Define a region of interest
var minimapRegion = new Rect(0, 0, 300, 300);

// Find elements only within the region
var trackPoint = BgiUI.BlueTrackPoint(page).WithRegion(minimapRegion);

// Take screenshot of specific region
page.SaveScreenshot("minimap.png", minimapRegion);
```

### Element Manipulation

```csharp
var element = await locator.WaitFor(timeout: 5000);

// Different click types
await element.ClickAsync();
await element.RightClickAsync();
await element.DoubleClickAsync();

// Click at relative position within element
await element.ClickRelativeAsync(0.5, 0.5); // Center
await element.ClickRelativeAsync(0.0, 0.0); // Top-left
await element.ClickRelativeAsync(1.0, 1.0); // Bottom-right

// Drag to another element or coordinates
await element.DragTo(targetElement);
await element.DragTo(targetX, targetY);

// Hover operations
await element.HoverAsync();

// Scroll operations
await element.ScrollAsync(scrollAmount: 3);
```

## Common Patterns

### Button Clicking with Fallback

```csharp
// Try to click any available confirm button
if (await BgiUI.WaitForAndClickConfirm(page, timeout: 10000))
{
    Logger.LogInformation("Confirm button clicked");
}
else
{
    Logger.LogWarning("No confirm button found");
}
```

### Complex Workflow

```csharp
using var page = BgiUI.NewPage(ct);

// Wait for game to be ready
await page.WaitForGameReady(timeout: 30000);

// Open inventory
page.PressKey(VirtualKeyCode.B);
await page.Wait(2000);

// Navigate to artifacts tab
var artifactTab = page.GetByText("圣遗物");
await artifactTab.WaitAndClick(timeout: 5000);

// Perform salvage operation
var salvageButton = BgiUI.ArtifactSalvageButton(page);
if (await salvageButton.WaitAndClick(timeout: 5000))
{
    var confirmButton = BgiUI.ArtifactSalvageConfirmButton(page);
    await confirmButton.WaitAndClick(timeout: 5000);
}

// Close inventory
page.PressKey(VirtualKeyCode.ESCAPE);
```

### Element Detection and Interaction

```csharp
// Find F key interaction prompt
var fKeyPrompt = BgiUI.FKey(page);
var element = await fKeyPrompt.WaitFor(timeout: 10000);

if (element != null)
{
    // Get interaction text
    var interactionText = await element.GetTextAsync();
    Logger.LogInformation($"Interaction: {interactionText}");
    
    // Perform interaction
    page.PressKey(VirtualKeyCode.F);
    
    // Wait for interaction to complete
    await element.WaitForHidden(timeout: 10000);
}
```

## Error Handling

The API includes comprehensive error handling:

```csharp
try
{
    var element = await locator.WaitFor(timeout: 5000);
    await element.ClickAsync();
}
catch (TimeoutException)
{
    Logger.LogWarning("Element not found within timeout");
}
catch (Exception ex)
{
    Logger.LogError(ex, "Unexpected error during operation");
}
```

## Best Practices

1. **Always use `using` statements** for BgiPage to ensure proper disposal
2. **Set appropriate timeouts** based on expected operation duration
3. **Use preset UI elements** from BgiUI when available
4. **Handle exceptions** gracefully with appropriate logging
5. **Use cancellation tokens** for long-running operations
6. **Take screenshots** for debugging complex interactions
7. **Use regions** to improve performance and accuracy
8. **Combine multiple strategies** (template + OCR + color) for robust detection

## Performance Tips

- Use smaller search regions when possible
- Cache elements that are used multiple times
- Use appropriate thresholds for template matching
- Avoid unnecessary screenshots and OCR operations
- Use color detection for simple state checks

## Integration with Existing Code

The new API is designed to work seamlessly with existing BGI code:

```csharp
// Can be used in existing task classes
public class MyAutomationTask : ISoloTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        
        // Your automation logic here
        await page.WaitForGameReady();
        
        // ... rest of your task
    }
}
```

This API provides a powerful, flexible, and user-friendly way to create automation tasks for Better Genshin Impact while maintaining the reliability and performance of the existing system.