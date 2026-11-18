@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ========================================================
echo   KHỞI ĐỘNG CHẾ ĐỘ PHÁT TRIỂN (DEV MODE - HOT RELOAD)
echo   Backend API:  http://localhost:5215
echo   Frontend Dev: http://localhost:5173
echo   Whisper GPU:  http://localhost:5216
echo ========================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $r = Invoke-RestMethod -Uri 'http://127.0.0.1:5216/health' -TimeoutSec 1; if ($r.status -eq 'healthy') { exit 0 } } catch {}; exit 1"
if %errorlevel% neq 0 (
    echo Đang khởi động STP Whisper GPU Service...
    start "STP Whisper GPU Service" cmd /k "python tools\whisper_service.py"
)

start "STP Backend API (5215)" cmd /k "dotnet run --project src\Stp.Api --urls http://localhost:5215"
start "STP Frontend Vite (5173)" cmd /k "npm --prefix src\Stp.Web run dev"

ping 127.0.0.1 -n 4 >nul
start http://localhost:5173

endlocal

