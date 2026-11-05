"""Genera, a partir de colector.js, el marcador (marcador.txt) y la copia de la extensión.

Ejecutar después de cambiar colector.js:  python colector/generar.py
"""

import shutil
from pathlib import Path
from urllib.parse import quote

carpeta = Path(__file__).parent
origen = carpeta / "colector.js"

lineas = origen.read_text(encoding="utf-8").splitlines()
codigo = "\n".join(l.strip() for l in lineas if l.strip() and not l.strip().startswith("//"))
(carpeta / "marcador.txt").write_text("javascript:" + quote(codigo, safe="()[]{}=;,.:'!*~/_-+<>?&|$@^"), encoding="utf-8")
print(f"marcador.txt: {len(codigo)} caracteres de código")

# La extensión no puede leer archivos fuera de su carpeta: lleva su propia copia
shutil.copy(origen, carpeta.parent / "extension" / "colector.js")
print("extension/colector.js actualizado")
