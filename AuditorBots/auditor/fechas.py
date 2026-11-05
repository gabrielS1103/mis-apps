"""Lectura de las fechas que muestra Facebook, en español y en inglés.

Cuando la fecha es imprecisa ("3 a", "marzo de 2021", "2021") se devuelve la fecha más
reciente posible. Así la regla de inactividad nunca exagera la antigüedad de un perfil.
"""

import calendar
import re
import unicodedata
from datetime import date, timedelta

MESES = {
    "enero": 1, "ene": 1, "january": 1, "jan": 1,
    "febrero": 2, "feb": 2, "february": 2,
    "marzo": 3, "mar": 3, "march": 3,
    "abril": 4, "abr": 4, "april": 4, "apr": 4,
    "mayo": 5, "may": 5,
    "junio": 6, "jun": 6, "june": 6,
    "julio": 7, "jul": 7, "july": 7,
    "agosto": 8, "ago": 8, "august": 8, "aug": 8,
    "septiembre": 9, "setiembre": 9, "sep": 9, "sept": 9, "set": 9, "september": 9,
    "octubre": 10, "oct": 10, "october": 10,
    "noviembre": 11, "nov": 11, "november": 11,
    "diciembre": 12, "dic": 12, "december": 12, "dec": 12,
}

# Unidad relativa -> "anios", "meses" o días equivalentes
UNIDADES = {
    **dict.fromkeys(["a", "ano", "anos", "y", "yr", "yrs", "year", "years"], "anios"),
    **dict.fromkeys(["mes", "meses", "mo", "mos", "month", "months"], "meses"),
    **dict.fromkeys(["sem", "semana", "semanas", "w", "wk", "wks", "week", "weeks"], 7),
    **dict.fromkeys(["d", "dia", "dias", "day", "days"], 1),
    **dict.fromkeys(["h", "hr", "hrs", "hora", "horas", "hour", "hours",
                     "m", "min", "mins", "minuto", "minutos", "minute", "minutes",
                     "s", "seg", "segundo", "segundos", "second", "seconds"], 0),
}

RE_RELATIVA = re.compile(r"^(?:hace\s+)?(\d+)\s*([a-z]+)\.?(?:\s+ago)?$")
RE_HORA = re.compile(r"\b\d{1,2}:\d{2}(?:\s*[ap]\.?\s*m\.?)?")
RE_ANIO = re.compile(r"\b(19[9]\d|20\d{2})\b")


def normalizar(texto: str) -> str:
    sin_acentos = unicodedata.normalize("NFKD", texto).encode("ascii", "ignore").decode()
    return re.sub(r"\s+", " ", sin_acentos.lower()).strip()


def _restar_anios(base: date, anios: int) -> date:
    anio = base.year - anios
    dia = min(base.day, calendar.monthrange(anio, base.month)[1])
    return base.replace(year=anio, day=dia)


def _restar_meses(base: date, meses: int) -> date:
    total = base.year * 12 + (base.month - 1) - meses
    anio, mes = divmod(total, 12)
    mes += 1
    return date(anio, mes, min(base.day, calendar.monthrange(anio, mes)[1]))


def leer_fecha(texto: str | None, base: date) -> date | None:
    """Convierte el texto de una fecha de Facebook en una fecha. `base` es el día de la captura."""
    if not texto:
        return None
    t = normalizar(texto)
    if re.match(r"(hoy|today|ahora|justo ahora|just now)\b", t):
        return base
    if re.match(r"(ayer|yesterday)\b", t):          # también "Ayer a las 10:15"
        return base - timedelta(days=1)

    relativa = RE_RELATIVA.match(t)
    if relativa and relativa.group(2) in UNIDADES:
        n = int(relativa.group(1))
        unidad = UNIDADES[relativa.group(2)]
        if unidad == "anios":
            return _restar_anios(base, n)
        if unidad == "meses":
            return _restar_meses(base, n)
        return base - timedelta(days=n * unidad)

    t = RE_HORA.sub(" ", t)
    if iso := re.search(r"\b(\d{4})-(\d{2})-(\d{2})\b", t):
        return _fecha_valida(int(iso[1]), int(iso[2]), int(iso[3]))
    if numerica := re.search(r"\b(\d{1,2})/(\d{1,2})/(\d{4})\b", t):
        a, b, anio = int(numerica[1]), int(numerica[2]), int(numerica[3])
        dia, mes = (b, a) if b > 12 else (a, b)   # día/mes salvo que no pueda serlo
        return _fecha_valida(anio, mes, dia)

    fichas = re.findall(r"[a-z]+|\d+", t)
    mes = next((MESES[f] for f in fichas if f in MESES), None)
    anio_m = RE_ANIO.search(t)
    anio = int(anio_m[1]) if anio_m else None
    dias = [int(f) for f in fichas if f.isdigit() and len(f) <= 2 and 1 <= int(f) <= 31]

    if mes is None:
        if anio and re.fullmatch(r"(?:en\s+|in\s+)?\d{4}", t):
            return min(date(anio, 12, 31), base)
        return None
    if anio is None:
        # Facebook omite el año en las fechas del año en curso
        anio = base.year
        fecha = _fecha_valida(anio, mes, dias[0] if dias else calendar.monthrange(anio, mes)[1])
        if fecha and fecha > base:
            fecha = _fecha_valida(anio - 1, mes, dias[0] if dias else calendar.monthrange(anio - 1, mes)[1])
        return fecha
    dia = dias[0] if dias else calendar.monthrange(anio, mes)[1]
    return _fecha_valida(anio, mes, dia)


def _fecha_valida(anio: int, mes: int, dia: int) -> date | None:
    try:
        return date(anio, mes, dia)
    except ValueError:
        return None


def leer_anio_union(texto: str | None) -> int | None:
    """Año de "Se unió en marzo de 2019", "Joined March 2019", "Miembro desde 2019"..."""
    if not texto:
        return None
    t = normalizar(texto)
    if not re.search(r"unio|joined|miembro desde|member since", t):
        return None
    anio = RE_ANIO.search(t)
    return int(anio[1]) if anio else None
