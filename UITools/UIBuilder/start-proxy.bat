@echo off
REM ============================================================
REM  UI Builder - AI Proxy launcher
REM  Double-click to start the local AI proxy (127.0.0.1:8788)
REM  Keep this window open while using the builder. Ctrl+C to stop.
REM ============================================================
cd /d "%~dp0"

where node >nul 2>&1
if errorlevel 1 (
  echo [ERROR] Node.js not found in PATH. Please install Node.js and add it to PATH.
  echo         Download: https://nodejs.org/
  pause
  exit /b 1
)

echo Starting UI Builder AI proxy on http://127.0.0.1:8788 ...
echo Keep this window open. Press Ctrl+C to stop.
echo.
node ai-proxy.js
pause
