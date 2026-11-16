CREATE TABLE IF NOT EXISTS publicaciones (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  fecha INTEGER NOT NULL,      -- segundos desde 1970 (UTC)
  tipo TEXT NOT NULL,          -- 'perdida', 'velocidad' o 'ruta'
  proveedor TEXT NOT NULL,     -- lo da Cloudflare según la conexión, p. ej. 'Net Uno'
  asn INTEGER NOT NULL,
  pais TEXT NOT NULL,          -- 'VE'
  duracion INTEGER NOT NULL,   -- segundos que duró la prueba
  destino TEXT NOT NULL DEFAULT '', -- ruta en vivo: servidor de juego
  region TEXT NOT NULL DEFAULT '',  -- estado aproximado (Cloudflare), p. ej. 'Distrito Capital'
  ciudad TEXT NOT NULL DEFAULT ''   -- ciudad aproximada (Cloudflare)
);
CREATE TABLE IF NOT EXISTS filas (
  pub INTEGER NOT NULL,
  servidor TEXT NOT NULL,
  zona TEXT NOT NULL,          -- 've' (Venezuela) o 'fuera'
  perdida REAL, ms REAL, jitter REAL,
  bajada REAL, subida REAL, carga REAL,
  oficial INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_pub_fecha ON publicaciones (tipo, fecha);
CREATE INDEX IF NOT EXISTS idx_filas_pub ON filas (pub);
-- v2: destino de la ruta en vivo (vacío en las otras pruebas). Ya aplicado en el servidor con:
-- (bases creadas antes de v2) ALTER TABLE publicaciones ADD COLUMN destino TEXT NOT NULL DEFAULT '';
-- v3: ALTER TABLE publicaciones ADD COLUMN region TEXT NOT NULL DEFAULT ''; ALTER TABLE publicaciones ADD COLUMN ciudad TEXT NOT NULL DEFAULT '';
