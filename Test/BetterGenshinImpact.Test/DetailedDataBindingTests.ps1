# DetailedDataBindingTests.ps1
# Comprehensive script to verify data binding and localization in refactored modules

Write-Host "Starting Detailed Data Binding and Localization Tests..." -ForegroundColor Green

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

# Define expected data bindings for each module
$expectedBindings = @{
    "AutoGeniusInvocationTaskControl" = @(
        "Config.AutoGeniusInvokationConfig.StrategyName",
        "SwitchAutoGeniusInvokationEnabled",
        "SwitchAutoGeniusInvokationButtonText",
        "OnSwitchAutoGeniusInvokation",
        "OnGoToAutoGeniusInvokationUrlAsync"
    )
    "AutoWoodTaskControl" = @(
        "Config.AutoWoodConfig",
        "AutoWoodRoundNum",
        "AutoWoodDailyMaxCount",
        "SwitchAutoWoodEnabled",
        "SwitchAutoWoodButtonText",
        "OnSwitchAutoWood",
        "OnGoToAutoWoodUrlAsync"
    )
    "AutoFightTaskControl" = @(
        "Config.AutoFightConfig",
        "SwitchAutoFightEnabled",
        "SwitchAutoFightButtonText",
        "OnSwitchAutoFight",
        "OnGoToAutoFightUrlAsync",
        "OnOpenFightFolder"
    )
    "AutoDomainTaskControl" = @(
        "Config.AutoDomainConfig",
        "AutoDomainRoundNum",
        "SwitchAutoDomainEnabled",
        "SwitchAutoDomainButtonText",
        "OnSwitchAutoDomain",
        "OnGoToAutoDomainUrlAsync"
    )
    "AutoStygianOnslaughtTaskControl" = @(
        "Config.AutoStygianOnslaughtConfig",
        "AutoStygianOnslaughtRoundNum",
        "SwitchAutoStygianOnslaughtEnabled",
        "SwitchAutoStygianOnslaughtButtonText",
        "OnSwitchAutoStygianOnslaught",
        "OnGoToAutoStygianOnslaughtUrlAsync"
    )
    "AutoMusicGameTaskControl" = @(
        "Config.AutoMusicGameConfig",
        "SwitchAutoMusicGameEnabled",
        "SwitchAutoMusicGameButtonText",
        "OnSwitchAutoMusicGame",
        "OnGoToAutoMusicGameUrlAsync"
    )
    "AutoAlbumTaskControl" = @(
        "Config.AutoMusicGameConfig",
        "SwitchAutoAlbumEnabled",
        "SwitchAutoAlbumButtonText",
        "OnSwitchAutoAlbum"
    )
    "AutoFishingTaskControl" = @(
        "Config.AutoFishingConfig",
        "SwitchAutoFishingEnabled",
        "SwitchAutoFishingButtonText",
        "OnSwitchAutoFishing",
        "OnGoToAutoFishingUrlAsync",
        "OnGoToTorchPreviousVersionsAsync"
    )
    "AutoRedeemCodeTaskControl" = @(
        "SwitchAutoRedeemCodeEnabled",
        "SwitchAutoRedeemCodeButtonText",
        "OnSwitchAutoRedeemCode"
    )
    "AutoArtifactSalvageTaskControl" = @(
        "Config.AutoArtifactSalvageConfig",
        "SwitchArtifactSalvageEnabled",
        "OnSwitchArtifactSalvage",
        "OnOpenArtifactSalvageTestOCRWindow"
    )
    "GetGridIconsTaskControl" = @(
        "Config.GetGridIconsConfig",
        "SwitchGetGridIconsEnabled",
        "SwitchGetGridIconsButtonText",
        "OnSwitchGetGridIcons",
        "OnGoToGridIconsFolder"
    )
    "AutoTrackTaskControl" = @(
        "SwitchAutoTrackEnabled",
        "SwitchAutoTrackButtonText",
        "OnSwitchAutoTrack",
        "OnGoToAutoTrackUrlAsync",
        "SelectedTeamConfiguration",
        "TeamConfigurations"
    )
}

# Define expected localization keys for each module
$expectedLocalizationKeys = @{
    "AutoGeniusInvocationTaskControl" = @(
        "task.tcg",
        "task.tcg.strategy",
        "common.start",
        "common.help"
    )
    "AutoWoodTaskControl" = @(
        "task.wood",
        "task.wood.round",
        "task.wood.dailyMax",
        "common.start",
        "common.help"
    )
    "AutoFightTaskControl" = @(
        "task.fight",
        "task.fight.strategy",
        "common.start",
        "common.help"
    )
    "AutoDomainTaskControl" = @(
        "task.domain",
        "task.domain.round",
        "common.start",
        "common.help"
    )
    # Add expected localization keys for other modules
}

# Function to test data binding and localization for a module
function Test-DetailedDataBindingAndLocalization {
    param (
        [string]$ModuleName,
        [string[]]$ExpectedBindings,
        [string[]]$ExpectedLocalizationKeys
    )
    
    Write-Host "`nTesting detailed data binding and localization for: $ModuleName" -ForegroundColor Cyan
    
    # Check if the module file exists
    $xamlPath = "BetterGenshinImpact/View/Pages/TaskSettings/$ModuleName.xaml"
    
    if (-not (Test-Path $xamlPath)) {
        Write-Host "  [FAIL] XAML file not found: $xamlPath" -ForegroundColor Red
        return $false
    }
    
    # Read the XAML content
    $xamlContent = Get-Content $xamlPath -Raw
    
    # Check for expected data bindings
    Write-Host "  Checking data bindings:" -ForegroundColor Yellow
    $missingBindings = @()
    
    foreach ($binding in $ExpectedBindings) {
        $bindingPattern = [regex]::Escape($binding)
        if ($xamlContent -match $bindingPattern -or 
            $xamlContent -match [regex]::Escape("{Binding $binding}") -or
            $xamlContent -match [regex]::Escape("Binding Path=$binding")) {
            Write-Host "    [PASS] Found binding: $binding" -ForegroundColor Green
        } else {
            Write-Host "    [FAIL] Missing binding: $binding" -ForegroundColor Red
            $missingBindings += $binding
        }
    }
    
    # Check for expected localization keys
    Write-Host "  Checking localization keys:" -ForegroundColor Yellow
    $missingLocalizationKeys = @()
    
    if ($ExpectedLocalizationKeys) {
        foreach ($key in $ExpectedLocalizationKeys) {
            $keyPattern = [regex]::Escape("Key=$key")
            if ($xamlContent -match $keyPattern) {
                Write-Host "    [PASS] Found localization key: $key" -ForegroundColor Green
            } else {
                Write-Host "    [WARN] Potential missing localization key: $key" -ForegroundColor Yellow
                $missingLocalizationKeys += $key
            }
        }
    } else {
        Write-Host "    [INFO] No expected localization keys defined for this module" -ForegroundColor Gray
    }
    
    # Check for DataContext assignment
    if ($xamlContent -match "DataContext=""{Binding}""") {
        Write-Host "  [PASS] Module correctly sets DataContext" -ForegroundColor Green
    } else {
        Write-Host "  [WARN] Module may not be setting DataContext correctly" -ForegroundColor Yellow
    }
    
    # Summary
    if ($missingBindings.Count -eq 0) {
        Write-Host "  [PASS] All expected data bindings found" -ForegroundColor Green
        return $true
    } else {
        Write-Host "  [FAIL] Missing $($missingBindings.Count) data bindings" -ForegroundColor Red
        return $false
    }
}

# Run tests for each module
$passedTests = 0
$failedTests = 0

foreach ($module in $modules) {
    $result = Test-DetailedDataBindingAndLocalization -ModuleName $module -ExpectedBindings $expectedBindings[$module] -ExpectedLocalizationKeys $expectedLocalizationKeys[$module]
    if ($result) {
        $passedTests++
    } else {
        $failedTests++
    }
}

# Print summary
Write-Host "`n===== Detailed Data Binding and Localization Test Summary =====" -ForegroundColor Magenta
Write-Host "Total modules tested: $($modules.Count)" -ForegroundColor White
Write-Host "Passed: $passedTests" -ForegroundColor Green
Write-Host "Failed: $failedTests" -ForegroundColor Red

if ($failedTests -eq 0) {
    Write-Host "`nAll detailed data binding and localization tests PASSED!" -ForegroundColor Green
} else {
    Write-Host "`nSome modules have data binding or localization issues. Please review the output above." -ForegroundColor Red
}