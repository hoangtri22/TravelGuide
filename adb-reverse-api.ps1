# Chuyen cong API (5096) va AdminWeb (5280) tu may host vao Android Emulator qua localhost tren emulator.
# Chay tren may Windows TRUOC khi F5 app MAUI (sau khi emulator da bat).
$ErrorActionPreference = "Stop"
$adb = Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"
if (-not (Test-Path $adb)) { $adb = "adb" }

Write-Host "ADB: $adb" -ForegroundColor Cyan
& $adb devices
& $adb reverse tcp:5096 tcp:5096
& $adb reverse tcp:5280 tcp:5280
Write-Host "OK: tren emulator, http://127.0.0.1:5096 va :5280 toi may host." -ForegroundColor Green
