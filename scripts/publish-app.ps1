param(
    [string]$OutputDirectory = "artifacts/app/win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repositoryRoot '.tools/dotnet/dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$resolvedOutput = Join-Path $repositoryRoot $OutputDirectory
$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }

& $dotnetCommand publish `
    (Join-Path $repositoryRoot 'src/CodexModelSwitcher.CredentialBridge/CodexModelSwitcher.CredentialBridge.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained $selfContainedValue `
    -p:PublishSingleFile=true `
    --output $resolvedOutput
if ($LASTEXITCODE -ne 0) { throw 'Credential bridge publish failed.' }

& $dotnetCommand publish `
    (Join-Path $repositoryRoot 'src/CodexModelSwitcher.App/CodexModelSwitcher.App.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained $selfContainedValue `
    -p:PublishSingleFile=true `
    --output $resolvedOutput
if ($LASTEXITCODE -ne 0) { throw 'WPF application publish failed.' }

Write-Host "Published Codex Model Switcher to $resolvedOutput"
