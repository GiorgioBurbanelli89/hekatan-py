using System;
using System.Collections.Generic;

namespace Calcpad.Core.Python
{
    /// <summary>Que es cada nombre encontrado en el codigo del usuario.</summary>
    public enum SimboloTipo
    {
        /// <summary>Variable asignada: <c>x = ...</c>, <c>for i in ...</c>, <c>with ... as f</c>.</summary>
        Variable,
        /// <summary>Funcion declarada con <c>def</c>.</summary>
        Funcion,
        /// <summary>Argumento de un <c>def</c> (existe dentro de el).</summary>
        Parametro,
        /// <summary>Clase declarada con <c>class</c>.</summary>
        Clase,
        /// <summary>Modulo importado: <c>import numpy as np</c> → <c>np</c>.</summary>
        Modulo,
    }

    /// <summary>Un nombre declarado por el usuario y donde se declaro.
    /// <paramref name="Firma"/> solo la llevan funciones y clases: <c>momento(q, L)</c>.</summary>
    public readonly record struct Simbolo(string Nombre, int Linea, SimboloTipo Tipo, string Firma = null);

    /// <summary>
    /// Lee el codigo y devuelve QUE declaro el usuario y en que linea: variables, funciones,
    /// clases, argumentos y modulos importados. Lo usan el autocompletado y el coloreado del
    /// editor plegable.
    ///
    /// POR QUE EXISTE: el <c>UserDefined</c> de Calcpad es el lector de CALCPAD, donde un
    /// <c>;</c> final significa "la linea sigue" y un <c>#</c> es una directiva. En Python el
    /// <c>#</c> es comentario y cada linea es una sentencia, asi que veia mal casi todo.
    ///
    /// Como <see cref="PythonBlocks"/>, vive en el MOTOR y es texto puro: la piel WPF, una
    /// piel Avalonia o el CLI ven los mismos simbolos.
    ///
    /// Lo que reconoce:
    ///   - <c>x = ...</c>, <c>x, y = ...</c>, <c>x: float = ...</c>, <c>x += ...</c>
    ///   - <c>def momento(q, L=1):</c> — la funcion, su firma y sus argumentos
    ///   - <c>class Viga(Barra):</c>
    ///   - <c>for i, j in ...:</c>, <c>with open(f) as fh:</c>, <c>except E as e:</c>
    ///   - <c>import numpy as np</c>, <c>from math import pi, sqrt as raiz</c>
    ///   - <c>global a</c>, <c>nonlocal b</c>, y el morsa <c>n := len(x)</c>
    /// Lo que NO cuenta como asignacion: <c>==</c>, <c>!=</c>, <c>&lt;=</c>, <c>&gt;=</c>, y
    /// cualquier <c>=</c> dentro de parentesis (es un argumento con nombre, no una variable).
    /// </summary>
    public static class PythonSymbols
    {
        /// <summary>Palabras del lenguaje que nunca son un nombre del usuario.</summary>
        private static readonly HashSet<string> Reservadas = new(StringComparer.Ordinal)
        {
            "and", "as", "assert", "async", "await", "break", "class", "continue", "def",
            "del", "elif", "else", "except", "finally", "for", "from", "global", "if",
            "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise",
            "return", "try", "while", "with", "yield", "True", "False", "None",
        };

        public static List<Simbolo> Find(string texto)
        {
            var simbolos = new List<Simbolo>();
            if (string.IsNullOrEmpty(texto)) return simbolos;

            var estado = new PythonBlocks.Estado();
            var n = 0;
            foreach (var lineaCruda in texto.Split('\n'))
            {
                ++n;
                var enCadena = estado.Triple is not null;
                // Fuera comentarios y contenido de cadenas: se reusa el mismo escaneo que el
                // plegado, que ya arrastra docstrings y parentesis abiertos.
                var codigo = PythonBlocks.Escanear(lineaCruda.TrimEnd('\r'), estado).Trim();
                if (enCadena || codigo.Length == 0) continue;

                var palabra = PrimeraPalabra(codigo);

                switch (palabra)
                {
                    case "def": Definicion(simbolos, codigo, n, SimboloTipo.Funcion); continue;
                    case "class": Definicion(simbolos, codigo, n, SimboloTipo.Clase); continue;
                    case "async": Definicion(simbolos, QuitarPrimeraPalabra(codigo), n, SimboloTipo.Funcion); continue;
                    case "import": Importar(simbolos, QuitarPrimeraPalabra(codigo), n); continue;
                    case "from": DesdeImportar(simbolos, codigo, n); continue;
                    case "global":
                    case "nonlocal": Declaradas(simbolos, QuitarPrimeraPalabra(codigo), n); continue;
                    case "for": codigo = HastaIn(QuitarPrimeraPalabra(codigo)); break;
                    case "with": ComoAlias(simbolos, codigo, n); continue;
                    case "except": ComoAlias(simbolos, codigo, n); continue;
                    default:
                        if (Reservadas.Contains(palabra)) continue;   // if / while / return…: no asignan
                        break;
                }

                Morsa(simbolos, codigo, n);
                Asignacion(simbolos, codigo, n);
            }
            return simbolos;
        }

        /// <summary>Las lineas que son TEXTO, no codigo: las de dentro de un docstring de varias
        /// lineas. El coloreado del editor las salta (si no, pintaria los nombres del texto).</summary>
        public static HashSet<int> LineasDeCadena(string texto)
        {
            var salida = new HashSet<int>();
            if (string.IsNullOrEmpty(texto)) return salida;

            var estado = new PythonBlocks.Estado();
            var n = 0;
            foreach (var linea in texto.Split('\n'))
            {
                ++n;
                var dentro = estado.Triple is not null;
                PythonBlocks.Escanear(linea.TrimEnd('\r'), estado);
                if (dentro) salida.Add(n);
            }
            return salida;
        }

        /// <summary>Un nombre encontrado en una linea, con DONDE esta. Lo usa el editor para
        /// pintar; por eso hacen falta las posiciones exactas (<see cref="PythonBlocks.SoloCodigo"/>
        /// no sirve: al vaciar las cadenas cambia las longitudes).</summary>
        public readonly record struct Palabra(int Inicio, int Largo, string Nombre, bool EsLlamada);

        /// <summary>Los identificadores de una linea que estan en CODIGO: se salta el comentario
        /// <c>#</c> y las cadenas (incluidas las <c>f"…"</c>, donde la <c>f</c> es prefijo, no un
        /// nombre). <c>EsLlamada</c> = le sigue un <c>(</c>, o sea se usa como funcion.</summary>
        public static List<Palabra> Identificadores(string linea)
        {
            var salida = new List<Palabra>();
            if (string.IsNullOrEmpty(linea)) return salida;

            for (var i = 0; i < linea.Length; i++)
            {
                var c = linea[i];

                if (c == '#') break;                                   // comentario

                if (c is '"' or '\'') { i = FinDeCadena(linea, i); continue; }

                if (char.IsLetter(c) || c == '_')
                {
                    var j = i;
                    while (j < linea.Length && (char.IsLetterOrDigit(linea[j]) || linea[j] == '_')) j++;

                    // f"…", r'…', rb"…": la letra pegada a la comilla es PREFIJO de cadena
                    if (j < linea.Length && linea[j] is '"' or '\'' && j - i <= 2)
                    {
                        i = FinDeCadena(linea, j);
                        continue;
                    }

                    var k = j;
                    while (k < linea.Length && linea[k] == ' ') k++;
                    salida.Add(new Palabra(i, j - i, linea[i..j], k < linea.Length && linea[k] == '('));
                    i = j - 1;
                }
            }
            return salida;
        }

        /// <summary>Indice del ultimo caracter de la cadena que empieza en <paramref name="i"/>
        /// (triple o normal). Si no cierra en la linea, el final de la linea.</summary>
        private static int FinDeCadena(string linea, int i)
        {
            var c = linea[i];
            if (i + 2 < linea.Length && linea[i + 1] == c && linea[i + 2] == c)
            {
                var cierre = linea.IndexOf(new string(c, 3), i + 3, StringComparison.Ordinal);
                return cierre < 0 ? linea.Length - 1 : cierre + 2;
            }
            for (var j = i + 1; j < linea.Length; j++)
            {
                if (linea[j] == '\\') { j++; continue; }
                if (linea[j] == c) return j;
            }
            return linea.Length - 1;
        }

        // ---------- casos ----------

        /// <summary><c>def momento(q, L=1):</c> → la funcion con su firma y sus argumentos como
        /// nombres vivos dentro de ella. <c>class Viga(Barra):</c> → la clase (las bases NO son
        /// argumentos, asi que ahi no se sacan nombres).</summary>
        private static void Definicion(List<Simbolo> salida, string codigo, int linea, SimboloTipo tipo)
        {
            var resto = QuitarPrimeraPalabra(codigo).TrimEnd(':').Trim();
            var par = resto.IndexOf('(');
            var nombre = (par < 0 ? resto : resto[..par]).Trim();
            if (nombre.Length == 0 || !EsIdentificador(nombre)) return;

            var firma = par < 0 ? nombre + "()" : resto;
            salida.Add(new Simbolo(nombre, linea, tipo, firma));

            if (tipo != SimboloTipo.Funcion || par < 0) return;
            var cierre = resto.LastIndexOf(')');
            if (cierre <= par) return;
            foreach (var arg in Partir(resto[(par + 1)..cierre]))
            {
                // quitar valor por defecto y anotacion: `n: int = 10` → `n`
                var p = arg;
                var corte = p.IndexOfAny(['=', ':']);
                if (corte >= 0) p = p[..corte];
                p = p.Trim().TrimStart('*');
                if (EsIdentificador(p) && !Reservadas.Contains(p))
                    salida.Add(new Simbolo(p, linea, SimboloTipo.Parametro));
            }
        }

        /// <summary><c>import numpy as np, math</c> → np (alias) y math. La FIRMA guarda el
        /// modulo de verdad, que es lo que hace falta para saber que ofrecer tras el punto:
        /// con <c>np</c> a secas no se sabe que <c>np.</c> es numpy.</summary>
        private static void Importar(List<Simbolo> salida, string resto, int linea)
        {
            foreach (var trozo in Partir(resto))
            {
                var partes = trozo.Split(" as ", StringSplitOptions.RemoveEmptyEntries);
                var real = partes[0].Trim().Split('.')[0].Trim();       // `import os.path` → os
                var nombre = partes.Length > 1 ? partes[1].Trim() : real;
                if (EsIdentificador(nombre))
                    salida.Add(new Simbolo(nombre, linea, SimboloTipo.Modulo, real));
            }
        }

        /// <summary><c>from math import pi, sqrt as raiz</c> → pi y raiz (no math).</summary>
        private static void DesdeImportar(List<Simbolo> salida, string codigo, int linea)
        {
            var i = codigo.IndexOf(" import ", StringComparison.Ordinal);
            if (i < 0) return;
            var resto = codigo[(i + 8)..].Trim().Trim('(', ')');
            if (resto == "*") return;
            foreach (var trozo in Partir(resto))
            {
                var partes = trozo.Split(" as ", StringSplitOptions.RemoveEmptyEntries);
                var nombre = (partes.Length > 1 ? partes[1] : partes[0]).Trim();
                if (EsIdentificador(nombre)) salida.Add(new Simbolo(nombre, linea, SimboloTipo.Variable));
            }
        }

        /// <summary><c>global a, b</c> — separadas por comas.</summary>
        private static void Declaradas(List<Simbolo> salida, string resto, int linea)
        {
            foreach (var s in Nombres(resto))
                salida.Add(new Simbolo(s, linea, SimboloTipo.Variable));
        }

        /// <summary><c>with open(f) as fh:</c> y <c>except ValueError as e:</c> → el nombre
        /// que va detras de <c>as</c>.</summary>
        private static void ComoAlias(List<Simbolo> salida, string codigo, int linea)
        {
            var resto = codigo.TrimEnd(':');
            var i = resto.IndexOf(" as ", StringComparison.Ordinal);
            while (i >= 0)
            {
                var trozo = resto[(i + 4)..];
                var coma = trozo.IndexOf(',');
                if (coma >= 0) trozo = trozo[..coma];
                foreach (var s in Nombres(trozo))
                    salida.Add(new Simbolo(s, linea, SimboloTipo.Variable));
                i = resto.IndexOf(" as ", i + 4, StringComparison.Ordinal);
            }
        }

        /// <summary><c>x = …</c>, <c>x, y = …</c>, <c>x: float = …</c>, <c>x += …</c>.</summary>
        private static void Asignacion(List<Simbolo> salida, string codigo, int linea)
        {
            var igual = IgualDeAsignacion(codigo);
            if (igual < 0) return;
            var izquierda = codigo[..igual].TrimEnd('+', '-', '*', '/', '%', '@', '&', '|', '^', '>', '<');
            var anotacion = ColonDeAnotacion(izquierda);           // `x: float = 0` → `x`
            if (anotacion >= 0) izquierda = izquierda[..anotacion];
            foreach (var s in Nombres(izquierda))
                salida.Add(new Simbolo(s, linea, SimboloTipo.Variable));
        }

        /// <summary>El operador morsa: <c>if (n := len(v)) > 3:</c> declara <c>n</c>.</summary>
        private static void Morsa(List<Simbolo> salida, string codigo, int linea)
        {
            var i = codigo.IndexOf(":=", StringComparison.Ordinal);
            while (i > 0)
            {
                var j = i - 1;
                while (j >= 0 && codigo[j] == ' ') j--;
                var fin = j + 1;
                while (j >= 0 && (char.IsLetterOrDigit(codigo[j]) || codigo[j] == '_')) j--;
                var nombre = codigo[(j + 1)..fin];
                if (EsIdentificador(nombre) && !Reservadas.Contains(nombre))
                    salida.Add(new Simbolo(nombre, linea, SimboloTipo.Variable));
                i = codigo.IndexOf(":=", i + 2, StringComparison.Ordinal);
            }
        }

        // ---------- ayudas ----------

        /// <summary>De <c>for i, j in enumerate(v):</c> deja <c>i, j</c>.</summary>
        private static string HastaIn(string resto)
        {
            var i = resto.IndexOf(" in ", StringComparison.Ordinal);
            return i < 0 ? resto.TrimEnd(':') : resto[..i];
        }

        /// <summary>Posicion del <c>=</c> que ASIGNA: el primero de nivel 0 que no forma parte de
        /// <c>==</c>, <c>!=</c>, <c>&lt;=</c>, <c>&gt;=</c> ni del morsa <c>:=</c>. -1 si no hay.
        /// El <c>=</c> de dentro de parentesis es un argumento con nombre, no una variable.</summary>
        private static int IgualDeAsignacion(string codigo)
        {
            var nivel = 0;
            for (var i = 0; i < codigo.Length; i++)
            {
                var c = codigo[i];
                if (c is '(' or '[' or '{') nivel++;
                else if (c is ')' or ']' or '}') { if (nivel > 0) nivel--; }
                else if (c == '=' && nivel == 0)
                {
                    if (i + 1 < codigo.Length && codigo[i + 1] == '=') { i++; continue; }   // ==
                    if (i > 0 && codigo[i - 1] is '=' or '!' or '<' or '>' or ':') continue;
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Los <c>:</c> de una anotacion de tipo (nivel 0). -1 si no hay.</summary>
        private static int ColonDeAnotacion(string izquierda)
        {
            var nivel = 0;
            for (var i = 0; i < izquierda.Length; i++)
            {
                var c = izquierda[i];
                if (c is '(' or '[' or '{') nivel++;
                else if (c is ')' or ']' or '}') { if (nivel > 0) nivel--; }
                else if (c == ':' && nivel == 0) return i;
            }
            return -1;
        }

        /// <summary>Trocea por comas de NIVEL 0: <c>a, f(x, y), b</c> → <c>a</c>, <c>f(x, y)</c>,
        /// <c>b</c>. Partir a secas romperia los argumentos con coma dentro.</summary>
        private static IEnumerable<string> Partir(string trozo)
        {
            var nivel = 0;
            var inicio = 0;
            for (var i = 0; i < trozo.Length; i++)
            {
                var c = trozo[i];
                if (c is '(' or '[' or '{') nivel++;
                else if (c is ')' or ']' or '}') { if (nivel > 0) nivel--; }
                else if (c == ',' && nivel == 0)
                {
                    if (i > inicio) yield return trozo[inicio..i].Trim();
                    inicio = i + 1;
                }
            }
            if (inicio < trozo.Length) yield return trozo[inicio..].Trim();
        }

        /// <summary>Los nombres de un lado izquierdo: <c>a, b</c>, <c>x</c>, <c>v[3]</c> (queda
        /// <c>v</c>), <c>s.campo</c> (se descarta: el nombre nuevo no es <c>s</c>), <c>_</c>.</summary>
        private static IEnumerable<string> Nombres(string trozo)
        {
            trozo = trozo.Trim();
            if (trozo.StartsWith('(') && trozo.EndsWith(')') ||
                trozo.StartsWith('[') && trozo.EndsWith(']'))
                trozo = trozo[1..^1];

            foreach (var parte in Partir(trozo))
            {
                var p = parte.Trim().TrimStart('*');
                if (p.Contains('.')) continue;                 // s.campo: `s` ya existia
                var corte = p.IndexOfAny(['(', '[', '{']);
                if (corte >= 0) p = p[..corte];                // v[3] = … : la variable es v
                p = p.Trim();
                if (EsIdentificador(p) && !Reservadas.Contains(p)) yield return p;
            }
        }

        private static bool EsIdentificador(string s)
        {
            if (s.Length == 0 || !(char.IsLetter(s[0]) || s[0] == '_')) return false;
            foreach (var c in s)
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        private static string PrimeraPalabra(string codigo)
        {
            var i = 0;
            while (i < codigo.Length && (char.IsLetterOrDigit(codigo[i]) || codigo[i] == '_')) i++;
            return codigo[..i];
        }

        private static string QuitarPrimeraPalabra(string codigo) =>
            codigo[PrimeraPalabra(codigo).Length..].Trim();
    }
}
