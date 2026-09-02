@echo off
setlocal
rem Remove the 'spot' CLI directory from the user PATH. Leaves build output untouched.

set "REPO=%~dp0.."
for %%I in ("%REPO%") do set "REPO=%%~fI"
set "BIN=%REPO%\bin\Spot.Cli\Release\net10.0"

echo Removing "%BIN%" from your user PATH...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$bin = '%BIN%';" ^
    "$path = [Environment]::GetEnvironmentVariable('Path','User');" ^
    "if ([string]::IsNullOrEmpty($path)) { Write-Host 'User PATH is empty; nothing to do.'; exit 0 }" ^
    "$parts = $path.Split(';') | Where-Object { $_ -ne '' -and $_ -ne $bin };" ^
    "$new = ($parts) -join ';';" ^
    "[Environment]::SetEnvironmentVariable('Path', $new, 'User');" ^
    "Write-Host 'PATH updated.'"
if errorlevel 1 (
    echo error: failed to update the user PATH.
    exit /b 1
)

echo.
echo Done. Open a NEW terminal for the change to take effect.
exit /b 0
