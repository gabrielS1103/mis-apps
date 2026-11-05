@echo off
chcp 65001 >nul
python "%~dp0arreglar_gfn.py" %*
echo.
pause
