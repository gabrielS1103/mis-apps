@echo off
chcp 65001 >nul
python "%~dp0arreglar_gfn.py" restaurar --no-abrir
echo.
pause
