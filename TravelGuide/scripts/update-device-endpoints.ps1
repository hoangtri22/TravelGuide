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
    # 1) Ưu tiên card đang có default gateway (thường là Wi‑Fi / Ethernet thật), tránh Hyper‑V / vEthernet / WSL…
    try {
        $configs = Get-NetIPConfiguration -ErrorAction SilentlyContinue | Where-Object {
            $null -ne $_.IPv4DefaultGateway -and
            $_.NetAdapter -and
            $_.NetAdapter.Status -eq "Up" -and
            $_.InterfaceAlias -notmatch "Loopback|vEthernet|Hyper-V|VirtualBox|VMware|WSL|Tailscale|ZeroTier|TAP|TUN|Bluetooth"
        }
        foreach ($c in ($configs | Sort-Object { $_.InterfaceMetric })) {
            $ip = $c.IPv4Address.IPAddress
            if ([string]::IsNullOrWhiteSpace($ip)) { continue }
            if ($ip -like "127.*" -or $ip -like "169.254.*") { continue }
            return $ip
        }
    }
    catch {
        # Module NetTCPIP không có (hiếm) → dùng bước 2
    }

    # 2) Fallback: tất cả IPv4 của máy, ưu dải private
    $candidates = [System.Net.Dns]::GetHostAddresses([System.Net.Dns]::GetHostName()) |
        Where-Object { $_.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork } |
        ForEach-Object { $_.IPAddressToString } |
        Where-Object { $_ -notlike "127.*" -and $_ -notlike "169.254.*" }

    $private = $candidates | Where-Object {
        $_ -like "192.168.*" -or $_ -like "10.*" -or
        $_ -match '^172\.(1[6-9]|2[0-9]|3[0-1])\.'
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

$targetFile = Join-Path $rawDir "device_endpoints.json"
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
  "notes": "Auto LAN IP at last Windows build (PrepareForBuild). Rebuild after Wi-Fi change or set env TRAVELGUIDE_LAN_IP."
}
"@

$tmpFile = $targetFile + ".tmp"
try {
    Set-Content -Path $tmpFile -Value $json -Encoding UTF8 -Force
    Move-Item -Path $tmpFile -Destination $targetFile -Force
}
catch {
    Write-Warning "[endpoint-auto] Could not write $targetFile : $($_.Exception.Message)"
    if (Test-Path $tmpFile) { Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue }
    exit 0
}
Write-Host "[endpoint-auto] androidPhysicalApiBaseUrl => $apiBase"
Write-Host "[endpoint-auto] androidPhysicalAdminWebBaseUrl => $adminBase"
