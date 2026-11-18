@echo off
chcp 65001 >nul
title STP Archive - Khởi động Ngrok Tunnel
cd /d "%~dp0"

echo ========================================================
echo   KHỞI ĐỘNG NGROK TUNNEL CHO STP ARCHIVE
echo   Cổng nội bộ: http://localhost:5215
echo   Link cố định: https://mangle-tastiness-french.ngrok-free.dev
echo ========================================================
echo.

:: 1. Kiểm tra ngrok đã cài đặt chưa
where ngrok >nul 2>&1
if %errorlevel% neq 0 (
    echo [LỖI] Chưa tìm thấy ngrok.exe trong PATH!
    echo Vui lòng đảm bảo ngrok đã được cài đặt.
    pause
    exit /b
)

:: 2. Thử chạy ngrok với domain cố định
echo Đang kết nối ngrok tunnel ra Internet...
ngrok http --domain=mangle-tastiness-french.ngrok-free.dev 5215

:: 3. Nếu gãy do chưa có authtoken
if %errorlevel% neq 0 (
    echo.
    echo ========================================================
    echo   NGROK YÊU CẦU MÃ AUTHTOKEN!
    echo ========================================================
    echo 1. Đăng nhập: https://dashboard.ngrok.com/get-started/your-authtoken
    echo 2. Sao chép mã Authtoken của bạn.
    echo 3. Nhập token vào bên dưới và nhấn Enter:
    echo.
    set /p USER_TOKEN="Dán Authtoken vào đây: "
    if defined USER_TOKEN (
        ngrok config add-authtoken %USER_TOKEN%
        echo.
        echo Đã lưu Authtoken thành công! Đang thử kết nối lại...
        ngrok http --domain=mangle-tastiness-french.ngrok-free.dev 5215
    )
)

pause
