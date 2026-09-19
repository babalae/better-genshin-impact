param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../BetterGenshinImpact'))
function Get-Method([string]$relativePath, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $sourceRoot $relativePath))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production method: $signature" }
    $open = $source.IndexOf('{', $start)
    $depth = 0
    for ($i = $open; $i -lt $source.Length; $i++) {
        if ($source[$i] -eq '{') { $depth++ }
        if ($source[$i] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $source.Substring($start, $i - $start + 1) }
        }
    }
    throw "Unbalanced production method: $signature"
}
$sleep = Get-Method 'GameTask/Common/TaskControl.cs' 'public static void Sleep(int millisecondsTimeout, CancellationToken ct)'
$fight = Get-Method 'GameTask/AutoDomain/AutoDomainTask.cs' 'private Task StartFight(CombatScenes combatScenes, List<CombatCommand> combatCommands)'
$end = Get-Method 'GameTask/AutoDomain/AutoDomainTask.cs' 'private Task DomainEndDetectionTask(CancellationTokenSource cts)'
$potFile = 'GameTask/Common/Job/GoToSereniteaPotTask.cs'
$mapEntry = Get-Method $potFile 'private async Task<bool> IntoSereniteaPot(CancellationToken ct)'
$bagEntry = Get-Method $potFile 'private async Task<bool> IntoSereniteaPotByBag(CancellationToken ct)'
$executeEntry = Get-Method $potFile 'private async Task<bool> Execute(CancellationToken ct, bool entryOnly)'
$readRealm = Get-Method $potFile 'private async Task<bool> ReadRealmName(CancellationToken ct)'
$hotkey = Get-Method 'GameTask/QuickSereniteaPot/QuickSereniteaPotTask.cs' 'public static void Done()'
$generated = @"
// Generated from current production source on every build; do not maintain copied method fixtures.
// Only input, window, OCR and combat-command dependencies are substituted in this offline harness.
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
namespace SereniteaPot.Regression.Compatibility {
static partial class SleepSource { $sleep }
partial class DomainProbe { $fight
$end }
}
namespace SereniteaPot.Regression.Entry {
partial class PotEntryProbe { $mapEntry
$bagEntry
$readRealm
$executeEntry }
}
namespace SereniteaPot.Regression.Hotkey {
static partial class HotkeyProbe { $hotkey }
}
"@
$path = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
[IO.File]::WriteAllText($path, $generated)
