#!/usr/bin/env python3
"""Suite de regresion de la VENTANA (XAML + editor) de Hekatan Python3.

Por que existe: el motor se prueba con el CLI (entra un .py, sale un numero), pero la
ventana no. Y ahi viven fallos que ningun test de motor puede ver:

  * el .py calculado con el motor EQUIVOCADO (el parser heredado de Calcpad, que ve
    `**` como "* *", el `#` de dentro de una cadena como comentario y pide un `end`);
  * botones que escriben en el RichTextBox OCULTO -> el texto sale donde esta SU cursor;
  * Tags heredados de MATLAB/Calcpad (`nthroot(`, `strcat(`, `f = @(x,y)`, `[a b]`),
    que en Python son NameError o error de sintaxis.

De ahi el nombre de la pregunta que responde la suite: "¿esta ya SOLO de lo Python?".

Como se conecta la terminal con la ventana (sin adivinar coordenadas ni mover el raton):
la app arranca con `--ctl <carpeta>` y se queda escuchando esa carpeta. Aqui se deja
`cmd-0001.json` y la app contesta `resp-0001.json`. Un solo arranque (~2 s) sirve para
todos los casos. Ver MainWindow.Ctl.cs y MainWindow.Pruebas.cs.

Los botones se pulsan POR SU x:Name de la XAML, asi que el Tag que se prueba es el que
esta escrito en MainWindow.xaml: si alguien lo cambia mal, el test lo caza.

Uso:
    python run_tests.py                    # usa el build Release del repo
    python run_tests.py --exe <ruta.exe>   # o uno concreto (p.ej. el instalado)
    python run_tests.py -v                 # ademas, el detalle de cada caso

Sale con codigo != 0 si algo falla -> sirve de pre-commit / pre-instalador.
"""
import argparse
import builtins
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_EXE = os.path.abspath(os.path.join(
    HERE, "..", "..", "Symbolic.Wpf", "bin", "Release", "net10.0-windows", "HekatanPython3.exe"))

# El .py de partida de casi todos los casos de marcado. Linea 1 titulo (#"), 2 texto de
# hoja (#'), 3 codigo con comentario al final, 4 codigo pelado.
BASE = "\n".join([
    '#" Titulo de prueba',
    "#' Esta linea es de documento.",
    "L = 3.50   # luz de la viga en m",
    "M = 5",
])

# Las trampas del plegado Y del motor a la vez: `#` dentro de cadena, `**`, docstring
# con un `def` dentro, sangria, continuacion con `\`, celdas `#%%`.
TRAMPAS = "\n".join([
    "#%% Celda 1 - trampas",
    "import numpy as np",
    "",
    'texto = "esto # NO es comentario"',
    "otro = 'tampoco # este'",
    "",
    "def viga(q, L=6.0):",
    '    """Momento de una viga simple.',
    "",
    "    Ojo: aqui dentro hay un def que NO debe plegar,",
    "    y un # que NO es comentario.",
    '    """',
    "    M = q * L ** 2 / 8",
    "    # comentario pegado al margen, dentro de la funcion",
    "    if M > 100:",
    "        return M",
    "    return 0.0",
    "",
    "print(viga(30.0))",   # 30*6^2/8 = 135 -> pasa por el if
])

PLEGABLE = "\n".join([
    "s = 0",
    "for k in range(1, 11):",
    "    a = k ** 2",
    "    b = a / 2",
    "    s = s + b",
    "print(s)",
])

# Palabras que en un Tag NO son una llamada a funcion aunque el regex las vea asi.
NO_SON_LLAMADAS = {"if", "for", "while", "elif", "return", "print", "and", "or", "not", "in"}


class App:
    """La ventana viva, hablada por carpeta de comandos."""

    def __init__(self, exe, verbose=False):
        self.dir = tempfile.mkdtemp(prefix="hkpy-ctl-")
        self.n = 0
        self.verbose = verbose
        self.proc = subprocess.Popen([exe, "--ctl", self.dir])
        ready = os.path.join(self.dir, "ready.txt")
        for _ in range(300):                      # hasta 30 s: el primer arranque tarda
            if os.path.exists(ready):
                break
            time.sleep(0.1)
        else:
            raise RuntimeError("la ventana no llego a estar lista (sin ready.txt)")
        time.sleep(1.5)                           # que termine de montarse el editor

    def cmd(self, **kw):
        self.n += 1
        cid = "%04d" % self.n
        tmp = os.path.join(self.dir, "tmp-" + cid)
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(kw, f)
        os.rename(tmp, os.path.join(self.dir, "cmd-%s.json" % cid))   # atomico
        resp = os.path.join(self.dir, "resp-%s.json" % cid)
        for _ in range(600):                      # 60 s (un run con graficas tarda)
            if os.path.exists(resp):
                with open(resp, encoding="utf-8") as f:
                    r = json.load(f)
                if not r.get("ok", False):
                    raise RuntimeError("%s -> %s" % (kw, r.get("error")))
                return r
            time.sleep(0.1)
        raise RuntimeError("sin respuesta a %s" % (kw,))

    def code(self, text):
        self.cmd(op="setcode", text=text)

    def text(self):
        return self.cmd(op="gettext")["text"].replace("\r\n", "\n")

    def close(self):
        try:
            self.cmd(op="quit")
        except Exception:
            pass
        try:
            self.proc.wait(10)
        except Exception:
            self.proc.kill()
        shutil.rmtree(self.dir, ignore_errors=True)


FALLOS = []
OK = 0


def check(nombre, obtenido, esperado):
    global OK
    if obtenido == esperado:
        OK += 1
        print("  ok   %s" % nombre)
    else:
        FALLOS.append(nombre)
        print("  FALLA %s" % nombre)
        print("        esperado: %r" % (esperado,))
        print("        obtenido: %r" % (obtenido,))


def linea(app, n):
    return app.text().split("\n")[n - 1]


def sin_cadenas(s):
    """Quita lo que hay entre comillas: dentro de una cadena no hay llamadas."""
    return re.sub(r"'[^']*'|\"[^\"]*\"", "''", s)


def analizar_tag(tag):
    """De un Tag saca (nombres a los que llama, nombres que el propio Tag define).

    Se saltan las lineas de comentario/hoja (`#`, `#'`, `#"`): ahi la prosa lleva
    parentesis y no es codigo. El `␣` separa la version HTML de la markdown; las dos
    se miran igual.
    """
    llama, define = set(), set()
    for l in tag.replace("␣", "\n").split("§"):
        for sub in l.split("\n"):
            s = sub.strip()
            if not s or s.startswith("#") or s.startswith("<"):
                continue
            s = sin_cadenas(s)
            for m in re.finditer(r"\b(?:def|class)\s+([A-Za-z_]\w*)", s):
                define.add(m.group(1))
            # import x as y / from a.b import c as d  -> el nombre local
            for m in re.finditer(r"\bimport\s+([\w.]+)(?:\s+as\s+(\w+))?", s):
                define.add(m.group(2) or m.group(1).split(".")[0])
            for m in re.finditer(r"([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*\(", s):
                n = m.group(1)
                if n.split(".")[0] in NO_SON_LLAMADAS or n in NO_SON_LLAMADAS:
                    continue
                llama.add(n)
    return llama, define


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", default=DEFAULT_EXE)
    ap.add_argument("-v", "--verbose", action="store_true")
    a = ap.parse_args()
    if not os.path.exists(a.exe):
        print("No esta el exe: %s\n(compila Symbolic.Wpf en Release)" % a.exe)
        return 2

    print("Ventana: %s" % a.exe)
    app = App(a.exe, a.verbose)
    try:
        st = app.cmd(op="state")
        if st.get("editor") != "avalon":
            print("El editor plegable no esta activo: la suite prueba ESE editor.")
            return 2

        # --- 1. EL MOTOR: un .py se calcula con el motor de PYTHON -----------------
        # Si sale "calcpad", el parser heredado se come el `#` de dentro de la cadena,
        # ve `**` como "* *" y pide un `end` que en Python no existe.
        app.cmd(op="settext", text=TRAMPAS)
        st = app.cmd(op="state")
        check("el .py lo calcula el motor de Python", st.get("motor"), "python")

        salida = app.cmd(op="getoutput")["output"]
        check("el # dentro de una cadena no es comentario",
              "Incomplete expression" not in salida, True)
        check("el ** no se lee como '* *'", 'Invalid syntax: "* *"' not in salida, True)
        check("no pide el 'end' de MATLAB", "Missing \"end\"" not in salida, True)
        check("el script corre entero (imprime el momento)", "135" in salida, True)
        if a.verbose:
            print("        salida: %r" % salida[:400])

        # --- 2. marcado en una linea de documento --------------------------------
        app.code(BASE)
        app.cmd(op="setcaret", line=2, col=24)          # dentro de "documento"
        app.cmd(op="button", name="BoldButton")
        check("negrita envuelve la palabra del cursor", linea(app, 2),
              "#' Esta linea es de <strong>documento</strong>.")

        # --- 3. el boton es un interruptor ---------------------------------------
        app.cmd(op="button", name="BoldButton")
        check("negrita otra vez la quita", linea(app, 2),
              "#' Esta linea es de documento.")

        # --- 4. con seleccion, marca lo seleccionado ------------------------------
        app.code(BASE)
        app.cmd(op="select", line=2, col=4, len=4)       # "Esta"
        app.cmd(op="button", name="ItalicButton")
        check("cursiva sobre la seleccion", linea(app, 2),
              "#' <em>Esta</em> linea es de documento.")

        # --- 5. linea de CODIGO: no se toca --------------------------------------
        app.code(BASE)
        app.cmd(op="setcaret", line=4, col=2)            # dentro de "M = 5"
        app.cmd(op="button", name="BoldButton")
        t = app.text().split("\n")
        check("el codigo no se toca", t[3], "M = 5")
        check("la marca baja a una linea #' nueva", t[4], "#'<strong></strong>")

        # --- 6. dentro del comentario de una linea de codigo ----------------------
        app.code(BASE)
        app.cmd(op="setcaret", line=3, col=25)           # dentro de "viga"
        app.cmd(op="button", name="SuperscriptButton")
        check("marca dentro del comentario, sin tocar el codigo", linea(app, 3),
              "L = 3.50   # luz de la <sup>viga</sup> en m")

        # --- 7. encabezado = comentario de titulo de Python (#") -------------------
        app.code(BASE + "\n")
        app.cmd(op="setcaret", line=5)                   # linea vacia del final
        app.cmd(op="button", name="H3Button")
        check("el titulo se escribe con #\" (no con <h3> de Calcpad)",
              linea(app, 5).startswith('#"'), True)

        # --- 8. bloques de varias lineas: TODAS comentario de Python ---------------
        app.code(BASE)
        app.cmd(op="setcaret", line=2)
        app.cmd(op="button", name="BulletsMenu")
        lista = [l for l in app.text().split("\n") if "<li>" in l or "ul>" in l]
        check("la lista: todas sus lineas empiezan por #'",
              all(l.startswith("#'") for l in lista) and len(lista) == 5, True)

        # --- 9. NINGUN Tag escribe comentarios de Calcpad ni de MATLAB ------------
        # Calcpad comenta con `'` (en Python abre una cadena que no cierra) y MATLAB
        # con `%` (en Python es el operador modulo: error de sintaxis a principio de
        # linea). Es el fallo que ya paso en el Lab dos veces.
        botones = app.cmd(op="buttons")["buttons"]
        malos = []
        for b in botones:
            for l in b["tag"].replace("␣", "\n").replace("§", "\n").split("\n"):
                s = l.strip()
                # `%` a secas es el operador resto de Python; `%'`, `%{`, `% texto`
                # son comentarios de MATLAB. Igual con `'`: solo el de un caracter
                # es la tecla de la comilla.
                if s.startswith("%") and len(s) > 1:
                    malos.append((b["name"] or "(sin nombre)", "MATLAB %", l))
                elif s.startswith("'") and len(s) > 1 and not s.endswith("'"):
                    malos.append((b["name"] or "(sin nombre)", "Calcpad '", l))
        check("ningun Tag comenta como Calcpad (') ni como MATLAB (%%)", malos, [])

        # --- 10. TODO lo que llaman los Tags lo conoce el motor de Python ----------
        # Se le pregunta al motor (op knows -> PythonBuiltins, generado leyendo el
        # motor de verdad). Lo que no conoce NADIE no es Python: es herencia de
        # MATLAB (nthroot, strcat, isempty, repmat...).
        llama, define = set(), set()
        for b in botones:
            ll, de = analizar_tag(b["tag"])
            llama |= ll
            define |= de
        candidatos = sorted(n for n in llama if n.split(".")[0] not in define
                            and n.split(".")[-1] not in define)
        r = app.cmd(op="knows", names=candidatos)
        # Regla: un nombre PELADO (sin punto) solo vale si el motor lo da sin importar
        # nada, o si es un builtin de CPython (el motor cae a python real). `sin(x)` o
        # `sqrt(x)` pelados son NameError aunque existan en math: hay que escribir
        # `math.sin(x)`. Lo cualificado (plt.plot, np.zeros) se salva: el modulo se
        # importa en el propio Tag.
        falla = sorted(n for n in r["nadie"] + r["import"]
                       if "." not in n and not hasattr(builtins, n))
        check("ningun Tag pelado llama a algo que Python no resuelve", falla, [])
        if a.verbose or falla:
            print("        de fuera de Python: %s" % ", ".join(falla))

        # --- 11. plegado + el salto desde el reporte ------------------------------
        app.code(PLEGABLE)
        app.cmd(op="fold", all=True)
        st = app.cmd(op="state")
        check("el for se plego", st["folded"] >= 1, True)
        app.cmd(op="gotoline", line=4)                   # "b = a / 2" (dentro del pliegue)
        st = app.cmd(op="state")
        check("el clic en el reporte cae en la linea 4", st["line"], 4)
        check("...y abre el pliegue que la tapaba", st["folded"], 0)
        check("...y el foco se queda en el editor que se ve", st["focus"], True)

    finally:
        app.close()

    print("\n%d ok, %d fallan" % (OK, len(FALLOS)))
    for f in FALLOS:
        print("  - %s" % f)
    return 1 if FALLOS else 0


if __name__ == "__main__":
    sys.exit(main())
