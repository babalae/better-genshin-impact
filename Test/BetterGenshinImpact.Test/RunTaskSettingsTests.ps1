# RunTaskSettingsTests.ps1
# Main script to run all task settings module tests

Write-Host "===== Task Settings Module Test Suite =====" -ForegroundColor Magenta
Write-Host "This script will run comprehensive tests on the refactored TaskSettings modules" -ForegroundColor White
Write-Host "Tests include functionality verification, UI consistency, and data binding/localization checks" -ForegroundColor White
Write-Host "=======================================" -ForegroundColor Magenta

# Create test results directory if it doesn't exist
$resultsDir = "Test/BetterGenshinImpact.Test/Results"
if (-not (Test-Path $resultsDir)) {
    New-Item -ItemType Directory -Path $resultsDir | Out-Null
    Write-Host "Created results directory: $resultsDir" -ForegroundColor Yellow
}

# Function to run a test script and log results
function Run-TestScript {
    param (
        [string]$ScriptPath,
        [string]$TestName
    )
    
    Write-Host "`n===== Running $TestName =====" -ForegroundColor Cyan
    
    # Create timestamp for log file
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $logFile = "$resultsDir/$TestName-$timestamp.log"
    
    # Run the test script and capture output
    try {
        $output = & $ScriptPath *>&1
        $output | Out-File -FilePath $logFile
        
        # Display output
        $output | ForEach-Object { Write-Host $_ }
        
        Write-Host "`n$TestName completed. Log saved to: $logFile" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "Error running $TestName: $_" -ForegroundColor Red
        $_ | Out-File -FilePath $logFile -Append
        return $false
    }
}

# Run all test scripts
$testScripts = @(
    @{
        Path = "Test/BetterGenshinImpact.Test/TaskSettingsModuleTests.ps1"
        Name = "Functionality Tests"
    },
    @{
        Path = "Test/BetterGenshinImpact.Test/UIConsistencyTests.ps1"
        Name = "UI Consistency Tests"
    },
    @{
        Path = "Test/BetterGenshinImpact.Test/DataBindingLocalizationTests.ps1"
        Name = "Data Binding and Localization Tests"
    }
)

$successCount = 0
$failCount = 0

foreach ($test in $testScripts) {
    $result = Run-TestScript -ScriptPath $test.Path -TestName $test.Name
    if ($result) {
        $successCount++
    } else {
        $failCount++
    }
}

# Print final summary
Write-Host "`n===== Test Suite Summary =====" -ForegroundColor Magenta
Write-Host "Total test scripts: $($testScripts.Count)" -ForegroundColor White
Write-Host "Successful: $successCount" -ForegroundColor Green
Write-Host "Failed: $failCount" -ForegroundColor Red

if ($failCount -eq 0) {
    Write-Host "`nAll test scripts completed successfully!" -ForegroundColor Green
} else {
    Write-Host "`nSome test scripts failed. Please review the logs in $resultsDir" -ForegroundColor Red
}

Write-Host "`nTest suite completed at $(Get-Date)" -ForegroundColor White