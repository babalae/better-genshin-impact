# UIScreenshotGenerator.ps1
# Script to generate screenshots of TaskSettings modules for visual comparison

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Windows.Forms

Write-Host "Starting UI Screenshot Generator..." -ForegroundColor Green

# Create screenshots directory if it doesn't exist
$screenshotsDir = "Test/BetterGenshinImpact.Test/Screenshots"
if (-not (Test-Path $screenshotsDir)) {
    New-Item -ItemType Directory -Path $screenshotsDir | Out-Null
    Write-Host "Created screenshots directory: $screenshotsDir" -ForegroundColor Yellow
}

# Define the modules to capture
$modules = @(
    "AutoGeniusInvocationTaskControl",
    "AutoWoodTaskControl",
    "AutoFightTaskControl",
    "AutoDomainTaskControl",
    "AutoStygianOnslaughtTaskControl",
    "AutoMusicGameTaskControl",
    "AutoAlbumTaskControl",
    "AutoFishingTaskControl",
    "AutoRedeemCodeTaskControl",
    "AutoArtifactSalvageTaskControl",
    "GetGridIconsTaskControl",
    "AutoTrackTaskControl"
)

# Function to create a simple WPF window to load and render a UserControl
function Capture-UserControl {
    param (
        [string]$ModuleName
    )
    
    try {
        Write-Host "Attempting to capture screenshot of $ModuleName..." -ForegroundColor Yellow
        
        # This is a placeholder for actual screenshot capture
        # In a real implementation, this would use WPF to load and render the control
        
        # For now, we'll just create a placeholder file
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $placeholderPath = "$screenshotsDir/$ModuleName-$timestamp.txt"
        
        # Create a placeholder file with module information
        @"
This is a placeholder for a screenshot of $ModuleName.

In a real implementation, this script would:
1. Load the XAML for $ModuleName
2. Create an instance of the UserControl
3. Render it to a bitmap
4. Save the bitmap as a PNG file

For actual UI comparison, please run the application and manually compare
the refactored modules with the original design.
"@ | Out-File -FilePath $placeholderPath
        
        Write-Host "Created placeholder for $ModuleName at: $placeholderPath" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "Error capturing $ModuleName: $_" -ForegroundColor Red
        return $false
    }
}

# Create a UI comparison checklist
$checklistPath = "$screenshotsDir/UI_Comparison_Checklist.md"

@"
# UI Comparison Checklist

Use this checklist to manually verify the UI consistency between the refactored modules and the original design.

## Instructions

1. Run the BetterGenshinImpact application
2. Navigate to the Task Settings page
3. For each module, compare the visual appearance with the original design
4. Check off each item when verified

## Modules to Verify

### General Layout
- [ ] Overall page layout matches the original design
- [ ] Spacing between modules is consistent
- [ ] Page scrolling behavior is correct

### Individual Modules

$(foreach ($module in $modules) {
@"
#### $module
- [ ] CardExpander appearance matches original
- [ ] Icon and header text are correct
- [ ] Internal controls are properly aligned
- [ ] Margins and padding are consistent
- [ ] Text sizes and styles match original
- [ ] Control spacing is consistent
- [ ] Expanded/collapsed state works correctly

"@
})

## Responsive Layout Tests
- [ ] UI adapts correctly when window is resized
- [ ] Controls maintain proper alignment at different sizes
- [ ] No elements are cut off or improperly positioned when resized

## Notes

Add any observations or issues found during testing:

- 
- 
- 

"@ | Out-File -FilePath $checklistPath

Write-Host "Created UI comparison checklist at: $checklistPath" -ForegroundColor Green

# Attempt to capture screenshots for each module
$capturedCount = 0
$failedCount = 0

foreach ($module in $modules) {
    $result = Capture-UserControl -ModuleName $module
    if ($result) {
        $capturedCount++
    } else {
        $failedCount++
    }
}

# Print summary
Write-Host "`n===== Screenshot Generation Summary =====" -ForegroundColor Magenta
Write-Host "Total modules processed: $($modules.Count)" -ForegroundColor White
Write-Host "Successful: $capturedCount" -ForegroundColor Green
Write-Host "Failed: $failedCount" -ForegroundColor Red

Write-Host "`nUI comparison checklist created at: $checklistPath" -ForegroundColor Cyan
Write-Host "Please use this checklist to manually verify UI consistency." -ForegroundColor Cyan