# AuditorBots

**Windows 10/11 + Chrome o Edge** · Script de Python, sin instalación manual · [Volver a todas las apps](../)

Revisa quién reacciona y comenta en publicaciones o videos de páginas de Facebook y busca señales de granjas de bots o perfiles zombi: cuentas abandonadas desde hace años, plantillas de recaudación repetidas, textos idénticos copiados por varios perfiles y perfiles sin foto ni amigos. Al final genera un informe con el porcentaje estimado de interacciones sospechosas.

Todo ocurre en tu PC. La app **no pide tu contraseña ni abre otro navegador**. Tú entras a Facebook como siempre y eliges la publicación. La extensión de AuditorBots recorre la lista de reacciones o comentarios a ritmo de persona y la guarda en un archivo. No envía nada a ningún servidor.

> **El puntaje es un indicio, no una prueba.** Mucha gente real tiene el perfil cerrado, no publica o usa las plantillas de cumpleaños de Facebook. Revisa los motivos de cada perfil antes de sacar conclusiones y no publiques listas de personas señaladas como bots.

## Cómo se usa

### 1. Instalar la extensión (una vez)
1. Abre `chrome://extensions` (en Edge, `edge://extensions`) y activa el **Modo de desarrollador**.
2. Pulsa **Cargar descomprimida** y elige la carpeta [`extension`](extension/) de AuditorBots.
3. Fija el ícono de la lupa en la barra (en el ícono del rompecabezas, la chincheta).

La extensión solo actúa en facebook.com: abre el panel cuando pulsas su ícono y, en el modo Lote, abre los perfiles de la muestra. No envía nada a ningún servidor.

Si no quieres instalar una extensión, hay otras dos formas de usar el mismo colector:
- **Como marcador:** crea un marcador en la barra de favoritos, llámalo `AuditorBots` y pega como URL todo el contenido de [`colector/marcador.txt`](colector/marcador.txt).
- **Desde la consola:** en Facebook, pulsa F12 → *Consola* y pega el contenido de [`colector/colector.js`](colector/colector.js). La primera vez Chrome pide escribir `allow pasting` (o `permitir pegar`). Facebook muestra un aviso grande de «¡Detente!»: es su advertencia general contra estafas. El colector es código abierto, no envía nada a ninguna parte y puedes leerlo en ese archivo.

### 2. Exportar las reacciones de la publicación
1. Abre la publicación o el video y pulsa el ícono de AuditorBots. Aparece su panel en la esquina.
2. Elige **Reacciones** y pulsa **Automático**. Si la lista de reacciones no está abierta, la abre; si no la encuentra, haz clic tú en el número de reacciones y vuelve a pulsar **Automático**.
3. Espera **con la pestaña a la vista**. La lista baja sola con pausas al azar, como si la desplazaras tú. Si cambias de pestaña o minimizas la ventana, el modo automático se pone en pausa: el navegador frena las pestañas ocultas y se perderían filas. Sigue solo cuando vuelves.
4. Se detiene al llegar a **2.000 perfiles** (puedes cambiarlo en el panel; 0 = todos) y descarga `reacciones-<fecha>.json` en Descargas. También puedes pulsar **Detener** y luego **Descargar**.

**No hace falta leer todas las reacciones.** Con 178.000 reacciones serían horas, y para estimar el porcentaje de bots basta con unos miles. El archivo guarda también el total que muestra Facebook, y el informe extrapola a ese total. Ojo: Facebook no muestra la lista al azar (pone primero a tus amigos y sigue su propio orden), así que la extrapolación es aproximada.

Para los **comentarios**, elige primero «Todos los comentarios» en el orden de la publicación, cambia el panel a **Comentarios** y pulsa **Automático**: pulsa «Ver más comentarios» y «Ver respuestas» hasta que no quede nada y luego descarga. Para saber el tipo de cada reacción, abre la pestaña «Me gusta», «Me encanta»… en la lista y repite **Automático** en cada una.

También puedes hacerlo a mano: desplaza la lista con la rueda y pulsa **Descargar**. El panel lee las filas en cuanto aparecen; hace falta porque Facebook borra de la página las que ya pasaron.

**¿Facebook lo detecta?** La extensión solo lee la página que tienes abierta, así que en sí no es visible para Facebook. Lo que Facebook vigila es el comportamiento: cientos de acciones muy rápidas o durante horas. Por eso el modo automático va a ritmo de persona. Aun así, no audites decenas de publicaciones seguidas; si te preocupa, usa una cuenta secundaria.

### 3. Primera pasada: elegir la muestra
```
auditar.bat C:\Auditorias\pagina-x --descargas
```
`--descargas` mueve a esa carpeta los archivos del colector que haya en Descargas (solo los suyos). También puedes arrastrar la carpeta encima de `auditar.bat`, pero así no se usa `--descargas` y tendrás que mover los archivos a mano. La primera vez prepara Python solo; necesita Python 3.11 o superior y conexión a Internet.

Si el archivo de reacciones es de una versión anterior y no trae el total de Facebook, agrégalo con `--total-reacciones 178000`.

Se genera `muestra.html` con **100 perfiles al azar** (`--muestra 200` para más; con 100 el margen de error ronda el ±10 %). Revisar a todos sería eterno: con una muestra aleatoria el porcentaje de sospechosos es representativo y el informe indica su margen de error.

### 4. Exportar los perfiles de la muestra
**Automático (modo Lote):** en `muestra.html` pulsa **Copiar enlaces**. Abre Facebook, pulsa la lupa, elige **Lote**, pega los enlaces y pulsa **Empezar**. La extensión abre cada perfil en esa pestaña, guarda su ficha y **pasa sola al siguiente hasta terminar**; no hace falta tocar nada. Entre perfil y perfil el panel muestra una cuenta regresiva, y el número sobre el ícono indica por cuál va. Hay dos modos:
- **Vistazo rápido** (recomendado): lee solo lo de arriba de cada perfil, que es su publicación más reciente, cuándo se unió y si tiene foto. Unos 15 s por perfil: 100 perfiles en unos 25 minutos.
- **Completo:** recorre el muro hasta juntar 10 publicaciones. Es más lento (1 o 2 minutos por perfil), pero es el único que detecta las plantillas de recaudación repetidas. Úsalo para revisar a fondo los perfiles que salieron sospechosos.

- Deja la pestaña a la vista mientras trabaja.
- Si Facebook pide verificar la cuenta, el lote se detiene en el acto. Resuelve la verificación y espera unas horas antes de seguir.
- Los perfiles que Facebook ya no muestra (eliminados, suspendidos o que bloquearon la cuenta) quedan como **no disponibles**. No suman puntos, pero el informe los cuenta: Facebook elimina cuentas falsas, así que muchos casos en la muestra son una señal.
- Al usar una extensión con este modo, es mejor hacerlo con una cuenta dedicada solo a esto.

**A mano:** si prefieres revisar cada perfil, ábrelo desde `muestra.html`, pulsa la lupa (se abre en modo **Perfil**) y luego **Automático**. Revisa lo que detectó:
- **Fechas de las publicaciones:** corrígelas si alguna quedó vacía o rara. Basta con lo que muestra Facebook («3 a», «12 de marzo de 2021»…).
- **¿Tiene foto propia? ¿Se ve su lista de amigos?** Si no lo sabes, deja «No sé». Esa opción no suma puntos.
- Quita con **×** las publicaciones que no sean suyas, como las de otras personas en su muro.
- Si el perfil no tiene nada público, marca «No tiene publicaciones visibles».

Pulsa **Descargar** y pulsa **Vaciar** antes de pasar al siguiente perfil.

### 5. Segunda pasada: el informe
Ejecuta el mismo comando otra vez. Sale un resumen en la consola, y en la carpeta quedan:
- `informe.html`: resumen, distribución por año de última actividad, tipos de reacción, clústeres y tabla de perfiles con los motivos (se puede ordenar y filtrar).
- `perfiles.csv` (para Excel) y `perfiles.json`: el detalle de cada perfil.
- `auditoria.sqlite`: todo lo importado. Puedes borrar los `.json` ya importados; volver a ejecutar no duplica nada ni cambia la muestra.

Una carpeta puede reunir varias publicaciones de la misma página: así se ven los perfiles que aparecen en todas.

## Qué revisa
| Regla | Puntos | Cuándo |
|---|---|---|
| Zombi | +35 | Su última publicación visible tiene 3 años o más |
| Plantilla de recaudación | +30 | Repite casi el mismo texto de cumpleaños, donación o GoFundMe con causas o enlaces distintos en menos de 90 días |
| Texto compartido | +20 | Comparte un texto (de 20 letras o más) o un enlace con otros perfiles de la auditoría |
| Identidad | +5 cada una, máx. 15 | El nombre termina en números, no tiene foto propia o no se ven sus amigos |
| Comentario de relleno | +10 | Todos sus comentarios son solo emojis o fórmulas como «Amén», «Bendiciones», «Excelente» |

Nivel: **bajo** de 0 a 29, **medio** de 30 a 59 y **alto** de 60 en adelante. Los perfiles de los que solo se tiene la reacción quedan como **no evaluado**. Los pesos y umbrales están al inicio de [`auditor/heuristicas.py`](auditor/heuristicas.py).

Los porcentajes del informe solo usan los perfiles de la muestra aleatoria con ficha exportada. Si eliges perfiles a mano, el porcentaje deja de ser representativo.

## Si Facebook cambia su página
Si el colector deja de encontrar perfiles o comentarios, ajusta los selectores del bloque `SEL` al inicio de `colector/colector.js` y ejecuta `python colector/generar.py`: regenera el marcador y la copia de la extensión (`extension/colector.js`). Luego, en `chrome://extensions`, pulsa el botón de recargar de AuditorBots. Como respaldo, `auditar.bat` también importa una página guardada con Ctrl+S (*Página web completa*) que tenga abierta la lista de reacciones. Solo trae las filas visibles en ese momento.

## Pruebas
```
.venv\Scripts\python -m pytest pruebas
auditar.bat ejemplos
```
La carpeta `ejemplos` tiene datos inventados (no son personas reales) para ver el informe completo sin usar Facebook.
