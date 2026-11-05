# Mis apps

Utilidades para Windows y Android. Cada app tiene su propia página, con lo que hace, cómo se usa y su enlace de descarga.

| App | Plataforma | Qué hace | Descargar |
|---|---|---|---|
| [**MonitorPing**](MonitorPing/) | Windows | Ping, pérdida de paquetes y cortes de GeForce NOW y Xbox Cloud, con widget, diagnóstico y temas | [MonitorPing.exe (v1.2)](https://github.com/gabrielS1103/mis-apps/releases/download/monitorping-v1.2/MonitorPing.exe) |
| [**ModoJuegoWifi**](ModoJuegoWifi/) | Windows | Apaga la búsqueda de redes Wi‑Fi en segundo plano que provoca picos de ping | [ModoJuegoWifi.exe (v1.0)](https://github.com/gabrielS1103/mis-apps/releases/download/modojuegowifi-v1.0/ModoJuegoWifi.exe) |
| [**MantenerPantalla**](MantenerPantalla/) | Windows | Evita que la pantalla se apague o que el equipo se suspenda | [MantenerPantalla.exe (v1.0)](https://github.com/gabrielS1103/mis-apps/releases/download/mantenerpantalla-v1.0/MantenerPantalla.exe) |
| [**ScrollRemoto**](ScrollRemoto/) | Windows | Pasa los videos del teléfono Android desde la PC, por Wi‑Fi | [ScrollRemoto.exe (v1.0)](https://github.com/gabrielS1103/mis-apps/releases/download/scrollremoto-v1.0/ScrollRemoto.exe) |
| [**OptimizadorPC**](OptimizadorPC/) | Windows | Aligera servicios, arranque y Windows Update, con diagnóstico y opción de deshacer | [Ejecutar.bat](OptimizadorPC/) (script) |
| [**ActualizarReVanced**](ActualizarReVanced/) | Windows + Android | Construye YouTube y YT Music ReVanced con los parches oficiales verificados y los instala por adb | [ActualizarReVanced.bat](ActualizarReVanced/) (script) |
| [**LetrasEnVivo**](LetrasEnVivo/) | Android | Letra sincronizada y traducida al español sobre YouTube Music | [LetrasEnVivo.apk (v1.0)](https://github.com/gabrielS1103/mis-apps/releases/download/letrasenvivo-v1.0/LetrasEnVivo.apk) |
| [**AuditorBots**](AuditorBots/) | Windows (Python) | Extensión que extrae reacciones, comentarios y perfiles de una publicación de Facebook, y análisis de bots y perfiles zombi con informe y margen de error | [auditar.bat](AuditorBots/) (script) |

Las apps de Windows son un solo `.exe` sin instalación ni dependencias: usan el .NET Framework 4 que ya trae Windows.

## Versiones
Cada versión lleva el nombre de su app (por ejemplo, *MonitorPing v1.2*). En la página de cada app hay un enlace con solo sus versiones; todas juntas están en [Releases](https://github.com/gabrielS1103/mis-apps/releases).

## Compilar
- **Apps de Windows:** cada carpeta tiene un `compilar.bat`. Al hacer doble clic genera el `.exe` con el compilador `csc.exe` que incluye Windows. No hace falta Visual Studio.
- **LetrasEnVivo:** se compila con Android Studio o con Gradle; los pasos están en [su página](LetrasEnVivo/).
- **AuditorBots:** no se compila; `auditar.bat` prepara Python la primera vez. Los pasos están en [su página](AuditorBots/).
