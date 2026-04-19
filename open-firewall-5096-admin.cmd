@echo off
title TravelGuide - Mo firewall cong 5096 API (can quyen Admin)
echo Dang them rule Windows Firewall (TCP 5096 inbound)...
netsh advfirewall firewall delete rule name="TravelGuide.API 5096" >nul 2>&1
netsh advfirewall firewall add rule name="TravelGuide.API 5096" dir=in action=allow protocol=TCP localport=5096 profile=any
if errorlevel 1 (
  echo.
  echo LOI: netsh that bai. Thu mo Windows Firewall bang tay: Win+R, go wf.msc, hoac hoi quan tri IT.
  goto end
)
echo.
echo XONG. Chay API: dotnet run --project TravelGuide.API/TravelGuide.API.csproj --launch-profile http
echo Emulator goi: http://10.0.2.2:5096
echo.
:end
pause
