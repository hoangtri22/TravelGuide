@echo off
title TravelGuide - Mo firewall cong 5280 (can quyen Admin)
echo Dang them rule Windows Firewall (TCP 5280 inbound)...
netsh advfirewall firewall delete rule name="TravelGuide AdminWeb 5280" >nul 2>&1
netsh advfirewall firewall add rule name="TravelGuide AdminWeb 5280" dir=in action=allow protocol=TCP localport=5280 profile=any
if errorlevel 1 (
  echo.
  echo LOI: netsh that bai. Thu mo Windows Firewall bang tay: Win+R, go wf.msc, hoac hoi quan tri IT.
  goto end
)
echo.
echo XONG. Dung IP Wi-Fi may tinh, vi du:
echo   http://192.168.x.x:5280
echo.
echo Cac dong IPv4 (tim adapter Wi-Fi):
ipconfig | findstr /i "IPv4"
:end
echo.
pause
