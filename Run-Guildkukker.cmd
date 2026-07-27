@echo off
setlocal

set "OUTPUT_DIR=%~dp0..\tauriachievements.github.io\src\guild-analysis"
dotnet run --project "%~dp0Guildkukker" -- Evermoon Endless --output-directory "%OUTPUT_DIR%"

if errorlevel 1 (
    echo.
    echo Guildkukker failed.
    pause
    exit /b 1
)

echo.
echo Export completed in: %OUTPUT_DIR%
pause
