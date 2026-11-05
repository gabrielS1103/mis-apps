"""Reconoce enlaces a perfiles de Facebook y normaliza enlaces externos.

La misma lógica está en colector/colector.js (función `identificar`); si se cambia aquí,
hay que cambiarla también allí.
"""

import re
from dataclasses import dataclass
from urllib.parse import parse_qs, urlencode, urlsplit, urlunsplit

# Primeras partes de la ruta que no son perfiles
RESERVADAS = {
    "ads", "bookmarks", "business", "commerce", "dialog", "donate", "events", "friends",
    "fundraisers", "gaming", "groups", "hashtag", "help", "home.php", "l.php", "live", "login",
    "marketplace", "me", "media", "memories", "messages", "notifications", "pages", "permalink.php",
    "photo", "photo.php", "photos", "plugins", "policies", "posts", "privacy", "profile", "reel",
    "reels", "saved", "search", "settings", "share", "sharer", "sharer.php", "stories", "story.php",
    "ufi", "videos", "watch",
}

RE_USUARIO = re.compile(r"[A-Za-z0-9.\-]{2,80}")


@dataclass(frozen=True)
class Identidad:
    clave: str            # "id:<número>" o "u:<usuario>"
    url: str              # URL canónica del perfil
    id: str | None = None
    usuario: str | None = None


def es_facebook(host: str) -> bool:
    host = host.lower().split(":")[0]
    return host in ("facebook.com", "fb.com") or host.endswith(".facebook.com")


def identificar(url: str | None) -> Identidad | None:
    """Devuelve la identidad del perfil al que apunta `url`, o None si no es un perfil."""
    if not url:
        return None
    try:
        partes_url = urlsplit(url.strip())
    except ValueError:
        return None
    if not es_facebook(partes_url.netloc):
        return None
    partes = [p for p in partes_url.path.split("/") if p]
    consulta = parse_qs(partes_url.query)

    numero = None
    if partes[:1] == ["profile.php"] and consulta.get("id", [""])[0].isdigit():
        numero = consulta["id"][0]
    elif len(partes) >= 3 and partes[0] == "people" and partes[2].isdigit():
        numero = partes[2]
    elif len(partes) >= 4 and partes[0] == "groups" and partes[2] == "user" and partes[3].isdigit():
        numero = partes[3]
    if numero:
        return Identidad(f"id:{numero}", f"https://www.facebook.com/profile.php?id={numero}", id=numero)

    if len(partes) == 1 and partes[0].lower() not in RESERVADAS and RE_USUARIO.fullmatch(partes[0]):
        usuario = partes[0].lower()
        return Identidad(f"u:{usuario}", f"https://www.facebook.com/{usuario}", usuario=usuario)
    return None


def normalizar_enlace(url: str) -> str | None:
    """Forma comparable de un enlace externo o de recaudación, o None si no sirve para comparar.

    Deshace la redirección l.facebook.com/l.php?u=..., quita parámetros de rastreo y descarta los
    enlaces internos de Facebook salvo los de donaciones y recaudaciones.
    """
    try:
        partes = urlsplit(url.strip())
        if es_facebook(partes.netloc) and partes.path.startswith("/l.php"):
            destino = parse_qs(partes.query).get("u", [""])[0]
            if not destino:
                return None
            partes = urlsplit(destino)
    except ValueError:
        return None
    if partes.scheme not in ("http", "https") or not partes.netloc:
        return None
    host = partes.netloc.lower().removeprefix("www.").removeprefix("m.")
    ruta = partes.path.rstrip("/")
    if es_facebook(host) and not re.match(r"/(donate|fundraisers)/", ruta + "/"):
        return None
    if len(ruta) <= 1:
        return None    # la portada de un sitio no identifica nada
    consulta = [
        (k, v) for k, v in parse_qs(partes.query).items()
        if not (k.startswith("utm_") or k in ("fbclid", "ref", "__cft__", "__tn__", "mibextid", "sfnsn"))
    ]
    return urlunsplit(("https", host, ruta, urlencode(sorted(consulta), doseq=True), ""))
