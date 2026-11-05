# OptimizadorPC

**Windows 10/11** · Script de PowerShell, sin instalación · [Volver a todas las apps](../)

Aligera Windows en equipos modestos: deja en Manual los servicios que no se usan, quita programas del arranque, aplica ajustes de rendimiento, limpia temporales y puede desactivar Windows Update. Cada cambio queda en un respaldo y se puede deshacer.

- **Diagnóstico:** muestra la CPU, la RAM, los procesos que más consumen, los servicios que aún se pueden aligerar, los programas de arranque y el estado de Windows Update.
- **Servicios de Windows:** deja en Manual Cola de impresión, WIA, diagnóstico, geolocalización, servicio telefónico, dispositivos conectados, Optimización de distribución y otros. Deshabilita los servicios de Xbox. Se elige con casillas qué aplicar.
- **Arranque:** quita Razer Synapse y AnyDesk del inicio (siguen funcionando si los abres a mano), pasa a Manual el servicio de UrbanVPN y desactiva la tarea huérfana de Advanced SystemCare. Windscribe es opcional.
- **Rendimiento:** activa el plan Alto rendimiento y desactiva la transparencia, las apps en segundo plano, las sugerencias y apps promocionadas de Windows y Game DVR.
- **Limpieza:** borra temporales con más de 2 días (así no rompe lo que está en uso) y la caché de descargas de Windows Update. La papelera la vacía solo si se lo pides.
- **Windows Update y Edge:** desactiva Windows Update y el actualizador de Edge por directiva, servicios y tareas. Como las firmas de Defender dejan de actualizarse solas, la opción 9 las descarga directamente de Microsoft.
- **Deshacer:** restaura servicios, registro, tareas y plan de energía desde cualquier respaldo anterior. Antes del primer cambio también crea un punto de restauración.

**Nunca toca:** Google Drive, Mem Reduct, Hotspot Shield, la app de UrbanVPN, Seguridad de Windows, Claude, ChatGPT/Codex, VS Code ni Python. Tampoco desinstala nada.

## Cómo se usa
Doble clic en `Ejecutar.bat`. Pide permisos de administrador y abre el menú. La opción **8** aplica todo lo recomendado. Al terminar, reinicia el PC.

Desde una consola también funciona así:
- `OptimizadorPC.ps1 -Diagnostico`: solo muestra el diagnóstico, sin cambiar nada ni pedir administrador.
- `OptimizadorPC.ps1 -Todo`: aplica todo sin preguntas.

Los respaldos y el log se guardan en `%LOCALAPPDATA%\OptimizadorPC`.

## CerrarIA
`CerrarIA.bat` cierra Claude, ChatGPT/Codex y VS Code con todos sus procesos de fondo y detiene sus servicios (`CoworkVMService` y `CodexSandboxService`) hasta el próximo reinicio. Primero cierra las ventanas como si pulsaras la X, así VS Code guarda su sesión, y a los 10 s termina lo que quede. No cambia cómo arrancan, así que al reiniciar todo vuelve a la normalidad.

- `CerrarIA.bat -SinCode`: deja VS Code abierto.
- `CerrarIA.bat -Prueba`: solo muestra lo que cerraría.
