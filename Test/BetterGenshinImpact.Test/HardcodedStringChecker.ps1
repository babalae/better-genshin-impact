# HardcodedStringChecker.ps1
# Script to check for hardcoded strings in XAML files that should be localized

Write-Host "Starting Hardcoded String Checker..." -ForegroundColor Green

# Define the modules to check
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

# Properties that should use localization
$textProperties = @(
    "Text=",
    "Header=",
    "Content=",
    "Title=",
    "ToolTip=",
    "Description=",
    "PlaceholderText="
)

# Patterns that indicate proper localization
$localizationPatterns = @(
    "{local:Localize\s+Key=",
    "Binding.*SwitchAutoGeniusInvokationButtonText",
    "Binding.*ButtonText"
)

# Function to check for hardcoded strings in a module
function Check-HardcodedStrings {
    param (
        [string]$ModuleName
    )
    
    Write-Host "`nChecking for hardcoded strings in: $ModuleName" -ForegroundColor Cyan
    
    # Check if the module file exists
    $xamlPath = "BetterGenshinImpact/View/Pages/TaskSettings/$ModuleName.xaml"
    
    if (-not (Test-Path $xamlPath)) {
        Write-Host "  [FAIL] XAML file not found: $xamlPath" -ForegroundColor Red
        return @{
            ModuleName = $ModuleName
            Success = $false
            HardcodedStrings = @()
        }
    }
    
    # Read the XAML content
    $xamlContent = Get-Content $xamlPath
    $hardcodedStrings = @()
    $lineNumber = 0
    
    foreach ($line in $xamlContent) {
        $lineNumber++
        
        # Skip comment lines
        if ($line -match "^\s*<!--" -and $line -match "-->") {
            continue
        }
        
        # Check each text property
        foreach ($property in $textProperties) {
            if ($line -match "$property""([^{][^""]+)""") {
                $value = $Matches[1]
                
                # Skip if it's just whitespace, numbers, or special characters
                if ($value -match "^\s*$" -or $value -match "^[\d\s\.\,\:\;\-\+\*\/\(\)\[\]\{\}\<\>\=\!\?\@\#\$\%\^\&\_\|\\]+$") {
                    continue
                }
                
                # Skip if it contains a localization pattern
                $isLocalized = $false
                foreach ($pattern in $localizationPatterns) {
                    if ($line -match $pattern) {
                        $isLocalized = $true
                        break
                    }
                }
                
                if (-not $isLocalized) {
                    $hardcodedStrings += @{
                        LineNumber = $lineNumber
                        Line = $line.Trim()
                        Value = $value
                        Property = $property
                    }
                }
            }
        }
    }
    
    # Report findings
    if ($hardcodedStrings.Count -eq 0) {
        Write-Host "  [PASS] No hardcoded strings found" -ForegroundColor Green
        return @{
            ModuleName = $ModuleName
            Success = $true
            HardcodedStrings = @()
        }
    } else {
        Write-Host "  [WARN] Found $($hardcodedStrings.Count) potential hardcoded strings:" -ForegroundColor Yellow
        foreach ($string in $hardcodedStrings) {
            Write-Host "    Line $($string.LineNumber): $($string.Property)""$($string.Value)""" -ForegroundColor Yellow
        }
        return @{
            ModuleName = $ModuleName
            Success = $false
            HardcodedStrings = $hardcodedStrings
        }
    }
}

# Run checks for each module
$results = @()

foreach ($module in $modules) {
    $result = Check-HardcodedStrings -ModuleName $module
    $results += $result
}

# Generate report
$reportPath = "Test/BetterGenshinImpact.Test/HardcodedStringsReport.md"

@"
# Hardcoded Strings Report

This report identifies potential hardcoded strings in the TaskSettings modules that should be localized.

## Summary

Total modules checked: $($modules.Count)
Modules with potential hardcoded strings: $($results | Where-Object { -not $_.Success } | Measure-Object).Count
Modules with no issues: $($results | Where-Object { $_.Success } | Measure-Object).Count

## Detailed Findings

$(foreach ($result in $results) {
    if (-not $result.Success -and $result.HardcodedStrings.Count -gt 0) {
@"
### $($result.ModuleName)

$(foreach ($string in $result.HardcodedStrings) {
@"
- **Line $($string.LineNumber)**: `$($string.Property)"$($string.Value)"`
  - Suggested fix: Replace with `$($string.Property)"{local:Localize Key=appropriate.key}"`

"@
})

"@
    }
})

## Recommendations

1. Replace all hardcoded strings with localization markup
2. Add any missing localization keys to the language files
3. Re-run this check after making changes

## False Positives

Some strings may be intentionally hardcoded, such as:
- Technical identifiers
- URLs
- Format strings with no translatable text

Review each finding to determine if localization is appropriate.

"@ | Out-File -FilePath $reportPath

Write-Host "`nHardcoded string check completed. Report saved to: $reportPath" -ForegroundColor Green

# Print summary
Write-Host "`n===== Hardcoded String Check Summary =====" -ForegroundColor Magenta
Write-Host "Total modules checked: $($modules.Count)" -ForegroundColor White
Write-Host "Modules with no issues: $($results | Where-Object { $_.Success } | Measure-Object).Count" -ForegroundColor Green
Write-Host "Modules with potential hardcoded strings: $($results | Where-Object { -not $_.Success } | Measure-Object).Count" -ForegroundColor Yellow

if (($results | Where-Object { -not $_.Success } | Measure-Object).Count -eq 0) {
    Write-Host "`nAll modules passed the hardcoded string check!" -ForegroundColor Green
} else {
    Write-Host "`nSome modules have potential hardcoded strings. See the report for details." -ForegroundColor Yellow
}