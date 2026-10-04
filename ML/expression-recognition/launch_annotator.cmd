@echo off
setlocal
cd /d "%~dp0"
chcp 65001 >nul
if exist ".venv\Scripts\python.exe" (
  ".venv\Scripts\python.exe" -u "analysis\laila_v2_candidate\annotator.py"
) else (
  powershell -NoProfile -Command "$projectPython = uv python find; if ($LASTEXITCODE -ne 0) { exit 1 }; & $projectPython -u 'analysis/laila_v2_candidate/annotator.py'; exit $LASTEXITCODE"
)
if errorlevel 1 pause
