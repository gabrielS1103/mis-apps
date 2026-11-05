"""Estructuras de datos que comparten el importador, el análisis y el informe."""

from dataclasses import asdict, dataclass, field
from datetime import date


@dataclass
class Publicacion:
    texto: str = ""
    fecha_texto: str = ""
    fecha: date | None = None
    enlaces: list[str] = field(default_factory=list)


@dataclass
class Perfil:
    clave: str
    nombre: str = ""
    url: str = ""
    id: str | None = None
    usuario: str | None = None
    anio_union: int | None = None
    tiene_foto: bool | None = None       # None: no se pudo saber
    amigos_visibles: bool | None = None
    sin_publicaciones: bool = False      # el usuario marcó que no hay publicaciones visibles
    no_disponible: bool = False          # Facebook dice que el perfil no está disponible
    ultima_marca: date | None = None     # fecha más reciente de los datos internos de la página
    publicaciones: list[Publicacion] = field(default_factory=list)
    capturado: date | None = None


@dataclass
class Interaccion:
    perfil: str              # clave del perfil
    publicacion: str         # URL de la publicación auditada
    tipo: str                # "reaccion" o "comentario"
    reaccion: str | None = None
    texto: str = ""
    fecha_texto: str = ""


def perfil_a_dict(perfil: Perfil) -> dict:
    datos = asdict(perfil)
    datos["capturado"] = perfil.capturado.isoformat() if perfil.capturado else None
    datos["ultima_marca"] = perfil.ultima_marca.isoformat() if perfil.ultima_marca else None
    for pub, original in zip(datos["publicaciones"], perfil.publicaciones):
        pub["fecha"] = original.fecha.isoformat() if original.fecha else None
    return datos


def perfil_desde_dict(datos: dict) -> Perfil:
    datos = dict(datos)
    datos["capturado"] = date.fromisoformat(datos["capturado"]) if datos.get("capturado") else None
    datos["ultima_marca"] = date.fromisoformat(datos["ultima_marca"]) if datos.get("ultima_marca") else None
    datos["publicaciones"] = [
        Publicacion(
            texto=p.get("texto", ""),
            fecha_texto=p.get("fecha_texto", ""),
            fecha=date.fromisoformat(p["fecha"]) if p.get("fecha") else None,
            enlaces=list(p.get("enlaces", [])),
        )
        for p in datos.get("publicaciones", [])
    ]
    return Perfil(**datos)
