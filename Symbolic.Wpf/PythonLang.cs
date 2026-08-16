using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Snippets;
using Calcpad.Core.Python;

namespace Calcpad.Wpf;

/// <summary>
/// "Language pack" Python para el autocompletado: las funciones REALES del motor
/// (<see cref="PythonBuiltins"/>, extraidas de Symbolic.Core/Python), las palabras clave, y
/// snippets de bloque que se insertan YA SANGRADOS.
/// Mismo patron que MatlabLang.cs en Hekatan Lab: cambiar de lenguaje = escribir otro de estos.
///
/// LO QUE CAMBIA RESPECTO A MATLAB: en Python casi todo se escribe con punto
/// (<c>np.zeros</c>, <c>math.sqrt</c>). Por eso <see cref="Items"/> acepta el CALIFICADOR: si
/// el usuario escribio <c>np.zer</c>, se ofrece lo de numpy, no las 271 del motor.
/// </summary>
internal static class PythonLang
{
    /// <summary>Firma + una linea de ayuda para lo que mas se escribe. El resto sale del motor
    /// con su nombre a secas — no me invento lo que hacen.</summary>
    private static readonly Dictionary<string, string> Ayuda = new(StringComparer.Ordinal)
    {
        ["print"] = "print(x) — escribe en la hoja",
        ["range"] = "range(n) / range(a, b, paso)",
        ["len"] = "len(v) — cuantos elementos hay",
        ["zeros"] = "np.zeros((m, n)) — matriz de ceros",
        ["ones"] = "np.ones((m, n)) — matriz de unos",
        ["eye"] = "np.eye(n) — matriz identidad",
        ["array"] = "np.array([...]) — vector o matriz",
        ["arange"] = "np.arange(a, b, paso)",
        ["linspace"] = "np.linspace(a, b, n) — n valores entre a y b",
        ["reshape"] = "np.reshape(A, (m, n))",
        ["solve"] = "np.linalg.solve(K, F) — resuelve K·x = F",
        ["inv"] = "np.linalg.inv(A) — inversa",
        ["det"] = "np.linalg.det(A) — determinante",
        ["eig"] = "np.linalg.eig(A) — valores y vectores propios",
        ["eigh"] = "np.linalg.eigh(A) — idem, matriz simetrica",
        ["norm"] = "np.linalg.norm(v)",
        ["dot"] = "np.dot(a, b) — producto punto",
        ["spsolve"] = "scipy.sparse.linalg.spsolve(K, F) — disperso",
        ["mesh_viewer"] = "mesh_viewer(nodos, elementos) — malla 2D interactiva",
        ["mesh3d_viewer"] = "mesh3d_viewer(...) — malla 3D orbitable",
        ["solid3d_viewer"] = "solid3d_viewer(...) — solido 3D orbitable",
        ["perf_counter"] = "time.perf_counter() — cronometro (tic/toc)",
    };

    private static readonly (string Trigger, string Doc, Func<Snippet> Build)[] Snippets =
    {
        ("def", "def nombre(args): … (bloque completo)", Def),
        ("class", "class Nombre: … con __init__", Clase),
        ("for", "Bucle for … in range(n)", For),
        ("forenum", "for i, x in enumerate(v)", ForEnum),
        ("if", "Condicion if …", If),
        ("ifelse", "if … else …", IfElse),
        ("while", "Bucle while …", While),
        ("with", "with open(...) as f: …", With),
        ("try", "try … except … ", Try),
        ("main", "if __name__ == '__main__': …", Main),
        ("celda", "Celda #%% (se pliega en el editor; NO sale en la hoja)", Celda),
        ("titulo", "#\" encabezado — SI sale en la hoja", Titulo),
        ("texto", "#' texto markdown — SI sale en la hoja", Texto),
        ("cp", "Bloque Calcpad embebido  #cp[ … #cp]", BloqueCp),
        ("tic", "Bloque tic/toc con time.perf_counter()", TicToc),
    };

    /// <summary>Items del popup, filtrados por lo que ya se escribio.
    ///
    /// <paramref name="calificador"/> es lo que va antes del punto (<c>np</c> en <c>np.zer</c>):
    /// si lo hay, SOLO se ofrece lo de ese modulo.
    /// <paramref name="declarados"/> son las variables, funciones y clases que el usuario
    /// escribio EN SU ARCHIVO, leidas por el motor (<see cref="PythonSymbols"/>). Van PRIMERO:
    /// al escribir, lo que mas se busca es lo propio, no la funcion 200 del motor.
    /// <paramref name="linea"/> es la del cursor: solo se ofrece lo declarado MAS ARRIBA.</summary>
    public static IEnumerable<ICompletionData> Items(
        string prefijo, string calificador = null,
        IReadOnlyList<Simbolo> declarados = null, int linea = int.MaxValue)
    {
        var lista = new List<ICompletionData>();

        if (!string.IsNullOrEmpty(calificador))
        {
            foreach (var f in DelModulo(calificador, declarados))
                lista.Add(new PythonCompletionData(f, Ayuda.TryGetValue(f, out var d) ? d : "del modulo"));
            return Filtrar(lista, prefijo);
        }

        if (declarados is not null)
            AgregarDelUsuario(lista, declarados, linea);

        foreach (var (trigger, doc, build) in Snippets)
            lista.Add(new PythonCompletionData(trigger, doc, snippet: build, priority: 3));

        foreach (var k in PythonBuiltins.Keywords)
            lista.Add(new PythonCompletionData(k, "palabra clave", priority: 2));

        foreach (var c in PythonBuiltins.Constants)
            lista.Add(new PythonCompletionData(c, "constante", priority: 1));

        foreach (var f in PythonBuiltins.Globales)
            lista.Add(new PythonCompletionData(f, Ayuda.TryGetValue(f, out var d) ? d : "funcion del motor"));

        // Las de los modulos tambien, por si se escribe el nombre suelto: la ayuda lleva delante
        // el modulo (`np.zeros`) para que se vea que hay que importarlo.
        foreach (var (mod, nombres) in PythonBuiltins.Modulos)
            foreach (var f in nombres)
                lista.Add(new PythonCompletionData(f, $"{mod}.{f}", priority: -1));

        return Filtrar(lista, prefijo);
    }

    private static IEnumerable<ICompletionData> Filtrar(List<ICompletionData> lista, string prefijo)
    {
        if (string.IsNullOrEmpty(prefijo)) return lista;
        return lista.Where(d => d.Text.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Que ofrecer detras de <c>alias.</c>. El alias sale de los imports del usuario
    /// (<c>import numpy as np</c> → np es numpy); si no hay import, se prueba el nombre tal cual
    /// (<c>math.</c>).</summary>
    private static IEnumerable<string> DelModulo(string calificador, IReadOnlyList<Simbolo> declarados)
    {
        var real = calificador;
        if (declarados is not null)
            foreach (var s in declarados)
                if (s.Tipo == SimboloTipo.Modulo && s.Nombre == calificador && !string.IsNullOrEmpty(s.Firma))
                {
                    real = s.Firma;
                    break;
                }

        if (PythonBuiltins.Modulos.TryGetValue(real, out var nombres)) return nombres;
        // np.linalg. / scipy.sparse. : el submodulo no tiene lista propia, se ofrece la del padre
        return PythonBuiltins.Modulos.TryGetValue("numpy", out var np) && np.Contains(real)
            ? np : Array.Empty<string>();
    }

    // ---------- lo que el usuario declaro en SU archivo ----------

    /// <summary>Lo declarado por el usuario, sin repetir y solo lo que ya existe en esta linea.
    /// Las funciones se ofrecen con su firma (<c>momento(q, L)</c>) para no tener que subir a
    /// mirar los argumentos. Las variables de MAS ABAJO no se ofrecen: todavia no existen ahi;
    /// las funciones y clases si, porque en Python se llaman desde cualquier sitio del modulo
    /// mientras la llamada ocurra despues.</summary>
    private static void AgregarDelUsuario(List<ICompletionData> lista, IReadOnlyList<Simbolo> simbolos, int linea)
    {
        var cVar = Pincel("#3F7A2E");   // variable
        var cFun = Pincel("#1A1A1A");   // funcion y clase (negrita)
        var cPar = Pincel("#8A5A00");   // argumento de def
        var cMod = Pincel("#0B7285");   // modulo importado

        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in simbolos)
        {
            var declaraDespues = s.Tipo is not (SimboloTipo.Funcion or SimboloTipo.Clase);
            if (declaraDespues && s.Linea >= linea) continue;
            if (!vistos.Add(s.Nombre)) continue;

            var (doc, color, negrita) = s.Tipo switch
            {
                SimboloTipo.Funcion => ($"funcion de tu archivo — {s.Firma}", cFun, true),
                SimboloTipo.Clase => ($"clase de tu archivo — {s.Firma}", cFun, true),
                SimboloTipo.Parametro => ("argumento de tu def", cPar, false),
                SimboloTipo.Modulo => ($"modulo importado ({s.Firma})", cMod, false),
                _ => ("variable de tu archivo", cVar, false),
            };
            lista.Add(new PythonCompletionData(s.Nombre, $"{doc} (linea {s.Linea})",
                                               priority: 5, color: color, negrita: negrita));
        }
    }

    private static Brush Pincel(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // ---------- fabricas de snippets ----------
    // En Python el bloque no se cierra con `end`: lo cierra la SANGRIA. Por eso los snippets
    // meten los 4 espacios y dejan el cursor dentro.

    private static SnippetReplaceableTextElement Hueco(string t) => new() { Text = t };
    private static SnippetTextElement Txt(string t) => new() { Text = t };

    private static Snippet Def()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("def "));
        s.Elements.Add(Hueco("nombre"));
        s.Elements.Add(Txt("("));
        s.Elements.Add(Hueco("x"));
        s.Elements.Add(Txt("):\n    "));
        s.Elements.Add(new SnippetCaretElement());
        return s;
    }

    private static Snippet Clase()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("class "));
        s.Elements.Add(Hueco("Nombre"));
        s.Elements.Add(Txt(":\n    def __init__(self, "));
        s.Elements.Add(Hueco("x"));
        s.Elements.Add(Txt("):\n        "));
        s.Elements.Add(new SnippetCaretElement());
        return s;
    }

    private static Snippet For()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("for "));
        s.Elements.Add(Hueco("i"));
        s.Elements.Add(Txt(" in range("));
        s.Elements.Add(Hueco("n"));
        s.Elements.Add(Txt("):\n    "));
        s.Elements.Add(new SnippetCaretElement());
        return s;
    }

    private static Snippet ForEnum()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("for "));
        s.Elements.Add(Hueco("i"));
        s.Elements.Add(Txt(", "));
        s.Elements.Add(Hueco("x"));
        s.Elements.Add(Txt(" in enumerate("));
        s.Elements.Add(Hueco("v"));
        s.Elements.Add(Txt("):\n    "));
        s.Elements.Add(new SnippetCaretElement());
        return s;
    }

    private static Snippet If()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("if "));
        s.Elements.Add(Hueco("cond"));
        s.Elements.Add(Txt(":\n    "));
        s.Elements.Add(new SnippetCaretElement());
        return s;
    }

    private static Snippet IfElse()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("if "));
        s.Elements.Add(Hueco("cond"));
        s.Elements.Add(Txt(":\n    "));
        s.Elements.Add(new SnippetCaretElement());
        s.Elements.Add(Txt("\nelse:\n    pass"));
        return s;
    }

    private static Snippet While()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("while "));
        s.Elements.Add(Hueco("cond"));
        s.Elements.Add(Txt(":\n    "));
        s.Elements.Add(new SnippetCaretElement());
        return s;
    }

    private static Snippet With()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("with open("));
        s.Elements.Add(Hueco("'datos.txt'"));
        s.Elements.Add(Txt(") as "));
        s.Elements.Add(Hueco("f"));
        s.Elements.Add(Txt(":\n    "));
        s.Elements.Add(new SnippetCaretElement());
        return s;
    }

    private static Snippet Try()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("try:\n    "));
        s.Elements.Add(new SnippetCaretElement());
        s.Elements.Add(Txt("\nexcept "));
        s.Elements.Add(Hueco("Exception"));
        s.Elements.Add(Txt(" as e:\n    print(e)"));
        return s;
    }

    private static Snippet Main()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("if __name__ == '__main__':\n    "));
        s.Elements.Add(new SnippetCaretElement());
        return s;
    }

    private static Snippet Celda()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("#%% "));
        s.Elements.Add(Hueco("Titulo de la celda"));
        s.Elements.Add(Txt("\n"));
        s.Elements.Add(new SnippetCaretElement());
        return s;
    }

    private static Snippet Titulo()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("#\""));
        s.Elements.Add(Hueco("Encabezado"));
        s.Elements.Add(Txt("\n"));
        s.Elements.Add(new SnippetCaretElement());
        return s;
    }

    private static Snippet Texto()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("#'"));
        s.Elements.Add(Hueco("texto que sale en la hoja"));
        s.Elements.Add(Txt("\n"));
        s.Elements.Add(new SnippetCaretElement());
        return s;
    }

    private static Snippet BloqueCp()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("#cp[\n#"));
        s.Elements.Add(Hueco("'Formula"));
        s.Elements.Add(Txt("\n#"));
        s.Elements.Add(new SnippetCaretElement());
        s.Elements.Add(Txt("\n#cp]\n"));
        return s;
    }

    private static Snippet TicToc()
    {
        var s = new Snippet();
        s.Elements.Add(Txt("t0 = time.perf_counter()\n"));
        s.Elements.Add(new SnippetCaretElement());
        s.Elements.Add(Txt("\ndt = time.perf_counter() - t0"));
        return s;
    }
}

/// <summary>Un item del popup. Con snippet: borra lo escrito e inserta el bloque con sus
/// huecos; sin snippet: inserta la palabra.</summary>
internal sealed class PythonCompletionData : ICompletionData
{
    private readonly Func<Snippet> _snippet;
    private readonly Brush _color;
    private readonly bool _negrita;

    public PythonCompletionData(string text, string doc, Func<Snippet> snippet = null,
                                double priority = 0, Brush color = null, bool negrita = false)
    {
        Text = text;
        Description = doc;
        _snippet = snippet;
        Priority = priority;
        _color = color;
        _negrita = negrita;
    }

    public ImageSource Image => null;
    public string Text { get; }

    /// <summary>Lo que se VE en la lista. Con color/negrita cuando es algo declarado por el
    /// usuario (mismo codigo de colores que el editor); "▸" marca los snippets.</summary>
    public object Content
    {
        get
        {
            var etiqueta = _snippet is null ? Text : Text + "   ▸";
            if (_color is null && !_negrita) return etiqueta;
            return new System.Windows.Controls.TextBlock
            {
                Text = etiqueta,
                Foreground = _color,
                FontWeight = _negrita ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
            };
        }
    }

    public object Description { get; }
    public double Priority { get; }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs e)
    {
        if (_snippet is not null)
        {
            textArea.Document.Remove(completionSegment.Offset, completionSegment.Length);
            _snippet().Insert(textArea);
        }
        else
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }
}
