@echo off
setlocal
cd /d "%~dp0"

echo ============================================================
echo TF2 Idle Manager v1.0.0 - Development Runner
echo ============================================================
echo.

dotnet --version >nul 2>&1
if errorlevel 1 (
  echo ERROR: .NET 8 SDK was not found.
  echo Install the .NET 8 SDK first.
  echo.
  pause
  exit /b 1
)

echo Starting TF2 Idle Manager...
echo.
dotnet run --project TF2IdleManager.csproj
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
  echo.
  echo ============================================================
  echo RUN FAILED with exit code %EXITCODE%.
  echo The window will stay open so the error can be copied.
  echo ============================================================
  echo.
  pause
  exit /b %EXITCODE%
)

exit /b 0
