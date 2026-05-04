@echo off
setlocal EnableDelayedExpansion

set SCRIPTDIR=%~dp0
set DOTNET_CHANNEL=8.0
set DOTNET_INSTALL_DIR=%LOCALAPPDATA%\dotnet

REM --- Locate dotnet.exe -------------------------------------------------
set "DOTNET="

if exist "%DOTNET_INSTALL_DIR%\dotnet.exe" set "DOTNET=%DOTNET_INSTALL_DIR%\dotnet.exe"

if not defined DOTNET (
    for %%P in (dotnet.exe) do (
        if exist "%%~$PATH:P" set "DOTNET=%%~$PATH:P"
    )
)

if not defined DOTNET (
    if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
)

REM --- Verify SDK is .NET 8+ ---------------------------------------------
set NEED_INSTALL=0
if not defined DOTNET (
    set NEED_INSTALL=1
) else (
    "%DOTNET%" --list-sdks | findstr /b "8." >nul 2>&1
    if errorlevel 1 set NEED_INSTALL=1
)

REM --- Install .NET 8 SDK (user-local, no admin) -------------------------
if "%NEED_INSTALL%"=="1" (
    echo .NET 8 SDK not found. Installing to %DOTNET_INSTALL_DIR% ...
    set "INSTALL_SCRIPT=%TEMP%\dotnet-install.ps1"
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile '%INSTALL_SCRIPT%'"
    if errorlevel 1 (
        echo ERROR: Failed to download dotnet-install.ps1
        exit /b 1
    )
    powershell -NoProfile -ExecutionPolicy Bypass -File "%INSTALL_SCRIPT%" -Channel %DOTNET_CHANNEL% -InstallDir "%DOTNET_INSTALL_DIR%"
    if errorlevel 1 (
        echo ERROR: dotnet-install.ps1 failed
        exit /b 1
    )
    set "DOTNET=%DOTNET_INSTALL_DIR%\dotnet.exe"
)

if not exist "%DOTNET%" (
    echo ERROR: dotnet.exe still not found at "%DOTNET%"
    exit /b 1
)

echo Using dotnet: %DOTNET%
"%DOTNET%" --version

REM --- Restore + publish -------------------------------------------------
echo Stopping any running SidebarLauncher instance...
taskkill /f /im SidebarLauncher.exe >nul 2>&1
timeout /t 1 /nobreak >nul

echo Restoring packages...
"%DOTNET%" restore "%SCRIPTDIR%SidebarLauncher.csproj"
if errorlevel 1 (
    echo Restore FAILED.
    exit /b 1
)

echo Building SidebarLauncher...
"%DOTNET%" publish "%SCRIPTDIR%SidebarLauncher.csproj" -c Release
if errorlevel 1 (
    echo Build FAILED.
    exit /b 1
)

echo.
echo Build successful!
for %%F in ("%SCRIPTDIR%bin\Release\net8.0-windows\win-x64\publish\SidebarLauncher.exe") do echo Output: %%~fF (%%~zF bytes)
endlocal
