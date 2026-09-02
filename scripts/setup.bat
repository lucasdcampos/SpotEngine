@echo off
setlocal
rem Compile the whole engine and put the 'spot' CLI on the user PATH.

set "REPO=%~dp0.."
for %%I in ("%REPO%") do set "REPO=%%~fI"

call "%~dp0build.bat"
if errorlevel 1 exit /b 1

set "BIN=%REPO%\bin\Spot.Cli\Release\net10.0"
if not exist "%BIN%\spot.exe" (
    echo error: 'spot.exe' was not found at "%BIN%" after the build.
    exit /b 1
)

echo Adding "%BIN%" to your user PATH...
rem Use PowerShell to edit the *user* PATH: SetEnvironmentVariable avoids setx's 1024-char truncation.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$bin = '%BIN%';" ^
    "$path = [Environment]::GetEnvironmentVariable('Path','User');" ^
    "if ([string]::IsNullOrEmpty($path)) { $path = '' }" ^
    "$parts = $path.Split(';') | Where-Object { $_ -ne '' };" ^
    "if ($parts -contains $bin) { Write-Host 'Already on PATH; nothing to do.' }" ^
    "else { $new = (@($parts) + $bin) -join ';'; [Environment]::SetEnvironmentVariable('Path', $new, 'User'); Write-Host 'PATH updated.' }"
if errorlevel 1 (
    echo error: failed to update the user PATH.
    exit /b 1
)

echo.
echo Done. Open a NEW terminal, then run:  spot help
exit /b 0
