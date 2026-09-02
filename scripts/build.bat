@echo off
setlocal
rem Compile the whole Spot solution in Release. Used standalone and by setup.bat.

set "REPO=%~dp0.."
for %%I in ("%REPO%") do set "REPO=%%~fI"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo error: 'dotnet' was not found on PATH. Install the .NET 10 SDK from https://dotnet.microsoft.com/download and try again.
    exit /b 1
)

echo Building SpotEngine.slnx (Release)...
dotnet build "%REPO%\SpotEngine.slnx" -c Release
if errorlevel 1 (
    echo error: build failed.
    exit /b 1
)

echo Build succeeded.
exit /b 0
