"""Normalización de textos para compararlos entre sí."""

import re
import unicodedata

RE_URL = re.compile(r"https?://\S+|www\.\S+", re.IGNORECASE)

# Comentarios cortos de relleno, típicos de cuentas automatizadas (ya normalizados)
GENERICOS = {
    "amen", "amen amen", "bendiciones", "dios te bendiga", "dios los bendiga", "excelente",
    "hermoso", "hermosa", "que bonito", "que lindo", "que hermoso", "felicidades", "felicitaciones",
    "genial", "muy bien", "gracias", "saludos", "me encanta", "wow", "super", "bravo", "exito",
    "exitos", "bello", "lindo", "increible", "top", "nice", "great", "beautiful", "amazing", "good",
    "so beautiful", "congratulations", "congrats", "love it", "awesome", "cool", "perfect", "yes",
    "hola", "hello", "hi", "ok", "si",
}


def normalizar_texto(texto: str) -> str:
    """Minúsculas, sin acentos, sin enlaces ni signos, y con los espacios unificados."""
    t = RE_URL.sub(" ", texto or "")
    t = unicodedata.normalize("NFKD", t).encode("ascii", "ignore").decode().lower()
    t = re.sub(r"[^a-z0-9]+", " ", t)
    return re.sub(r"\s+", " ", t).strip()


# Fecha relativa que Facebook pone al final de un comentario ("11 sem", "3 d", "2 a", "5h", "Ayer")
RE_FECHA_FINAL = re.compile(
    r"(?:^|\s+)((?:hace\s+)?\d+\s*(?:a|años?|y|sem|semanas?|w|d|días?|h|min|m|mes|meses)|ayer|yesterday)\s*$",
    re.IGNORECASE)
RE_VER_MAS_FINAL = re.compile(r"\s*(?:\.\.\.|…)?\s*(?:Ver más|See more)\s*$", re.IGNORECASE)


def limpiar_comentario(texto: str, fecha: str) -> tuple[str, str]:
    """Separa la fecha que quedó pegada al final del texto y quita el "… Ver más" de los textos cortados.

    Las versiones anteriores del colector guardaban así los comentarios.
    """
    texto = (texto or "").strip()
    if not fecha and (m := RE_FECHA_FINAL.search(texto)):
        fecha, texto = m.group(1), texto[: m.start()].rstrip()
    if RE_VER_MAS_FINAL.search(texto):
        texto = RE_VER_MAS_FINAL.sub("…", texto)
    return texto, fecha


def leer_cantidad(texto: str | None) -> int | None:
    """Número de una cifra de Facebook: "178 mil", "1,2 mill.", "178K", "12.345", "Todas 3,4 M"."""
    if not texto:
        return None
    t = texto.lower().replace(" ", " ")
    m = re.search(r"(\d+(?:[.,]\d+)*)\s*(mil\b|mill|millones|millón|m\b|k\b)?", t)
    if not m:
        return None
    cifra, sufijo = m.group(1), m.group(2)
    if sufijo:
        # Con sufijo, la coma o el punto es decimal: "1,2 mil" = 1200
        valor = float(cifra.replace(",", "."))
        return round(valor * (1000 if sufijo in ("mil", "k") else 1_000_000))
    return int(re.sub(r"[.,]", "", cifra))      # sin sufijo, son separadores de miles


def es_generico(texto: str) -> bool:
    """Solo emojis o signos, o una fórmula corta de relleno ("Amén", "Bendiciones"...)."""
    t = normalizar_texto(texto)
    if not t:
        return True
    t = re.sub(r"([a-z])\1{2,}", r"\1", t)     # "amennnn" -> "amen"
    return t in GENERICOS
