# Mo Windows Firewall cho AdminWeb (Kestrel) tren cong TCP 5280 — LAN/phone truy cap duoc.
# Chay: Right-click -> Run with PowerShell, hoac PowerShell "Run as administrator".

$ruleName = "TravelGuide AdminWeb 5280"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Can quyen Administrator — se mo cua so UAC..." -ForegroundColor Yellow
    $args = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    Start-Process powershell.exe -Verb RunAs -ArgumentList $args
    exit 0
}

$existing = netsh advfirewall firewall show rule name="$ruleName" 2>&1
if ($LASTEXITCODE -eq 0 -and $existing -match "Rule Name:") {
    Write-Host "Rule da ton tai: $ruleName" -ForegroundColor Green
    exit 0
}

netsh advfirewall firewall add rule name="$ruleName" dir=in action=allow protocol=TCP localport=5280 profile=any
if ($LASTEXITCODE -ne 0) {
    Write-Host "Them rule that bai (exit $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Da them rule: $ruleName (TCP 5280 inbound, tat ca profile)." -ForegroundColor Green
Write-Host "Dien thoai cung Wi-Fi co the mo: http://<IP-may-tinh>:5280/download/android/qr.png"
