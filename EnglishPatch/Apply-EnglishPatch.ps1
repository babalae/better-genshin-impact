[CmdletBinding()]
param(
    [string]$BetterGiDir = "",
    [switch]$Uninstall,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$msg) {
    Write-Host "[*] $msg" -ForegroundColor Cyan
}

function Write-Ok([string]$msg) {
    Write-Host "[+] $msg" -ForegroundColor Green
}

function Write-Warn([string]$msg) {
    Write-Host "[!] $msg" -ForegroundColor Yellow
}

# 1. Locate BetterGI installation directory
$PatchDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($BetterGiDir)) {
    $candidates = @(
        $PatchDir,
        (Split-Path -Parent $PatchDir),
        (Get-Location).Path
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c "BetterGI.exe"))) {
            $BetterGiDir = (Resolve-Path $c).Path
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($BetterGiDir) -or -not (Test-Path (Join-Path $BetterGiDir "BetterGI.exe"))) {
    Write-Warn "Could not automatically find BetterGI.exe in the current or parent folder."
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = "Select your BetterGI installation folder (containing BetterGI.exe)"
        $dlg.ShowNewFolderButton = $false
        if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $BetterGiDir = $dlg.SelectedPath
        }
    } catch {}
}

if ([string]::IsNullOrWhiteSpace($BetterGiDir) -or -not (Test-Path (Join-Path $BetterGiDir "BetterGI.exe"))) {
    Write-Host "[X] BetterGI.exe not found. Place the EnglishPatch folder inside your BetterGI directory or pass -BetterGiDir <path>." -ForegroundColor Red
    if (-not $NoPause) { Read-Host "Press Enter to exit" }
    exit 1
}

$BetterGiDir = (Resolve-Path $BetterGiDir).Path
$ExePath     = Join-Path $BetterGiDir "BetterGI.exe"
$ExeBakPath  = Join-Path $BetterGiDir "BetterGI.exe.bak"
$HookDllDst  = Join-Path $BetterGiDir "BgiEnHook.dll"
$TargetI18n  = Join-Path $BetterGiDir "User\I18n\en.json"
$TargetBak   = Join-Path $BetterGiDir "User\I18n\en.json.bak"
$ConfigPath  = Join-Path $BetterGiDir "User\config.json"

Write-Host "==========================================================" -ForegroundColor Magenta
Write-Host "         BetterGI Full English Menu Patcher               " -ForegroundColor Magenta
Write-Host "==========================================================" -ForegroundColor Magenta
Write-Host "Target Directory: $BetterGiDir"

# 2. Ensure BetterGI is not running
$running = Get-Process -Name "BetterGI" -ErrorAction SilentlyContinue
if ($running) {
    Write-Warn "BetterGI is currently running. Please close BetterGI before patching."
    if (-not $NoPause) {
        $ans = Read-Host "Close BetterGI automatically and continue? (Y/N)"
        if ($ans -match '^[Yy]') {
            $running | Stop-Process -Force
            Start-Sleep -Seconds 2
        } else {
            exit 1
        }
    } else {
        exit 1
    }
}

# 3. Handle Uninstall if requested
if ($Uninstall) {
    Write-Step "Uninstalling BetterGI English Patch..."
    if (Test-Path $ExeBakPath) {
        Copy-Item -Path $ExeBakPath -Destination $ExePath -Force
        Write-Ok "Restored original BetterGI.exe from BetterGI.exe.bak"
    }
    if (Test-Path $TargetBak) {
        Copy-Item -Path $TargetBak -Destination $TargetI18n -Force
        Write-Ok "Restored original User\I18n\en.json from en.json.bak"
    }
    if (Test-Path $HookDllDst) {
        Remove-Item -Path $HookDllDst -Force -ErrorAction SilentlyContinue
        Write-Ok "Removed BgiEnHook.dll"
    }
    Write-Ok "Uninstall complete!"
    if (-not $NoPause) { Read-Host "Press Enter to exit" }
    exit 0
}

# 4. Smart-Merge User\I18n\en.json
Write-Step "Updating and smart-merging User\I18n\en.json..."
$PatchJsonPath = Join-Path $PatchDir "en.json"
if (-not (Test-Path $PatchJsonPath)) {
    throw "Missing master translation dictionary: $PatchJsonPath"
}

$I18nDir = Split-Path -Parent $TargetI18n
if (-not (Test-Path $I18nDir)) {
    New-Item -ItemType Directory -Path $I18nDir -Force | Out-Null
}

# Helper to parse flat JSON dictionary in order
function Read-FlatJsonDict([string]$path) {
    $dict = [ordered]@{}
    if (-not (Test-Path $path)) { return $dict }
    $raw = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $obj = $raw | ConvertFrom-Json
    foreach ($prop in $obj.PSObject.Properties) {
        $dict[$prop.Name] = [string]$prop.Value
    }
    return $dict
}

function Write-FlatJsonDict([string]$path, $dict) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append("{" + "`r`n")
    $keys = @($dict.Keys)
    for ($idx = 0; $idx -lt $keys.Count; $idx++) {
        $k = $keys[$idx]
        $v = [string]$dict[$k]
        $kEsc = $k.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
        $vEsc = $v.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
        $comma = if ($idx -lt $keys.Count - 1) { "," } else { "" }
        [void]$sb.Append("  `"$kEsc`": `"$vEsc`"$comma`r`n")
    }
    [void]$sb.Append("}")
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $sb.ToString(), $utf8NoBom)
}

$patchDict = Read-FlatJsonDict $PatchJsonPath
$existingDict = Read-FlatJsonDict $TargetI18n

# Backup stock en.json if not already backed up or if BetterGI was updated with a smaller stock file
if ((Test-Path $TargetI18n) -and (-not (Test-Path $TargetBak) -or ($existingDict.Count -lt $patchDict.Count))) {
    Copy-Item -Path $TargetI18n -Destination $TargetBak -Force
    Write-Ok "Backed up stock dictionary to User\I18n\en.json.bak ($($existingDict.Count) keys)"
}

# Merge: start with existing target keys (so any new keys added in a BetterGI update are kept),
# then overlay all non-empty translations from our master patch dictionary
$mergedDict = [ordered]@{}
$newKeysFromUpdate = 0
foreach ($k in $existingDict.Keys) {
    $mergedDict[$k] = $existingDict[$k]
    if (-not $patchDict.Contains($k)) {
        $newKeysFromUpdate++
    }
}
foreach ($k in $patchDict.Keys) {
    if (-not [string]::IsNullOrEmpty($patchDict[$k])) {
        $mergedDict[$k] = $patchDict[$k]
    } elseif (-not $mergedDict.Contains($k)) {
        $mergedDict[$k] = $patchDict[$k]
    }
}

Write-FlatJsonDict -path $TargetI18n -dict $mergedDict
Write-Ok "Merged English dictionary saved to User\I18n\en.json ($($mergedDict.Count) total keys; $newKeysFromUpdate new keys preserved from version update)"

# Also keep EnglishPatch\en.json synced if new keys were discovered
if ($mergedDict.Count -gt $patchDict.Count) {
    Write-FlatJsonDict -path $PatchJsonPath -dict $mergedDict
    Write-Ok "Synced newly discovered keys back into EnglishPatch\en.json"
}

# 5. Deploy / Compile BgiEnHook.dll
Write-Step "Deploying runtime translation hook (BgiEnHook.dll)..."
$HookCsPath  = Join-Path $PatchDir "BgiEnHook.cs"
$HookDllSrc  = Join-Path $PatchDir "BgiEnHook.dll"
$cscPath     = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if ((Test-Path $HookCsPath) -and (Test-Path $cscPath)) {
    & $cscPath /nologo /utf8output /codepage:65001 /target:library /optimize+ /out:$HookDllDst $HookCsPath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile BgiEnHook.cs (exit code $LASTEXITCODE)"
    }
    if ((Test-Path $HookDllDst) -and ($HookDllSrc -ne $HookDllDst)) {
        Copy-Item -Path $HookDllDst -Destination $HookDllSrc -Force
    }
    Write-Ok "Compiled and deployed BgiEnHook.dll from BgiEnHook.cs"
} elseif (Test-Path $HookDllSrc) {
    if ($HookDllSrc -ne $HookDllDst) {
        Copy-Item -Path $HookDllSrc -Destination $HookDllDst -Force
    }
    Write-Ok "Copied prebuilt BgiEnHook.dll to $HookDllDst"
} else {
    throw "Neither BgiEnHook.cs nor BgiEnHook.dll was found in $PatchDir"
}

# 6. Patch BetterGI.exe (.NET Single-File Bundle)
Write-Step "Patching BetterGI.exe to auto-load BgiEnHook.dll..."

function Find-Bytes([byte[]]$haystack, [byte[]]$needle, [int]$startIndex, [int]$endIndex) {
    $limit = [Math]::Min($haystack.Length - $needle.Length, $endIndex)
    for ($i = $startIndex; $i -le $limit; $i++) {
        if ($haystack[$i] -eq $needle[0]) {
            $match = $true
            for ($j = 1; $j -lt $needle.Length; $j++) {
                if ($haystack[$i + $j] -ne $needle[$j]) {
                    $match = $false
                    break
                }
            }
            if ($match) { return $i }
        }
    }
    return -1
}

# Standard .NET Single-File Bundle Signature (32 bytes)
$bundleSig = [byte[]]@(
    0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
    0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
    0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
    0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
)
$rcNameBytes = [System.Text.Encoding]::ASCII.GetBytes("BetterGI.runtimeconfig.json")

# Check if current BetterGI.exe is already patched vs clean update
$exeBytes = [System.IO.File]::ReadAllBytes($ExePath)
$sigPos = Find-Bytes $exeBytes $bundleSig 0 ([Math]::Min($exeBytes.Length, 2097152))
if ($sigPos -lt 8) {
    throw "Could not find .NET single-file bundle signature in BetterGI.exe"
}

$manifestOffset = [BitConverter]::ToInt64($exeBytes, $sigPos - 8)
$rcPos = Find-Bytes $exeBytes $rcNameBytes ([int]$manifestOffset) ($exeBytes.Length - 1)
if ($rcPos -lt 0) {
    throw "Could not find BetterGI.runtimeconfig.json entry in BetterGI.exe bundle manifest"
}

$rcEntryOffset = [BitConverter]::ToInt64($exeBytes, $rcPos - 26)
$rcEntrySize   = [BitConverter]::ToInt64($exeBytes, $rcPos - 18)
$rcJsonStr     = [System.Text.Encoding]::UTF8.GetString($exeBytes, [int]$rcEntryOffset, [int]$rcEntrySize)

$isAlreadyPatched = $rcJsonStr -match '"STARTUP_HOOKS"'

if (-not $isAlreadyPatched) {
    # Fresh / updated BetterGI.exe! Back it up to BetterGI.exe.bak
    Copy-Item -Path $ExePath -Destination $ExeBakPath -Force
    Write-Ok "Backed up clean executable to BetterGI.exe.bak"
} elseif (Test-Path $ExeBakPath) {
    # Check if BetterGI.exe.bak is from the same version; if so, start from clean BetterGI.exe.bak
    $bakBytes = [System.IO.File]::ReadAllBytes($ExeBakPath)
    if ([Math]::Abs($exeBytes.Length - $bakBytes.Length) -lt 4096) {
        $exeBytes = $bakBytes
        $sigPos = Find-Bytes $exeBytes $bundleSig 0 ([Math]::Min($exeBytes.Length, 2097152))
        $manifestOffset = [BitConverter]::ToInt64($exeBytes, $sigPos - 8)
        $rcPos = Find-Bytes $exeBytes $rcNameBytes ([int]$manifestOffset) ($exeBytes.Length - 1)
        $rcEntryOffset = [BitConverter]::ToInt64($exeBytes, $rcPos - 26)
        $rcEntrySize   = [BitConverter]::ToInt64($exeBytes, $rcPos - 18)
        $rcJsonStr     = [System.Text.Encoding]::UTF8.GetString($exeBytes, [int]$rcEntryOffset, [int]$rcEntrySize)
    }
}

# Build patched runtimeconfig.json with STARTUP_HOOKS pointing to $HookDllDst
$escapedHookPath = $HookDllDst.Replace('\', '\\')
if ($rcJsonStr -match '"STARTUP_HOOKS"\s*:\s*"[^"]*"') {
    $newRcJsonStr = [regex]::Replace($rcJsonStr, '"STARTUP_HOOKS"\s*:\s*"[^"]*"', "`"STARTUP_HOOKS`": `"$escapedHookPath`"")
} else {
    $newRcJsonStr = [regex]::Replace(
        $rcJsonStr,
        '("CSWINRT_USE_WINDOWS_UI_XAML_PROJECTIONS"\s*:\s*(?:true|false))',
        "`$1,`n      `"STARTUP_HOOKS`": `"$escapedHookPath`""
    )
}

$newRcBytes = [System.Text.Encoding]::UTF8.GetBytes($newRcJsonStr)

# Update bundle manifest header preamble (v6.0+ stores runtimeconfig offset & size right after bundleId and depsJson)
$newManifestOffset = $manifestOffset + $newRcBytes.Length
$bundleIdLen = $exeBytes[[int]$manifestOffset + 12]
$preambleRcPos = [int]$manifestOffset + 13 + $bundleIdLen + 16
[Array]::Copy([BitConverter]::GetBytes([int64]$manifestOffset), 0, $exeBytes, $preambleRcPos, 8)
[Array]::Copy([BitConverter]::GetBytes([int64]$newRcBytes.Length), 0, $exeBytes, $preambleRcPos + 8, 8)

# Update bundle manifest file entry in memory (before splitting)
[Array]::Copy([BitConverter]::GetBytes([int64]$manifestOffset), 0, $exeBytes, $rcPos - 26, 8)
[Array]::Copy([BitConverter]::GetBytes([int64]$newRcBytes.Length), 0, $exeBytes, $rcPos - 18, 8)
[Array]::Copy([BitConverter]::GetBytes([int64]0), 0, $exeBytes, $rcPos - 10, 8)
[Array]::Copy([BitConverter]::GetBytes([int64]$newManifestOffset), 0, $exeBytes, $sigPos - 8, 8)

# Assemble new BetterGI.exe: [0 .. manifestOffset-1] + [newRcBytes] + [manifestOffset .. EOF]
$outLen = $exeBytes.Length + $newRcBytes.Length
$outBytes = New-Object byte[] $outLen
[Array]::Copy($exeBytes, 0, $outBytes, 0, [int]$manifestOffset)
[Array]::Copy($newRcBytes, 0, $outBytes, [int]$manifestOffset, $newRcBytes.Length)
[Array]::Copy($exeBytes, [int]$manifestOffset, $outBytes, [int]$newManifestOffset, $exeBytes.Length - [int]$manifestOffset)

[System.IO.File]::WriteAllBytes($ExePath, $outBytes)
Write-Ok "Patched BetterGI.exe with STARTUP_HOOKS -> $HookDllDst"

# 7. Ensure UI Language is set to English in User\config.json (if config exists)
if (Test-Path $ConfigPath) {
    $cfgRaw = [System.IO.File]::ReadAllText($ConfigPath, [System.Text.Encoding]::UTF8)
    if ($cfgRaw -match '"uiCultureInfoName"\s*:\s*"zh[^"]*"') {
        $cfgUpdated = [regex]::Replace($cfgRaw, '"uiCultureInfoName"\s*:\s*"zh[^"]*"', '"uiCultureInfoName": "en"')
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($ConfigPath, $cfgUpdated, $utf8NoBom)
        Write-Ok "Updated User\config.json: uiCultureInfoName -> 'en'"
    } else {
        Write-Ok "User\config.json already configured for English UI"
    }
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "   BetterGI English Menu Patch Applied Successfully!      " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
if (-not $NoPause) {
    Read-Host "Press Enter to finish"
}
