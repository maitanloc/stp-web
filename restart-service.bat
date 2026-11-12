@echo off
chcp 65001 >nul
title STP Archive - Khởi động lại dịch vụ

:: Kiểm tra quyền Administrator, nếu chưa có thì tự động xin cấp quyền qua PowerShell
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Yêu cầu quyền Quản trị viên (Administrator)...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd -ArgumentList '/c `\"%~f0`\"' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
echo ========================================================
echo   ĐANG KHỞI ĐỘNG LẠI STP ARCHIVE API (StpApiService)...
echo ========================================================
echo.
echo 1. Đang dừng dịch vụ...
net stop StpApiService
timeout /t 2 /nobreak >nul
echo.
echo 2. Đang khởi động lại dịch vụ và nạp mã nguồn mới...
net start StpApiService
echo.
echo ========================================================
echo   DỊCH VỤ ĐÃ KHỞI ĐỘNG LẠI THÀNH CÔNG!
echo   Mã nguồn mới nhất đã được nạp vào hệ thống.
echo   Local:  http://localhost:5215
echo   Ngrok:  https://mangle-tastiness-french.ngrok-free.dev
echo ========================================================
echo.
pause
