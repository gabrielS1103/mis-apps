# MonitorPing

**Windows 10/11** · Última versión: [**v1.3 – Pruebas de pérdida y velocidad**](https://github.com/gabrielS1103/mis-apps/releases/tag/monitorping-v1.3)

**[Descargar MonitorPing.exe](https://github.com/gabrielS1103/mis-apps/releases/download/monitorping-v1.3/MonitorPing.exe)** · [Todas sus versiones](https://github.com/gabrielS1103/mis-apps/releases?q=MonitorPing&expanded=true) · [Volver a todas las apps](../)

Monitor de conexión para jugar en la nube (GeForce NOW y Xbox Cloud), con un widget flotante y un icono en la barra de tareas.

- **Antes de jugar:** mide el ping a las zonas de GeForce NOW y a las regiones de Azure donde corre xCloud, elige la mejor y dice si es buen momento para jugar.
- **Jugando:** lee los encabezados del stream WebRTC/RTP para mostrar la pérdida real de paquetes, los cortes, los Mbps y el ping dentro del stream.
- **Diagnóstico:** compara con un servidor en EE.UU. y con el router para saber si el problema es del servidor, de tu internet o de tu Wi‑Fi. Avisa si hay una VPN activa.
- **Prueba de pérdida de paquetes:** prueba varios servidores a la vez (router, servidores de Venezuela, Cloudflare, Google, EE.UU., Brasil, zonas de GFN, regiones de xCloud o cualquier host) durante 30 s a 30 min, a 1–10 paquetes por segundo. Muestra pérdida, ping mín/prom/máx, jitter y rachas de pérdidas, dice si el problema es tu red, tu proveedor, su salida internacional o un servidor, y copia los resultados.
- **Prueba de velocidad sin Speedtest:** mide bajada, subida, ping y ping con carga (bufferbloat) con servidores que no son de Speedtest (Leaseweb y Vultr en EE.UU., Vultr y universidades en Brasil, Cloudflare), con 4 conexiones a la vez. Mide también los Speedtest más cercanos y compara, para ver si tu proveedor trata mejor al tráfico de Speedtest.
- **Sin pasar por la VPN:** las dos pruebas pueden salir por el Wi‑Fi o el cable aunque haya una VPN encendida.
- **Ratón y PC:** detecta tirones del ratón y pausas del sistema, e incluye una prueba de ratón en primer plano para ver su frecuencia real.
- **Widget tipo monitor:** el ratón lo atraviesa, tiene transparencia ajustable y modo compacto, y queda por debajo de los menús del sistema.
- **Temas:** Clásico, Aurora (azul marino con resplandores y degradados iridiscentes), Cristal claro, Cristal violeta (cristal esmerilado sobre degradados pastel) y Melodía (negro con resplandores verde y morado, botones en píldora verdes y de contorno). Botones, desplegables, cuadros de texto y listas con bordes redondeados. Cambian el widget, el menú, el icono y todas las ventanas, con fondo animado opcional.

## Cómo se usa
Abre `MonitorPing.exe`: no se instala. Pide permisos de administrador para leer el tráfico del stream (solo cabeceras, nunca el contenido). Todo se controla con clic derecho en su icono de la bandeja: mover el widget, modo compacto, transparencia, tema, prueba de pérdida de paquetes, prueba de velocidad, prueba de ratón y configuración.

## Compilar
Doble clic en `compilar.bat`: genera `MonitorPing.exe` con el `csc.exe` que trae Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`).

## Versiones
| Versión | Novedades |
|---|---|
| [v1.3](https://github.com/gabrielS1103/mis-apps/releases/tag/monitorping-v1.3) | Prueba de pérdida con servidores de Venezuela, EE.UU. y Brasil; prueba de velocidad sin Speedtest; pruebas sin pasar por la VPN; tema Melodía; controles redondeados; widget que se pega a la barra de tareas; los 22 servidores de GeForce NOW en América |
| [v1.2](https://github.com/gabrielS1103/mis-apps/releases/tag/monitorping-v1.2) | Temas: Clásico, Aurora, Cristal claro y Cristal violeta |
| [v1.1](https://github.com/gabrielS1103/mis-apps/releases/tag/monitorping-v1.1) | Prueba de pérdida de paquetes |
| [v1.0](https://github.com/gabrielS1103/mis-apps/releases/tag/monitorping-v1.0) | Primera versión |
