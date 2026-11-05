"""Salidas de la auditoría: resumen en consola, perfiles.csv, perfiles.json, informe.html y muestra.html.

Todo lo que viene de Facebook (nombres, textos, enlaces) se trata como no confiable: se escapa al
escribir HTML y se neutraliza al escribir CSV para que Excel no lo ejecute como fórmula.
"""

import csv
import json
from datetime import datetime
from html import escape
from pathlib import Path

from .almacen import Datos
from .analisis import Auditoria, FilaPerfil
from .heuristicas import NIVEL_ALTO, NIVEL_MEDIO

AVISO = ("El puntaje es un indicio, no una prueba. Mucha gente real tiene el perfil cerrado, no publica "
         "o usa plantillas de Facebook. Revisa los motivos de cada perfil antes de sacar conclusiones "
         "y no publiques listas de personas señaladas como bots.")


def _si_no(valor: bool | None) -> str:
    return "" if valor is None else ("sí" if valor else "no")


def _pct(p: float) -> str:
    return f"{p * 100:.1f} %".replace(".", ",")


# --- Consola -----------------------------------------------------------------

def resumen_consola(a: Auditoria) -> str:
    lineas = [
        "",
        "=== Resumen de la auditoría ===",
        f"Publicaciones: {len(a.publicaciones)}  ·  Reacciones: {a.total_reacciones}  ·  "
        f"Comentarios: {a.total_comentarios}  ·  Perfiles distintos: {a.poblacion}",
        f"Muestra: {len(a.muestra)} perfiles, {a.muestra_con_ficha} con ficha exportada",
    ]
    if a.muestra_con_ficha:
        for etiqueta, prop in ((f"Sospechosos (puntaje ≥ {NIVEL_MEDIO})", a.sospechosos),
                               (f"Alto riesgo (puntaje ≥ {NIVEL_ALTO})", a.altos)):
            lineas.append(
                f"{etiqueta}: {prop.casos} de {prop.total} = {_pct(prop.p)} ± {_pct(a.margen(prop))}"
                f"  →  unas {round(prop.p * a.poblacion)} de {a.poblacion} interacciones"
            )
        if a.total_facebook > a.total_reacciones:
            lineas.append(_extrapolacion(a))
        if aviso := _aviso_fechas(a):
            lineas.append("¡OJO! " + aviso)
        if a.no_disponibles.casos:
            lineas.append(f"No disponibles en Facebook: {a.no_disponibles.casos} de {a.no_disponibles.total} "
                          f"(eliminados, suspendidos o que bloquearon tu cuenta)")
        lineas.append("Última actividad visible (muestra con ficha):")
        maximo = max(a.por_anio.values())
        for anio, n in a.por_anio.items():
            lineas.append(f"  {anio:>28}  {'█' * max(1, round(n * 30 / maximo))} {n}")
    else:
        lineas.append("Aún no hay fichas de la muestra: abre muestra.html y exporta esos perfiles.")
    if a.clusteres:
        lineas.append(f"Clústeres de perfiles con textos o enlaces compartidos: {len(a.clusteres)}")
        for c in a.clusteres[:5]:
            ejemplo = (c.textos or c.enlaces or [""])[0]
            lineas.append(f"  {c.id}: {len(c.miembros)} perfiles  ·  «{ejemplo[:70]}»")
    lineas.append("")
    lineas.append(AVISO)
    return "\n".join(lineas)


def _aviso_fechas(a: Auditoria) -> str | None:
    if a.muestra_con_ficha and a.sin_fecha * 2 > a.muestra_con_ficha:
        return (f"Resultado poco fiable: {a.sin_fecha} de {a.muestra_con_ficha} perfiles de la muestra no tienen "
                f"ninguna fecha, así que no se pudo comprobar si están inactivos (la regla principal). Puede ser "
                f"que sean perfiles cerrados o que el colector no haya podido leer las fechas.")
    return None


def _miles(n: int) -> str:
    return f"{n:,}".replace(",", ".")


def _extrapolacion(a: Auditoria) -> str:
    return (f"Facebook muestra {_miles(a.total_facebook)} reacciones y se leyeron {_miles(a.total_reacciones)}. "
            f"Si la proporción se mantuviera en el total, serían unas {_miles(round(a.sospechosos.p * a.total_facebook))} "
            f"sospechosas. Ojo: Facebook no muestra la lista al azar (pone primero a tus amigos y a otros "
            f"perfiles según su propio orden), así que las primeras reacciones pueden no representar al resto.")


# --- CSV y JSON ----------------------------------------------------------------

def _celda(valor) -> str:
    texto = "" if valor is None else str(valor)
    return "'" + texto if texto[:1] in ("=", "+", "-", "@", "\t", "\r") else texto


def _fila_dict(f: FilaPerfil) -> dict:
    r = f.resultado
    return {
        "clave": f.clave,
        "nombre": f.nombre,
        "url": f.url,
        "puntaje": r.puntaje,
        "nivel": r.nivel,
        "banderas": [{"regla": b.regla, "puntos": b.puntos, "motivo": b.motivo} for b in r.banderas],
        "datos": f.datos,
        "en_muestra": f.en_muestra,
        "reacciones": f.reacciones,
        "comentarios": f.n_comentarios,
        "cluster": f.cluster,
        "ultima_publicacion": r.ultima_publicacion.isoformat() if r.ultima_publicacion else None,
        "anio_union": f.anio_union,
        "tiene_foto": f.tiene_foto,
        "amigos_visibles": f.amigos_visibles,
        "publicaciones_leidas": f.n_publicaciones,
    }


def escribir_csv(a: Auditoria, ruta: Path):
    filas = [_fila_dict(f) for f in a.filas]
    columnas = list(filas[0]) if filas else []
    with ruta.open("w", newline="", encoding="utf-8-sig") as archivo:   # con BOM para Excel
        escritor = csv.writer(archivo, delimiter=";")
        escritor.writerow(columnas)
        for fila in filas:
            fila["banderas"] = " | ".join(f"{b['regla']} +{b['puntos']}: {b['motivo']}" for b in fila["banderas"])
            fila["reacciones"] = ", ".join(fila["reacciones"])
            escritor.writerow([_celda(fila[c]) for c in columnas])


def escribir_json(a: Auditoria, ruta: Path):
    salida = {
        "generado": datetime.now().isoformat(timespec="seconds"),
        "aviso": AVISO,
        "publicaciones": a.publicaciones,
        "perfiles": [_fila_dict(f) for f in a.filas],
        "clusteres": [{"id": c.id, "miembros": c.miembros, "textos": c.textos, "enlaces": c.enlaces}
                      for c in a.clusteres],
    }
    ruta.write_text(json.dumps(salida, ensure_ascii=False, indent=2), encoding="utf-8")


# --- HTML ----------------------------------------------------------------------

ESTILO = """
:root{--fondo:#f6f7f9;--panel:#fff;--texto:#1d2330;--suave:#5d6577;--borde:#dde1e8;
--acento:#3a5bd9;--alto:#c62f3c;--medio:#c27a0e;--bajo:#2e8b57;--barra:#3a5bd9}
@media (prefers-color-scheme:dark){:root{--fondo:#14171d;--panel:#1d2129;--texto:#e6e8ee;
--suave:#9aa3b5;--borde:#2f3542;--acento:#8fa6ff;--alto:#ff6b78;--medio:#f0a73a;--bajo:#5cc98a;--barra:#8fa6ff}}
*{box-sizing:border-box}
body{margin:0;background:var(--fondo);color:var(--texto);font:15px/1.5 system-ui,"Segoe UI",sans-serif}
main{max-width:1150px;margin:0 auto;padding:24px 16px 48px}
h1{font-size:1.6rem;margin:0 0 4px}h2{font-size:1.15rem;margin:32px 0 10px}
.suave{color:var(--suave)}
.aviso{border-left:4px solid var(--medio);background:var(--panel);padding:10px 14px;margin:16px 0;border-radius:6px}
.tarjetas{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:12px}
.tarjeta{background:var(--panel);border:1px solid var(--borde);border-radius:10px;padding:12px 14px}
.tarjeta b{display:block;font-size:1.5rem}
.barras div{display:grid;grid-template-columns:200px 1fr 40px;gap:8px;align-items:center;margin:4px 0}
.barras span.b{height:14px;background:var(--barra);border-radius:3px;display:block}
.tabla{overflow-x:auto;background:var(--panel);border:1px solid var(--borde);border-radius:10px}
table{border-collapse:collapse;width:100%;font-size:.9rem}
th,td{padding:7px 10px;border-bottom:1px solid var(--borde);text-align:left;vertical-align:top}
th{cursor:pointer;user-select:none;white-space:nowrap;position:sticky;top:0;background:var(--panel)}
th:hover{color:var(--acento)}
a{color:var(--acento)}
.n-alto{color:var(--alto);font-weight:600}.n-medio{color:var(--medio);font-weight:600}
.n-bajo{color:var(--bajo)}.n-no{color:var(--suave)}
ul.motivos{margin:0;padding-left:16px}
input[type=search]{width:100%;max-width:360px;padding:7px 10px;margin:0 0 10px;border:1px solid var(--borde);
border-radius:6px;background:var(--panel);color:var(--texto)}
.cluster{background:var(--panel);border:1px solid var(--borde);border-radius:10px;padding:10px 14px;margin:8px 0}
.cluster q{display:block;margin:4px 0;color:var(--suave)}
li.hecho{color:var(--suave)}li.hecho a{text-decoration:line-through}
"""

ORDENAR = """
document.querySelectorAll('th').forEach((th, col) => th.addEventListener('click', () => {
  const tbody = th.closest('table').tBodies[0];
  const asc = th.dataset.asc !== '1'; th.dataset.asc = asc ? '1' : '0';
  const valor = tr => { const td = tr.children[col]; const v = td.dataset.v ?? td.textContent.trim();
    const n = parseFloat(v); return isNaN(n) ? v.toLowerCase() : n; };
  [...tbody.rows].sort((a, b) => { const x = valor(a), y = valor(b);
    return (x > y ? 1 : x < y ? -1 : 0) * (asc ? 1 : -1); }).forEach(tr => tbody.appendChild(tr));
}));
const filtro = document.getElementById('filtro');
if (filtro) filtro.addEventListener('input', () => { const t = filtro.value.toLowerCase();
  document.querySelectorAll('#perfiles tbody tr').forEach(tr =>
    tr.hidden = !tr.textContent.toLowerCase().includes(t)); });
"""


def _enlace(url: str, texto: str) -> str:
    if url.startswith(("https://", "http://")):
        return f'<a href="{escape(url)}" target="_blank" rel="noopener noreferrer">{escape(texto)}</a>'
    return escape(texto)


def _pagina(titulo: str, cuerpo: str) -> str:
    return (f'<!doctype html><html lang="es"><head><meta charset="utf-8">'
            f'<meta name="viewport" content="width=device-width,initial-scale=1">'
            f"<title>{escape(titulo)}</title><style>{ESTILO}</style></head>"
            f"<body><main>{cuerpo}</main><script>{ORDENAR}</script></body></html>")


def escribir_html(a: Auditoria, carpeta: Path, ruta: Path):
    p = []
    p.append(f"<h1>Auditoría de interacciones</h1><p class='suave'>{escape(carpeta.name)} · "
             f"generado el {datetime.now():%d/%m/%Y %H:%M}</p>")
    p.append(f"<div class='aviso'>{escape(AVISO)}</div>")

    tarjetas = [
        ("Publicaciones", len(a.publicaciones)),
        ("Reacciones", a.total_reacciones),
        ("Comentarios", a.total_comentarios),
        ("Perfiles distintos", a.poblacion),
        ("Muestra con ficha", f"{a.muestra_con_ficha} / {len(a.muestra)}"),
    ]
    if a.muestra_con_ficha:
        tarjetas += [
            (f"Sospechosos (≥ {NIVEL_MEDIO})", f"{_pct(a.sospechosos.p)} ± {_pct(a.margen(a.sospechosos))}"),
            (f"Alto riesgo (≥ {NIVEL_ALTO})", f"{_pct(a.altos.p)} ± {_pct(a.margen(a.altos))}"),
        ]
        if a.no_disponibles.casos:
            tarjetas.append(("No disponibles", f"{a.no_disponibles.casos} de {a.no_disponibles.total}"))
    if a.total_facebook > a.total_reacciones:
        tarjetas.insert(2, ("Reacciones en Facebook", _miles(a.total_facebook)))
    p.append("<div class='tarjetas'>" + "".join(
        f"<div class='tarjeta'><span class='suave'>{escape(t)}</span><b>{escape(str(v))}</b></div>"
        for t, v in tarjetas) + "</div>")
    if a.muestra_con_ficha:
        p.append(f"<p class='suave'>Los porcentajes salen de la muestra aleatoria con ficha "
                 f"({a.muestra_con_ficha} perfiles) con un margen al 95 %. Aplicados a los {a.poblacion} "
                 f"perfiles, serían unos {round(a.sospechosos.p * a.poblacion)} sospechosos.</p>")
    if a.muestra_con_ficha and a.total_facebook > a.total_reacciones:
        p.append(f"<p class='aviso'>{escape(_extrapolacion(a))}</p>")
    if aviso := _aviso_fechas(a):
        p.append(f"<p class='aviso' style='border-left-color:var(--alto)'><b>{escape(aviso)}</b></p>")

    if a.publicaciones:
        p.append("<h2>Publicaciones auditadas</h2><ul>" + "".join(
            f"<li>{_enlace(u, u)}</li>" for u in a.publicaciones) + "</ul>")

    if a.por_anio:
        maximo = max(a.por_anio.values())
        p.append("<h2>Última actividad visible (muestra con ficha)</h2><div class='barras'>" + "".join(
            f"<div><span>{escape(anio)}</span><span class='b' style='width:{n * 100 / maximo:.0f}%'></span>"
            f"<span>{n}</span></div>" for anio, n in a.por_anio.items()) + "</div>")

    if a.tipos_reaccion and set(a.tipos_reaccion) != {"sin tipo"}:
        maximo = max(a.tipos_reaccion.values())
        p.append("<h2>Tipos de reacción</h2><div class='barras'>" + "".join(
            f"<div><span>{escape(t)}</span><span class='b' style='width:{n * 100 / maximo:.0f}%'></span>"
            f"<span>{n}</span></div>" for t, n in a.tipos_reaccion.most_common()) + "</div>")

    nombres = {f.clave: f for f in a.filas}
    p.append(f"<h2>Clústeres ({len(a.clusteres)})</h2>")
    if not a.clusteres:
        p.append("<p class='suave'>Ningún grupo de perfiles comparte textos o enlaces.</p>")
    for c in a.clusteres:
        miembros = ", ".join(_enlace(nombres[m].url, nombres[m].nombre or m) if m in nombres else escape(m)
                             for m in c.miembros)
        ejemplos = "".join(f"<q>{escape(t[:300])}</q>" for t in c.textos[:3])
        ejemplos += "".join(f"<q>{_enlace(e, e)}</q>" for e in c.enlaces[:3])
        p.append(f"<div class='cluster'><b>{c.id}</b> · {len(c.miembros)} perfiles<br>{miembros}{ejemplos}</div>")

    p.append("<h2>Perfiles</h2><input type='search' id='filtro' placeholder='Filtrar por nombre, motivo…'>")
    filas = []
    for f in a.filas:
        r = f.resultado
        clase = {"alto": "n-alto", "medio": "n-medio", "bajo": "n-bajo", "no disponible": "n-medio"}.get(r.nivel, "n-no")
        motivos = "<ul class='motivos'>" + "".join(
            f"<li>+{b.puntos} {escape(b.motivo)}</li>" for b in r.banderas) + "</ul>" if r.banderas else ""
        filas.append(
            f"<tr><td data-v='{r.puntaje if r.puntaje is not None else -1}' class='{clase}'>"
            f"{r.puntaje if r.puntaje is not None else '–'}</td>"
            f"<td class='{clase}'>{escape(r.nivel)}</td>"
            f"<td>{_enlace(f.url, f.nombre or f.clave)}</td>"
            f"<td>{motivos}</td>"
            f"<td>{r.ultima_publicacion.isoformat() if r.ultima_publicacion else ''}</td>"
            f"<td>{f.anio_union or ''}</td>"
            f"<td>{escape(f.datos)}{' · muestra' if f.en_muestra else ''}</td>"
            f"<td>{escape(', '.join(sorted(set(f.reacciones))))}</td>"
            f"<td>{f.cluster or ''}</td></tr>")
    p.append("<div class='tabla'><table id='perfiles'><thead><tr><th>Puntaje</th><th>Nivel</th><th>Perfil</th>"
             "<th>Motivos</th><th>Última publicación</th><th>Se unió</th><th>Datos</th><th>Reacción</th>"
             "<th>Clúster</th></tr></thead><tbody>" + "".join(filas) + "</tbody></table></div>")
    ruta.write_text(_pagina("Auditoría de interacciones", "".join(p)), encoding="utf-8")


COPIAR = """<script>
const boton = document.getElementById('copiar');
if (boton) boton.addEventListener('click', async () => {
  const caja = document.getElementById('enlaces');
  try { await navigator.clipboard.writeText(caja.value); }
  catch (e) { caja.select(); document.execCommand('copy'); }
  document.getElementById('copiado').textContent = 'Copiados';
});
</script>"""


def escribir_muestra(datos: Datos, ruta: Path):
    pendientes = [c for c in datos.muestra if c not in datos.perfiles]
    urls_pendientes = [datos.nombres.get(c, ("", ""))[1] for c in pendientes]
    urls_pendientes = [u for u in urls_pendientes if u.startswith("https://")]
    items = []
    for n, clave in enumerate(datos.muestra, 1):
        nombre, url = datos.nombres.get(clave, ("", ""))
        hecho = clave in datos.perfiles
        if not url and hecho:
            url = datos.perfiles[clave].url
        items.append(f"<li class='{'hecho' if hecho else ''}'>{_enlace(url, nombre or clave)}"
                     f"{' ✓' if hecho else ''}</li>")
    cuerpo = (
        f"<h1>Muestra de perfiles</h1>"
        f"<p>Faltan <b>{len(pendientes)}</b> de {len(datos.muestra)}. Abre cada enlace con tu sesión de "
        f"Facebook, desplázate un poco por sus publicaciones, abre el colector en modo <b>Perfil</b>, "
        f"revisa los datos y pulsa <b>Descargar</b>. Después vuelve a ejecutar <code>auditar.bat</code>.</p>"
        f"<p class='suave'>La muestra es aleatoria: no la cambies eligiendo perfiles a mano, o el "
        f"porcentaje de sospechosos dejará de ser representativo.</p>"
        + (f"<h2>Hacerlo automático</h2><p>Copia estos enlaces, abre Facebook, pulsa la lupa de AuditorBots, "
           f"elige <b>Lote</b>, pégalos y pulsa <b>Empezar</b>.</p>"
           f"<textarea id='enlaces' readonly rows='6' style='width:100%;font:13px monospace'>"
           f"{escape(chr(10).join(urls_pendientes))}</textarea>"
           f"<p><button id='copiar' type='button'>Copiar enlaces</button> <span id='copiado' class='suave'></span></p>"
           if urls_pendientes else "")
        + f"<h2>Perfiles</h2><ol>{''.join(items)}</ol>"
        + COPIAR
    )
    ruta.write_text(_pagina("Muestra de perfiles", cuerpo), encoding="utf-8")
