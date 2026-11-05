@echo off
rem AuditorBots: prepara Python la primera vez y ejecuta la auditoria.
rem Uso: auditar.bat CARPETA [--muestra 30] [--descargas]   (o arrastra la carpeta sobre este archivo)
setlocal
set "APP=%~dp0"
if exist "%APP%.venv\listo" goto ejecutar
echo Preparando el entorno de Python, solo la primera vez...
py -3 -m venv "%APP%.venv" 2>nul || python -m venv "%APP%.venv"
if errorlevel 1 goto sin_python
"%APP%.venv\Scripts\python.exe" -m pip install --disable-pip-version-check -q -r "%APP%requirements.txt"
if errorlevel 1 goto error_pip
echo.> "%APP%.venv\listo"
:ejecutar
if "%~1"=="" (
  "%APP%.venv\Scripts\python.exe" "%APP%auditar.py" --help
  pause
  exit /b 1
)
"%APP%.venv\Scripts\python.exe" "%APP%auditar.py" %*
set "CODIGO=%errorlevel%"
rem Si se abrio con doble clic o arrastrando una carpeta, deja ver el resultado
echo %cmdcmdline% | find /i "%~0" >nul && pause
exit /b %CODIGO%
:sin_python
echo No se encontro Python 3. Instalalo desde https://www.python.org/downloads/ y vuelve a intentarlo.
pause
exit /b 1
:error_pip
echo No se pudieron instalar las dependencias. Revisa la conexion a Internet y vuelve a intentarlo.
pause
exit /b 1
