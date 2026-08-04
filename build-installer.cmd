@echo off
setlocal

echo.
echo === DeskSpaceOS Installer Build ===
echo.

:: ── Accept args or prompt ────────────────────────────────────────────────────
if not "%~1"=="" (
    set VERSION=%~1
) else (
    set /p VERSION="Version (e.g. 0.1.0): "
)

if not "%~2"=="" (
    set LOCAL_PATH=%~2
) else (
    set /p LOCAL_PATH="Local releases folder (leave blank to use GitHub): "
)

:: ── Check vpk is available ───────────────────────────────────────────────────
where vpk >nul 2>&1
if errorlevel 1 (
    echo.
    echo ERROR: 'vpk' not found. Install it with:
    echo   dotnet tool install -g vpk
    echo.
    pause
    exit /b 1
)

:: ── Invoke the PowerShell script ─────────────────────────────────────────────
if "%LOCAL_PATH%"=="" (
    powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -Version "%VERSION%"
) else (
    powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -Version "%VERSION%" -LocalReleasesPath "%LOCAL_PATH%"
)

if errorlevel 1 (
    echo.
    echo Build FAILED.
    pause
    exit /b 1
)

echo.
pause
