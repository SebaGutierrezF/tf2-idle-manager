@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title TF2 Idle Manager v1.0.0 - Public Release Builder

echo ============================================================
echo TF2 Idle Manager v1.0.0 - Audited Portable Build
 echo ============================================================
where dotnet >nul 2>&1
if errorlevel 1 (
 echo ERROR: .NET 8 SDK not found.
 pause
 exit /b 1
)
where powershell >nul 2>&1
if errorlevel 1 (
 echo ERROR: PowerShell is required for distribution auditing.
 pause
 exit /b 1
)

echo [1/5] Running dependency-free offline regression tests...
dotnet run --project "tests\TF2IdleManager.Tests.csproj" -c Release
if errorlevel 1 goto failed

if not exist release mkdir release
set "STAGE=%CD%\release\_publish_tmp_v1.0.0"
set "PACKAGE=%CD%\release\TF2IdleManager_v1.0.0"
set "ZIP=%CD%\release\TF2IdleManager_v1.0.0-win-x64-portable.zip"
if exist "%STAGE%" rmdir /s /q "%STAGE%"
if exist "%PACKAGE%" rmdir /s /q "%PACKAGE%"
if exist "%ZIP%" del /q "%ZIP%"

echo [2/5] Publishing self-contained, single-file Windows x64 EXE...
dotnet publish "TF2IdleManager.csproj" -c Release -r win-x64 --self-contained true ^
 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
 -p:DebugType=None -p:DebugSymbols=false -o "%STAGE%"
if errorlevel 1 goto failed
if not exist "%STAGE%\TF2IdleManager.exe" goto failed

rem IMPORTANT: allowlisted files only. Do not ship settings/logs/keys/accounts or source.
copy /y "README.md" "%STAGE%\README.md" >nul
copy /y "RELEASE-NOTES.txt" "%STAGE%\RELEASE-NOTES.txt" >nul
copy /y "PORTABLE.md" "%STAGE%\PORTABLE.md" >nul
rem The project embeds the icon into the EXE, but audit requires the two
rem standalone assets too. PublishSingleFile does not guarantee that files
rem marked CopyToOutputDirectory are copied into the publish directory.
rem Explicitly stage only the approved, verified assets; never copy the
rem entire assets folder or user-local app data.
echo [2/5] Staging audited application assets...
if not exist "assets\TF2IdleManager.ico" (
 echo ERROR: Source asset missing: assets\TF2IdleManager.ico
 goto failed
)
if not exist "assets\TF2IdleManager.png" (
 echo ERROR: Source asset missing: assets\TF2IdleManager.png
 goto failed
)
if not exist "%STAGE%\assets" mkdir "%STAGE%\assets"
if errorlevel 1 goto failed
copy /y "assets\TF2IdleManager.ico" "%STAGE%\assets\TF2IdleManager.ico" >nul
if errorlevel 1 (
 echo ERROR: Could not stage assets\TF2IdleManager.ico
 goto failed
)
copy /y "assets\TF2IdleManager.png" "%STAGE%\assets\TF2IdleManager.png" >nul
if errorlevel 1 (
 echo ERROR: Could not stage assets\TF2IdleManager.png
 goto failed
)
if not exist "%STAGE%\assets\TF2IdleManager.ico" goto failed
if not exist "%STAGE%\assets\TF2IdleManager.png" goto failed

echo [3/5] Auditing staging directory before release...
powershell -NoProfile -ExecutionPolicy Bypass -File "AUDIT_RELEASE.ps1" -PackagePath "%STAGE%"
if errorlevel 1 goto failed

echo [4/5] Creating portable ZIP from audited directory...
move "%STAGE%" "%PACKAGE%" >nul
if errorlevel 1 goto failed
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%PACKAGE%\*' -DestinationPath '%ZIP%' -CompressionLevel Optimal -Force"
if errorlevel 1 goto failed

echo [5/5] Auditing portable ZIP and EXE...
powershell -NoProfile -ExecutionPolicy Bypass -File "AUDIT_RELEASE.ps1" -PackagePath "%PACKAGE%" -ZipPath "%ZIP%"
if errorlevel 1 goto failed

echo.
echo Writing SHA-256 checksum for the portable ZIP...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$f='%ZIP%'; $h=(Get-FileHash -Algorithm SHA256 -LiteralPath $f).Hash.ToLowerInvariant(); Set-Content -LiteralPath '%CD%\release\SHA256SUMS-v1.0.0.txt' -Value ($h + '  ' + [IO.Path]::GetFileName($f)) -Encoding ASCII"
if errorlevel 1 goto failed
echo.
echo V1.0.0 BUILD + SECURITY AUDIT OK (LIVE VALIDATION STILL REQUIRED)
echo EXE: %PACKAGE%\TF2IdleManager.exe
echo ZIP: %ZIP%
echo.
pause
exit /b 0
:failed
echo.
echo BUILD OR AUDIT FAILED. Release candidate must NOT be distributed.
echo Check console output. Do not bypass AUDIT_RELEASE.ps1.
pause
exit /b 1
