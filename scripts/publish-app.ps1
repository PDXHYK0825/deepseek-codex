param(
    [string]$OutputDirectory = ".",
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repositoryRoot '.tools/dotnet/dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$runtimeOutput = Join-Path $resolvedOutput 'runtime'
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$stagingRoot = Join-Path $temporaryRoot ("CodexModelSwitcher-publish-" + [Guid]::NewGuid().ToString('N'))
$credentialStaging = Join-Path $stagingRoot 'credential'
$applicationStaging = Join-Path $stagingRoot 'app'
$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }

try {
    New-Item -ItemType Directory -Path $credentialStaging -Force | Out-Null
    New-Item -ItemType Directory -Path $applicationStaging -Force | Out-Null

    & $dotnetCommand publish `
        (Join-Path $repositoryRoot 'src/CodexModelSwitcher.CredentialBridge/CodexModelSwitcher.CredentialBridge.csproj') `
        --configuration Release `
        --runtime win-x64 `
        --self-contained $selfContainedValue `
        -p:PublishSingleFile=true `
        -p:DebugSymbols=false `
        -p:DebugType=None `
        --output $credentialStaging
    if ($LASTEXITCODE -ne 0) { throw 'Credential bridge publish failed.' }

    & $dotnetCommand publish `
        (Join-Path $repositoryRoot 'src/CodexModelSwitcher.App/CodexModelSwitcher.App.csproj') `
        --configuration Release `
        --runtime win-x64 `
        --self-contained $selfContainedValue `
        -p:PublishSingleFile=true `
        -p:DebugSymbols=false `
        -p:DebugType=None `
        --output $applicationStaging
    if ($LASTEXITCODE -ne 0) { throw 'WPF application publish failed.' }

    New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
    New-Item -ItemType Directory -Path $runtimeOutput -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $applicationStaging 'CodexModelSwitcher.exe') -Destination $resolvedOutput -Force
    Copy-Item -LiteralPath (Join-Path $credentialStaging 'codex-model-switcher-credential.exe') -Destination $runtimeOutput -Force
}
finally {
    if (Test-Path -LiteralPath $stagingRoot)
    {
        $resolvedStaging = (Resolve-Path -LiteralPath $stagingRoot).Path
        $expectedPrefix = $temporaryRoot + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedStaging.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Split-Path -Leaf $resolvedStaging).StartsWith('CodexModelSwitcher-publish-', [StringComparison]::Ordinal))
        {
            throw "Refusing to remove an unexpected staging directory: $resolvedStaging"
        }

        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }
}

Write-Host "Application: $(Join-Path $resolvedOutput 'CodexModelSwitcher.exe')"
Write-Host "Runtime helper: $(Join-Path $runtimeOutput 'codex-model-switcher-credential.exe')"
