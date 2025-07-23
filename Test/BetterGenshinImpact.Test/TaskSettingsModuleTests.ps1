# TaskSettingsModuleTests.ps1
# Comprehensive test script for verifying the functionality of task settings modules

Write-Host "Starting TaskSettings Module Tests..." -ForegroundColor Green

# Define the list of modules to test
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

# Define test cases for each module
$testCases = @{
    "AutoGeniusInvocationTaskControl" = @{
        "Controls" = @("CardExpander", "ComboBox", "Button")
        "DataBindings" = @("Config.AutoGeniusInvokationConfig.StrategyName", "SwitchAutoGeniusInvokationEnabled")
        "Commands" = @("OnSwitchAutoGeniusInvokation", "OnGoToAutoGeniusInvokationUrlAsync")
    }
    "AutoWoodTaskControl" = @{
        "Controls" = @("CardExpander", "NumberBox", "Button")
        "DataBindings" = @("Config.AutoWoodConfig", "AutoWoodRoundNum", "AutoWoodDailyMaxCount", "SwitchAutoWoodEnabled")
        "Commands" = @("OnSwitchAutoWood", "OnGoToAutoWoodUrlAsync")
    }
    "AutoFightTaskControl" = @{
        "Controls" = @("CardExpander", "ComboBox", "Button", "CheckBox")
        "DataBindings" = @("Config.AutoFightConfig", "SwitchAutoFightEnabled")
        "Commands" = @("OnSwitchAutoFight", "OnGoToAutoFightUrlAsync", "OnOpenFightFolder")
    }
    "AutoDomainTaskControl" = @{
        "Controls" = @("CardExpander", "NumberBox", "CheckBox", "Button")
        "DataBindings" = @("Config.AutoDomainConfig", "AutoDomainRoundNum", "SwitchAutoDomainEnabled")
        "Commands" = @("OnSwitchAutoDomain", "OnGoToAutoDomainUrlAsync")
    }
    "AutoStygianOnslaughtTaskControl" = @{
        "Controls" = @("CardExpander", "NumberBox", "CheckBox", "Button")
        "DataBindings" = @("Config.AutoStygianOnslaughtConfig", "AutoStygianOnslaughtRoundNum", "SwitchAutoStygianOnslaughtEnabled")
        "Commands" = @("OnSwitchAutoStygianOnslaught", "OnGoToAutoStygianOnslaughtUrlAsync")
    }
    "AutoMusicGameTaskControl" = @{
        "Controls" = @("CardExpander", "ComboBox", "Button")
        "DataBindings" = @("Config.AutoMusicGameConfig", "SwitchAutoMusicGameEnabled")
        "Commands" = @("OnSwitchAutoMusicGame", "OnGoToAutoMusicGameUrlAsync")
    }
    "AutoAlbumTaskControl" = @{
        "Controls" = @("CardExpander", "ComboBox", "Button")
        "DataBindings" = @("Config.AutoMusicGameConfig", "SwitchAutoAlbumEnabled")
        "Commands" = @("OnSwitchAutoAlbum")
    }
    "AutoFishingTaskControl" = @{
        "Controls" = @("CardExpander", "ComboBox", "CheckBox", "Button")
        "DataBindings" = @("Config.AutoFishingConfig", "SwitchAutoFishingEnabled")
        "Commands" = @("OnSwitchAutoFishing", "OnGoToAutoFishingUrlAsync", "OnGoToTorchPreviousVersionsAsync")
    }
    "AutoRedeemCodeTaskControl" = @{
        "Controls" = @("CardExpander", "Button")
        "DataBindings" = @("SwitchAutoRedeemCodeEnabled")
        "Commands" = @("OnSwitchAutoRedeemCode")
    }
    "AutoArtifactSalvageTaskControl" = @{
        "Controls" = @("CardExpander", "ComboBox", "TextBox", "Button")
        "DataBindings" = @("Config.AutoArtifactSalvageConfig", "SwitchArtifactSalvageEnabled")
        "Commands" = @("OnSwitchArtifactSalvage", "OnOpenArtifactSalvageTestOCRWindow")
    }
    "GetGridIconsTaskControl" = @{
        "Controls" = @("CardExpander", "ComboBox", "NumberBox", "Button")
        "DataBindings" = @("Config.GetGridIconsConfig", "SwitchGetGridIconsEnabled")
        "Commands" = @("OnSwitchGetGridIcons", "OnGoToGridIconsFolder")
    }
    "AutoTrackTaskControl" = @{
        "Controls" = @("CardExpander", "ComboBox", "Button")
        "DataBindings" = @("SwitchAutoTrackEnabled", "SelectedTeamConfiguration")
        "Commands" = @("OnSwitchAutoTrack", "OnGoToAutoTrackUrlAsync")
    }
}

# Function to test a module
function Test-Module {
    param (
        [string]$ModuleName,
        [hashtable]$TestCase
    )
    
    Write-Host "`nTesting module: $ModuleName" -ForegroundColor Cyan
    
    # Check if the module file exists
    $xamlPath = "BetterGenshinImpact/View/Pages/TaskSettings/$ModuleName.xaml"
    $csPath = "BetterGenshinImpact/View/Pages/TaskSettings/$ModuleName.xaml.cs"
    
    if (-not (Test-Path $xamlPath)) {
        Write-Host "  [FAIL] XAML file not found: $xamlPath" -ForegroundColor Red
        return $false
    } else {
        Write-Host "  [PASS] XAML file exists" -ForegroundColor Green
    }
    
    if (-not (Test-Path $csPath)) {
        Write-Host "  [FAIL] Code-behind file not found: $csPath" -ForegroundColor Red
        return $false
    } else {
        Write-Host "  [PASS] Code-behind file exists" -ForegroundColor Green
    }
    
    # Read the XAML content
    $xamlContent = Get-Content $xamlPath -Raw
    
    # Test for required controls
    Write-Host "  Testing controls:" -ForegroundColor Yellow
    foreach ($control in $TestCase.Controls) {
        if ($xamlContent -match $control) {
            Write-Host "    [PASS] Found control: $control" -ForegroundColor Green
        } else {
            Write-Host "    [FAIL] Missing control: $control" -ForegroundColor Red
            return $false
        }
    }
    
    # Test for data bindings
    Write-Host "  Testing data bindings:" -ForegroundColor Yellow
    foreach ($binding in $TestCase.DataBindings) {
        if ($xamlContent -match [regex]::Escape($binding) -or $xamlContent -match [regex]::Escape("{Binding $binding}")) {
            Write-Host "    [PASS] Found data binding: $binding" -ForegroundColor Green
        } else {
            Write-Host "    [FAIL] Missing data binding: $binding" -ForegroundColor Red
            return $false
        }
    }
    
    # Test for commands
    Write-Host "  Testing commands:" -ForegroundColor Yellow
    foreach ($command in $TestCase.Commands) {
        if ($xamlContent -match [regex]::Escape($command)) {
            Write-Host "    [PASS] Found command: $command" -ForegroundColor Green
        } else {
            Write-Host "    [FAIL] Missing command: $command" -ForegroundColor Red
            return $false
        }
    }
    
    return $true
}

# Run tests for each module
$passedTests = 0
$failedTests = 0

foreach ($module in $modules) {
    $result = Test-Module -ModuleName $module -TestCase $testCases[$module]
    if ($result) {
        $passedTests++
    } else {
        $failedTests++
    }
}

# Print summary
Write-Host "`n===== Test Summary =====" -ForegroundColor Magenta
Write-Host "Total modules tested: $($modules.Count)" -ForegroundColor White
Write-Host "Passed: $passedTests" -ForegroundColor Green
Write-Host "Failed: $failedTests" -ForegroundColor Red

if ($failedTests -eq 0) {
    Write-Host "`nAll module tests PASSED!" -ForegroundColor Green
} else {
    Write-Host "`nSome module tests FAILED. Please review the output above." -ForegroundColor Red
}