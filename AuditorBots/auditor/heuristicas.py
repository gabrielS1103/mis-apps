"""BotAuditor: puntaje de 0 a 100 de que un perfil sea automatizado o zombi.

El puntaje es un indicio, no una prueba: cada regla deja su motivo para revisarlo a mano.
"""

import re
from dataclasses import dataclass, field
from datetime import date
from difflib import SequenceMatcher
from itertools import combinations

from .clusteres import Cluster
from .enlaces import normalizar_enlace
from .modelos import Perfil
from .textos import RE_URL, es_generico, normalizar_texto

PESOS = {
    "zombi": 35,                 # última publicación hace 3 años o más
    "plantilla": 30,             # la misma plantilla de recaudación repetida con causas distintas
    "texto_compartido": 20,      # comparte textos o enlaces con otros perfiles de la auditoría
    "identidad": 5,              # por cada anomalía de identidad...
    "identidad_maximo": 15,      # ...con este tope
    "comentario_generico": 10,   # todos sus comentarios son relleno o solo emojis
}
ANIOS_ZOMBI = 3
SIMILITUD_PLANTILLA = 0.9
DIAS_PLANTILLA = 90              # "periodo corto" entre dos copias de la plantilla
NIVEL_MEDIO = 30
NIVEL_ALTO = 60

PALABRAS_RECAUDACION = re.compile(
    r"cumpleanos|birthday|gofundme|go fund me|recaudacion|recaudando|recaudar|donacion|donaciones|"
    r"donar|donate|donation|fundraiser|fundraising|vaki|kickstarter|paypal me|colecta",
)
RE_NOMBRE_NUMERICO = re.compile(r"\d{2,}\s*$")


@dataclass
class Bandera:
    regla: str
    puntos: int
    motivo: str


@dataclass
class Resultado:
    clave: str
    puntaje: int | None                 # None: no evaluado
    nivel: str
    banderas: list[Bandera] = field(default_factory=list)
    ultima_publicacion: date | None = None


def nivel_de(puntaje: int | None) -> str:
    if puntaje is None:
        return "no evaluado"
    if puntaje >= NIVEL_ALTO:
        return "alto"
    if puntaje >= NIVEL_MEDIO:
        return "medio"
    return "bajo"


class BotAuditor:
    def __init__(self, hoy: date | None = None, pesos: dict | None = None):
        self.hoy = hoy or date.today()
        self.pesos = {**PESOS, **(pesos or {})}

    def evaluar(self, clave: str, nombre: str, perfil: Perfil | None,
                comentarios: list[str], cluster: Cluster | None) -> Resultado:
        """Evalúa un perfil. Sin ficha de perfil ni comentarios queda como "no evaluado"."""
        ultima = self._ultima_publicacion(perfil)
        if perfil is None and not comentarios:
            return Resultado(clave, None, nivel_de(None), ultima_publicacion=ultima)
        if perfil and perfil.no_disponible:
            # Eliminado, suspendido o te bloqueó: no se puede revisar. Se cuenta aparte en el informe.
            return Resultado(clave, 0, "no disponible",
                             [Bandera("no_disponible", 0, "Facebook dice que el perfil no está disponible")])

        banderas = []
        if perfil:
            banderas += self._zombi(perfil, ultima)
            banderas += self._plantilla(perfil)
        if cluster:
            otros = len(cluster.miembros) - 1
            banderas.append(Bandera(
                "texto_compartido", self.pesos["texto_compartido"],
                f"comparte textos o enlaces con {otros} perfil{'es' if otros != 1 else ''} ({cluster.id})",
            ))
        banderas += self._identidad(nombre or (perfil.nombre if perfil else ""), perfil)
        if comentarios and all(es_generico(c) for c in comentarios):
            banderas.append(Bandera(
                "comentario_generico", self.pesos["comentario_generico"],
                f"comentarios solo de relleno: «{comentarios[0][:40]}»",
            ))

        puntaje = min(100, sum(b.puntos for b in banderas))
        return Resultado(clave, puntaje, nivel_de(puntaje), banderas, ultima)

    # --- Reglas --------------------------------------------------------------

    @staticmethod
    def _ultima_publicacion(perfil: Perfil | None) -> date | None:
        if not perfil:
            return None
        fechas = [p.fecha for p in perfil.publicaciones if p.fecha]
        # Si no se pudo leer ninguna fecha visible, se usa la de los datos internos de la página
        return max(fechas, default=None) or perfil.ultima_marca

    def _zombi(self, perfil: Perfil, ultima: date | None) -> list[Bandera]:
        if ultima is None:
            return []
        referencia = perfil.capturado or self.hoy
        anios = referencia.year - ultima.year - ((referencia.month, referencia.day) < (ultima.month, ultima.day))
        if anios < ANIOS_ZOMBI:
            return []
        return [Bandera("zombi", self.pesos["zombi"],
                        f"última publicación visible: {ultima.isoformat()} (hace {anios} años)")]

    def _plantilla(self, perfil: Perfil) -> list[Bandera]:
        candidatas = [p for p in perfil.publicaciones
                      if PALABRAS_RECAUDACION.search(normalizar_texto(p.texto + " " + " ".join(p.enlaces)))]
        for a, b in combinations(candidatas, 2):
            ta, tb = RE_URL.sub("", a.texto).strip(), RE_URL.sub("", b.texto).strip()
            if not ta or not tb:
                continue
            if SequenceMatcher(None, normalizar_texto(ta), normalizar_texto(tb)).ratio() < SIMILITUD_PLANTILLA:
                continue
            enlaces_a = {normalizar_enlace(e) for e in a.enlaces} - {None}
            enlaces_b = {normalizar_enlace(e) for e in b.enlaces} - {None}
            if a.texto == b.texto and enlaces_a == enlaces_b:
                continue    # la misma publicación compartida dos veces
            if a.fecha and b.fecha and abs((a.fecha - b.fecha).days) > DIAS_PLANTILLA:
                continue
            cuando = (f" entre {min(a.fecha, b.fecha)} y {max(a.fecha, b.fecha)}"
                      if a.fecha and b.fecha else "")
            return [Bandera("plantilla", self.pesos["plantilla"],
                            f"plantilla de recaudación repetida con causas o enlaces distintos{cuando}: "
                            f"«{ta[:60]}»")]
        return []

    def _identidad(self, nombre: str, perfil: Perfil | None) -> list[Bandera]:
        motivos = []
        if RE_NOMBRE_NUMERICO.search(nombre):
            motivos.append(f"el nombre termina en números ({nombre})")
        if perfil and perfil.tiene_foto is False:
            motivos.append("sin foto de perfil propia")
        if perfil and perfil.amigos_visibles is False:
            motivos.append("sin amigos visibles")
        if not motivos:
            return []
        puntos = min(self.pesos["identidad_maximo"], self.pesos["identidad"] * len(motivos))
        return [Bandera("identidad", puntos, "; ".join(motivos))]
