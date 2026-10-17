# Read through the Windows provider, independently of this process's registry view.
function Get-WineCabinetSystemStartup {
    [CmdletBinding()]
    param()

    try {
        $userSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $result = Invoke-CimMethod -Namespace root/default -ClassName StdRegProv -MethodName GetStringValue -Arguments @{
            hDefKey = [uint32]2147483651 # HKEY_USERS
            sSubKeyName = $userSid + '\Software\Microsoft\Windows\CurrentVersion\Run'
            sValueName = 'Wine Cabinet'
        } -ErrorAction Stop
        if ($result.ReturnValue -notin @(0, 2)) {
            throw "Windows registry provider returned error $($result.ReturnValue)."
        }
        [pscustomobject]@{
            Available = $true
            Command = if ($result.ReturnValue -eq 0) { [string]$result.sValue } else { '' }
            Error = $null
        }
    } catch {
        [pscustomobject]@{ Available = $false; Command = $null; Error = $_.Exception.Message }
    }
}
