[CmdletBinding()]
param([string]$Executable)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'startup-registry.ps1')
$processCommand = ''
$processReadError = $null
$key = $null
try {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    $processCommand = if ($key) { [string]$key.GetValue('Wine Cabinet') } else { '' }
} catch {
    $processReadError = $_.Exception.Message
}
finally { if ($key) { $key.Dispose() } }
$systemStartup = Get-WineCabinetSystemStartup
$command = if ($systemStartup.Available) { $systemStartup.Command } else { $processCommand }
$registeredPath = if ($command -match '^"([^"]+)"(?:\s+--startup)?$') { $matches[1] } else { '' }
if (-not $Executable) {
    $Executable = if ($registeredPath) { $registeredPath } else { Join-Path $env:LOCALAPPDATA 'Programs\WineCabinet\Wine Cabinet.exe' }
}
$file = [System.IO.Path]::GetFullPath($Executable)
$exists = Test-Path -LiteralPath $file -PathType Leaf
$approvedKey = $null
$approval = $null
$approvalReadError = $null
try {
    $approvedKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run')
    $approval = if ($approvedKey) { $approvedKey.GetValue('Wine Cabinet') } else { $null }
} catch {
    $approvalReadError = $_.Exception.Message
}
finally { if ($approvedKey) { $approvedKey.Dispose() } }
[pscustomobject]@{
    Executable = $file
    FileExists = $exists
    StartupCommand = $command
    HasStartupArgument = $command -match '\s+--startup$'
    SystemRegistryVerified = $systemStartup.Available
    RegistryViewsMatch = if ($systemStartup.Available -and -not $processReadError) {
        $processCommand.Equals($systemStartup.Command, [System.StringComparison]::OrdinalIgnoreCase)
    } else { $null }
    ProcessViewStartupCommand = $processCommand
    ProcessRegistryReadError = $processReadError
    StartupPathMatches = $registeredPath.Equals($file, [System.StringComparison]::OrdinalIgnoreCase)
    RegisteredFileExists = [bool]($registeredPath -and (Test-Path -LiteralPath $registeredPath -PathType Leaf))
    StartupApprovalBytes = if ($approval -is [byte[]]) { [BitConverter]::ToString($approval) } elseif ($approvalReadError) { 'Unavailable' } else { 'No recorded override; check Windows Startup apps.' }
    StartupApprovalReadError = $approvalReadError
} | Format-List

if (-not $systemStartup.Available) {
    Write-Warning "Independent Windows startup verification is unavailable: $($systemStartup.Error)"
}
try {
    Get-CimInstance Win32_Process -Filter "Name = 'Wine Cabinet.exe'" -ErrorAction Stop |
        Select-Object ProcessId, ExecutablePath, CommandLine | Format-List
} catch {
    Write-Warning "Running instance information is unavailable: $($_.Exception.Message)"
}
Write-Output 'This report is read-only and does not change startup settings.'
