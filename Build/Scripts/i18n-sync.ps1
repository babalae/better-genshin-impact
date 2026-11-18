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

# 同级的 bettergi-plugins 仓库存在时，一并同步语言插件的 I18n 目录
$pluginsSource = Join-Path $repositoryRoot "..\bettergi-plugins\src"
if (Test-Path $pluginsSource)
{
    foreach ($pluginDirectory in Get-ChildItem -Path $pluginsSource -Directory -Filter "BetterGI.Plugin.*")
    {
        $pluginI18n = Join-Path $pluginDirectory.FullName "I18n"
        if (Test-Path $pluginI18n)
        {
            Write-Host "同步插件语言目录：$pluginI18n"
            $dotnetArguments += "--extra-i18n-dir", $pluginI18n
        }
    }
}

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
