@ECHO OFF

ECHO Spot Engine Building Tools
powershell -ExecutionPolicy ByPass -NoProfile -File "%~dp0build.ps1" %*
PAUSE