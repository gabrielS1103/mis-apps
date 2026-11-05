# Arregla la app de GeForce NOW cuando no arranca porque el fabricante de la BIOS
# ("CHUWI Innovation And Technology(ShenZhen)co.,Ltd") lleva coma y paréntesis.
# Cambia la cabecera NV-Device-Make por un nombre simple y actualiza la huella (SRI) del index.html.
#
# Uso:  python arreglar_gfn.py            -> aplica el arreglo (y reabre GeForce NOW)
#       python arreglar_gfn.py restaurar  -> deja los archivos originales de esta versión
# Hay que volver a ejecutarlo cada vez que GeForce NOW se actualice.
import base64, hashlib, os, re, shutil, subprocess, sys, time

FABRICANTE = "CHUWI"
BASE = os.path.join(os.environ["LOCALAPPDATA"], "NVIDIA Corporation", "GeForceNOW")
MALL = os.path.join(BASE, "Mall")
EXE = os.path.join(BASE, "CEF", "GeForceNOW.exe")
RESPALDOS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "respaldo")

# Cualquier asignación de la cabecera: X["NV-Device-Make"]=<expresión hasta la coma o ;>
PATRON = re.compile(rb'(\["NV-Device-Make"\]=)(this\.getDeviceMakeHeader\([\w$]*\)'
                    rb'|\(null==[\w$]+\?void 0:[\w$]+\.VendorName\)\|\|"undefined"'
                    rb'|[\w$.]+\.(?:Make|VendorName))')


def sri(datos):
    return "sha384-" + base64.b64encode(hashlib.sha384(datos).digest()).decode()


def leer(ruta):
    with open(ruta, "rb") as f:
        return f.read()


def escribir(ruta, datos):
    with open(ruta, "wb") as f:
        f.write(datos)


def cerrar_gfn():
    for exe in ("GeForceNOW.exe", "GeForceNOWContainer.exe"):
        subprocess.run(["taskkill", "/F", "/IM", exe], capture_output=True)
    time.sleep(1.5)


def main():
    restaurar = len(sys.argv) > 1 and sys.argv[1].lower().startswith("rest")
    html_ruta = os.path.join(MALL, "index.html")
    html = leer(html_ruta)
    m = re.search(rb'(main\.[0-9a-f]+\.js)"[^>]*integrity="(sha384-[A-Za-z0-9+/=]+)"', html)
    if not m:
        sys.exit("No encontré main.*.js con huella en index.html. Puede que NVIDIA haya cambiado la app.")
    js_nombre, huella = m.group(1).decode(), m.group(2).decode()
    js_ruta = os.path.join(MALL, js_nombre)
    respaldo = os.path.join(RESPALDOS, js_nombre)
    print("Versión encontrada:", js_nombre)

    if restaurar:
        if not os.path.isdir(respaldo):
            sys.exit("No hay respaldo de esta versión.")
        cerrar_gfn()
        for f in (js_nombre, "index.html"):
            shutil.copy2(os.path.join(respaldo, f), os.path.join(MALL, f))
        print("Restaurado.")
        return

    js = leer(js_ruta)
    if sri(js) != huella:
        sys.exit("La huella de index.html no coincide con el main.js. No toco nada.")

    nuevo, n = PATRON.subn(rb'\1"' + FABRICANTE.encode() + rb'"', js)
    if n == 0:
        if b'["NV-Device-Make"]="' + FABRICANTE.encode() + b'"' in js:
            print("Ya estaba arreglado.")
        else:
            sys.exit("No encontré la cabecera NV-Device-Make. NVIDIA cambió el código; hay que revisarlo a mano.")
    else:
        cerrar_gfn()
        os.makedirs(respaldo, exist_ok=True)
        for f, datos in ((js_nombre, js), ("index.html", html)):
            destino = os.path.join(respaldo, f)
            if not os.path.exists(destino):
                escribir(destino, datos)
        html_nuevo = html.replace(huella.encode(), sri(nuevo).encode())
        escribir(js_ruta, nuevo)
        escribir(html_ruta, html_nuevo)
        # Comprobación
        assert sri(leer(js_ruta)).encode() in leer(html_ruta)
        print(f"Arreglado: {n} cabecera(s) cambiada(s) a \"{FABRICANTE}\". Respaldo en {respaldo}")

    abierta = b"GeForceNOW.exe" in subprocess.run(["tasklist"], capture_output=True).stdout
    if "--no-abrir" not in sys.argv and not abierta and os.path.exists(EXE):
        subprocess.Popen([EXE], cwd=os.path.dirname(EXE))
        print("Abriendo GeForce NOW...")


if __name__ == "__main__":
    main()
