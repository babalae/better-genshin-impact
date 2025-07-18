# Integration Guide: Migrating to the New Playwright-Inspired API

This guide helps you migrate existing tasks to use the new Playwright-inspired API while maintaining compatibility with the existing codebase.

## Quick Start

### 1. Basic Migration Pattern

**Old Code:**
```csharp
public async Task RunAsync(CancellationToken ct)
{
    const int maxRetries = 10;
    
    for (int i = 0; i < maxRetries; i++)
    {
        using var screen = TaskControl.CaptureToRectArea();
        using var button = screen.Find(ElementAssets.Instance.BtnWhiteConfirm);
        
        if (button.IsExist())
        {
            button.Click();
            return;
        }
        
        await TaskControl.Delay(1000, ct);
    }
}
```

**New Code:**
```csharp
public async Task RunAsync(CancellationToken ct)
{
    using var page = BgiUI.NewPage(ct);
    
    var confirmButton = BgiUI.WhiteConfirmButton(page);
    await confirmButton.WaitAndClick(timeout: 10000);
}
```

### 2. OCR-Based Element Finding

**Old Code:**
```csharp
using var screen = TaskControl.CaptureToRectArea();
var ocrResults = screen.FindMulti(new RecognitionObject
{
    RecognitionType = RecognitionTypes.Ocr
});

foreach (var result in ocrResults)
{
    if (result.Text.Contains("开始"))
    {
        result.Click();
        break;
    }
}
```

**New Code:**
```csharp
using var page = BgiUI.NewPage(ct);
var startButton = page.GetByText("开始");
await startButton.WaitAndClick(timeout: 5000);
```

### 3. Complex Workflows

**Old Code:**
```csharp
// Multiple nested loops and manual state management
for (int i = 0; i < maxRetries; i++)
{
    using var screen = TaskControl.CaptureToRectArea();
    // ... complex logic ...
    await TaskControl.Delay(1000, ct);
}
```

**New Code:**
```csharp
using var page = BgiUI.NewPage(ct);

// Clean, readable workflow
await page.WaitForGameReady();
page.PressKey(VirtualKeyCode.B);
await page.GetByText("圣遗物").WaitAndClick();
await BgiUI.ArtifactSalvageButton(page).WaitAndClick();
```

## Migration Strategies

### Strategy 1: Full Migration (Recommended)

Replace entire methods with new API:

```csharp
public class MyTask : ISoloTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        
        // All your automation logic using the new API
        await page.WaitForGameReady();
        // ... rest of your logic
    }
}
```

### Strategy 2: Gradual Migration

Mix old and new APIs during transition:

```csharp
public class MyTask : ISoloTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        // Use old API for complex existing logic
        await ExistingComplexMethod(ct);
        
        // Use new API for new features
        using var page = BgiUI.NewPage(ct);
        await page.GetByText("新功能").WaitAndClick();
    }
    
    private async Task ExistingComplexMethod(CancellationToken ct)
    {
        // Keep existing implementation for now
        using var screen = TaskControl.CaptureToRectArea();
        // ... existing logic
    }
}
```

### Strategy 3: Wrapper Methods

Create wrapper methods for common patterns:

```csharp
public class MyTaskHelpers
{
    public static async Task<bool> ClickAnyConfirmButton(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        return await BgiUI.WaitForAndClickConfirm(page, timeout: 10000);
    }
    
    public static async Task<bool> WaitForMainUI(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        return await page.WaitForGameReady(timeout: 30000);
    }
}
```

## Common Migration Patterns

### Pattern 1: Button Clicking with Retry

**Before:**
```csharp
bool buttonClicked = false;
for (int i = 0; i < 10; i++)
{
    using var screen = TaskControl.CaptureToRectArea();
    using var button = screen.Find(ElementAssets.Instance.BtnWhiteConfirm);
    
    if (button.IsExist())
    {
        button.Click();
        buttonClicked = true;
        break;
    }
    await TaskControl.Delay(1000, ct);
}
```

**After:**
```csharp
using var page = BgiUI.NewPage(ct);
bool buttonClicked = await BgiUI.WhiteConfirmButton(page).WaitAndClick(timeout: 10000);
```

### Pattern 2: Text-Based Element Finding

**Before:**
```csharp
bool found = false;
for (int i = 0; i < 10; i++)
{
    using var screen = TaskControl.CaptureToRectArea();
    var ocrResults = screen.FindMulti(new RecognitionObject
    {
        RecognitionType = RecognitionTypes.Ocr
    });
    
    foreach (var result in ocrResults)
    {
        if (result.Text.Contains("设置"))
        {
            result.Click();
            found = true;
            break;
        }
    }
    
    if (found) break;
    await TaskControl.Delay(1000, ct);
}
```

**After:**
```csharp
using var page = BgiUI.NewPage(ct);
bool found = await page.GetByText("设置").WaitAndClick(timeout: 10000);
```

### Pattern 3: Multiple Element Checking

**Before:**
```csharp
bool anyButtonFound = false;
for (int i = 0; i < 10; i++)
{
    using var screen = TaskControl.CaptureToRectArea();
    
    using var whiteConfirm = screen.Find(ElementAssets.Instance.BtnWhiteConfirm);
    if (whiteConfirm.IsExist())
    {
        whiteConfirm.Click();
        anyButtonFound = true;
        break;
    }
    
    using var blackConfirm = screen.Find(ElementAssets.Instance.BtnBlackConfirm);
    if (blackConfirm.IsExist())
    {
        blackConfirm.Click();
        anyButtonFound = true;
        break;
    }
    
    await TaskControl.Delay(1000, ct);
}
```

**After:**
```csharp
using var page = BgiUI.NewPage(ct);
bool anyButtonFound = await BgiUI.WaitForAndClickConfirm(page, timeout: 10000);
```

## Integration with Existing Assets

The new API works seamlessly with existing assets:

```csharp
// Use existing RecognitionObject
var customButton = page.Locator(MyAssets.Instance.CustomButtonRo);
await customButton.WaitAndClick();

// Use existing ElementAssets
var paimonMenu = page.Locator(ElementAssets.Instance.PaimonMenuRo);
await paimonMenu.WaitFor(timeout: 5000);
```

## Best Practices for Migration

### 1. Start with Simple Cases
Begin by migrating simple button clicks and text finding operations.

### 2. Use Preset UI Elements
Leverage `BgiUI` preset elements for common operations.

### 3. Handle Exceptions Gracefully
```csharp
try
{
    using var page = BgiUI.NewPage(ct);
    await page.GetByText("目标文本").WaitAndClick(timeout: 5000);
}
catch (TimeoutException)
{
    Logger.LogWarning("Element not found within timeout");
    // Fallback logic
}
```

### 4. Maintain Backward Compatibility
Keep existing methods working while adding new API usage.

### 5. Test Thoroughly
Test migrated code thoroughly to ensure functionality is preserved.

## Performance Considerations

### Memory Management
- Always use `using` statements for BgiPage
- The new API handles resource disposal automatically
- Avoid creating multiple pages unnecessarily

### Timeout Settings
- Set appropriate timeouts based on expected operation duration
- Use longer timeouts for complex operations
- Use shorter timeouts for quick checks

### Region Optimization
```csharp
// Use regions to improve performance
var minimapRegion = new Rect(0, 0, 300, 300);
var trackPoint = BgiUI.BlueTrackPoint(page).WithRegion(minimapRegion);
```

## Common Gotchas

### 1. Cancellation Token Handling
Make sure to pass the cancellation token when creating pages:
```csharp
using var page = BgiUI.NewPage(ct); // ✓ Correct
using var page = BgiUI.NewPage();   // ✗ Missing cancellation token
```

### 2. Resource Disposal
Always use `using` statements:
```csharp
using var page = BgiUI.NewPage(ct); // ✓ Correct
var page = BgiUI.NewPage(ct);       // ✗ Missing using statement
```

### 3. Exception Handling
Handle TimeoutException specifically:
```csharp
try
{
    await element.WaitFor(timeout: 5000);
}
catch (TimeoutException)
{
    // Handle timeout specifically
}
catch (Exception ex)
{
    // Handle other exceptions
}
```

## Getting Help

- Check the README.md for comprehensive API documentation
- Review examples in the Examples folder
- Look at ApiComparisonExamples.cs for before/after comparisons
- Use the existing BgiVision classes as reference for complex operations

Remember: The new API is designed to make your code more readable, maintainable, and robust. Take advantage of its built-in retry mechanisms and fluent interface to simplify your automation tasks.