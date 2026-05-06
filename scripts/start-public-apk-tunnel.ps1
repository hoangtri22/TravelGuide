param(
    [string]$EnvFile = "TravelGuide.AdminWeb/.env",
    [string]$Project = "TravelGuide.AdminWeb/TravelGuide.AdminWeb.csproj",
    [int]$CloudflaredRestartDelaySeconds = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Import-DotEnv([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "Không tìm thấy file env: $Path. Hãy copy từ TravelGuide.AdminWeb/.env.example"
    }
    Get-Content $Path | ForEach-Object {
        $line = $_.Trim()
        if (-not $line -or $line.StartsWith("#")) { return }
        $idx = $line.IndexOf("=")
        if ($idx -lt 1) { return }
        $k = $line.Substring(0, $idx).Trim()
        $v = $line.Substring($idx + 1).Trim()
        [System.Environment]::SetEnvironmentVariable($k, $v, "Process")
    }
}

function Start-AdminWebProcess {
    Write-Host "Starting AdminWeb..." -ForegroundColor Cyan
    return Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", $Project) -PassThru
}

function Start-TunnelLoop {
    param([string]$TunnelName)
    while ($true) {
        Write-Host "Starting cloudflared tunnel: $TunnelName" -ForegroundColor Yellow
        & cloudflared tunnel run $TunnelName
        $code = $LASTEXITCODE
        Write-Host "cloudflared stopped (exit=$code), reconnecting in $CloudflaredRestartDelaySeconds s..." -ForegroundColor Red
        Start-Sleep -Seconds $CloudflaredRestartDelaySeconds
    }
}

Import-DotEnv $EnvFile

$publicApkUrl = [System.Environment]::GetEnvironmentVariable("TRAVELGUIDE_PUBLIC_APK_URL", "Process")
$tunnelName = [System.Environment]::GetEnvironmentVariable("TRAVELGUIDE_CLOUDFLARE_TUNNEL_NAME", "Process")
if ([string]::IsNullOrWhiteSpace($publicApkUrl)) {
    throw "Thiếu TRAVELGUIDE_PUBLIC_APK_URL trong $EnvFile"
}
if ([string]::IsNullOrWhiteSpace($tunnelName)) {
    throw "Thiếu TRAVELGUIDE_CLOUDFLARE_TUNNEL_NAME trong $EnvFile"
}

$adminWeb = Start-AdminWebProcess
Write-Host "Public APK URL: $publicApkUrl" -ForegroundColor Green

try {
    Start-TunnelLoop -TunnelName $tunnelName
}
finally {
    if ($adminWeb -and -not $adminWeb.HasExited) {
        Stop-Process -Id $adminWeb.Id -Force
    }
}
