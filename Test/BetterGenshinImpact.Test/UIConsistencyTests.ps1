# UIConsistencyTests.ps1
# Script to verify UI consistency between refactored modules and original design

Write-Host "Starting UI Consistency Tests..." -ForegroundColor Green

# Define the modules to test
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

# UI elements to check for consistency
$uiElements = @(
    "Margin",
    "Padding",
    "HorizontalAlignment",
    "VerticalAlignment",
    "FontSize",
    "FontWeight",
    "Foreground",
    "Background",
    "BorderThickness",
    "CornerRadius"
)

# Function to test UI consistency for a module
function Test-UIConsistency {
    param (
        [string]$ModuleName
    )
    
    Write-Host "`nTesting UI consistency for: $ModuleName" -ForegroundColor Cyan
    
    # Check if the module file exists
    $xamlPath = "BetterGenshinImpact/View/Pages/TaskSettings/$ModuleName.xaml"
    
    if (-not (Test-Path $xamlPath)) {
        Write-Host "  [FAIL] XAML file not found: $xamlPath" -ForegroundColor Red
        return $false
    }
    
    # Read the XAML content
    $xamlContent = Get-Content $xamlPath -Raw
    
    # Check for UI element consistency
    $consistencyIssues = 0
    
    Write-Host "  Checking UI elements:" -ForegroundColor Yellow
    
    # Check for CardExpander styling
    if ($xamlContent -match "<ui:CardExpander\s+Margin=""0,0,0,12""") {
        Write-Host "    [PASS] CardExpander has correct margin" -ForegroundColor Green
    } else {
        Write-Host "    [WARN] CardExpander may have inconsistent margin" -ForegroundColor Yellow
        $consistencyIssues++
    }
    
    # Check for ContentPadding
    if ($xamlContent -match "ContentPadding=""0""") {
        Write-Host "    [PASS] CardExpander has correct content padding" -ForegroundColor Green
    } else {
        Write-Host "    [WARN] CardExpander may have inconsistent content padding" -ForegroundColor Yellow
        $consistencyIssues++
    }
    
    # Check for proper localization
    if ($xamlContent -match "{local:Localize\s+Key=") {
        Write-Host "    [PASS] Module uses localization" -ForegroundColor Green
    } else {
        Write-Host "    [WARN] Module may not be using localization properly" -ForegroundColor Yellow
        $consistencyIssues++
    }
    
    # Check for proper button styling
    if ($xamlContent -match "<ui:Button\s+Appearance=""Primary""") {
        Write-Host "    [PASS] Module uses primary button appearance" -ForegroundColor Green
    } else {
        Write-Host "    [WARN] Module may have inconsistent button styling" -ForegroundColor Yellow
        $consistencyIssues++
    }
    
    # Check for proper spacing between controls
    if ($xamlContent -match "Margin=""0,8,0,0""") {
        Write-Host "    [PASS] Module uses consistent control spacing" -ForegroundColor Green
    } else {
        Write-Host "    [WARN] Module may have inconsistent control spacing" -ForegroundColor Yellow
        $consistencyIssues++
    }
    
    if ($consistencyIssues -gt 0) {
        Write-Host "  Module has $consistencyIssues potential UI consistency issues" -ForegroundColor Yellow
        return $false
    } else {
        Write-Host "  [PASS] Module has consistent UI styling" -ForegroundColor Green
        return $true
    }
}

# Run tests for each module
$passedTests = 0
$warningTests = 0

foreach ($module in $modules) {
    $result = Test-UIConsistency -ModuleName $module
    if ($result) {
        $passedTests++
    } else {
        $warningTests++
    }
}

# Print summary
Write-Host "`n===== UI Consistency Test Summary =====" -ForegroundColor Magenta
Write-Host "Total modules tested: $($modules.Count)" -ForegroundColor White
Write-Host "Passed: $passedTests" -ForegroundColor Green
Write-Host "Warnings: $warningTests" -ForegroundColor Yellow

if ($warningTests -eq 0) {
    Write-Host "`nAll UI consistency tests PASSED!" -ForegroundColor Green
} else {
    Write-Host "`nSome modules have potential UI consistency issues. Please review the output above." -ForegroundColor Yellow
}