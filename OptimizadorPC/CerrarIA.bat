@echo off
rem Cierra Claude, ChatGPT/Codex y VS Code con sus procesos de fondo hasta el próximo reinicio.
rem Con "CerrarIA.bat -SinCode" deja VS Code abierto.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0CerrarIA.ps1" %*
