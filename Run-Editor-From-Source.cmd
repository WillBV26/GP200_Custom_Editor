@echo off
setlocal
cd /d "%~dp0"

echo Building the current source in Debug...
dotnet build "%~dp0Editor\Gp200.Replica.csproj" --configuration Debug --no-restore
if errorlevel 1 goto build_failed

set "EDITOR_DLL=%~dp0Editor\bin\Debug\net10.0-windows\GP200 Replica.dll"
if not exist "%EDITOR_DLL%" goto output_missing

echo.
echo Starting the source build:
echo "%EDITOR_DLL%"
dotnet "%EDITOR_DLL%" --spanish
set "EDITOR_EXIT_CODE=%ERRORLEVEL%"
if not "%EDITOR_EXIT_CODE%"=="0" (
    echo.
    echo The source build exited with code %EDITOR_EXIT_CODE%.
    pause
)
exit /b %EDITOR_EXIT_CODE%

:build_failed
echo.
echo Build failed. The editor was NOT started.
pause
exit /b 1

:output_missing
echo.
echo Debug output was not created. The editor was NOT started.
pause
exit /b 1
