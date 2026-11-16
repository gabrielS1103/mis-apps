# Servidor de la comunidad de MonitorPing

Recibe los resultados que la gente publica desde MonitorPing (botón *Publicar en la comunidad*) y devuelve los promedios por servidor y proveedor para la ventana *Resultados de la comunidad*. Corre en Cloudflare Workers con una base de datos D1.

- **Qué guarda:** proveedor, número de red (ASN), país, estado y ciudad aproximados, que pone Cloudflare según la conexión; la fecha; y, por servidor medido (o por tramo de red en la ruta en vivo, con su destino), la pérdida, el ping, el jitter y la velocidad.
- **Qué no guarda:** la IP (solo se usa al vuelo para limitar las publicaciones seguidas), el router, el Wi‑Fi ni texto libre. Todo se borra a los 180 días: una tarea diaria (09:00 UTC) lo limpia.
- **Contra abusos:** solo acepta números en rangos válidos y nombres de servidor cortos con caracteres simples; 3 publicaciones por minuto por conexión y 300 por hora en total.

## Direcciones
- `POST /publicar`: `{"tipo":"perdida"|"velocidad"|"ruta","destino":"(solo ruta)","duracion":s,"filas":[{"s":"Servidor","z":"ve"|"fuera","p":%,"m":ms,"j":ms,"b":Mbps,"u":Mbps,"c":ms,"o":bool}]}`
- `GET /ubicacion`: cómo te vería la comunidad (proveedor, país, estado y ciudad), para mostrarlo antes de publicar.
- `GET /resultados?tipo=perdida|velocidad|ruta&horas=24[&pais=VE][&formato=tsv]`

## Publicar cambios
Con Node.js: `npm install`, `npx wrangler login` y `npx wrangler deploy`. Las tablas se crean con `npx wrangler d1 execute monitorping-comunidad --remote --file=schema.sql`.
