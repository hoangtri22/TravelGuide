<#
.SYNOPSIS
  Mo Cloudflare Quick Tunnel (HTTPS) toi API hoac AdminWeb chay local.

.DESCRIPTION
  Can cai cloudflared (winget install Cloudflare.cloudflared).
  Tren Windows: tu dong tim URL trong log va **copy vao clipboard may tinh** (khong can copy tay tu log).

  Dien thoai: dung nut "Paste" tren man dang nhap app neu clipboard dong bo; neu khong, gui URL cho minh (chat) roi Paste.

.EXAMPLE
  .\scripts\dev-tunnel.ps1 -Target Api
  .\scripts\dev-tunnel.ps1 -Target AdminWeb
  .\scripts\dev-tunnel.ps1 -Target Api -NoCopyClipboard
#>
param(
    [ValidateSet("Api", "AdminWeb")]
    [string] $Target = "Api",
    [int] $ApiPort = 5096,
    [int] $AdminPort = 5280,
    [switch] $NoCopyClipboard
)

$ErrorActionPreference = "Stop"
$cf = Get-Command cloudflared -ErrorAction SilentlyContinue
if (-not $cf) {
    Write-Host "Chua tim thay 'cloudflared'. Cai dat:" -ForegroundColor Yellow
    Write-Host "  winget install Cloudflare.cloudflared" -ForegroundColor Cyan
    Write-Host "Hoac: https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/" -ForegroundColor Cyan
    exit 1
}

$localUrl = if ($Target -eq "Api") {
    "http://127.0.0.1:$ApiPort"
} else {
    "http://127.0.0.1:$AdminPort"
}

function Strip-Ansi([string] $s) {
    return [regex]::Replace($s, '\x1b\[[0-9;]*m', '')
}

function Try-Extract-TunnelUrl([string] $line) {
    $clean = (Strip-Ansi $line).Trim()
    # Cloudflare quick tunnel
    $m = [regex]::Match($clean, 'https://[\w.-]+\.trycloudflare\.com')
    if ($m.Success) { return $m.Value }
    # ngrok
    $m2 = [regex]::Match($clean, 'https://[a-zA-Z0-9.-]+\.ngrok-free\.app')
    if ($m2.Success) { return $m2.Value }
    $m3 = [regex]::Match($clean, 'https://[a-zA-Z0-9.-]+\.ngrok\.io')
    if ($m3.Success) { return $m3.Value }
    return $null
}

Write-Host ""
Write-Host "Tunnel -> $localUrl ($Target)" -ForegroundColor Green
if (-not $NoCopyClipboard) {
    Write-Host "Windows: URL tunnel se tu dong COPY vao clipboard khi xuat hien trong log." -ForegroundColor Cyan
}
Write-Host "App: nut Paste canh o API server (clipboard tren dien thoai)." -ForegroundColor DarkGray
Write-Host ""

$copied = $false
& cloudflared tunnel --url $localUrl 2>&1 | ForEach-Object {
    $raw = [string]$_
    Write-Host $raw
    if ($NoCopyClipboard -or $copied) { return }
    $url = Try-Extract-TunnelUrl $raw
    if ($null -eq $url) { return }
    try {
        Set-Clipboard -Value $url
        $copied = $true
        Write-Host ""
        Write-Host ">>> Da copy vao clipboard (may tinh): $url" -ForegroundColor Yellow
        Write-Host "    Mo app -> bam Paste canh o API server (neu clipboard dong bo)." -ForegroundColor DarkYellow
        Write-Host ""
    }
    catch {
        Write-Host "(Khong set duoc clipboard: $($_)) - hay copy URL o tren bang tay." -ForegroundColor Red
    }
}
