# DataBindingLocalizationTests.ps1
# Script to verify data binding and localization in refactored modules

Write-Host "Starting Data Binding and Localization Tests..." -ForegroundColor Green

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

# Function to test data binding and localization for a module
function Test-DataBindingAndLocalization {
    param (
        [string]$ModuleName
    )
    
    Write-Host "`nTesting data binding and localization for: $ModuleName" -ForegroundColor Cyan
    
    # Check if the module file exists
    $xamlPath = "BetterGenshinImpact/View/Pages/TaskSettings/$ModuleName.xaml"
    
    if (-not (Test-Path $xamlPath)) {
        Write-Host "  [FAIL] XAML file not found: $xamlPath" -ForegroundColor Red
        return $false
    }
    
    # Read the XAML content
    $xamlContent = Get-Content $xamlPath -Raw
    
    # Check for data binding
    $dataBindingCount = ([regex]::Matches($xamlContent, "{Binding\s+[^}]+}")).Count
    Write-Host "  Found $dataBindingCount data binding expressions" -ForegroundColor Yellow
    
    if ($dataBindingCount -eq 0) {
        Write-Host "  [FAIL] No data binding found in module" -ForegroundColor Red
        return $false
    } else {
        Write-Host "  [PASS] Module uses data binding" -ForegroundColor Green
    }
    
    # Check for DataContext assignment
    if ($xamlContent -match "DataContext=""{Binding}""") {
        Write-Host "  [PASS] Module correctly sets DataContext" -ForegroundColor Green
    } else {
        Write-Host "  [WARN] Module may not be setting DataContext correctly" -ForegroundColor Yellow
    }
    
    # Check for localization
    $localizationCount = ([regex]::Matches($xamlContent, "{local:Localize\s+Key=[^}]+}")).Count
    Write-Host "  Found $localizationCount localization expressions" -ForegroundColor Yellow
    
    if ($localizationCount -eq 0) {
        Write-Host "  [WARN] No localization found in module" -ForegroundColor Yellow
    } else {
        Write-Host "  [PASS] Module uses localization" -ForegroundColor Green
    }
    
    # Check for command bindings
    $commandBindingCount = ([regex]::Matches($xamlContent, "Command=""{Binding\s+[^}]+Command}""")).Count
    Write-Host "  Found $commandBindingCount command bindings" -ForegroundColor Yellow
    
    if ($commandBindingCount -eq 0) {
        Write-Host "  [WARN] No command bindings found in module" -ForegroundColor Yellow
    } else {
        Write-Host "  [PASS] Module uses command bindings" -ForegroundColor Green
    }
    
    # Check for namespace declarations
    $requiredNamespaces = @(
        "xmlns:local=""clr-namespace:BetterGenshinImpact.Markup""",
        "xmlns:ui=""http://schemas.lepo.co/wpfui/2022/xaml"""
    )
    
    $namespacesFound = $true
    foreach ($namespace in $requiredNamespaces) {
        if ($xamlContent -notmatch [regex]::Escape($namespace)) {
            Write-Host "  [FAIL] Missing required namespace: $namespace" -ForegroundColor Red
            $namespacesFound = $false
        }
    }
    
    if ($namespacesFound) {
        Write-Host "  [PASS] All required namespaces are declared" -ForegroundColor Green
    }
    
    return $dataBindingCount -gt 0 -and $namespacesFound
}

# Run tests for each module
$passedTests = 0
$failedTests = 0

foreach ($module in $modules) {
    $result = Test-DataBindingAndLocalization -ModuleName $module
    if ($result) {
        $passedTests++
    } else {
        $failedTests++
    }
}

# Print summary
Write-Host "`n===== Data Binding and Localization Test Summary =====" -ForegroundColor Magenta
Write-Host "Total modules tested: $($modules.Count)" -ForegroundColor White
Write-Host "Passed: $passedTests" -ForegroundColor Green
Write-Host "Failed: $failedTests" -ForegroundColor Red

if ($failedTests -eq 0) {
    Write-Host "`nAll data binding and localization tests PASSED!" -ForegroundColor Green
} else {
    Write-Host "`nSome modules have data binding or localization issues. Please review the output above." -ForegroundColor Red
}