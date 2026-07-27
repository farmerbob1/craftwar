@echo off
setlocal

rem Starts the Craftwar relay server for local dev. Double-click this, or
rem run from a terminal with extra args, e.g.:
rem   start-server.bat --port 27020 --db C:\path\to\other.db
rem Args and CRAFTWAR_* env vars both work - see Server\README.md.

cd /d "%~dp0Craftwar.NetServer"
echo Starting Craftwar relay server...
echo (Ctrl+C stops it cleanly; closing this window does not.)
echo.

dotnet run -- %*

echo.
echo Server stopped (exit code %ERRORLEVEL%).
pause
endlocal
