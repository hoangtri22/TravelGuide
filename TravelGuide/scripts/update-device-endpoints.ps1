param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir,
    [int]$ApiPort = 5096,
    [int]$AdminWebPort = 5280,
    # Ghi đè IP LAN (vd khi auto chọn nhầm card): $env:TRAVELGUIDE_LAN_IP = "192.168.2.10"
    [string]$LanIp = ""
)

$ErrorActionPreference = "Stop"

function Get-PreferredIpv4 {
    $candidates = [System.Net.Dns]::GetHostAddresses([System.Net.Dns]::GetHostName()) |
        Where-Object { $_.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork } |
        ForEach-Object { $_.IPAddressToString } |
        Where-Object { $_ -notlike "127.*" -and $_ -notlike "169.254.*" }

    # Ưu tiên dải private phổ biến.
    $private = $candidates | Where-Object {
        $_ -like "192.168.*" -or $_ -like "10.*" -or $_ -like "172.16.*" -or $_ -like "172.17.*" -or $_ -like "172.18.*" -or $_ -like "172.19.*" -or $_ -like "172.2?.*" -or $_ -like "172.3?.*"
    }

    if ($private -and $private.Count -gt 0) { return $private[0] }
    if ($candidates -and $candidates.Count -gt 0) { return $candidates[0] }
    return $null
}

$projectRoot = [System.IO.Path]::GetFullPath($ProjectDir)
$rawDir = Join-Path $projectRoot "Resources\Raw"
if (-not (Test-Path $rawDir)) {
    New-Item -Path $rawDir -ItemType Directory -Force | Out-Null
}

$targetFile = Join-Path $rawDir "device-endpoints.json"
$fromEnv = if ([string]::IsNullOrWhiteSpace($env:TRAVELGUIDE_LAN_IP)) { "" } else { $env:TRAVELGUIDE_LAN_IP.Trim() }
$ip = if (-not [string]::IsNullOrWhiteSpace($LanIp)) { $LanIp.Trim() }
    elseif (-not [string]::IsNullOrWhiteSpace($fromEnv)) { $fromEnv }
    else { Get-PreferredIpv4 }
if ([string]::IsNullOrWhiteSpace($ip)) {
    Write-Host "[endpoint-auto] Could not detect LAN IPv4; keeping existing file."
    exit 0
}

$apiBase = "http://${ip}:$ApiPort"
$adminBase = "http://${ip}:$AdminWebPort"
$json = @"
{
  "androidPhysicalApiBaseUrl": "$apiBase",
  "androidPhysicalAdminWebBaseUrl": "$adminBase",
  "notes": "Auto-generated on build. Same Wi-Fi as phone. Override IP: `$env:TRAVELGUIDE_LAN_IP or -LanIp. Edit this file before deploy if needed."
}
"@

Set-Content -Path $targetFile -Value $json -Encoding UTF8
Write-Host "[endpoint-auto] androidPhysicalApiBaseUrl => $apiBase"
Write-Host "[endpoint-auto] androidPhysicalAdminWebBaseUrl => $adminBase"
