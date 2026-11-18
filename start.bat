@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ========================================================
echo   ĐANG KHỞI ĐỘNG HỆ THỐNG STP ARCHIVE (BE + FE)
echo   Địa chỉ: http://localhost:5215
echo ========================================================
echo.

:: 0. Khởi động STP Whisper GPU Service nếu chưa chạy
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $r = Invoke-RestMethod -Uri 'http://127.0.0.1:5216/health' -TimeoutSec 1; if ($r.status -eq 'healthy') { exit 0 } } catch {}; exit 1"
if %errorlevel% neq 0 (
    echo Đang khởi động STP Whisper GPU Service...
    start "STP Whisper GPU Service" cmd /k "python tools\whisper_service.py"
)

:: 1. Kiểm tra nếu chưa build FE thì tự động build một lần
if not exist "src\Stp.Api\wwwroot\index.html" (
    echo Đang đóng gói giao diện Frontend lần đầu...
    call npm --prefix src\Stp.Web run build
)

:: 2. Khởi động server API tích hợp SPA
start "STP Archive Web Server" cmd /k "dotnet run --project src\Stp.Api --urls http://localhost:5215"

:: 3. Đợi server sẵn sàng và tự động mở trình duyệt
echo Đang chờ server phản hồi...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$url='http://localhost:5215';" ^
  "for($i=0; $i -lt 30; $i++){" ^
  "  try { $r=Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 2; if($r.StatusCode -eq 200){ Start-Process $url; exit } } catch {}" ^
  "  Start-Sleep -Seconds 1" ^
  "};" ^
  "Write-Host 'Đã gửi lệnh chạy. Nếu trình duyệt chưa tự mở, vui lòng vào: http://localhost:5215'"

endlocal
