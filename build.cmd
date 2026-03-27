@echo off
setlocal

set DOTNET=%LOCALAPPDATA%\dotnet\dotnet.exe
set SCRIPTDIR=%~dp0

echo Building SidebarLauncher...
"%DOTNET%" publish "%SCRIPTDIR%SidebarLauncher.csproj" -c Release --no-restore

if %ERRORLEVEL% equ 0 (
    echo.
    echo Build successful!
    for %%F in ("%SCRIPTDIR%bin\Release\net8.0-windows\win-x64\publish\SidebarLauncher.exe") do echo Output: %%~fF (%%~zF bytes)
) else (
    echo Build FAILED.
)
