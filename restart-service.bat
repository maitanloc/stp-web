@echo off
chcp 65001 >nul
title STP Archive - Cập nhật và Khởi động lại dịch vụ

:: Kiểm tra quyền Administrator, nếu chưa có thì tự động xin cấp quyền qua PowerShell
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Yêu cầu quyền Quản trị viên (Administrator)...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd -ArgumentList '/c `\"%~f0`\"' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
echo ========================================================
echo   ĐANG CẬP NHẬT VÀ KHỞI ĐỘNG LẠI STP ARCHIVE API...
echo ========================================================
echo.
echo 1. Đang dừng dịch vụ StpApiService...
net stop StpApiService
timeout /t 2 /nobreak >nul

echo.
echo 2. Đang build bản mới nhất của hệ thống...
dotnet build StpSuite.sln -c Debug
if %errorLevel% neq 0 (
    echo [LỖI] Build thất bại! Đang khởi động lại bản trước...
    net start StpApiService
    pause
    exit /b %errorLevel%
)

echo.
echo 3. Đang khởi động lại dịch vụ và nạp mã nguồn mới...
net start StpApiService
echo.
echo ========================================================
echo   DỊCH VỤ ĐÃ CẬP NHẬT VÀ KHỞI ĐỘNG LẠI THÀNH CÔNG!
echo   Mã nguồn mới nhất đã được nạp lên Link hệ thống.
echo   Local:  http://localhost:5215
echo   Ngrok:  https://mangle-tastiness-french.ngrok-free.dev
echo ========================================================
echo.
pause
