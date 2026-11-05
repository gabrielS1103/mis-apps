# ActualizarReVanced

**Windows + Android** · Script de PowerShell · [Volver a todas las apps](../)

Construye **YouTube ReVanced** y **YT Music ReVanced** con los parches oficiales de ReVanced y los instala en el teléfono por adb. Antes de usar cada pieza comprueba que sea auténtica. Si alguna firma no coincide o algún parche falla, no instala nada.

## Qué hace
1. **Parches:** descarga los oficiales desde `api.revanced.app` y verifica su firma PGP contra la huella de la clave de ReVanced, que está fijada en el script (`A783 5DFC … FBE6 A2FF`).
2. **Apps originales:** elige la versión más nueva que soportan los parches y descarga el APK original de APKMirror. Después comprueba que lo firmó Google con el mismo certificado que la app del Play Store.
3. **Parcheo:** aplica todos los parches salvo los que descargan contenido y firma con tu clave propia. Así cada versión nueva se instala encima de la anterior sin perder ajustes.
4. **MicroG:** descarga el oficial de `github.com/ReVanced/GmsCore` y verifica su firma.
5. **Teléfono:** instala solo lo que cambió, en el espacio principal (usuario 0). Al final actualiza la carpeta **ReVanced para compartir** del Escritorio con los tres APK, un `LEEME.txt` y las huellas SHA-256.

Si ya está todo al día, lo dice y no cambia nada.

## Cómo se usa
Doble clic en `ActualizarReVanced.bat`. Para que instale en el teléfono, este tiene que estar conectado por cable o por depuración inalámbrica. Si no lo está, igual deja los APK listos en la carpeta para compartir.

- `ActualizarReVanced.bat -SinInstalar`: solo construye los APK.
- `ActualizarReVanced.bat -Forzar`: vuelve a construir aunque ya esté al día.

## Requisitos
Funciona en esta PC sin Claude. Usa lo que ya está instalado:
- Java 17 y el SDK de Android, en `%LOCALAPPDATA%\Android`.
- `gpg`, que viene con Git para Windows.
- `curl.exe`, que viene con Windows.
- Internet.

## Si algo falla
- **APKMirror no deja descargar:** el script dice qué versión hace falta. Descárgala a mano desde apkmirror.com, eligiendo la variante **APK** (no BUNDLE) para `arm64-v8a` o `universal`. Guárdala en `%LOCALAPPDATA%\ReVancedPropio\manual` y vuelve a ejecutar. El script igual comprueba que la firmó Google.
- **"Está firmada con otra clave":** la app del teléfono no la hizo este script. Hay que desinstalarla primero.
- **Fallan parches o cambian las direcciones de ReVanced:** se mantiene la versión anterior. Hay que ajustar el script.

Todo se guarda en `%LOCALAPPDATA%\ReVancedPropio`: la clave de firma, los APK, el estado y los registros de cada parcheo. **No borres `revanced-propio.keystore`:** sin esa clave, para actualizar habría que desinstalar las apps.
