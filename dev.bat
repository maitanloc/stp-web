@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ========================================================
echo   KHỞI ĐỘNG CHẾ ĐỘ PHÁT TRIỂN (DEV MODE - HOT RELOAD)
echo   Backend API:  http://localhost:5215
echo   Frontend Dev: http://localhost:5173
echo ========================================================
echo.

start "STP Backend API (5215)" cmd /k "dotnet run --project src\Stp.Api --urls http://localhost:5215"
start "STP Frontend Vite (5173)" cmd /k "npm --prefix src\Stp.Web run dev"

timeout /t 3 >nul
start http://localhost:5173

endlocal

