@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "RID=win-x64"
if /I "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "RID=win-arm64"
if /I "%PROCESSOR_ARCHITEW6432%"=="ARM64" set "RID=win-arm64"

call :ensure_dotnet_10
if errorlevel 1 goto :failed

echo.
echo [Wine Cabinet] Publishing the %RID% self-contained application...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" -RuntimeIdentifier "%RID%"
if errorlevel 1 (
  echo.
  echo [Wine Cabinet] Build failed. Review the messages above.
  goto :failed
)

set "APP=%~dp0artifacts\publish\%RID%\Wine Cabinet.exe"
if not exist "%APP%" (
  echo [Wine Cabinet] Build completed without the expected executable: "%APP%"
  goto :failed
)

echo [Wine Cabinet] Installing the local build to a stable per-user location...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" -Executable "%APP%" -Launch
if errorlevel 1 goto :failed
exit /b 0

:ensure_dotnet_10
call :has_dotnet_10
if not errorlevel 1 (
  echo [Wine Cabinet] Compatible .NET 10 SDK found: %DOTNET_SDK_VERSION%
  exit /b 0
)

echo [Wine Cabinet] .NET 10 SDK was not found. Installing it now...
where winget >nul 2>nul
if not errorlevel 1 (
  winget install --id Microsoft.DotNet.SDK.10 --exact --source winget --accept-source-agreements --accept-package-agreements
  if not errorlevel 1 (
    call :has_dotnet_10
    if not errorlevel 1 exit /b 0
  )
  echo [Wine Cabinet] WinGet installation did not provide a usable SDK. Trying a per-user installation...
) else (
  echo [Wine Cabinet] WinGet is unavailable. Trying a per-user installation...
)

set "DOTNET_INSTALL_DIR=%LOCALAPPDATA%\Microsoft\dotnet"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $installer = Join-Path $env:TEMP 'wine-cabinet-dotnet-install.ps1'; Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer; try { & $installer -Channel '10.0' -Quality 'GA' -InstallDir $env:DOTNET_INSTALL_DIR -NoPath; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } } finally { Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue }"
if errorlevel 1 (
  echo [Wine Cabinet] Automatic .NET 10 SDK installation failed.
  exit /b 1
)

call :has_dotnet_10
if errorlevel 1 (
  echo [Wine Cabinet] .NET 10 SDK is still unavailable after installation.
  exit /b 1
)

echo [Wine Cabinet] .NET 10 SDK installed successfully: %DOTNET_SDK_VERSION%
exit /b 0

:has_dotnet_10
set "DOTNET_SDK_VERSION="
set "DOTNET_EXE="
for /f "delims=" %%D in ('where dotnet 2^>nul') do if not defined DOTNET_EXE call :probe_dotnet "%%D"
if not defined DOTNET_EXE if exist "%ProgramFiles%\dotnet\dotnet.exe" call :probe_dotnet "%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET_EXE if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" call :probe_dotnet "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if not defined DOTNET_EXE exit /b 1
for %%D in ("%DOTNET_EXE%") do (
  set "DOTNET_ROOT=%%~dpD"
  set "PATH=%%~dpD;%PATH%"
)
exit /b 0

:probe_dotnet
set "DOTNET_CANDIDATE=%~1"
set "DOTNET_CANDIDATE_VERSION="
for /f "tokens=1" %%V in ('""%DOTNET_CANDIDATE%" --list-sdks 2^>nul"') do call :accept_dotnet_10 "%%V"
if defined DOTNET_CANDIDATE_VERSION (
  set "DOTNET_EXE=%DOTNET_CANDIDATE%"
  set "DOTNET_SDK_VERSION=%DOTNET_CANDIDATE_VERSION%"
)
exit /b 0

:accept_dotnet_10
for /f "tokens=1 delims=." %%M in ("%~1") do if "%%M"=="10" set "DOTNET_CANDIDATE_VERSION=%~1"
exit /b 0

:failed
echo.
echo [Wine Cabinet] The application was not started.
pause
exit /b 1
