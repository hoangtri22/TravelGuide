<#
.SYNOPSIS
    Tai app qua QR qua Internet: dong bo APK, Admin Web (5280), Cloudflare Quick Tunnel, mo trang QR tren URL tunnel.

.DESCRIPTION
    Mac dinh: BAT BUOC co cloudflared de dien thoai 4G / Wi-Fi khac tai duoc (khong can cung mang voi PC).
    Chi khi them -NoTunnel: chi mo localhost / mang noi bo (dev LAN).

.PARAMETER BuildAndroid
    Build MAUI Android (Debug) truoc khi dong bo APK.

.PARAMETER NoTunnel
    Khong chay cloudflared; mo http://localhost:5280 (chi hop khi dien thoai cung mang / ban tu y dung LAN).

.PARAMETER NoBrowser
    Khong mo trinh duyet.

.PARAMETER TunnelWaitSec
    So giay cho URL trycloudflare xuat hien trong log (mac dinh 50).

.EXAMPLE
    .\scripts\QrAppDownload.ps1
.EXAMPLE
    .\scripts\QrAppDownload.ps1 -BuildAndroid
.EXAMPLE
    .\scripts\QrAppDownload.ps1 -NoTunnel
#>
param(
    [switch]$BuildAndroid,
    [switch]$NoTunnel,
    [switch]$NoBrowser,
    [int]$Port = 5280,
    [int]$TunnelWaitSec = 50
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Test-TcpPort([int]$port) {
    $c = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    return $null -ne $c
}

function Wait-TcpPort([int]$port, [int]$maxWaitSec = 60) {
    $deadline = (Get-Date).AddSeconds($maxWaitSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-TcpPort $port) { return $true }
        Start-Sleep -Milliseconds 400
    }
    return $false
}

function Find-TryCloudflareUrl([string[]]$paths) {
    $rx = 'https://[a-z0-9-]+\.trycloudflare\.com'
    foreach ($p in $paths) {
        if (-not (Test-Path -LiteralPath $p)) { continue }
        $m = Select-String -LiteralPath $p -Pattern $rx -AllMatches | Select-Object -First 1
        if ($m -and $m.Matches.Count -gt 0) {
            return $m.Matches[0].Value.TrimEnd('/')
        }
    }
    return $null
}

Write-Host "=== TravelGuide: QR tai app (tunnel / Internet) ===" -ForegroundColor Cyan

if ($BuildAndroid) {
    Write-Host "[1/4] Build Android (Debug)..." -ForegroundColor Yellow
    dotnet build (Join-Path $repoRoot "TravelGuide\TravelGuide.csproj") -f net9.0-android -c Debug -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Build Android failed." }
}
else {
    Write-Host "[1/4] Bo qua build Android (dung -BuildAndroid neu can)." -ForegroundColor DarkGray
}

Write-Host "[2/4] Dong bo APK vao Admin Web..." -ForegroundColor Yellow
& (Join-Path $repoRoot "sync-android-apk.ps1")

$apkPath = Join-Path $repoRoot "TravelGuide.AdminWeb\WEB\apk\travelguide-latest.apk"
if (-not (Test-Path -LiteralPath $apkPath)) {
    throw "Khong co file APK tai $apkPath - chay: dotnet build TravelGuide\TravelGuide.csproj -f net9.0-android -c Debug roi chay lai (hoac -BuildAndroid)."
}

$apkSizeStr = '{0:N1}' -f ((Get-Item $apkPath).Length / 1MB)
Write-Host "      APK OK ($apkSizeStr MB)" -ForegroundColor Green

if (Test-TcpPort $Port) {
    Write-Host "Cong $Port dang duoc dung - gia dinh Admin Web da chay." -ForegroundColor Yellow
}
else {
    Write-Host "[3/4] Build + khoi dong Admin Web (cong $Port)..." -ForegroundColor Yellow
    $csproj = Join-Path $repoRoot "TravelGuide.AdminWeb\TravelGuide.AdminWeb.csproj"
    dotnet build $csproj -v q
    if ($LASTEXITCODE -ne 0) { throw "Build Admin Web failed." }
    Start-Process -FilePath "dotnet" -ArgumentList @(
        "run", "--project", $csproj, "--launch-profile", "http", "--no-build"
    ) -WindowStyle Minimized
    if (-not (Wait-TcpPort $Port 90)) {
        throw "Admin Web khong len duoc tren cong $Port trong 90s."
    }
    Write-Host "      Admin Web dang listen $Port" -ForegroundColor Green
}

$page = "/download-android.html"
$tunnelUrl = $null
$openUrl = $null

if ($NoTunnel) {
    Write-Host "[4/4] Bo qua tunnel (-NoTunnel) - chi mang noi bo / localhost." -ForegroundColor Yellow
    $openUrl = "http://127.0.0.1:$Port$page"
}
else {
    $cfCmd = Get-Command cloudflared -ErrorAction SilentlyContinue
    $cf = if ($cfCmd) { $cfCmd.Source } else { $null }
    if (-not $cf -or -not (Test-Path -LiteralPath $cf)) {
        $cf = "C:\Program Files (x86)\cloudflared\cloudflared.exe"
    }
    if (-not (Test-Path -LiteralPath $cf)) {
        $msg = "Can cai cloudflared de tai app tu mang KHAC (4G, Wi-Fi khac) - khong can cung mang voi PC.`n" +
            "Huong dan: https://developers.cloudflare.com/cloudflare-one/connections/connect-apps/install-and-setup/installation/`n" +
            "Sau khi cai, chay lai: .\scripts\QrAppDownload.ps1`n" +
            "Neu CHI thu noi bo (LAN): .\scripts\QrAppDownload.ps1 -NoTunnel"
        throw $msg
    }

    Write-Host "[4/4] Cloudflare Quick Tunnel -> http://127.0.0.1:$Port (cho $TunnelWaitSec s)..." -ForegroundColor Yellow
    $log = Join-Path $env:TEMP "travelguide-cloudflared-$Port.log"
    $cfOut = Join-Path $env:TEMP "travelguide-cloudflared-out.log"
    Remove-Item $log, $cfOut -ErrorAction SilentlyContinue
    Start-Process -FilePath $cf -ArgumentList @("tunnel", "--url", "http://127.0.0.1:$Port") `
        -WindowStyle Hidden -RedirectStandardError $log -RedirectStandardOutput $cfOut

    $deadline = (Get-Date).AddSeconds($TunnelWaitSec)
    while ((Get-Date) -lt $deadline) {
        $tunnelUrl = Find-TryCloudflareUrl @($log, $cfOut)
        if ($tunnelUrl) { break }
        Start-Sleep -Milliseconds 500
    }

    if (-not $tunnelUrl) {
        throw "Khong doc duoc URL *.trycloudflare.com sau $TunnelWaitSec s. Mo file log: $log va $cfOut (dam bao Admin Web dang chay tren cong $Port)."
    }

        Write-Host "      Tunnel: $tunnelUrl" -ForegroundColor Green
    $openUrl = "$tunnelUrl$page"
    try {
        Set-Content -Path (Join-Path $repoRoot "LAST_TUNNEL_URL.txt") -Value $openUrl -Encoding utf8
        Write-Host "      Da luu URL vao LAST_TUNNEL_URL.txt (de copy sau)." -ForegroundColor DarkGray
    }
    catch { }
}

Write-Host ""
Write-Host "Mo trang QR / tai app (mang bat ky, qua tunnel):" -ForegroundColor Cyan
Write-Host "  $openUrl"
Write-Host ""
Write-Host "LUU Y: URL *.trycloudflare.com CHI ton tai khi process cloudflared dang chay." -ForegroundColor Yellow
Write-Host "        Dong tunnel / tat may / mo bookmark cu -> loi DNS nhu NXDOMAIN." -ForegroundColor Yellow
Write-Host ""

if (-not $NoBrowser) {
    Start-Process $openUrl
}
