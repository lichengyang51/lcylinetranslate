@echo off
setlocal
title LineTranslate - Release

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0MultiChatManager2\Publish-Release.ps1"

if errorlevel 1 (
    echo.
    echo Release failed. Check the error above.
    pause
    exit /b 1
)

echo.
echo Release complete.
pause
