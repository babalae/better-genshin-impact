param([Parameter(Mandatory)][string]$BaseRef)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Normalize([string]$text) { return $text.Replace("`r`n", "`n").TrimEnd() }
$checks = @(
    'BetterGenshinImpact/GameTask/Common/TaskControl.cs',
    'BetterGenshinImpact/GameTask/AutoTrackPath/TpTask.cs',
    'BetterGenshinImpact/BetterGenshinImpact.csproj',
    'BetterGenshinImpact/GameTask/AutoDomain/AutoDomainTask.cs',
    'BetterGenshinImpact/GameTask/RunnerContext.cs',
    'BetterGenshinImpact/GameTask/TaskRunner.cs',
    'BetterGenshinImpact/GameTask/Model/GameUI/GridScroller.cs',
    'BetterGenshinImpact/App.xaml.cs',
    'BetterGenshinImpact/View/Pages/OneDragonFlowPage.xaml',
    'BetterGenshinImpact/ViewModel/Pages/OneDragonFlowViewModel.cs'
)
foreach ($relative in $checks) {
    $expected = (& git -C $repo show "${BaseRef}:$relative") -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read baseline: $relative" }
    $actual = [IO.File]::ReadAllText((Join-Path $repo $relative))
    if ($relative.EndsWith('/TpTask.cs')) {
        $actual = $actual.Replace('public partial class TpTask', 'public class TpTask')
    }
    if ($relative.EndsWith('/BetterGenshinImpact.csproj')) {
        # The merge base used 1.0.25; upstream main 42e1c0e7 already uses 1.0.27.
        # Allow only that exact reference transition, never an arbitrary current version.
        $oldReference = '<PackageReference Include="BetterGI.Assets.Other" Version="1.0.25" />'
        $newReference = '<PackageReference Include="BetterGI.Assets.Other" Version="1.0.27" />'
        if ($actual.Contains($newReference)) {
            $expected = $expected.Replace($oldReference, $newReference)
        }
    }
    if ((Normalize $actual) -cne (Normalize $expected)) { throw "Unrelated shared behavior changed: $relative" }
    Write-Output "PASS $relative"
}
