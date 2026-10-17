[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,

    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\WineCabinet'),

    [switch]$Launch
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'startup-registry.ps1')
$source = (Resolve-Path -LiteralPath $Executable).Path
if ([System.IO.Path]::GetFileName($source) -ne 'Wine Cabinet.exe') {
    throw 'Expected the self-contained Wine Cabinet.exe.'
}
$destinationDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
$destination = Join-Path $destinationDirectory 'Wine Cabinet.exe'
if (-not $PSCmdlet.ShouldProcess($destination, 'Copy Wine Cabinet and register current-user startup')) { return }
$running = @(Get-Process -Name 'Wine Cabinet' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    throw 'Exit the running Wine Cabinet instance before installing or updating.'
}
New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
if (-not $source.Equals($destination, [System.StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $source -Destination $destination -Force
}
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
    throw 'Installed file does not match the source executable.'
}
$registryPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$command = '"' + $destination + '" --startup'
$key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath, $false)
try { $existingCommand = if ($key) { [string]$key.GetValue('Wine Cabinet') } else { '' } }
finally { if ($key) { $key.Dispose() } }
if (-not $command.Equals($existingCommand, [System.StringComparison]::OrdinalIgnoreCase)) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
    if ($null -eq $key) { throw 'Could not open the current-user startup registry key.' }
    try { $key.SetValue('Wine Cabinet', $command, [Microsoft.Win32.RegistryValueKind]::String) }
    finally { $key.Dispose() }
}
$key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath, $false)
try {
    if ($null -eq $key -or [string]$key.GetValue('Wine Cabinet') -ne $command) {
        throw 'The startup command could not be verified after installation.'
    }
}
finally { if ($key) { $key.Dispose() } }

$systemStartup = Get-WineCabinetSystemStartup
if ($systemStartup.Available) {
    if (-not $command.Equals($systemStartup.Command, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Windows sees a different startup command than this process. Installation files were copied, but startup registration is not verified. Run this installer from a regular Windows PowerShell window, then check scripts\diagnose-startup.ps1.'
    }
} else {
    Write-Warning "Independent Windows startup verification is unavailable: $($systemStartup.Error)"
}
Write-Output $destination
if ($Launch) { Start-Process -FilePath $destination -WindowStyle Hidden }
