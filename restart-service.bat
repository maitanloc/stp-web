@echo off
chcp 65001 >nul
echo Đang dừng dịch vụ STP Archive API (StpApiService)...
net stop StpApiService
timeout /t 3 /nobreak >nul
echo Đang khởi động lại dịch vụ STP Archive API (StpApiService)...
net start StpApiService
echo.
echo ========================================================
echo Dịch vụ StpApiService đã khởi động lại thành công!
echo Hệ thống đang chạy tại: http://localhost:5215
echo Tunnel ngrok: https://mangle-tastiness-french.ngrok-free.dev
echo ========================================================
pause
