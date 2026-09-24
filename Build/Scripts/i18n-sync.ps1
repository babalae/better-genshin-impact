[CmdletBinding()]
param(
    [switch]$AddOnly,
    [switch]$Check
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$toolProject = Join-Path $repositoryRoot "Build\BetterGenshinImpact.I18nSync\BetterGenshinImpact.I18nSync.csproj"
$applicationProject = Join-Path $repositoryRoot "BetterGenshinImpact"

$dotnetArguments = @(
    "run",
    "--project", $toolProject,
    "--",
    "--project", $applicationProject
)

if ($AddOnly)
{
    $dotnetArguments += "--add-only"
}

if ($Check)
{
    $dotnetArguments += "--check"
}

& dotnet @dotnetArguments
exit $LASTEXITCODE
