import json
import shutil
from datetime import date
from pathlib import Path

from auditar import main
from auditor.almacen import Almacen
from auditor.importar import importar_carpeta, recoger_descargas

EJEMPLOS = Path(__file__).resolve().parent.parent / "ejemplos"


def copiar_ejemplos(destino: Path) -> Path:
    carpeta = destino / "auditoria"
    carpeta.mkdir()
    for archivo in EJEMPLOS.glob("*.json"):
        if archivo.name != "perfiles.json":
            shutil.copy(archivo, carpeta)
    return carpeta


def test_flujo_completo_con_los_ejemplos(tmp_path, capsys):
    carpeta = copiar_ejemplos(tmp_path)
    assert main([str(carpeta), "--semilla", "1"]) == 0
    for salida in ("informe.html", "muestra.html", "perfiles.csv", "perfiles.json", "auditoria.sqlite"):
        assert (carpeta / salida).exists()

    datos = json.loads((carpeta / "perfiles.json").read_text(encoding="utf-8"))
    nivel = {p["nombre"]: p["nivel"] for p in datos["perfiles"]}
    assert nivel["Marta Zombi"] == "alto" and nivel["Lucas Zombi 48213"] == "alto"
    assert nivel["Ana Normal"] == "bajo" and nivel["Carla Normal"] == "bajo"
    assert nivel["Eva Normal"] == "no evaluado"
    assert len(datos["clusteres"]) == 1 and len(datos["clusteres"][0]["miembros"]) == 3

    # Volver a ejecutar no duplica nada ni cambia la muestra
    muestra = (carpeta / "muestra.html").read_text(encoding="utf-8")
    capsys.readouterr()
    assert main([str(carpeta), "--semilla", "99"]) == 0
    assert "Importado:" not in capsys.readouterr().out
    assert (carpeta / "muestra.html").read_text(encoding="utf-8") == muestra


def test_la_reaccion_conserva_su_tipo(tmp_path):
    carpeta = tmp_path / "a"
    carpeta.mkdir()
    base = {"fuente": "AuditorBots", "version": 1, "tipo": "reacciones", "capturado": "2026-11-01T00:00:00Z",
            "publicacion": "https://www.facebook.com/pagina/posts/1"}
    fila = {"nombre": "Ana", "url": "https://www.facebook.com/profile.php?id=1"}
    (carpeta / "reacciones-1.json").write_text(json.dumps({**base, "elementos": [{**fila, "reaccion": "Me encanta"}]}))
    (carpeta / "reacciones-2.json").write_text(json.dumps({**base, "elementos": [{**fila, "reaccion": None}]}))
    almacen = Almacen(carpeta / "auditoria.sqlite")
    importar_carpeta(carpeta, almacen)
    datos = almacen.cargar()
    almacen.cerrar()
    assert [(i.perfil, i.reaccion) for i in datos.interacciones] == [("id:1", "Me encanta")]


def test_perfil_por_usuario_se_une_con_su_numero(tmp_path):
    carpeta = tmp_path / "a"
    carpeta.mkdir()
    (carpeta / "reacciones-1.json").write_text(json.dumps({
        "fuente": "AuditorBots", "version": 1, "tipo": "reacciones", "publicacion": "x",
        "elementos": [{"nombre": "Ana", "url": "https://www.facebook.com/profile.php?id=55"}]}))
    (carpeta / "perfil-ana.json").write_text(json.dumps({
        "fuente": "AuditorBots", "version": 1, "tipo": "perfil", "capturado": "2026-11-01T00:00:00Z",
        "url": "https://www.facebook.com/ana.prueba", "id": "55", "usuario": "ana.prueba", "nombre": "Ana",
        "publicaciones": [{"fecha": "4 a", "texto": "Hola", "enlaces": []}]}))
    almacen = Almacen(carpeta / "auditoria.sqlite")
    importar_carpeta(carpeta, almacen)
    datos = almacen.cargar()
    almacen.cerrar()
    assert set(datos.perfiles) == {"id:55"}
    assert datos.perfiles["id:55"].publicaciones[0].fecha == date(2022, 11, 1)


def test_archivos_ajenos_y_pagina_guardada(tmp_path):
    carpeta = tmp_path / "a"
    carpeta.mkdir()
    (carpeta / "otro.json").write_text('{"hola": 1}')
    (carpeta / "roto.json").write_text("{")
    (carpeta / "publicacion.html").write_text(
        "<!-- saved from url=(0040)https://www.facebook.com/pagina/posts/77 -->"
        "<html><body><a href='https://www.facebook.com/autor'>Autor</a>"
        "<div role='dialog'><div role='tablist'></div>"
        "<a href='https://www.facebook.com/profile.php?id=9&amp;__cft__=x'>Bea</a>"
        "<a href='https://www.facebook.com/bea.prueba?__tn__=R'><img></a>"
        "<a href='https://www.facebook.com/bea.prueba?__tn__=R'>Bea Prueba</a></div></body></html>",
        encoding="utf-8")
    almacen = Almacen(carpeta / "auditoria.sqlite")
    mensajes = importar_carpeta(carpeta, almacen)
    datos = almacen.cargar()
    almacen.cerrar()
    assert any("otro.json: no se importó" in m for m in mensajes)
    assert any("roto.json: no se importó" in m for m in mensajes)
    assert sorted(i.perfil for i in datos.interacciones) == ["id:9", "u:bea.prueba"]
    assert {i.publicacion for i in datos.interacciones} == {"https://www.facebook.com/pagina/posts/77"}
    assert datos.nombres["u:bea.prueba"][0] == "Bea Prueba"


def test_csv_neutraliza_formulas(tmp_path):
    carpeta = tmp_path / "a"
    carpeta.mkdir()
    (carpeta / "reacciones-1.json").write_text(json.dumps({
        "fuente": "AuditorBots", "version": 1, "tipo": "reacciones", "publicacion": "x",
        "elementos": [{"nombre": "=HYPERLINK(\"http://malo\")", "url": "https://www.facebook.com/profile.php?id=1"}]}))
    assert main([str(carpeta)]) == 0
    assert "'=HYPERLINK" in (carpeta / "perfiles.csv").read_text(encoding="utf-8-sig")
    assert "=HYPERLINK(&quot;" in (carpeta / "informe.html").read_text(encoding="utf-8")


def test_recoger_descargas(tmp_path):
    descargas = tmp_path / "Descargas"
    descargas.mkdir()
    carpeta = tmp_path / "auditoria"
    carpeta.mkdir()
    (descargas / "perfil-ana.json").write_text('{"fuente": "AuditorBots", "tipo": "perfil"}')
    (descargas / "perfil-ajeno.json").write_text('{"otra": "cosa"}')
    (descargas / "factura.json").write_text('{"fuente": "AuditorBots"}')
    assert recoger_descargas(carpeta, descargas) == ["perfil-ana.json"]
    assert (carpeta / "perfil-ana.json").exists() and (descargas / "perfil-ajeno.json").exists()


def test_total_mostrado_y_extrapolacion(tmp_path, capsys):
    carpeta = tmp_path / "a"
    carpeta.mkdir()
    base = {"fuente": "AuditorBots", "version": 1, "capturado": "2026-11-01T00:00:00Z",
            "publicacion": "https://www.facebook.com/reel/1"}
    (carpeta / "reacciones-1.json").write_text(json.dumps({**base, "tipo": "reacciones", "total_mostrado": "Todas 178 mil",
        "elementos": [{"nombre": "Ana", "url": "https://www.facebook.com/profile.php?id=1"}]}))
    (carpeta / "perfil-1.json").write_text(json.dumps({**base, "tipo": "perfil", "url": "https://www.facebook.com/profile.php?id=1",
        "id": "1", "nombre": "Ana", "publicaciones": [{"fecha": "5 a", "texto": "Hola", "enlaces": []}]}))
    assert main([str(carpeta), "--muestra", "1"]) == 0
    salida = capsys.readouterr().out
    assert "de 178.000 que muestra Facebook" in salida
    assert "Facebook muestra 178.000 reacciones" in salida
    assert "serían unas 178.000 sospechosas" in salida


def test_total_reacciones_a_mano(tmp_path, capsys):
    carpeta = tmp_path / "a"
    carpeta.mkdir()
    (carpeta / "reacciones-1.json").write_text(json.dumps({
        "fuente": "AuditorBots", "version": 1, "tipo": "reacciones", "publicacion": "https://www.facebook.com/pagina",
        "elementos": [{"nombre": "Ana", "url": "https://www.facebook.com/profile.php?id=1"}]}))
    assert main([str(carpeta), "--total-reacciones", "5000"]) == 0
    salida = capsys.readouterr().out
    assert "no la de una publicación" in salida
    almacen = Almacen(carpeta / "auditoria.sqlite")
    assert almacen.cargar().totales == {"https://www.facebook.com/pagina": 5000}
    almacen.cerrar()


def test_titulo_de_pestana_no_se_usa_como_nombre(tmp_path):
    carpeta = tmp_path / "a"
    carpeta.mkdir()
    base = {"fuente": "AuditorBots", "version": 1, "capturado": "2026-11-01T00:00:00Z"}
    (carpeta / "reacciones-1.json").write_text(json.dumps({**base, "tipo": "reacciones", "publicacion": "x",
        "elementos": [{"nombre": "Ana Real", "url": "https://www.facebook.com/profile.php?id=7"}]}))
    (carpeta / "zperfil-7.json").write_text(json.dumps({**base, "tipo": "perfil", "id": "7", "nombre": "(1) Facebook",
        "url": "https://www.facebook.com/profile.php?id=7", "marcas_tiempo": [1761696000, 9999999999],
        "publicaciones": [{"fecha": "", "texto": "Hola", "enlaces": []}]}))
    almacen = Almacen(carpeta / "auditoria.sqlite")
    importar_carpeta(carpeta, almacen)
    datos = almacen.cargar()
    almacen.cerrar()
    assert datos.nombres["id:7"][0] == "Ana Real"
    assert datos.perfiles["id:7"].ultima_marca == date(2025, 10, 29)   # la marca futura se descarta
