[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\WineCabinet\WineCabinet.csproj'
$usesDefaultOutput = [string]::IsNullOrWhiteSpace($OutputDirectory)

if ($usesDefaultOutput) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\publish\$RuntimeIdentifier"
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $resolvedOutput) {
    if ($usesDefaultOutput) {
        $artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts')).TrimEnd('\')
        if (-not $resolvedOutput.StartsWith($artifactsRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe publish cleanup path: $resolvedOutput"
        }
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    } elseif (Get-ChildItem -LiteralPath $resolvedOutput -Force | Select-Object -First 1) {
        throw "Custom output directory must be empty: $resolvedOutput"
    }
}
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

dotnet publish $project `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    -p:PublishProfile=$RuntimeIdentifier `
    --output $resolvedOutput

if ($LASTEXITCODE -ne 0) {
    throw "Publishing failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $resolvedOutput 'Wine Cabinet.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "The expected executable was not created: $executable"
}

$unexpectedFiles = @(Get-ChildItem -LiteralPath $resolvedOutput -File | Where-Object Name -ne 'Wine Cabinet.exe')
if ($unexpectedFiles.Count -gt 0) {
    throw "Unexpected files were produced: $($unexpectedFiles.Name -join ', ')"
}

Write-Output $executable
