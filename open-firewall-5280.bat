@echo off
setlocal
title TravelGuide - Mo firewall cong 5280

:: Neu nhap dup .bat ma khong len UAC: hay nhap dup file open-firewall-5280.vbs (on dinh hon).
if exist "%~dp0open-firewall-5280.vbs" (
    echo Ban co the dong cua so nay va nhap DUP vao: open-firewall-5280.vbs
    echo.
)

:: Kiem tra quyen admin; neu chua co thi yeu cau UAC (Run as administrator).
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Dang yeu cau quyen Administrator (UAC)...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b 0
)

echo Them rule Windows Firewall: TCP 5280 (inbound)...
netsh advfirewall firewall delete rule name="TravelGuide AdminWeb 5280" >nul 2>&1
netsh advfirewall firewall add rule name="TravelGuide AdminWeb 5280" dir=in action=allow protocol=TCP localport=5280 profile=any
if %errorLevel% neq 0 (
    echo.
    echo LOI: Khong them duoc rule. Co the rule da ton tai hoac policy chuyen nghiep chan.
    echo Thu xoa rule cu roi chay lai, hoac them rule bang giao dien Windows Firewall.
    pause
    exit /b 1
)

echo.
echo XONG. Dien thoai cung Wi-Fi co the vao (thay IP duoi day):
echo   http://^<IP-may-tinh^>:5280/download/android/qr.png
echo.
echo Cac IPv4 tren may (tim dong Wi-Fi, thuong 192.168.x.x):
ipconfig | findstr /i "IPv4"
echo.
pause
exit /b 0
