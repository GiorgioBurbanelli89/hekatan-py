using System;
using System.Collections.Generic;
using System.Text;

namespace Calcpad.Core.Python
{
    /// <summary>Un bloque plegable: del final de la linea que ABRE al final de la ultima
    /// linea del cuerpo.</summary>
    public readonly record struct FoldSpan(int Start, int End, string Label);

    /// <summary>
    /// Encuentra los bloques de Python (def, class, if, for, while, with, try, celdas
    /// <c>#%%</c>, docstrings) para el +/- del margen del editor.
    ///
    /// POR QUE VIVE EN EL MOTOR y no en la ventana: es analisis de TEXTO puro, sin una sola
    /// referencia al editor. Asi la piel WPF (AvalonEdit), una futura piel Avalonia
    /// (AvaloniaEdit) y el CLI pliegan IDENTICO. Misma receta que MatlabBlocks en Hekatan Lab
    /// y FortranBlocks en Hekatan Fortran.
    ///
    /// LA DIFERENCIA CON MATLAB: alli el bloque lo cierra un <c>end</c>; aqui lo cierra la
    /// SANGRIA. Un bloque va desde la linea que termina en <c>:</c> hasta la ultima linea que
    /// sigue mas adentro. Por eso todo el trabajo esta en saber cual es la sangria de verdad,
    /// y hay tres cosas que la falsean:
    ///   1. Los parentesis abiertos: <c>f(a,</c> ↵ <c>b)</c> es UNA linea logica; lo de la
    ///      segunda linea no es sangria, es continuacion.
    ///   2. Las cadenas triples (<c>"""…"""</c>): dentro no hay codigo, es texto.
    ///   3. La barra invertida al final: tambien continua la linea.
    /// Ademas, un comentario NO cierra bloque: en Python su sangria es libre y es normal
    /// escribir <c># ---</c> pegado al margen dentro de una funcion.
    ///
    /// Se pliegan:
    ///   - bloques por sangria: <c>def / class / if / elif / else / for / while / with /
    ///     try / except / finally</c> (cualquier linea que acabe en <c>:</c>)
    ///   - celdas <c>#%%</c> (estilo Spyder/Jupyter): de una marca a la siguiente
    ///   - cadenas triples de mas de una linea (docstrings)
    ///   - bloques Calcpad embebidos <c>#cp[ … #cp]</c>
    ///   - lineas logicas partidas en varias fisicas (listas y llamadas largas)
    /// </summary>
    public static class PythonBlocks
    {
        /// <summary>Los pliegues del texto, ordenados por donde empiezan.</summary>
        public static List<FoldSpan> Find(string texto)
        {
            var pliegues = new List<FoldSpan>();
            if (string.IsNullOrEmpty(texto)) return pliegues;

            var fin = FinesDeLinea(texto, out var inicios);
            var total = fin.Count;

            var pila = new Stack<Abierto>();          // bloques por sangria abiertos
            int seccion = -1, seccionFin = -1;        // celda #%% abierta
            int cpBloque = -1;                        // #cp[ abierto
            int ultimaConTexto = 0;                   // ultima linea NO vacia (cierra los bloques)

            var estado = new Estado();
            var n = 1;
            while (n <= total)
            {
                // ── una LINEA LOGICA: la fisica n mas las que arrastren parentesis,
                //    cadena triple o barra invertida ──
                var primera = n;
                var tripleAbre = -1;
                var sb = new StringBuilder();
                while (n <= total)
                {
                    var linea = texto[inicios[n - 1]..fin[n - 1]];
                    var antesTriple = estado.Triple;
                    sb.Append(Escanear(linea, estado));
                    // una cadena triple que ARRANCA en esta linea: si no cierra aqui, es
                    // docstring de varias lineas y se pliega aparte
                    if (antesTriple is null && estado.Triple is not null && tripleAbre < 0)
                        tripleAbre = n;
                    else if (antesTriple is not null && estado.Triple is null && tripleAbre >= 0)
                    {
                        if (n > tripleAbre) Anadir(pliegues, fin, tripleAbre, n);
                        tripleAbre = -1;
                    }

                    if (estado.Triple is not null || estado.Nivel > 0 || estado.Continua) { n++; continue; }
                    break;
                }
                var ultima = Math.Min(n, total);
                n = ultima + 1;

                var cruda = texto[inicios[primera - 1]..fin[primera - 1]];
                var recortado = cruda.Trim();
                var codigo = sb.ToString().TrimEnd();

                // una linea logica partida en varias fisicas tambien se pliega
                if (ultima > primera) Anadir(pliegues, fin, primera, ultima);

                // ── bloque Calcpad embebido  #cp[ … #cp] ──
                if (cpBloque >= 0)
                {
                    if (recortado.StartsWith("#cp]", StringComparison.Ordinal))
                    {
                        Anadir(pliegues, fin, cpBloque, ultima);
                        cpBloque = -1;
                    }
                    continue;
                }
                if (recortado.StartsWith("#cp[", StringComparison.Ordinal)) { cpBloque = primera; continue; }

                // ── celda #%% (estilo Spyder): de una marca a la siguiente ──
                if (EsCelda(recortado))
                {
                    if (seccion >= 0 && seccionFin > seccion) Anadir(pliegues, fin, seccion, seccionFin);
                    seccion = primera;
                    seccionFin = primera;
                    continue;
                }
                if (recortado.Length > 0) seccionFin = ultima;

                var sangria = Sangria(cruda);

                // Vacia o solo comentario: no toca la sangria. Un comentario SI cuenta como
                // parte del bloque —para que al plegar no quede colgando— pero solo si esta
                // mas adentro; en Python es normal escribir "# ---" pegado al margen.
                if (codigo.Trim().Length == 0)
                {
                    if (recortado.Length > 0 && pila.Count > 0 && sangria > pila.Peek().Sangria)
                        ultimaConTexto = ultima;
                    continue;
                }

                // ── sangria: cierra lo que quede mas afuera, abre lo que termine en ':' ──
                // Se cierra con la ultima linea que YA estaba dentro, no con esta: esta es la
                // que saca del bloque (el `def` siguiente no es parte del `def` anterior).
                while (pila.Count > 0 && sangria <= pila.Peek().Sangria)
                    Cerrar(pliegues, fin, pila.Pop(), ultimaConTexto);

                ultimaConTexto = ultima;

                if (codigo.EndsWith(':'))
                    pila.Push(new Abierto(sangria, ultima, fin[ultima - 1]));
            }

            // lo que quedo abierto al final del archivo (hasta la ultima linea con codigo: las
            // lineas en blanco del final no son parte de la funcion)
            while (pila.Count > 0) Cerrar(pliegues, fin, pila.Pop(), ultimaConTexto);
            if (seccion >= 0 && seccionFin > seccion) Anadir(pliegues, fin, seccion, seccionFin);
            if (cpBloque >= 0) Anadir(pliegues, fin, cpBloque, total);

            // El editor exige los pliegues ordenados por donde empiezan, y NO admite dos que
            // empiecen en el mismo sitio: un docstring que ocupa el todo de su linea logica sale
            // dos veces (como cadena triple y como linea partida). Se queda el mas largo.
            pliegues.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.End.CompareTo(a.End));
            var unicos = new List<FoldSpan>(pliegues.Count);
            foreach (var p in pliegues)
                if (unicos.Count == 0 || unicos[^1].Start != p.Start) unicos.Add(p);
            return unicos;
        }

        // ---------- estado del escaneo ----------

        /// <summary>Lo que sobrevive de una linea fisica a la siguiente: la cadena triple
        /// abierta, los parentesis sin cerrar y la barra invertida final. Publica porque el
        /// coloreado del editor tambien recorre el archivo linea a linea.</summary>
        public sealed class Estado
        {
            /// <summary><c>"""</c> o <c>'''</c> mientras la cadena triple sigue abierta.</summary>
            public string Triple;
            /// <summary>Cuantos <c>( [ {</c> quedaron sin cerrar.</summary>
            public int Nivel;
            /// <summary>La linea acabo en barra invertida.</summary>
            public bool Continua;
        }

        private readonly record struct Abierto(int Sangria, int Linea, int FinDeLinea);

        /// <summary>Una celda de codigo: <c>#%%</c> o <c># %%</c>.</summary>
        private static bool EsCelda(string recortado) =>
            recortado.StartsWith("#%%", StringComparison.Ordinal) ||
            recortado.StartsWith("# %%", StringComparison.Ordinal);

        /// <summary>Columnas de sangria. El tabulador cuenta 4, como el editor.</summary>
        private static int Sangria(string linea)
        {
            var s = 0;
            foreach (var c in linea)
            {
                if (c == ' ') s++;
                else if (c == '\t') s += 4;
                else break;
            }
            return s;
        }

        /// <summary>El codigo de UNA linea suelta, sin arrastre entre lineas: sirve cuando no
        /// importa que esa linea vaya dentro de un docstring.</summary>
        public static string SoloCodigo(string linea) => Escanear(linea, new Estado());

        /// <summary>
        /// Devuelve la linea sin comentario ni contenido de cadenas —lo unico que hace falta
        /// para saber si termina en <c>:</c>— y deja en <paramref name="e"/> lo que sigue
        /// abierto al pasar a la linea siguiente.
        /// </summary>
        public static string Escanear(string linea, Estado e)
        {
            var sb = new StringBuilder(linea.Length);
            var i = 0;
            e.Continua = false;

            while (i < linea.Length)
            {
                // dentro de una cadena triple: solo se busca el cierre
                if (e.Triple is not null)
                {
                    var cierre = linea.IndexOf(e.Triple, i, StringComparison.Ordinal);
                    if (cierre < 0) return sb.ToString();
                    i = cierre + 3;
                    e.Triple = null;
                    sb.Append("''");
                    continue;
                }

                var c = linea[i];

                if (c == '#') break;                          // comentario hasta fin de linea

                if (c is '"' or '\'')
                {
                    // ¿triple?  """ o '''
                    if (i + 2 < linea.Length && linea[i + 1] == c && linea[i + 2] == c)
                    {
                        var marca = new string(c, 3);
                        var cierre = linea.IndexOf(marca, i + 3, StringComparison.Ordinal);
                        if (cierre < 0) { e.Triple = marca; sb.Append("''"); return sb.ToString(); }
                        i = cierre + 3;
                        sb.Append("''");
                        continue;
                    }
                    // cadena normal: la barra invertida escapa la comilla
                    i++;
                    while (i < linea.Length)
                    {
                        if (linea[i] == '\\') { i += 2; continue; }
                        if (linea[i] == c) { i++; break; }
                        i++;
                    }
                    sb.Append("''");
                    continue;
                }

                if (c is '(' or '[' or '{') e.Nivel++;
                else if (c is ')' or ']' or '}') { if (e.Nivel > 0) e.Nivel--; }
                else if (c == '\\' && i == linea.Length - 1) { e.Continua = true; i++; continue; }

                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        // ---------- ayudas ----------

        /// <summary>Fin de cada linea SIN el salto (\r\n o \n), y donde empieza cada una.
        /// Son los mismos offsets que usa el editor, por eso se calculan aqui una sola vez.</summary>
        private static List<int> FinesDeLinea(string texto, out List<int> inicios)
        {
            var fines = new List<int>();
            inicios = new List<int>();
            var pos = 0;
            while (true)
            {
                var salto = texto.IndexOf('\n', pos);
                var fin = salto < 0 ? texto.Length : salto;
                inicios.Add(pos);
                fines.Add(fin > pos && texto[fin - 1] == '\r' ? fin - 1 : fin);
                if (salto < 0) break;
                pos = salto + 1;
            }
            return fines;
        }

        private static string Etiqueta(int lineas) =>
            lineas == 1 ? " ⋯ 1 línea" : $" ⋯ {lineas} líneas";

        private static void Anadir(List<FoldSpan> pliegues, List<int> fin, int desde, int hasta)
        {
            if (hasta <= desde) return;
            int a = fin[desde - 1], b = fin[hasta - 1];
            if (b > a) pliegues.Add(new FoldSpan(a, b, Etiqueta(hasta - desde)));
        }

        private static void Cerrar(List<FoldSpan> pliegues, List<int> fin, Abierto abierto, int lineaFinal)
        {
            if (lineaFinal <= abierto.Linea) return;
            var b = fin[lineaFinal - 1];
            if (b > abierto.FinDeLinea)
                pliegues.Add(new FoldSpan(abierto.FinDeLinea, b, Etiqueta(lineaFinal - abierto.Linea)));
        }
    }
}
