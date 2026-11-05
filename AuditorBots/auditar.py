"""AuditorBots: importa lo exportado con el colector, elige la muestra y genera el informe.

Uso:  python auditar.py CARPETA [--muestra 30] [--semilla N] [--descargas] [--hoy AAAA-MM-DD]
"""

import argparse
import sys
from datetime import date
from pathlib import Path

from auditor.almacen import Almacen
from auditor.analisis import analizar
from auditor.importar import importar_carpeta, recoger_descargas
from auditor.informe import escribir_csv, escribir_html, escribir_json, escribir_muestra, resumen_consola


def main(argv: list[str] | None = None) -> int:
    for flujo in (sys.stdout, sys.stderr):
        if hasattr(flujo, "reconfigure"):
            flujo.reconfigure(encoding="utf-8", errors="replace")   # nombres con emojis en la consola de Windows

    parser = argparse.ArgumentParser(
        prog="auditar",
        description="Audita reacciones y comentarios de Facebook exportados con el colector de AuditorBots.",
    )
    parser.add_argument("carpeta", type=Path, help="carpeta de la auditoría (se crea si no existe)")
    parser.add_argument("--muestra", type=int, default=100, metavar="N",
                        help="perfiles de la muestra aleatoria a revisar (por defecto 100)")
    parser.add_argument("--semilla", type=int, default=None,
                        help="semilla del sorteo de la muestra, para poder repetirlo")
    parser.add_argument("--descargas", nargs="?", const=Path.home() / "Downloads", type=Path, metavar="CARPETA",
                        help="mueve antes los archivos del colector que haya en Descargas")
    parser.add_argument("--total-reacciones", type=int, default=None, metavar="N",
                        help="reacciones que muestra Facebook en la publicación, si el archivo no lo trae")
    parser.add_argument("--hoy", type=date.fromisoformat, default=None, help=argparse.SUPPRESS)
    args = parser.parse_args(argv)

    carpeta: Path = args.carpeta
    carpeta.mkdir(parents=True, exist_ok=True)

    if args.descargas:
        movidos = recoger_descargas(carpeta, args.descargas)
        if movidos:
            print(f"Traídos de {args.descargas}: {len(movidos)} archivos")

    almacen = Almacen(carpeta / "auditoria.sqlite")
    try:
        mensajes = importar_carpeta(carpeta, almacen)
        if mensajes:
            print("Importado:")
            print("\n".join(mensajes))

        if args.total_reacciones:
            con_reacciones = sorted({i.publicacion for i in almacen.cargar().interacciones if i.tipo == "reaccion"})
            if len(con_reacciones) == 1:
                almacen.guardar_total(con_reacciones[0], args.total_reacciones)
                almacen.con.commit()
            else:
                print(f"--total-reacciones no se aplicó: hay {len(con_reacciones)} publicaciones con reacciones "
                      "en la carpeta y no sé a cuál corresponde.")

        almacen.ampliar_muestra(args.muestra, args.semilla)
        datos = almacen.cargar()
    finally:
        almacen.cerrar()

    if not datos.interacciones and not datos.perfiles:
        print(f"No hay datos en {carpeta.resolve()}.\n"
              "Exporta las reacciones o los comentarios con el colector (colector/colector.js), guarda "
              "los archivos .json en esa carpeta y vuelve a ejecutar.")
        return 1

    escribir_muestra(datos, carpeta / "muestra.html")
    auditoria = analizar(datos, args.hoy)
    escribir_csv(auditoria, carpeta / "perfiles.csv")
    escribir_json(auditoria, carpeta / "perfiles.json")
    escribir_html(auditoria, carpeta.resolve(), carpeta / "informe.html")

    print(resumen_consola(auditoria))
    pendientes = sum(1 for c in datos.muestra if c not in datos.perfiles)
    print(f"\nInforme:  {(carpeta / 'informe.html').resolve()}")
    if pendientes:
        print(f"Muestra:  {(carpeta / 'muestra.html').resolve()}  ({pendientes} perfiles por exportar)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
