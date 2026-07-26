// =============================================================================
// Hekatan Python3 — Python Tokenizer (indentation-aware)
// =============================================================================
//   Convierte código Python en tokens, emitiendo INDENT/DEDENT como hace
//   CPython. Maneja:
//     - Indentación significativa (stack de niveles).
//     - Continuación implícita dentro de () [] {} y explícita con `\`.
//     - Strings normales / raw / f-strings / triple-quoted.
//     - Números int/float/hex/bin/oct/scientific con `_` separador.
//     - Comentarios `#...`.
//   No usa el tokenizer de Calcpad ni el de MATLAB.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Calcpad.Core.Python
{
    public enum PyTok
    {
        Number, String, Name, Op, Newline, Indent, Dedent, Comment, EndMarker
    }

    public sealed class PyToken
    {
        public PyTok Type;
        public string Text;        // texto crudo del token (o el operador)
        public double Num;         // valor numérico (Type == Number)
        public bool IsInt;         // entero literal
        public string Str;         // valor decodificado (Type == String, no f-string)
        public bool IsFString;     // prefijo f / rf
        public bool IsRaw;         // prefijo r
        public bool IsBytes;       // prefijo b
        public bool IsComplex;     // sufijo j
        public int Line;
        public int Col;
        public override string ToString() => $"{Type}:{Text}";
    }

    public sealed class PythonTokenizeException : Exception
    {
        public int Line;
        public PythonTokenizeException(string msg, int line) : base(msg) { Line = line; }
    }

    public static class PythonTokenizer
    {
        // Operadores ordenados por longitud (greedy match).
        private static readonly string[] Operators = new[]
        {
            "**=", "//=", ">>=", "<<=", "...", "!=",
            "**", "//", ">>", "<<", "<=", ">=", "==", "->", ":=",
            "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "@=",
            "+", "-", "*", "/", "%", "@", "&", "|", "^", "~",
            "<", ">", "(", ")", "[", "]", "{", "}", ",", ":", ".",
            ";", "=",
        };

        public static List<PyToken> Tokenize(string src)
        {
            var toks = new List<PyToken>();
            if (src == null) src = string.Empty;
            // Normalizar saltos de línea.
            src = src.Replace("\r\n", "\n").Replace("\r", "\n");
            int n = src.Length;
            int i = 0;
            int line = 1;
            int colBase = 0; // índice del char donde empieza la línea actual
            var indents = new Stack<int>();
            indents.Push(0);
            int parenDepth = 0;
            bool atLineStart = true;

            int Col(int idx) => idx - colBase + 1;

            while (i < n)
            {
                // ── Inicio de línea lógica: medir indentación (sólo si no estamos
                //    dentro de paréntesis) ──
                if (atLineStart && parenDepth == 0)
                {
                    int indent = 0;
                    int j = i;
                    while (j < n && (src[j] == ' ' || src[j] == '\t'))
                    {
                        indent += src[j] == '\t' ? 8 - (indent % 8) : 1;
                        j++;
                    }
                    // Línea en blanco → no afecta indentación.
                    if (j >= n || src[j] == '\n')
                    {
                        if (j < n) { line++; colBase = j + 1; }
                        i = j + 1;
                        continue;
                    }
                    // Línea sólo-comentario → normalmente no afecta indentación, pero se emite.
                    if (src[j] == '#')
                    {
                        int k = j;
                        while (k < n && src[k] != '\n') k++;
                        var ctext = src.Substring(j, k - j);
                        // Un comentario de MARKUP (#' #" #hide #show #cp) a MENOR indentación que el
                        // bloque actual debe DEDENTAR: si no, un for/def previo lo absorbe en su cuerpo
                        // (los comentarios no dedentan) y el heading/toggle nunca llega al render.
                        // Los comentarios normales (# ...) siguen sin afectar la indentación (seguro).
                        var cbody = ctext.Length > 1 ? ctext.Substring(1).TrimStart() : "";
                        bool isMarkup = ctext.StartsWith("#'") || ctext.StartsWith("#\"")
                                        || cbody.StartsWith("hide") || cbody.StartsWith("show")
                                        || cbody.StartsWith("cp");
                        if (isMarkup)
                            while (indent < indents.Peek())
                            {
                                indents.Pop();
                                toks.Add(new PyToken { Type = PyTok.Dedent, Text = "", Line = line, Col = Col(j) });
                            }
                        toks.Add(new PyToken { Type = PyTok.Comment, Text = ctext, Line = line, Col = Col(j) });
                        toks.Add(new PyToken { Type = PyTok.Newline, Text = "\n", Line = line, Col = Col(k) });
                        if (k < n) { line++; colBase = k + 1; }
                        i = k + 1;
                        continue;
                    }
                    // Ajustar INDENT/DEDENT.
                    int top = indents.Peek();
                    if (indent > top)
                    {
                        indents.Push(indent);
                        toks.Add(new PyToken { Type = PyTok.Indent, Text = "", Line = line, Col = Col(i) });
                    }
                    else if (indent < top)
                    {
                        while (indents.Peek() > indent)
                        {
                            indents.Pop();
                            toks.Add(new PyToken { Type = PyTok.Dedent, Text = "", Line = line, Col = Col(i) });
                        }
                        // (lenient: si no matchea exacto, ya dedenteó al nivel <=)
                    }
                    i = j;
                    atLineStart = false;
                }

                if (i >= n) break;
                char c = src[i];

                // ── Espacios (no al inicio) ──
                if (c == ' ' || c == '\t')
                {
                    i++;
                    continue;
                }

                // ── Continuación de línea explícita ──
                if (c == '\\' && i + 1 < n && src[i + 1] == '\n')
                {
                    line++; colBase = i + 2; i += 2;
                    continue;
                }

                // ── Salto de línea ──
                if (c == '\n')
                {
                    if (parenDepth == 0)
                    {
                        // Evitar NEWLINE redundante.
                        if (toks.Count > 0 && toks[^1].Type != PyTok.Newline && toks[^1].Type != PyTok.Indent && toks[^1].Type != PyTok.Dedent)
                            toks.Add(new PyToken { Type = PyTok.Newline, Text = "\n", Line = line, Col = Col(i) });
                        atLineStart = true;
                    }
                    line++; colBase = i + 1; i++;
                    continue;
                }

                // ── Comentario inline ──
                if (c == '#')
                {
                    int k = i;
                    while (k < n && src[k] != '\n') k++;
                    toks.Add(new PyToken { Type = PyTok.Comment, Text = src.Substring(i, k - i), Line = line, Col = Col(i) });
                    i = k;
                    continue;
                }

                // ── String (con posibles prefijos) ──
                if (TryReadStringPrefix(src, i, out int prefixLen, out bool isF, out bool isR, out bool isB))
                {
                    int qpos = i + prefixLen;
                    if (qpos < n && (src[qpos] == '"' || src[qpos] == '\''))
                    {
                        var tok = ReadString(src, ref i, ref line, ref colBase, prefixLen, isF, isR, isB);
                        toks.Add(tok);
                        continue;
                    }
                }
                if (c == '"' || c == '\'')
                {
                    var tok = ReadString(src, ref i, ref line, ref colBase, 0, false, false, false);
                    toks.Add(tok);
                    continue;
                }

                // ── Número ──
                if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(src[i + 1])))
                {
                    var tok = ReadNumber(src, ref i, line, Col(i));
                    toks.Add(tok);
                    continue;
                }

                // ── Identificador / keyword ──
                if (c == '_' || char.IsLetter(c))
                {
                    int start = i;
                    while (i < n && (src[i] == '_' || char.IsLetterOrDigit(src[i]))) i++;
                    toks.Add(new PyToken { Type = PyTok.Name, Text = src.Substring(start, i - start), Line = line, Col = Col(start) });
                    continue;
                }

                // ── Operador / delimitador ──
                bool matched = false;
                foreach (var op in Operators)
                {
                    if (i + op.Length <= n && string.CompareOrdinal(src, i, op, 0, op.Length) == 0)
                    {
                        if (op == "(" || op == "[" || op == "{") parenDepth++;
                        else if (op == ")" || op == "]" || op == "}") { if (parenDepth > 0) parenDepth--; }
                        toks.Add(new PyToken { Type = PyTok.Op, Text = op, Line = line, Col = Col(i) });
                        i += op.Length;
                        matched = true;
                        break;
                    }
                }
                if (matched) continue;

                throw new PythonTokenizeException($"Carácter inesperado '{c}'", line);
            }

            // Final: NEWLINE de cierre + DEDENTs + EndMarker.
            if (toks.Count > 0 && toks[^1].Type != PyTok.Newline)
                toks.Add(new PyToken { Type = PyTok.Newline, Text = "\n", Line = line, Col = 1 });
            while (indents.Peek() > 0)
            {
                indents.Pop();
                toks.Add(new PyToken { Type = PyTok.Dedent, Text = "", Line = line, Col = 1 });
            }
            toks.Add(new PyToken { Type = PyTok.EndMarker, Text = "", Line = line, Col = 1 });
            return toks;
        }

        private static bool TryReadStringPrefix(string src, int i, out int len, out bool isF, out bool isR, out bool isB)
        {
            len = 0; isF = false; isR = false; isB = false;
            int n = src.Length;
            int max = Math.Min(i + 2, n);
            int j = i;
            while (j < max)
            {
                char c = char.ToLowerInvariant(src[j]);
                if (c == 'f') isF = true;
                else if (c == 'r') isR = true;
                else if (c == 'b') isB = true;
                else break;
                j++;
            }
            len = j - i;
            return len > 0;
        }

        private static PyToken ReadString(string src, ref int i, ref int line, ref int colBase, int prefixLen, bool isF, bool isR, bool isB)
        {
            int n = src.Length;
            int startLine = line;
            int startCol = i - colBase + 1;
            i += prefixLen;
            char quote = src[i];
            bool triple = (i + 2 < n && src[i + 1] == quote && src[i + 2] == quote);
            int qlen = triple ? 3 : 1;
            i += qlen;
            var raw = new StringBuilder();   // contenido crudo (para f-strings)
            var dec = new StringBuilder();   // contenido decodificado
            bool closed = false;
            while (i < n)
            {
                // Cierre
                if (!triple && src[i] == quote)
                {
                    i++;
                    closed = true;
                    break;
                }
                if (triple && src[i] == quote && i + 2 < n && src[i + 1] == quote && src[i + 2] == quote)
                {
                    i += 3;
                    closed = true;
                    break;
                }
                char ch = src[i];
                // Python: un string de comilla simple/doble NO puede contener un salto
                // de línea crudo → "unterminated string literal" (igual que Python).
                if (ch == '\n' && !triple)
                    throw new PythonTokenizeException(
                        $"unterminated string literal (detected at line {startLine})", startLine);
                if (ch == '\n') { line++; colBase = i + 1; }
                if (ch == '\\' && !isR && i + 1 < n)
                {
                    char e = src[i + 1];
                    raw.Append(ch); raw.Append(e);
                    switch (e)
                    {
                        case 'n': dec.Append('\n'); break;
                        case 't': dec.Append('\t'); break;
                        case 'r': dec.Append('\r'); break;
                        case '\\': dec.Append('\\'); break;
                        case '\'': dec.Append('\''); break;
                        case '"': dec.Append('"'); break;
                        case '0': dec.Append('\0'); break;
                        case 'a': dec.Append('\a'); break;
                        case 'b': dec.Append('\b'); break;
                        case 'f': dec.Append('\f'); break;
                        case 'v': dec.Append('\v'); break;
                        case '\n': break; // line continuation
                        default: dec.Append('\\'); dec.Append(e); break;
                    }
                    i += 2;
                    continue;
                }
                if (ch == '\\' && isR && i + 1 < n)
                {
                    raw.Append(ch); raw.Append(src[i + 1]);
                    dec.Append(ch); dec.Append(src[i + 1]);
                    i += 2;
                    continue;
                }
                raw.Append(ch);
                dec.Append(ch);
                i++;
            }
            if (!closed)
                throw new PythonTokenizeException(
                    triple ? $"unterminated triple-quoted string literal (detected at line {startLine})"
                           : $"unterminated string literal (detected at line {startLine})", startLine);
            return new PyToken
            {
                Type = PyTok.String,
                Text = raw.ToString(),     // f-strings usan el crudo
                Str = dec.ToString(),
                IsFString = isF,
                IsRaw = isR,
                IsBytes = isB,
                Line = startLine,
                Col = startCol,
            };
        }

        private static PyToken ReadNumber(string src, ref int i, int line, int col)
        {
            int n = src.Length;
            int start = i;
            bool isInt = true;
            bool isComplex = false;

            // Prefijos 0x / 0b / 0o
            if (src[i] == '0' && i + 1 < n && (src[i + 1] == 'x' || src[i + 1] == 'X' ||
                                               src[i + 1] == 'b' || src[i + 1] == 'B' ||
                                               src[i + 1] == 'o' || src[i + 1] == 'O'))
            {
                char baseC = char.ToLowerInvariant(src[i + 1]);
                i += 2;
                int radixStart = i;
                while (i < n && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                string digits = src.Substring(radixStart, i - radixStart).Replace("_", "");
                int radix = baseC == 'x' ? 16 : baseC == 'o' ? 8 : 2;
                double val = 0;
                try
                {
                    long lv = Convert.ToInt64(digits, radix);
                    val = lv;
                }
                catch { val = 0; }
                return new PyToken { Type = PyTok.Number, Text = src.Substring(start, i - start), Num = val, IsInt = true, Line = line, Col = col };
            }

            while (i < n && (char.IsDigit(src[i]) || src[i] == '_')) i++;
            if (i < n && src[i] == '.')
            {
                isInt = false;
                i++;
                while (i < n && (char.IsDigit(src[i]) || src[i] == '_')) i++;
            }
            if (i < n && (src[i] == 'e' || src[i] == 'E'))
            {
                int save = i;
                i++;
                if (i < n && (src[i] == '+' || src[i] == '-')) i++;
                if (i < n && char.IsDigit(src[i]))
                {
                    isInt = false;
                    while (i < n && (char.IsDigit(src[i]) || src[i] == '_')) i++;
                }
                else i = save; // no era exponente
            }
            if (i < n && (src[i] == 'j' || src[i] == 'J'))
            {
                isComplex = true;
                i++;
            }
            string text = src.Substring(start, i - start);
            string clean = text.Replace("_", "").TrimEnd('j', 'J');
            double num = double.Parse(clean, CultureInfo.InvariantCulture);
            return new PyToken { Type = PyTok.Number, Text = text, Num = num, IsInt = isInt && !isComplex, IsComplex = isComplex, Line = line, Col = col };
        }
    }
}
