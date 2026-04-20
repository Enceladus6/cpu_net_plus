param(
    [string]$ConfigPath = "$PSScriptRoot\autologin-config.json"
)

$ErrorActionPreference = "Stop"

function Write-Log {
    param([string]$Message)
    $logDir = Join-Path $PSScriptRoot "logs"
    if (-not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }
    $logFile = Join-Path $logDir ("autologin-" + (Get-Date -Format "yyyyMMdd") + ".log")
    $line = "{0} {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Add-Content -Path $logFile -Value $line
}

function Parse-JsonpPayload {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $trimmed = $Text.Trim()
    $start = $trimmed.IndexOf("(")
    $end = $trimmed.LastIndexOf(")")
    if ($start -ge 0 -and $end -gt $start) {
        return $trimmed.Substring($start + 1, $end - $start - 1)
    }
    return $trimmed
}

function In-MaintenanceWindow {
    param(
        [string]$Start = "04:00",
        [string]$End = "04:15"
    )
    $now = Get-Date
    $startTs = [timespan]::ParseExact($Start, "hh\:mm", $null)
    $endTs = [timespan]::ParseExact($End, "hh\:mm", $null)
    $current = $now.TimeOfDay
    if ($startTs -le $endTs) {
        return ($current -ge $startTs -and $current -lt $endTs)
    }
    return ($current -ge $startTs -or $current -lt $endTs)
}

function Get-PreferredLocalIPv4 {
    try {
        $route = Get-NetRoute -DestinationPrefix "0.0.0.0/0" |
            Where-Object { $_.NextHop -ne "0.0.0.0" } |
            Sort-Object RouteMetric, InterfaceMetric |
            Select-Object -First 1
        if ($route) {
            $adapter = Get-NetAdapter -InterfaceIndex $route.InterfaceIndex -ErrorAction SilentlyContinue
            if ($adapter -and $adapter.InterfaceDescription -notmatch "Hyper-V|Virtual|Tunnel|Meta|Loopback") {
                $ip = Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $route.InterfaceIndex -ErrorAction SilentlyContinue |
                    Where-Object {
                        $_.IPAddress -notlike "127.*" -and
                        $_.IPAddress -notlike "169.254.*" -and
                        $_.IPAddress -notlike "198.18.*" -and
                        $_.IPAddress -notlike "192.168.0.*"
                    } |
                    Select-Object -First 1 -ExpandProperty IPAddress
                if ($ip) { return $ip }
            }
        }
    }
    catch {}

    return (Get-NetIPAddress -AddressFamily IPv4 | Where-Object {
        $_.IPAddress -notlike "127.*" -and
        $_.IPAddress -notlike "169.254.*" -and
        $_.IPAddress -notlike "198.18.*" -and
        $_.IPAddress -notlike "192.168.0.*"
    } | Select-Object -First 1 -ExpandProperty IPAddress)
}

function Test-StatusEndpoint {
    param([string]$PortalIp)
    $statusUrl = "http://$PortalIp/drcom/chkstatus?callback=dr1002"
    try {
        $statusRaw = (Invoke-WebRequest -Uri $statusUrl -UseBasicParsing -TimeoutSec 6).Content
        $statusPayload = Parse-JsonpPayload -Text $statusRaw
        if (-not $statusPayload) { return $null }
        return ($statusPayload | ConvertFrom-Json)
    }
    catch {
        return $null
    }
}

function Select-Profile {
    param(
        [array]$Profiles,
        [string]$LocalIp,
        [string]$PreferredProfile
    )

    if (-not [string]::IsNullOrWhiteSpace($PreferredProfile)) {
        $preferred = $Profiles | Where-Object { [string]$_.name -eq $PreferredProfile } | Select-Object -First 1
        if ($preferred) { return $preferred }
    }

    foreach ($p in $Profiles) {
        if ($p.match_prefixes -and $LocalIp) {
            foreach ($prefix in $p.match_prefixes) {
                if ($LocalIp.StartsWith([string]$prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return $p
                }
            }
        }
    }

    foreach ($p in $Profiles) {
        $st = Test-StatusEndpoint -PortalIp ([string]$p.portal_ip)
        if ($st -and $st.ss5) { return $p }
    }

    return $Profiles[0]
}

try {
    if (-not (Test-Path $ConfigPath)) {
        throw "Config not found: $ConfigPath"
    }

    $config = Get-Content -Path $ConfigPath -Raw | ConvertFrom-Json
    $username = [string]$config.username
    $password = [string]$config.password
    $callback = if ($config.callback) { [string]$config.callback } else { "dr1004" }
    $preferredProfile = if ($config.preferred_profile) { [string]$config.preferred_profile } else { "" }
    $profiles = if ($config.profiles) { @($config.profiles) } else { @() }

    if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($password) -or -not $profiles -or $profiles.Count -eq 0) {
        throw "Missing required config fields: username/password/profiles"
    }

    $localIp = Get-PreferredLocalIPv4
    if ([string]::IsNullOrWhiteSpace($localIp)) {
        throw "Cannot detect local IPv4 address"
    }

    $selected = Select-Profile -Profiles $profiles -LocalIp $localIp -PreferredProfile $preferredProfile
    $baseIp = [string]$selected.portal_ip
    $profileName = if ($selected.name) { [string]$selected.name } else { $baseIp }
    $maintenanceEnabled = if ($null -eq $selected.maintenance_enabled) { $false } else { [bool]$selected.maintenance_enabled }
    $maintenanceStart = if ($selected.maintenance_start) { [string]$selected.maintenance_start } else { "04:00" }
    $maintenanceEnd = if ($selected.maintenance_end) { [string]$selected.maintenance_end } else { "04:15" }

    if ([string]::IsNullOrWhiteSpace($baseIp)) {
        throw "Selected profile has empty portal_ip"
    }

    if ($maintenanceEnabled -and (In-MaintenanceWindow -Start $maintenanceStart -End $maintenanceEnd)) {
        Write-Log "Skip profile=$profileName ip=$baseIp due to maintenance window $maintenanceStart-$maintenanceEnd"
        exit 0
    }

    $statusObj = Test-StatusEndpoint -PortalIp $baseIp
    if ($statusObj -and $statusObj.ss5) {
        $localIp = [string]$statusObj.ss5
    }
    if ([string]::IsNullOrWhiteSpace($localIp)) {
        $localIp = Get-PreferredLocalIPv4
    }

    $userEsc = [uri]::EscapeDataString($username)
    $passEsc = [uri]::EscapeDataString($password)
    $ipEsc = [uri]::EscapeDataString($localIp)
    $loginUrl = "http://${baseIp}:801/eportal/?c=Portal&a=login&callback=$callback&login_method=1&user_account=%2C0%2C$userEsc&user_password=$passEsc&wlan_user_ip=$ipEsc&wlan_user_ipv6=&wlan_user_mac=000000000000&wlan_ac_ip=&wlan_ac_name=&jsVersion=3.3.3&v=1954"

    $respRaw = (Invoke-WebRequest -Uri $loginUrl -UseBasicParsing -TimeoutSec 10).Content
    $respPayload = Parse-JsonpPayload -Text $respRaw
    if (-not $respPayload) {
        throw "Empty login response"
    }

    $respObj = $respPayload | ConvertFrom-Json
    $result = [int]$respObj.result
    $msg = [string]$respObj.msg
    $retCode = if ($null -eq $respObj.ret_code) { "" } else { [string]$respObj.ret_code }

    if ($result -eq 1) {
        Write-Log "Login success profile=$profileName portal=$baseIp ip=$localIp msg=$msg"
        exit 0
    }

    if ($retCode -eq "2") {
        Write-Log "Already online profile=$profileName portal=$baseIp ip=$localIp msg=$msg ret_code=2"
        exit 0
    }

    Write-Log "Login failed profile=$profileName portal=$baseIp ip=$localIp result=$result ret_code=$retCode msg=$msg"
    exit 1
}
catch {
    Write-Log ("Exception: " + $_.Exception.Message)
    exit 1
}
