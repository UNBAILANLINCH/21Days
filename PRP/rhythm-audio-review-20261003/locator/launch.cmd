@echo off
rem Explicit local launcher; this visible window owns the server lifetime.
setlocal
title 21Days Rhythm Locator - keep this window open
cd /d "%~dp0"
set "locatorNode=node"
where node >nul 2>&1
if not errorlevel 1 goto run
set "locatorNode=%ProgramFiles%\nodejs\node.exe"
if exist "%locatorNode%" goto run
echo ERROR: Node.js was not found in PATH or Program Files\nodejs.
echo Install or enable Node.js, then launch this file again. No installation was attempted.
pause
exit /b 1
:run
"%locatorNode%" "%~dp0launch.mjs" "%~1"
if errorlevel 1 pause
endlocal
