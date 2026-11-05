from datetime import date

import pytest

from auditor.fechas import leer_anio_union, leer_fecha

BASE = date(2026, 11, 1)


@pytest.mark.parametrize("texto, esperado", [
    ("3 a", date(2023, 11, 1)),
    ("hace 2 años", date(2024, 11, 1)),
    ("1 año", date(2025, 11, 1)),
    ("2 y", date(2024, 11, 1)),
    ("3 years ago", date(2023, 11, 1)),
    ("5 meses", date(2026, 6, 1)),
    ("2 sem", date(2026, 10, 18)),
    ("3w", date(2026, 10, 11)),
    ("4 d", date(2026, 10, 28)),
    ("5 h", BASE),
    ("12 min", BASE),
    ("Ayer", date(2026, 10, 31)),
    ("Yesterday at 10:15 PM", date(2026, 10, 31)),
    ("Ayer a las 9:05", date(2026, 10, 31)),
    ("12 de marzo de 2021", date(2021, 3, 12)),
    ("12 de marzo de 2021 a las 18:30", date(2021, 3, 12)),
    ("Sábado, 3 de mayo de 2025", date(2025, 5, 3)),
    ("March 12, 2021", date(2021, 3, 12)),
    ("12 March 2021", date(2021, 3, 12)),
    ("marzo de 2021", date(2021, 3, 31)),          # sin día: el último del mes
    ("2021", date(2021, 12, 31)),                  # solo año: el último día del año
    ("10 de octubre", date(2026, 10, 10)),         # sin año: el año en curso...
    ("15 de diciembre", date(2025, 12, 15)),       # ...salvo que quede en el futuro
    ("2021-03-12", date(2021, 3, 12)),
    ("25/12/2020", date(2020, 12, 25)),
    ("", None),
    ("Compartido con: Público", None),
])
def test_leer_fecha(texto, esperado):
    assert leer_fecha(texto, BASE) == esperado


def test_anio_bisiesto():
    assert leer_fecha("1 a", date(2024, 2, 29)) == date(2023, 2, 28)


@pytest.mark.parametrize("texto, esperado", [
    ("Se unió en marzo de 2019", 2019),
    ("Joined February 2012", 2012),
    ("Miembro desde 2015", 2015),
    ("Vive en Lima", None),
    ("", None),
])
def test_anio_union(texto, esperado):
    assert leer_anio_union(texto) == esperado
