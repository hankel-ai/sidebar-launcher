@echo off
set SCRIPTDIR=%~dp0
set DOTNET=%LOCALAPPDATA%\dotnet\dotnet.exe
set DEST=C:\Users\admin\OneDrive\Programs\SidebarLauncher.exe

if not exist "%DOTNET%" (
    echo ERROR: .NET 8 SDK not found at %DOTNET%
    exit /b 1
)

echo === Stopping running instance ===
taskkill /f /im SidebarLauncher.exe >nul 2>&1
timeout /t 1 /nobreak >nul

echo === Building single-file EXE ===
"%DOTNET%" publish "%SCRIPTDIR%SidebarLauncher.csproj" -c Release --no-restore 2>nul || "%DOTNET%" publish "%SCRIPTDIR%SidebarLauncher.csproj" -c Release
if errorlevel 1 (
    echo BUILD FAILED
    exit /b 1
)

echo === Copying to %DEST% ===
copy /y "%SCRIPTDIR%bin\Release\net8.0-windows\win-x64\publish\SidebarLauncher.exe" "%DEST%"
if errorlevel 1 (
    echo COPY FAILED
    exit /b 1
)

echo === Starting ===
start "" "%DEST%"
echo Done.
