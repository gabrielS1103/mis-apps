"""Agrupa perfiles que comparten textos o enlaces (posible coordinación)."""

from dataclasses import dataclass, field

import networkx as nx

from .enlaces import normalizar_enlace
from .textos import es_generico, normalizar_texto

LARGO_MINIMO = 20      # los textos más cortos coinciden demasiado entre personas reales


@dataclass
class Cluster:
    id: str
    miembros: list[str]
    textos: list[str] = field(default_factory=list)     # ejemplos de textos compartidos
    enlaces: list[str] = field(default_factory=list)    # enlaces compartidos


def agrupar(textos: dict[str, list[str]], enlaces: dict[str, list[str]]) -> list[Cluster]:
    """`textos` y `enlaces` van por clave de perfil. Devuelve los grupos de 2 o más perfiles."""
    por_texto: dict[str, tuple[str, set[str]]] = {}
    for clave, lista in textos.items():
        for texto in lista:
            norm = normalizar_texto(texto)
            if len(norm) < LARGO_MINIMO or es_generico(texto):
                continue
            por_texto.setdefault(norm, (texto.strip(), set()))[1].add(clave)

    por_enlace: dict[str, set[str]] = {}
    for clave, lista in enlaces.items():
        for enlace in lista:
            if norm := normalizar_enlace(enlace):
                por_enlace.setdefault(norm, set()).add(clave)

    grafo = nx.Graph()
    for ejemplo, claves in por_texto.values():
        _unir(grafo, claves, "texto", ejemplo)
    for enlace, claves in por_enlace.items():
        _unir(grafo, claves, "enlace", enlace)

    clusteres = []
    for componente in nx.connected_components(grafo):
        if len(componente) < 2:
            continue
        sub = grafo.subgraph(componente)
        textos_c, enlaces_c = set(), set()
        for _, _, motivos in sub.edges(data="motivos"):
            for tipo, valor in motivos:
                (textos_c if tipo == "texto" else enlaces_c).add(valor)
        clusteres.append(Cluster("", sorted(componente), sorted(textos_c), sorted(enlaces_c)))

    clusteres.sort(key=lambda c: (-len(c.miembros), c.miembros))
    for n, c in enumerate(clusteres, 1):
        c.id = f"C{n}"
    return clusteres


def _unir(grafo: nx.Graph, claves: set[str], tipo: str, valor: str):
    if len(claves) < 2:
        return
    claves = sorted(claves)
    # Una estrella basta para dejarlos en el mismo componente, sin crear n² aristas
    for otra in claves[1:]:
        if grafo.has_edge(claves[0], otra):
            grafo.edges[claves[0], otra]["motivos"].add((tipo, valor))
        else:
            grafo.add_edge(claves[0], otra, motivos={(tipo, valor)})
