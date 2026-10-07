@echo off
setlocal
title 21Days Rhythm Locator Playback Fix - keep open
cd /d "%~dp0"
set "locatorNode=node"
where node >nul 2>&1
if not errorlevel 1 goto run
set "locatorNode=%ProgramFiles%\nodejs\node.exe"
if exist "%locatorNode%" goto run
echo ERROR: Node.js is missing. Enable Node.js in PATH; no installation attempted.
pause
exit /b 1
:run
echo Open http://127.0.0.1:8768 after READY. Old port 8766 is untouched.
"%locatorNode%" "%~dp0server.mjs" %1
if errorlevel 1 pause
endlocal
