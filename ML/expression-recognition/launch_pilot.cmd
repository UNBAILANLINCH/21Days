@echo off
setlocal
cd /d "%~dp0"
call launch_annotator.cmd --pilot %*
