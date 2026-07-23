@echo off
setlocal

set "PRIVATE_DIR=%~dp0.."
dotnet run --project "%~dp0Guildkukker" -- Evermoon Endless --output-directory "%PRIVATE_DIR%"

if errorlevel 1 (
    echo.
    echo Guildkukker failed.
    pause
    exit /b 1
)

echo.
echo Export completed in: %PRIVATE_DIR%
pause
