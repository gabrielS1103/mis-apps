"""Une los datos importados, los clústeres y el BotAuditor en un solo resultado."""

import math
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from datetime import date

from .almacen import Datos
from .clusteres import Cluster, agrupar
from .heuristicas import NIVEL_ALTO, NIVEL_MEDIO, BotAuditor, Resultado


@dataclass
class FilaPerfil:
    clave: str
    nombre: str
    url: str
    datos: str                    # qué se tiene de él: "ficha", "comentarios", "solo reacción"...
    en_muestra: bool
    reacciones: list[str]
    n_comentarios: int
    resultado: Resultado
    cluster: str | None
    anio_union: int | None = None
    tiene_foto: bool | None = None
    amigos_visibles: bool | None = None
    n_publicaciones: int = 0
    no_disponible: bool = False


@dataclass
class Proporcion:
    casos: int
    total: int

    @property
    def p(self) -> float:
        return self.casos / self.total if self.total else 0.0


@dataclass
class Auditoria:
    filas: list[FilaPerfil]
    clusteres: list[Cluster]
    publicaciones: list[str]
    total_reacciones: int
    total_comentarios: int
    poblacion: int                         # perfiles distintos que interactuaron
    muestra: list[str]
    muestra_con_ficha: int
    sospechosos: Proporcion                # puntaje >= NIVEL_MEDIO en la muestra con ficha
    altos: Proporcion                      # puntaje >= NIVEL_ALTO en la muestra con ficha
    no_disponibles: Proporcion             # perfiles de la muestra que Facebook ya no muestra
    sin_fecha: int = 0                     # perfiles de la muestra con ficha pero sin ninguna fecha
    total_facebook: int = 0                # reacciones que muestra Facebook en las publicaciones con total conocido
    por_anio: dict[str, int] = field(default_factory=dict)
    tipos_reaccion: Counter = field(default_factory=Counter)

    def margen(self, prop: Proporcion) -> float:
        """Margen de error al 95 % de una proporción de la muestra, con corrección por población finita."""
        n, N = prop.total, self.poblacion
        if n == 0:
            return 0.0
        error = 1.96 * math.sqrt(prop.p * (1 - prop.p) / n)
        if N > 1 and n < N:
            error *= math.sqrt((N - n) / (N - 1))
        elif n >= N:
            error = 0.0
        return error


def analizar(datos: Datos, hoy: date | None = None) -> Auditoria:
    reacciones: dict[str, list[str]] = defaultdict(list)
    comentarios: dict[str, list[str]] = defaultdict(list)
    publicaciones = []
    tipos = Counter()
    for i in datos.interacciones:
        if i.publicacion not in publicaciones:
            publicaciones.append(i.publicacion)
        if i.tipo == "reaccion":
            reacciones[i.perfil].append(i.reaccion or "")
            tipos[i.reaccion or "sin tipo"] += 1
        else:
            comentarios[i.perfil].append(i.texto)

    # Los textos y enlaces de cada perfil alimentan los clústeres
    textos = {c: list(t) for c, t in comentarios.items()}
    enlaces: dict[str, list[str]] = {}
    for clave, perfil in datos.perfiles.items():
        textos.setdefault(clave, []).extend(p.texto for p in perfil.publicaciones)
        enlaces[clave] = [e for p in perfil.publicaciones for e in p.enlaces]
    clusteres = agrupar(textos, enlaces)
    cluster_de = {m: c for c in clusteres for m in c.miembros}

    auditor = BotAuditor(hoy)
    muestra = set(datos.muestra)
    claves = set(reacciones) | set(comentarios) | set(datos.perfiles)
    filas = []
    for clave in claves:
        perfil = datos.perfiles.get(clave)
        nombre, url = datos.nombres.get(clave, ("", ""))
        nombre = nombre or (perfil.nombre if perfil else "")
        url = url or (perfil.url if perfil else "")
        resultado = auditor.evaluar(clave, nombre, perfil, comentarios.get(clave, []), cluster_de.get(clave))
        partes = (["ficha"] if perfil else []) + (["comentarios"] if clave in comentarios else [])
        filas.append(FilaPerfil(
            clave=clave, nombre=nombre, url=url,
            datos=" + ".join(partes) or "solo reacción",
            en_muestra=clave in muestra,
            reacciones=[r for r in reacciones.get(clave, []) if r],
            n_comentarios=len(comentarios.get(clave, [])),
            resultado=resultado,
            cluster=cluster_de[clave].id if clave in cluster_de else None,
            anio_union=perfil.anio_union if perfil else None,
            tiene_foto=perfil.tiene_foto if perfil else None,
            amigos_visibles=perfil.amigos_visibles if perfil else None,
            n_publicaciones=len(perfil.publicaciones) if perfil else 0,
            no_disponible=perfil.no_disponible if perfil else False,
        ))
    filas.sort(key=lambda f: (-(f.resultado.puntaje if f.resultado.puntaje is not None else -1), f.nombre))

    # Las proporciones solo usan la muestra aleatoria con ficha: es la parte sin sesgo de selección
    evaluadas = [f for f in filas if f.en_muestra and "ficha" in f.datos]
    por_anio = Counter()
    for f in evaluadas:
        ultima = f.resultado.ultima_publicacion
        por_anio[str(ultima.year) if ultima else "sin publicaciones con fecha"] += 1

    poblacion = len(set(reacciones) | set(comentarios))
    return Auditoria(
        filas=filas,
        clusteres=clusteres,
        publicaciones=publicaciones,
        total_reacciones=sum(len(v) for v in reacciones.values()),
        total_comentarios=sum(len(v) for v in comentarios.values()),
        poblacion=poblacion,
        muestra=datos.muestra,
        muestra_con_ficha=len(evaluadas),
        sospechosos=Proporcion(sum(f.resultado.puntaje >= NIVEL_MEDIO for f in evaluadas), len(evaluadas)),
        altos=Proporcion(sum(f.resultado.puntaje >= NIVEL_ALTO for f in evaluadas), len(evaluadas)),
        no_disponibles=Proporcion(sum(f.no_disponible for f in evaluadas), len(evaluadas)),
        por_anio=dict(sorted(por_anio.items())),
        tipos_reaccion=tipos,
        sin_fecha=sum(1 for f in evaluadas if f.resultado.ultima_publicacion is None and not f.no_disponible),
        total_facebook=sum(t for url, t in datos.totales.items() if url in publicaciones),
    )
