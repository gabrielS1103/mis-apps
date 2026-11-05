"""Base SQLite de una auditoría (auditoria.sqlite dentro de su carpeta).

Acumula todo lo importado, así los archivos JSON se pueden borrar después de importarlos.
"""

import json
import random
import sqlite3
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

from .modelos import Interaccion, Perfil, perfil_a_dict, perfil_desde_dict

ESQUEMA = """
CREATE TABLE IF NOT EXISTS archivos(
    huella TEXT PRIMARY KEY, nombre TEXT, tipo TEXT, importado TEXT);
CREATE TABLE IF NOT EXISTS perfiles(
    clave TEXT PRIMARY KEY, datos TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS nombres(
    clave TEXT PRIMARY KEY, nombre TEXT, url TEXT);
CREATE TABLE IF NOT EXISTS alias(
    alias TEXT PRIMARY KEY, clave TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS interacciones(
    perfil TEXT NOT NULL, publicacion TEXT NOT NULL, tipo TEXT NOT NULL,
    reaccion TEXT, texto TEXT NOT NULL DEFAULT '', fecha_texto TEXT,
    UNIQUE(perfil, publicacion, tipo, texto));
CREATE TABLE IF NOT EXISTS publicaciones(
    url TEXT PRIMARY KEY, total_mostrado INTEGER);
CREATE TABLE IF NOT EXISTS muestra(
    clave TEXT PRIMARY KEY, orden INTEGER NOT NULL);
"""


@dataclass
class Datos:
    """Todo lo que hay en la base, con las claves ya unificadas por alias."""
    perfiles: dict[str, Perfil]
    nombres: dict[str, tuple[str, str]]          # clave -> (nombre, url)
    interacciones: list[Interaccion]
    muestra: list[str]
    totales: dict[str, int] = field(default_factory=dict)   # publicación -> reacciones que muestra Facebook


class Almacen:
    def __init__(self, ruta: Path):
        self.con = sqlite3.connect(ruta)
        self.con.executescript(ESQUEMA)

    def cerrar(self):
        self.con.commit()
        self.con.close()

    # --- Importación ---------------------------------------------------------

    def ya_importado(self, huella: str) -> bool:
        return self.con.execute("SELECT 1 FROM archivos WHERE huella=?", (huella,)).fetchone() is not None

    def marcar_importado(self, huella: str, nombre: str, tipo: str):
        self.con.execute(
            "INSERT OR REPLACE INTO archivos VALUES(?,?,?,?)",
            (huella, nombre, tipo, datetime.now().isoformat(timespec="seconds")),
        )
        self.con.commit()

    def guardar_nombre(self, clave: str, nombre: str, url: str):
        self.con.execute(
            """INSERT INTO nombres VALUES(?,?,?) ON CONFLICT(clave) DO UPDATE SET
               nombre=CASE WHEN excluded.nombre<>'' THEN excluded.nombre ELSE nombre END""",
            (clave, nombre, url),
        )

    def guardar_alias(self, alias: str, clave: str):
        if alias != clave:
            self.con.execute("INSERT OR REPLACE INTO alias VALUES(?,?)", (alias, clave))

    def guardar_interaccion(self, i: Interaccion):
        # Si la misma reacción llega primero desde la pestaña "Todas" (sin tipo) y luego desde
        # la pestaña de un tipo concreto, se conserva el tipo.
        self.con.execute(
            """INSERT INTO interacciones VALUES(?,?,?,?,?,?)
               ON CONFLICT(perfil, publicacion, tipo, texto) DO UPDATE SET
               reaccion=COALESCE(excluded.reaccion, reaccion)""",
            (i.perfil, i.publicacion, i.tipo, i.reaccion, i.texto, i.fecha_texto),
        )

    def guardar_total(self, publicacion: str, total: int):
        # Se queda con el mayor: el total crece mientras la publicación sigue recibiendo reacciones
        self.con.execute(
            """INSERT INTO publicaciones VALUES(?,?) ON CONFLICT(url) DO UPDATE SET
               total_mostrado=MAX(total_mostrado, excluded.total_mostrado)""",
            (publicacion, total),
        )

    def guardar_perfil(self, perfil: Perfil):
        self.con.execute(
            "INSERT OR REPLACE INTO perfiles VALUES(?,?)",
            (perfil.clave, json.dumps(perfil_a_dict(perfil), ensure_ascii=False)),
        )

    # --- Muestra -------------------------------------------------------------

    def ampliar_muestra(self, tamano: int, semilla: int | None) -> list[str]:
        """Completa la muestra aleatoria hasta `tamano` perfiles y la devuelve en orden.

        La muestra se guarda: volver a ejecutar no la cambia, solo la amplía si se pide una mayor.
        Los perfiles que ya tienen ficha cuentan como parte de la muestra.
        """
        datos = self.cargar()
        actual = datos.muestra
        if len(actual) < tamano:
            con_interaccion = sorted({i.perfil for i in datos.interacciones} - set(actual))
            # Primero entran los perfiles ya exportados, para no pedir trabajo de más
            con_ficha = [c for c in con_interaccion if c in datos.perfiles]
            sin_ficha = [c for c in con_interaccion if c not in datos.perfiles]
            random.Random(semilla).shuffle(sin_ficha)
            nuevos = (con_ficha + sin_ficha)[: tamano - len(actual)]
            self.con.executemany(
                "INSERT INTO muestra VALUES(?,?)",
                [(c, len(actual) + n) for n, c in enumerate(nuevos)],
            )
            self.con.commit()
            actual = actual + nuevos
        return actual

    # --- Lectura -------------------------------------------------------------

    def cargar(self) -> Datos:
        alias = dict(self.con.execute("SELECT alias, clave FROM alias"))

        def canon(clave: str) -> str:
            vistos = set()
            while clave in alias and clave not in vistos:
                vistos.add(clave)
                clave = alias[clave]
            return clave

        perfiles = {}
        for clave, datos in self.con.execute("SELECT clave, datos FROM perfiles"):
            perfil = perfil_desde_dict(json.loads(datos))
            perfil.clave = canon(clave)
            perfiles[perfil.clave] = perfil

        nombres = {}
        for clave, nombre, url in self.con.execute("SELECT clave, nombre, url FROM nombres"):
            c = canon(clave)
            if c not in nombres or (nombre and not nombres[c][0]):
                nombres[c] = (nombre or "", url or "")

        interacciones = {}
        for fila in self.con.execute("SELECT * FROM interacciones"):
            i = Interaccion(canon(fila[0]), *fila[1:])
            llave = (i.perfil, i.publicacion, i.tipo, i.texto)
            if llave not in interacciones or (i.reaccion and not interacciones[llave].reaccion):
                interacciones[llave] = i

        muestra = []
        for (clave,) in self.con.execute("SELECT clave FROM muestra ORDER BY orden"):
            if canon(clave) not in muestra:
                muestra.append(canon(clave))

        totales = dict(self.con.execute("SELECT url, total_mostrado FROM publicaciones WHERE total_mostrado > 0"))
        return Datos(perfiles, nombres, list(interacciones.values()), muestra, totales)
