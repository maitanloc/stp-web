@echo off
setlocal
cd /d "%~dp0"

echo Dang khoi dong STP Web tai http://localhost:5080 ...
start "STP Web Server" cmd /k "dotnet run --project StpWeb.csproj --urls http://localhost:5080"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$url='http://localhost:5080';" ^
  "for($i=0; $i -lt 60; $i++){" ^
  "  try { $r=Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 2; if($r.StatusCode -eq 200){ Start-Process $url; exit } } catch {}" ^
  "  Start-Sleep -Seconds 1" ^
  "};" ^
  "Write-Host 'Khong ket noi duoc server. Hay xem cua so STP Web Server.'"

endlocal
