from datetime import date

from auditor.clusteres import Cluster, agrupar
from auditor.enlaces import identificar, normalizar_enlace
from auditor.heuristicas import BotAuditor
from auditor.modelos import Perfil, Publicacion

HOY = date(2026, 11, 1)
PLANTILLA = ("Por mi cumpleaños este año, pido donaciones para {}. Elegí esta organización porque su misión "
             "significa mucho para mí, y espero que consideres contribuir como forma de celebrar conmigo.")


def perfil(*publicaciones, foto=True, amigos=True, nombre="Ana Prueba"):
    return Perfil(clave="u:ana", nombre=nombre, tiene_foto=foto, amigos_visibles=amigos,
                  publicaciones=list(publicaciones), capturado=HOY)


def reglas(resultado):
    return {b.regla: b.puntos for b in resultado.banderas}


def evaluar(p=None, comentarios=(), cluster=None, nombre=None):
    return BotAuditor(HOY).evaluar("u:ana", nombre or (p.nombre if p else "Ana"), p, list(comentarios), cluster)


def test_sin_datos_no_se_evalua():
    r = evaluar(None)
    assert r.puntaje is None and r.nivel == "no evaluado"


def test_perfil_reciente_y_completo_es_limpio():
    r = evaluar(perfil(Publicacion("Hola", fecha=date(2026, 10, 1))))
    assert r.puntaje == 0 and r.nivel == "bajo"


def test_perfil_sin_publicaciones_no_es_zombi():
    assert reglas(evaluar(perfil())) == {}


def test_zombi():
    assert reglas(evaluar(perfil(Publicacion("Hola", fecha=date(2023, 11, 1))))) == {"zombi": 35}
    assert reglas(evaluar(perfil(Publicacion("Hola", fecha=date(2023, 11, 2))))) == {}


def test_zombi_usa_la_publicacion_mas_reciente():
    p = perfil(Publicacion("vieja", fecha=date(2019, 1, 1)), Publicacion("nueva", fecha=date(2026, 1, 1)))
    assert "zombi" not in reglas(evaluar(p))


def test_plantilla_con_causas_distintas():
    p = perfil(
        Publicacion(PLANTILLA.format("Fundación Uno"), fecha=date(2026, 3, 1), enlaces=["https://gofundme.com/f/uno"]),
        Publicacion(PLANTILLA.format("Fundación Dos"), fecha=date(2026, 3, 20), enlaces=["https://gofundme.com/f/dos"]),
    )
    assert reglas(evaluar(p)) == {"plantilla": 30}


def test_plantilla_no_cuenta_si_estan_lejos_en_el_tiempo():
    p = perfil(
        Publicacion(PLANTILLA.format("Fundación Uno"), fecha=date(2025, 3, 1)),
        Publicacion(PLANTILLA.format("Fundación Dos"), fecha=date(2026, 3, 1)),
    )
    assert "plantilla" not in reglas(evaluar(p))


def test_plantilla_no_cuenta_la_misma_publicacion_compartida_dos_veces():
    pub = dict(texto=PLANTILLA.format("Fundación Uno"), enlaces=["https://gofundme.com/f/uno"])
    p = perfil(Publicacion(**pub, fecha=date(2026, 3, 1)), Publicacion(**pub, fecha=date(2026, 3, 5)))
    assert "plantilla" not in reglas(evaluar(p))


def test_textos_parecidos_sin_palabras_de_recaudacion_no_son_plantilla():
    p = perfil(Publicacion("Buenos días a todos mis amigos, que tengan un lindo día"),
               Publicacion("Buenos días a todos mis amigos, que tengan un lindo dia!"))
    assert "plantilla" not in reglas(evaluar(p))


def test_identidad_suma_cinco_por_anomalia():
    assert reglas(evaluar(perfil(nombre="Ana 48213"))) == {"identidad": 5}
    assert reglas(evaluar(perfil(nombre="Ana 48213", foto=False, amigos=False))) == {"identidad": 15}
    assert reglas(evaluar(perfil(foto=None, amigos=None))) == {}     # "no sé" no penaliza


def test_comentario_generico():
    assert reglas(evaluar(comentarios=["Amén 🙏", "Bendicionesss"])) == {"comentario_generico": 10}
    assert reglas(evaluar(comentarios=["🙏🙏"])) == {"comentario_generico": 10}
    assert reglas(evaluar(comentarios=["Amén", "¿Dónde fue esto?"])) == {}


def test_texto_compartido():
    cluster = Cluster("C1", ["u:ana", "u:otra", "u:tercera"])
    r = evaluar(comentarios=["Excelente trabajo, siempre informando con la verdad"], cluster=cluster)
    assert reglas(r) == {"texto_compartido": 20}
    assert "2 perfiles" in r.banderas[0].motivo


def test_puntaje_con_tope_de_100():
    auditor = BotAuditor(HOY, pesos={"zombi": 90, "identidad": 20, "identidad_maximo": 60})
    p = perfil(Publicacion("Hola", fecha=date(2020, 1, 1)), nombre="Ana 999", foto=False)
    r = auditor.evaluar("u:ana", p.nombre, p, [], None)
    assert r.puntaje == 100 and r.nivel == "alto"


def test_clusteres_por_texto_y_por_enlace():
    texto = "Excelente trabajo de esta pagina, siempre informando con la verdad"
    clusteres = agrupar(
        {"a": [texto], "b": [texto.upper() + " 👏"], "c": ["Amén"], "d": ["Amén"], "e": ["otra cosa distinta y larga"]},
        {"c": ["https://ejemplo.invalid/nota?fbclid=1"], "e": ["https://ejemplo.invalid/nota?utm_source=x"],
         "f": ["https://www.facebook.com/auditorbots.pagina/posts/1"], "g": ["https://www.facebook.com/auditorbots.pagina/posts/1"]},
    )
    assert [c.miembros for c in clusteres] == [["a", "b"], ["c", "e"]]   # "Amén" es genérico; los enlaces de FB no cuentan


def test_identificar_perfiles():
    assert identificar("https://www.facebook.com/profile.php?id=123&__cft__=x").clave == "id:123"
    assert identificar("https://m.facebook.com/Ana.Prueba?__tn__=R").clave == "u:ana.prueba"
    assert identificar("https://www.facebook.com/groups/99/user/456/").clave == "id:456"
    assert identificar("https://www.facebook.com/people/Ana-Prueba/789/").clave == "id:789"
    assert identificar("https://www.facebook.com/watch/?v=1") is None
    assert identificar("https://www.facebook.com/ana/posts/1") is None
    assert identificar("https://ejemplo.invalid/ana") is None


def test_normalizar_enlace():
    redireccion = "https://l.facebook.com/l.php?u=https%3A%2F%2Fwww.gofundme.com%2Ff%2Fcausa%3Futm_source%3Dfb&h=x"
    assert normalizar_enlace(redireccion) == "https://gofundme.com/f/causa"
    assert normalizar_enlace("https://www.facebook.com/donate/123/") == "https://facebook.com/donate/123"
    assert normalizar_enlace("https://www.facebook.com/ana") is None
    assert normalizar_enlace("https://ejemplo.invalid/") is None


def test_limpiar_comentario():
    from auditor.textos import limpiar_comentario
    assert limpiar_comentario("¿A quién le hace falta un airbag? 11 sem", "") == ("¿A quién le hace falta un airbag?", "11 sem")
    assert limpiar_comentario("Resultó perfecto. 100% l... Ver más 11 sem", "") == ("Resultó perfecto. 100% l…", "11 sem")
    assert limpiar_comentario("Tengo 3 d", "2 d") == ("Tengo 3 d", "2 d")      # con fecha ya separada no toca el texto
    assert limpiar_comentario("Hola Ayer", "") == ("Hola", "Ayer")
    assert limpiar_comentario("11 sem", "") == ("", "11 sem")     # comentario que es solo un sticker o GIF


def test_leer_cantidad():
    from auditor.textos import leer_cantidad
    assert leer_cantidad("Todas 178 mil") == 178_000
    assert leer_cantidad("1,2 mil") == 1_200
    assert leer_cantidad("1,2 mill.") == 1_200_000
    assert leer_cantidad("All 3.4M") == 3_400_000
    assert leer_cantidad("178K") == 178_000
    assert leer_cantidad("12.345") == 12_345
    assert leer_cantidad("Todas") is None


def test_perfil_no_disponible_no_suma_puntos():
    p = perfil(Publicacion("Hola", fecha=date(2019, 1, 1)), nombre="Ana 999", foto=False)
    p.no_disponible = True
    r = evaluar(p)
    assert r.puntaje == 0 and r.nivel == "no disponible"
