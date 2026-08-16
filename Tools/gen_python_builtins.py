# -*- coding: utf-8 -*-
"""
Genera Symbolic.Wpf/PythonBuiltins.g.cs y la lista de <Word> de Python.xshd
LEYENDO EL MOTOR (Symbolic.Core/Python/*.cs).

Por que un script y no una lista a mano: los nombres que el editor colorea y
autocompleta tienen que ser los que el motor RECONOCE DE VERDAD. Si el motor gana
funciones, se vuelve a correr esto y no hay que inventar nada.

    python Tools/gen_python_builtins.py

(En Hekatan Lab el gen_xshd.py se perdio y el .xshd quedo a mano. Aqui se guarda.)
"""
import re, pathlib, sys

RAIZ = pathlib.Path(__file__).resolve().parent.parent
CORE = RAIZ / "Symbolic.Core" / "Python"
SALIDA_CS = RAIZ / "Symbolic.Wpf" / "PythonBuiltins.g.cs"
SALIDA_XSHD = RAIZ / "Symbolic.Wpf" / "Python.xshd"

# Plantilla del resaltado. El ORDEN de los <Span> manda: AvalonEdit prueba los Span antes que
# las Rule y se queda con el primero que engancha. Por eso las cadenas van ARRIBA del todo:
# si no, un '#' dentro de un docstring abriria comentario y se veria media cadena verde
# (el mismo tropiezo que en Hekatan Lab con el % dentro de comillas).
XSHD = (pathlib.Path(__file__).resolve().parent / "python.xshd.tmpl").read_text(encoding="utf-8")

# como registra nombres el motor: Reg("x"), Fn("x"), m.Attrs["x"], new PyBuiltin("x"
PATRONES = [
    re.compile(r'\bReg\("([A-Za-z_][A-Za-z0-9_]*)"'),
    re.compile(r'\bFn2?\("([A-Za-z_][A-Za-z0-9_]*)"'),
    re.compile(r'\.Attrs\["([A-Za-z_][A-Za-z0-9_]*)"\]'),
    re.compile(r'new PyBuiltin\("([A-Za-z_][A-Za-z0-9_.]*)"'),
]

# keywords REALES: las que el parser del motor entiende (PythonParser.cs)
KEYWORDS = ["and", "as", "assert", "async", "await", "break", "class", "continue",
            "def", "del", "elif", "else", "except", "finally", "for", "from",
            "global", "if", "import", "in", "is", "lambda", "nonlocal", "not",
            "or", "pass", "raise", "return", "try", "while", "with", "yield"]
CONSTANTES = ["True", "False", "None", "NotImplemented", "Ellipsis", "self"]

# de que archivos sale cada modulo
MODULOS = {
    "numpy":       ["PythonNumpy.cs"],
    "math":        ["PythonMath.cs"],
    "scipy":       ["PythonScipy.cs", "PythonScipySignal.cs", "PythonScipyIO.cs", "PythonFFT.cs"],
    "os":          [("PythonStdlib.cs", "Os")],
    "sys":         [("PythonStdlib.cs", "Sys")],
    "time":        [("PythonStdlib.cs", "Time")],
    "collections": [("PythonEvaluator.cs", "BuildCollections")],
}
GLOBALES_DE = "PythonEvaluator.cs"


def texto(archivo, funcion=None):
    """El archivo entero, o solo el cuerpo de una funcion (hasta la siguiente
    declaracion al mismo nivel de sangria)."""
    src = (CORE / archivo).read_text(encoding="utf-8", errors="replace")
    if funcion is None:
        return src
    # la DECLARACION, no una llamada: `_x ??= BuildCollections();` aparece antes en el archivo
    d = re.search(r'\b(?:private|public|internal|protected|static|\s)+\w[\w<>\[\], .]*\s'
                  + re.escape(funcion) + r'\s*\(', src)
    if d is None:
        sys.exit(f"no encontre la declaracion de {funcion} en {archivo}")
    i = d.start()
    # del cuerpo: desde la primera { hasta que se cierra
    j = src.find("{", i)
    nivel, k = 0, j
    while k < len(src):
        if src[k] == "{":
            nivel += 1
        elif src[k] == "}":
            nivel -= 1
            if nivel == 0:
                break
        k += 1
    return src[j:k]


def nombres(src):
    salida = set()
    for p in PATRONES:
        for m in p.finditer(src):
            n = m.group(1).split(".")[-1]      # "math.sqrt" -> "sqrt"
            if n and not n.startswith("__"):
                salida.add(n)
    return salida


def cs_lista(items, sangria="        "):
    """Una lista de strings de C#, cortada a ~110 columnas."""
    lineas, actual = [], sangria
    for it in items:
        trozo = f'"{it}", '
        if len(actual) + len(trozo) > 112:
            lineas.append(actual.rstrip())
            actual = sangria
        actual += trozo
    if actual.strip():
        lineas.append(actual.rstrip())
    return "\n".join(lineas)


def main():
    globales = sorted(nombres(texto(GLOBALES_DE)) - set(KEYWORDS) - set(CONSTANTES))

    mods = {}
    for mod, fuentes in MODULOS.items():
        s = set()
        for f in fuentes:
            s |= nombres(texto(*f) if isinstance(f, tuple) else texto(f))
        mods[mod] = sorted(s)

    # los globales del evaluador incluyen los de numpy que tambien viven sueltos
    todos = sorted(set(globales) | {n for v in mods.values() for n in v})

    cs = ['// GENERADO por Tools/gen_python_builtins.py — no editar a mano.',
          '// Origen: los nombres REALES que registra el motor en Symbolic.Core/Python/*.cs',
          '//   Reg("x") · Fn("x") · .Attrs["x"] · new PyBuiltin("x")',
          '// Regenerar cuando el motor gane funciones.',
          '',
          'using System.Collections.Generic;',
          '',
          'namespace Calcpad.Wpf;',
          '',
          'internal static class PythonBuiltins',
          '{',
          f'    /// <summary>Las {len(globales)} funciones que el motor ofrece SIN importar nada'
          ' (print, len, range...).</summary>',
          '    internal static readonly string[] Globales =',
          '    {',
          cs_lista(globales),
          '    };',
          '']

    for mod, items in mods.items():
        cs += [f'    /// <summary>Lo que expone <c>{mod}</c> ({len(items)} nombres).</summary>',
               f'    private static readonly string[] _{mod} =',
               '    {',
               cs_lista(items),
               '    };',
               '']

    cs += ['    /// <summary>Modulo → lo que expone. Para autocompletar despues del punto'
           ' (<c>np.zer</c> → <c>zeros</c>).</summary>',
           '    internal static readonly Dictionary<string, string[]> Modulos = new()',
           '    {']
    for mod in mods:
        cs.append(f'        ["{mod}"] = _{mod},')
    cs += ['    };', '',
           f'    /// <summary>Todos los nombres del motor ({len(todos)}), sin repetir: es lo que'
           ' colorea el editor.</summary>',
           '    internal static readonly string[] All =',
           '    {',
           cs_lista(todos),
           '    };',
           '',
           '    internal static readonly string[] Keywords =',
           '    {',
           cs_lista(KEYWORDS),
           '    };',
           '',
           '    internal static readonly string[] Constants =',
           '    {',
           cs_lista(CONSTANTES),
           '    };',
           '}',
           '']
    SALIDA_CS.write_text("\n".join(cs), encoding="utf-8")

    # el .xshd completo: plantilla + las mismas listas
    def words(items, por_fila=8):
        filas = []
        for i in range(0, len(items), por_fila):
            filas.append("      " + "".join(f"<Word>{n}</Word>" for n in items[i:i + por_fila]))
        return "\n".join(filas)

    xshd = (XSHD.replace("{KEYWORDS}", words(KEYWORDS))
                .replace("{CONSTANTES}", words(CONSTANTES))
                .replace("{BUILTINS}", words(todos)))
    SALIDA_XSHD.write_text(xshd, encoding="utf-8")

    print(f"{SALIDA_CS.name}: {len(globales)} globales, {len(todos)} en total")
    for mod, items in mods.items():
        print(f"   {mod:12s} {len(items)}")


if __name__ == "__main__":
    main()
