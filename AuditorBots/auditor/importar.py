"""Importa a la base los archivos que deja el colector (o una página guardada con Ctrl+S)."""

import hashlib
import json
import re
import shutil
from datetime import date, datetime, timezone
from pathlib import Path

from .almacen import Almacen
from .enlaces import identificar
from .fechas import leer_anio_union, leer_fecha
from .modelos import Interaccion, Perfil, Publicacion
from .textos import leer_cantidad, limpiar_comentario

FUENTE = "AuditorBots"
TIPOS = ("reacciones", "comentarios", "perfil")


class ArchivoInvalido(Exception):
    pass


def _fecha_captura(datos: dict) -> date:
    try:
        return datetime.fromisoformat(str(datos.get("capturado", "")).replace("Z", "+00:00")).date()
    except ValueError:
        return date.today()


def _texto(valor, limite: int = 5000) -> str:
    return str(valor or "").strip()[:limite]


def _bool(valor) -> bool | None:
    return valor if isinstance(valor, bool) else None


def importar_carpeta(carpeta: Path, almacen: Almacen) -> list[str]:
    """Importa los archivos nuevos de la carpeta y devuelve un mensaje por archivo."""
    mensajes = []
    archivos = sorted(
        p for p in carpeta.iterdir()
        if p.is_file() and p.suffix.lower() in (".json", ".html", ".htm")
        and p.name not in ("perfiles.json", "informe.html", "muestra.html")
    )
    for ruta in archivos:
        contenido = ruta.read_bytes()
        huella = hashlib.sha256(contenido).hexdigest()
        if almacen.ya_importado(huella):
            continue
        try:
            if ruta.suffix.lower() == ".json":
                tipo, resumen = importar_json(contenido, almacen)
            else:
                tipo, resumen = importar_html(contenido, ruta.name, almacen)
        except ArchivoInvalido as e:
            mensajes.append(f"  {ruta.name}: no se importó ({e})")
            continue
        almacen.marcar_importado(huella, ruta.name, tipo)
        mensajes.append(f"  {ruta.name}: {resumen}")
    return mensajes


def importar_json(contenido: bytes, almacen: Almacen) -> tuple[str, str]:
    try:
        datos = json.loads(contenido.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as e:
        raise ArchivoInvalido(f"JSON dañado: {e}") from None
    if not isinstance(datos, dict) or datos.get("fuente") != FUENTE or datos.get("tipo") not in TIPOS:
        raise ArchivoInvalido("no lo generó el colector de AuditorBots")
    if datos["tipo"] == "perfil":
        return "perfil", _importar_perfil(datos, almacen)
    return datos["tipo"], _importar_lista(datos, almacen)


def _importar_lista(datos: dict, almacen: Almacen) -> str:
    publicacion = _texto(datos.get("publicacion"), 1000) or "(publicación sin URL)"
    comentarios = datos["tipo"] == "comentarios"
    total = 0
    for el in datos.get("elementos") or []:
        if not isinstance(el, dict):
            continue
        ident = identificar(el.get("url"))
        if not ident:
            continue
        almacen.guardar_nombre(ident.clave, _texto(el.get("nombre"), 200), ident.url)
        texto, fecha = _texto(el.get("texto")), _texto(el.get("fecha"), 100)
        if comentarios:
            texto, fecha = limpiar_comentario(texto, fecha)
        almacen.guardar_interaccion(Interaccion(
            perfil=ident.clave,
            publicacion=publicacion,
            tipo="comentario" if comentarios else "reaccion",
            reaccion=None if comentarios else (_texto(el.get("reaccion"), 40) or None),
            texto=texto if comentarios else "",
            fecha_texto=fecha,
        ))
        total += 1
    if total == 0:
        raise ArchivoInvalido("no trae perfiles reconocibles")
    resumen = f"{total} {'comentarios' if comentarios else 'reacciones'}"
    if not comentarios and (mostrado := leer_cantidad(_texto(datos.get("total_mostrado"), 200))):
        almacen.guardar_total(publicacion, mostrado)
        resumen += f" de {mostrado:,} que muestra Facebook".replace(",", ".")
    if not identificar_publicacion(publicacion):
        resumen += " (aviso: la URL guardada es la de la página o el perfil, no la de una publicación)"
    almacen.con.commit()
    return resumen


def _nombre_valido(nombre: str) -> str:
    """Descarta el título de la pestaña ("(1) Facebook") que versiones anteriores guardaban como nombre."""
    return "" if re.fullmatch(r"(\(\d+\)\s*)?facebook", nombre, re.IGNORECASE) else nombre


def _ultima_marca(marcas, capturado: date) -> date | None:
    """La marca de tiempo más reciente (segundos Unix) que no sea posterior a la captura."""
    fechas = []
    for m in marcas or []:
        if isinstance(m, (int, float)) and 1_000_000_000 < m < 4_000_000_000:
            f = datetime.fromtimestamp(m, timezone.utc).date()
            if f <= capturado:
                fechas.append(f)
    return max(fechas, default=None)


def identificar_publicacion(url: str) -> bool:
    """Si la URL es de una publicación concreta (y no de la página o perfil que la publicó)."""
    return bool(re.search(r"/posts/|/reel/|/videos?/|/watch|story_fbid|permalink|fbid=|/photos?/|/share/[prv]/", url))


def _importar_perfil(datos: dict, almacen: Almacen) -> str:
    ident = identificar(datos.get("url"))
    id_numero = str(datos.get("id") or "")
    usuario = str(datos.get("usuario") or "").lower()
    if id_numero.isdigit():
        clave = f"id:{id_numero}"
    elif ident:
        clave = ident.clave
    elif usuario:
        clave = f"u:{usuario}"
    else:
        raise ArchivoInvalido("no se reconoce el perfil")
    # Un mismo perfil puede aparecer por su número o por su nombre de usuario
    for alias in {ident.clave if ident else None, f"u:{usuario}" if usuario else None} - {None}:
        almacen.guardar_alias(alias, clave)

    capturado = _fecha_captura(datos)
    publicaciones = []
    vistas = set()
    for p in datos.get("publicaciones") or []:
        if not isinstance(p, dict) or p.get("borrada"):
            continue
        pub = Publicacion(
            texto=_texto(p.get("texto")),
            fecha_texto=_texto(p.get("fecha"), 100),
            enlaces=[_texto(e, 2000) for e in (p.get("enlaces") or []) if isinstance(e, str)][:50],
        )
        pub.fecha = leer_fecha(pub.fecha_texto, capturado)
        llave = (pub.texto, pub.fecha_texto, tuple(pub.enlaces))
        if llave not in vistas:
            vistas.add(llave)
            publicaciones.append(pub)

    url = ident.url if ident else (f"https://www.facebook.com/profile.php?id={id_numero}"
                                   if id_numero.isdigit() else f"https://www.facebook.com/{usuario}")
    perfil = Perfil(
        clave=clave,
        nombre=_nombre_valido(_texto(datos.get("nombre"), 200)),
        url=url,
        id=id_numero if id_numero.isdigit() else None,
        usuario=usuario or None,
        anio_union=leer_anio_union(datos.get("se_unio")),
        tiene_foto=_bool(datos.get("tiene_foto")),
        amigos_visibles=_bool(datos.get("amigos_visibles")),
        sin_publicaciones=bool(datos.get("sin_publicaciones")),
        no_disponible=bool(datos.get("no_disponible")),
        ultima_marca=_ultima_marca(datos.get("marcas_tiempo"), capturado),
        publicaciones=publicaciones,
        capturado=capturado,
    )
    almacen.guardar_perfil(perfil)
    almacen.guardar_nombre(clave, perfil.nombre, perfil.url)
    almacen.con.commit()
    nombre = perfil.nombre or clave
    if perfil.no_disponible:
        return f"perfil de {nombre}: no disponible en Facebook"
    sin_fecha = sum(1 for p in publicaciones if p.fecha is None)
    aviso = f", {sin_fecha} sin fecha legible" if sin_fecha else ""
    if sin_fecha and perfil.ultima_marca:
        aviso += f" (última actividad según los datos de la página: {perfil.ultima_marca})"
    return f"perfil de {nombre} ({len(publicaciones)} publicaciones{aviso})"


def importar_html(contenido: bytes, nombre: str, almacen: Almacen) -> tuple[str, str]:
    """Respaldo: una página guardada con Ctrl+S con la lista de reacciones abierta."""
    from bs4 import BeautifulSoup

    html = contenido.decode("utf-8", errors="replace")
    sopa = BeautifulSoup(html, "html.parser")
    dialogos = sopa.select('[role="dialog"]')
    if not dialogos:
        raise ArchivoInvalido("la página no tiene una lista de reacciones abierta")

    publicacion = None
    if guardada := re.search(r"saved from url=\(\d+\)(\S+)", html[:2000]):
        publicacion = guardada[1]
    elif canonica := sopa.select_one('link[rel="canonical"], meta[property="og:url"]'):
        publicacion = canonica.get("href") or canonica.get("content")

    elementos = {}
    for a in dialogos[-1].select("a[href]"):
        ident = identificar(a["href"])
        if ident:
            texto = a.get_text(" ", strip=True)
            if texto or ident.clave not in elementos:
                elementos[ident.clave] = {"url": ident.url, "nombre": texto}
    datos = {
        "tipo": "reacciones",
        "publicacion": publicacion or f"(página guardada: {nombre})",
        "elementos": list(elementos.values()),
    }
    return "reacciones", _importar_lista(datos, almacen) + " (desde la página guardada)"


def recoger_descargas(carpeta: Path, descargas: Path) -> list[str]:
    """Mueve a la carpeta de la auditoría los archivos del colector que hay en Descargas."""
    movidos = []
    if not descargas.is_dir():
        return movidos
    ya_estan = {hashlib.sha256(p.read_bytes()).hexdigest() for p in carpeta.glob("*.json")}
    for ruta in sorted(descargas.glob("*.json")):
        if not re.match(r"(reacciones|comentarios|perfil)-", ruta.name):
            continue
        try:
            datos = json.loads(ruta.read_text(encoding="utf-8-sig"))
        except (OSError, UnicodeDecodeError, json.JSONDecodeError):
            continue
        if not isinstance(datos, dict) or datos.get("fuente") != FUENTE:
            continue
        if hashlib.sha256(ruta.read_bytes()).hexdigest() in ya_estan:
            continue    # ya está en la carpeta: se deja donde está
        destino = carpeta / ruta.name
        n = 2
        while destino.exists():
            destino = carpeta / f"{ruta.stem} ({n}){ruta.suffix}"
            n += 1
        shutil.move(str(ruta), destino)
        movidos.append(ruta.name)
    return movidos
