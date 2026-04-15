# Chay Admin Web sau khi tat process cu (tranh loi "file is being used by another process").
$ErrorActionPreference = "SilentlyContinue"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "Dang dung instance TravelGuide.AdminWeb / dotnet dang giu project nay..." -ForegroundColor Yellow
Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
    Where-Object { $_.CommandLine -match 'TravelGuide\.AdminWeb' } |
    ForEach-Object {
        Write-Host "  Stop-Process PID $($_.ProcessId)"
        Stop-Process -Id $_.ProcessId -Force
    }
Stop-Process -Name "TravelGuide.AdminWeb" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

Write-Host "Chay Admin Web: http://localhost:5280 (hoac https://localhost:7145)" -ForegroundColor Green
dotnet run --project "$root\TravelGuide.AdminWeb.csproj"
