@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title TF2 Idle Manager v1.0.0 - Automated Offline Regression Tests
where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: .NET 8 SDK is required for offline tests.
  pause
  exit /b 1
)
echo Running offline regression suite: no Steam requests or account-data changes.
dotnet run --project "tests\TF2IdleManager.Tests.csproj" -c Release
if errorlevel 1 goto failed
echo.
echo Automated offline tests PASSED.
if exist "release\TF2IdleManager_v1.0.0\TF2IdleManager.exe" (
  echo Checking existing portable distribution...
  powershell -NoProfile -ExecutionPolicy Bypass -File "AUDIT_RELEASE.ps1" -PackagePath "release\TF2IdleManager_v1.0.0" -ZipPath "release\TF2IdleManager_v1.0.0-win-x64-portable.zip"
  if errorlevel 1 goto failed
)
echo.
echo ALL AVAILABLE TESTS PASSED.
pause
exit /b 0
:failed
echo.
echo TEST FAILED. Do not distribute this release.
pause
exit /b 1
