@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ========================================================
echo   KHỞI ĐỘNG STP WHISPER LOCAL SERVICE (GPU CUDA)
echo   Port: http://localhost:5216
echo ========================================================
python tools\whisper_service.py
pause

