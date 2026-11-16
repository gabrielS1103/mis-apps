// Servidor de "Resultados de la comunidad" de MonitorPing (Cloudflare Workers + D1).
//
// Qué guarda: el proveedor, el país, el estado y la ciudad aproximados (los da Cloudflare a partir de la conexión, no la app), la fecha y, por servidor
// medido, la pérdida, el ping y la velocidad. Qué NO guarda: la IP de nadie (solo se usa al vuelo para limitar cuántas
// veces se puede publicar seguido), ni el router, ni el Wi-Fi, ni texto libre. Se borra todo lo que tenga más de 180 días (una tarea diaria lo limpia).

const DIAS_GUARDADOS = 180;
const MAX_FILAS = 40;
const NOMBRE = /^[\p{L}\p{N} ·.,()\-\/&'+]{2,70}$/u;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    try {
      if (request.method === "POST" && url.pathname === "/publicar") return await publicar(request, env);
      if (request.method === "GET" && url.pathname === "/resultados") return await resultados(url, env);
      // Cómo te verían los demás si publicas (para mostrarlo antes de preguntar)
      if (request.method === "GET" && url.pathname === "/ubicacion") return json(lugar(request.cf || {}));
      if (request.method === "GET" && url.pathname === "/") return texto("MonitorPing: resultados de la comunidad. Código: https://github.com/gabrielS1103/mis-apps/tree/main/MonitorPing/servidor");
      return texto("No existe", 404);
    } catch (e) {
      return texto("Error del servidor", 500);
    }
  },

  // Todos los días: borra lo que tenga más de DIAS_GUARDADOS, aunque nadie publique
  async scheduled(evento, env) {
    await env.DB.batch(limpiar(env, Math.floor(Date.now() / 1000)));
  },
};

function limpiar(env, ahora) {
  const limite = ahora - DIAS_GUARDADOS * 86400;
  return [
    env.DB.prepare("DELETE FROM filas WHERE pub IN (SELECT id FROM publicaciones WHERE fecha < ?)").bind(limite),
    env.DB.prepare("DELETE FROM publicaciones WHERE fecha < ?").bind(limite),
  ];
}

function texto(t, estado = 200) {
  return new Response(t, { status: estado, headers: { "content-type": "text/plain; charset=utf-8" } });
}

function json(o, estado = 200) {
  return new Response(JSON.stringify(o), { status: estado, headers: { "content-type": "application/json; charset=utf-8", "cache-control": "max-age=30" } });
}

function numero(v, max) {
  if (v === null || v === undefined) return null;
  const n = Number(v);
  if (!Number.isFinite(n) || n < 0 || n > max) throw new Error("número fuera de rango");
  return Math.round(n * 10) / 10;
}

// Proveedor tal como lo ve Cloudflare: "Net Uno C.A." -> "Net Uno"
function lugar(cf) {
  return { proveedor: proveedor(cf), pais: (cf.country || "").slice(0, 2), region: (cf.region || "").slice(0, 60), ciudad: (cf.city || "").slice(0, 60) };
}

function proveedor(cf) {
  let n = (cf && cf.asOrganization) || "";
  n = n.replace(/,.*$/, "").replace(/\b(C\.?\s?A\.?|S\.?\s?A\.?|LLC|Inc\.?|Ltd\.?|S\.?R\.?L\.?)$/i, "").trim();
  if (n === n.toUpperCase()) n = n.split(" ").map(w => (w.length > 5 ? w[0] + w.slice(1).toLowerCase() : w)).join(" ");
  return n.slice(0, 40) || (cf && cf.asn ? "AS" + cf.asn : "desconocido");
}

async function publicar(request, env) {
  // Límite: 3 publicaciones por minuto por conexión. La IP no se guarda en ningún sitio.
  const ip = request.headers.get("cf-connecting-ip") || "";
  const { success } = await env.LIMITE.limit({ key: ip });
  if (!success) return texto("Demasiadas publicaciones seguidas: espera un minuto.", 429);

  const largo = Number(request.headers.get("content-length") || "0");
  if (largo > 20000) return texto("Demasiado grande", 413);
  let d;
  try { d = await request.json(); } catch { return texto("Formato no válido", 400); }

  const tipo = d.tipo === "perdida" || d.tipo === "velocidad" || d.tipo === "ruta" ? d.tipo : null;
  // Ruta en vivo: a qué servidor de juego iba (p. ej. "GFN · Florida · Miami 04")
  const destino = tipo === "ruta" ? d.destino : "";
  if (tipo === "ruta" && (typeof destino !== "string" || !NOMBRE.test(destino))) return texto("Formato no válido", 400);
  if (!tipo || !Array.isArray(d.filas) || d.filas.length === 0 || d.filas.length > MAX_FILAS) return texto("Formato no válido", 400);
  const duracion = Math.round(numero(d.duracion, 3600) || 0);

  const filas = [];
  try {
    for (const f of d.filas) {
      if (typeof f.s !== "string" || !NOMBRE.test(f.s)) throw new Error("nombre");
      const zona = f.z === "ve" ? "ve" : "fuera";
      if (tipo === "perdida" || tipo === "ruta")
        filas.push([f.s, zona, numero(f.p, 100), numero(f.m, 5000), numero(f.j, 5000), null, null, null, 0]);
      else
        filas.push([f.s, zona, null, numero(f.m, 5000), null, numero(f.b, 100000), numero(f.u, 100000), numero(f.c, 10000), f.o ? 1 : 0]);
    }
  } catch { return texto("Datos no válidos", 400); }

  const cf = request.cf || {};
  const ahora = Math.floor(Date.now() / 1000);
  // Tope general contra inundaciones (el límite por conexión de Cloudflare es aproximado)
  const recientes = await env.DB.prepare("SELECT COUNT(*) AS n FROM publicaciones WHERE fecha >= ?").bind(ahora - 3600).first();
  if (recientes.n >= 300) return texto("El servidor recibió demasiadas publicaciones en la última hora: inténtalo más tarde.", 429);
  const pub = await env.DB.prepare("INSERT INTO publicaciones (fecha, tipo, proveedor, asn, pais, duracion, destino, region, ciudad) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?) RETURNING id")
    .bind(ahora, tipo, proveedor(cf), cf.asn || 0, (cf.country || "").slice(0, 2), duracion, destino, lugar(cf).region, lugar(cf).ciudad).first();
  const lote = filas.map(f => env.DB.prepare(
    "INSERT INTO filas (pub, servidor, zona, perdida, ms, jitter, bajada, subida, carga, oficial) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)")
    .bind(pub.id, ...f));
  await env.DB.batch(lote.concat(limpiar(env, ahora)));
  return json(Object.assign({ ok: true }, lugar(cf)));
}

// Promedios por servidor y proveedor en las últimas horas
async function resultados(url, env) {
  const pedido = url.searchParams.get("tipo");
  const tipo = pedido === "velocidad" || pedido === "ruta" ? pedido : "perdida";
  const horas = Math.min(DIAS_GUARDADOS * 24, Math.max(1, Number(url.searchParams.get("horas")) || 24));
  const desde = Math.floor(Date.now() / 1000) - horas * 3600;
  const pais = (url.searchParams.get("pais") || "").slice(0, 2).toUpperCase();
  const filtroPais = pais ? " AND p.pais = ?" : "";
  const args = pais ? [tipo, desde, pais] : [tipo, desde];

  const grupos = await env.DB.prepare(
    "SELECT p.destino, f.servidor, f.zona, p.proveedor, p.pais, p.region, p.ciudad, COUNT(*) AS pruebas, AVG(f.perdida) AS perdida, MAX(f.perdida) AS perdida_max, " +
    "AVG(f.ms) AS ms, AVG(f.jitter) AS jitter, AVG(f.bajada) AS bajada, MAX(f.bajada) AS bajada_max, AVG(f.subida) AS subida, " +
    "AVG(f.carga) AS carga, MAX(f.oficial) AS oficial, MAX(p.fecha) AS ultima " +
    "FROM filas f JOIN publicaciones p ON p.id = f.pub WHERE p.tipo = ? AND p.fecha >= ?" + filtroPais +
    " GROUP BY p.destino, f.servidor, p.proveedor, p.pais, p.region, p.ciudad ORDER BY p.destino, f.servidor, pruebas DESC LIMIT 1000").bind(...args).all();
  const total = await env.DB.prepare(
    "SELECT COUNT(*) AS publicaciones, COUNT(DISTINCT p.proveedor) AS proveedores FROM publicaciones p WHERE p.tipo = ? AND p.fecha >= ?" + filtroPais)
    .bind(...args).first();
  if (url.searchParams.get("formato") === "tsv") {
    // Para la app (sin librerías de JSON): una línea de totales y una por grupo, separadas por tabuladores
    const campos = ["servidor", "zona", "proveedor", "pais", "pruebas", "perdida", "perdida_max", "ms", "jitter", "bajada", "bajada_max", "subida", "carga", "oficial", "ultima", "destino", "region", "ciudad"];
    const lineas = ["#\t" + (total.publicaciones || 0) + "\t" + (total.proveedores || 0)];
    for (const g of grupos.results)
      lineas.push(campos.map(c => (g[c] === null || g[c] === undefined ? "" : String(typeof g[c] === "number" ? Math.round(g[c] * 10) / 10 : g[c]).replace(/[\t\r\n]/g, " "))).join("\t"));
    return new Response(lineas.join("\n"), { headers: { "content-type": "text/tab-separated-values; charset=utf-8", "cache-control": "max-age=30" } });
  }
  return json({ tipo, horas, ahora: Math.floor(Date.now() / 1000), total, grupos: grupos.results });
}
