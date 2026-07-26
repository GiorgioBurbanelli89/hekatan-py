// =============================================================================
// Hekatan Python3 — Render de statements Python a HTML estilo Calcpad
// =============================================================================
//   Reutiliza las clases CSS de Calcpad (.eq, var, .mat, sub, sup) para que el
//   reporte tenga el look de hoja de cálculo. No re-ejecuta nada: usa el AST y
//   el StatementResult ya calculado.
// =============================================================================
using System;
using System.Net;
using System.Text;
using Markdig;

namespace Calcpad.Core.Python
{
    public static class PythonHtmlWriter
    {
        /// <summary>Clasifica el comentario en una directiva Suite-Py. Devuelve uno de:
        /// "text" (#'), "heading" (#"), "show", "hide", "noc", "val", o "plain".
        /// `content` = el texto tras el marcador (para #' y #").</summary>
        public static string CommentKind(string text, out string content)
        {
            content = "";
            if (string.IsNullOrEmpty(text)) return "plain";
            string rest = text[0] == '#' ? text.Substring(1) : text;   // quitar UN '#'
            if (rest.StartsWith("'")) { content = rest.Substring(1); return "text"; }
            if (rest.StartsWith("\"")) { content = rest.Substring(1); return "heading"; }
            if (rest.StartsWith("cp[")) return "cp-open";     // abre bloque Calcpad simbólico
            if (rest.StartsWith("cp]")) return "cp-close";    // cierra bloque
            string t = rest.TrimStart();
            int sp = t.IndexOfAny(new[] { ' ', '\t' });
            string tok = sp < 0 ? t : t.Substring(0, sp);
            string after = sp < 0 ? "" : t.Substring(sp + 1).Trim();   // texto tras la directiva
            switch (tok)
            {
                case "show": content = after; return "show";
                case "hide": content = after; return "hide";
                case "noc": content = after; return "noc";
                case "val": content = after; return "val";
                case "cp": content = after; return "cp";     // inline: #cp <expr>
                default: return "plain";
            }
        }

        public static string RenderStatement(PyNode stmt, StatementResult result, string directive = null, string directiveText = null)
        {
            switch (stmt)
            {
                case CommentStmt cs:
                    return RenderComment(cs);
                case ExprStmt es when es.Expr is StringLit || es.Expr is FStringLit:
                {
                    // String/f-string literal suelto → texto plano (como #' / ' / " de Calcpad).
                    var txt = result.Value as string ?? PyOps.Str(result.Value);
                    return string.IsNullOrEmpty(txt) ? string.Empty : WebUtility.HtmlEncode(txt);
                }
                default:
                    break;
            }

            if (result.Assignments != null)
            {
                var sb = new StringBuilder();
                sb.Append("<span class=\"eq\">");
                // Todos los destinos: a = b = ...   (asignación encadenada)
                for (int i = 0; i < result.Assignments.Count; i++)
                {
                    sb.Append(TargetToHtml(result.Assignments[i].Target));
                    sb.Append(" = ");
                }
                var value = result.Assignments[result.Assignments.Count - 1].Value;
                var rhs = result.RhsExpr;
                // Eco de la fórmula SOLO para expresiones matemáticas (A = b·h = 0.15).
                // Para una LLAMADA opaca (uy = ops.nodeDisp(2,2)) se muestra solo "uy = valor".
                bool showFormula = directive != "val" && rhs != null
                                   && !IsLiteralNode(rhs) && !(rhs is CallExpr);
                bool showValue = directive != "noc";
                // Fórmula del lado derecho (estilo Calcpad: "A = b · h = 0.15").
                if (showFormula)
                {
                    var f = ExprToHtml(rhs);
                    if (!string.IsNullOrEmpty(f)) { sb.Append(f); if (showValue) sb.Append(" = "); }
                }
                if (showValue)
                    sb.Append(ValueToHtml(value));
                else if (!showFormula)
                    sb.Append(ValueToHtml(value));   // #noc sin fórmula → al menos el valor
                sb.Append("</span>");
                AppendCaption(sb, directive, directiveText);   // #show "texto" → caption junto al valor
                return sb.ToString();
            }

            if (result.HasValue)
            {
                var sb = new StringBuilder();
                sb.Append("<span class=\"eq\">");
                // expr = valor  (estilo hoja de cálculo)
                var exprHtml = ExprToHtml(result.Expr);
                if (!string.IsNullOrEmpty(exprHtml) && !IsLiteralNode(result.Expr) && directive != "val")
                {
                    sb.Append(exprHtml);
                    sb.Append(" = ");
                }
                sb.Append(ValueToHtml(result.Value));
                sb.Append("</span>");
                AppendCaption(sb, directive, directiveText);
                return sb.ToString();
            }

            return string.Empty;
        }

        // Caption a la derecha del valor (como el `'comentario` inline de Calcpad puro):
        //   `a = 6  #show texto`  → "a = 6   texto"
        //   `a = 6  #' texto`     → "a = 6   texto"   (inline #' = etiqueta, estilo Calcpad)
        // Con `#hide` no se llega acá (no se renderiza nada).
        private static void AppendCaption(StringBuilder sb, string directive, string directiveText)
        {
            if ((directive == "show" || directive == "text" || directive == "heading")
                && !string.IsNullOrWhiteSpace(directiveText))
                sb.Append($"<span style=\"margin-left:1.2em;color:#0a7f3f;font-style:italic\">{WebUtility.HtmlEncode(directiveText.Trim())}</span>");
        }

        private static bool IsLiteralNode(PyNode n) =>
            n is NumberLit || n is StringLit || n is BoolLit || n is NoneLit || n is FStringLit
            || (n is UnaryOp u && (u.Op == "-" || u.Op == "+") && u.Operand is NumberLit); // -3.5 sigue siendo literal

        // Comentarios:  #'texto → texto (como `'` de Calcpad) ; #"texto → título (como `"`).
        // Cualquier otro comentario (# , ## , #show/#hide sueltos) NO se renderiza (paridad Python).
        [ThreadStatic] private static bool _insideCp;
        [ThreadStatic] private static System.Collections.Generic.List<string> _cpLines;
        [ThreadStatic] private static Calcpad.Core.ExpressionParser _cpEp;

        private static string RenderComment(CommentStmt cs)
        {
            var kind = CommentKind(cs.Text, out var content);
            content = content.Trim();
            if (kind == "cp-open") { _insideCp = true; _cpLines = new System.Collections.Generic.List<string>(); return string.Empty; }
            if (kind == "cp-close")
            {
                _insideCp = false;
                var code = string.Join("\n", _cpLines ?? new System.Collections.Generic.List<string>());
                _cpLines = null;
                return RenderCalcpadBlock(code);   // bloque = Calcpad COMPLETO evaluado ($Sum, $Product, #for/#loop, sqrt…)
            }
            if (_insideCp)                          // dentro de #cp[ … #cp]: acumular la línea (quitar el '#')
            {
                _cpLines?.Add(cs.Text.TrimStart('#'));
                return string.Empty;
            }
            if (kind == "cp") return RenderCalcpadSymbolic(content);   // inline: simbólico (sin evaluar)
            if (kind == "heading")
                return content.Length == 0 ? string.Empty : $"<h3>{WebUtility.HtmlEncode(content)}</h3>";
            if (kind == "text")
                return content.Length == 0 ? string.Empty : RenderMarkdown(content);   // #' → Markdown
            return string.Empty;   // comentario normal → invisible (como en Python)
        }

        private static string CpBody(string html)
        {
            int b = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (b >= 0)
            {
                int s = html.IndexOf('>', b) + 1;
                int e = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                if (e > s) html = html.Substring(s, e - s);
            }
            // El ∂ (derivada parcial) sale en <i> (azul); lo pintamos morado como ∫/∑ (nary #C080F0).
            return html.Trim().Replace("<i>∂</i>", "<i style=\"color:#C080F0\">∂</i>");
        }

        // #' → Markdown (Markdig, GFM: negrita **, cursiva *, tachado ~~, listas, tablas, task lists, links).
        private static Markdig.MarkdownPipeline _mdPipe;
        internal static string RenderMarkdown(string md)
        {
            try
            {
                _mdPipe ??= new Markdig.MarkdownPipelineBuilder()
                    .UseEmphasisExtras().UseListExtras().UsePipeTables().UseGridTables().UseAutoLinks().UseTaskLists().Build();
                string html = Markdig.Markdown.ToHtml(md, _mdPipe).Trim();
                // Las tablas markdown salen sin clase → sin bordes. Marcarlas `.bordered`
                // para heredar el estilo de tabla del worksheet (header con fondo + celdas con borde).
                html = html.Replace("<table>", "<table class=\"bordered\">");
                if (html.StartsWith("<p>", StringComparison.Ordinal) && html.EndsWith("</p>", StringComparison.Ordinal)
                    && html.IndexOf("<p>", 3, StringComparison.Ordinal) < 0)
                    return html.Substring(3, html.Length - 7);   // inline (un párrafo) → sin wrapper <p>
                return html;
            }
            catch { return WebUtility.HtmlEncode(md); }
        }

        // #cp inline → fórmula simbólica (SIN evaluar), vía #sym (AngouriMath): sqrt→√, subíndices, fracciones.
        private static string RenderCalcpadSymbolic(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return string.Empty;
            try
            {
                _cpEp ??= new Calcpad.Core.ExpressionParser();
                _cpEp.Parse("#sym " + expr, calculate: true, getXml: true);
                return CpBody(_cpEp.HtmlResult ?? "");
            }
            catch { return WebUtility.HtmlEncode(expr); }
        }

        // #cp[ … #cp] bloque → Calcpad COMPLETO evaluado: $Sum{f @ k=a:b}, $Product, #for/#loop, sqrt→√, subíndices, fracciones.
        private static string RenderCalcpadBlock(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            try
            {
                _cpEp ??= new Calcpad.Core.ExpressionParser();
                _cpEp.Parse(code, calculate: true, getXml: true);
                return CpBody(_cpEp.HtmlResult ?? "");
            }
            catch { return WebUtility.HtmlEncode(code); }
        }

        // ── Target de asignación ──
        private static string TargetToHtml(PyNode target)
        {
            switch (target)
            {
                case NameRef nr: return VarName(nr.Name);
                case TupleLit t:
                {
                    var sb = new StringBuilder("(");
                    for (int i = 0; i < t.Elements.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(TargetToHtml(t.Elements[i])); }
                    return sb.Append(')').ToString();
                }
                case ListLit l:
                {
                    var sb = new StringBuilder("[");
                    for (int i = 0; i < l.Elements.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(TargetToHtml(l.Elements[i])); }
                    return sb.Append(']').ToString();
                }
                case IndexExpr ix: return ExprToHtml(ix);
                case AttributeExpr at: return ExprToHtml(at);
                case StarExpr se: return "*" + TargetToHtml(se.Value);
                default: return WebUtility.HtmlEncode(target?.ToString() ?? "?");
            }
        }

        // ── Valor calculado ──
        public static string ValueToHtml(object value)
        {
            switch (value)
            {
                case null: return "<span class=\"unit\">None</span>";
                case bool b: return $"<b>{(b ? "True" : "False")}</b>";
                case long l: return WebUtility.HtmlEncode(PyOps.Str(l));
                case double d: return WebUtility.HtmlEncode(PyOps.Str(d));
                case string s: return WebUtility.HtmlEncode(PyOps.Repr(s));
                case PyList list: return SeqToMatrix(list.Items, '[', ']');
                case PyTuple tup: return SeqToMatrix(tup.Items, '(', ')');
                case PySet set: return WebUtility.HtmlEncode(PyOps.Str(set));
                case PyDict d: return DictToHtml(d);
                case PyRange r: return WebUtility.HtmlEncode(PyOps.Str(r));
                case PyNdArray arr: return NdToHtml(arr);
                default: return WebUtility.HtmlEncode(PyOps.Str(value));
            }
        }

        private static System.Collections.Generic.List<object> AsSeq(object o) =>
            o is PyList pl ? pl.Items : o is PyTuple pt ? pt.Items : null;

        // Lista/tupla numérica → matriz fila; lista de listas numéricas → matriz 2D; si no → texto.
        private static string SeqToMatrix(System.Collections.Generic.List<object> items, char open, char close)
        {
            // --- 2D: todos los elementos son secuencias numéricas → matriz con filas ---
            if (items.Count > 0)
            {
                var rows = new System.Collections.Generic.List<System.Collections.Generic.List<object>>();
                bool all2d = true;
                foreach (var x in items)
                {
                    var row = AsSeq(x);
                    if (row == null) { all2d = false; break; }
                    foreach (var c in row) if (!PyOps.IsNumber(c)) { all2d = false; break; }
                    if (!all2d) break;
                    rows.Add(row);
                }
                if (all2d)
                {
                    var sbm = new StringBuilder();
                    ClassicMatrixOpen(sbm);
                    foreach (var row in rows)
                    {
                        sbm.Append("<span class=\"tr\"><span class=\"td\"></span>");
                        foreach (var c in row)
                            sbm.Append("<span class=\"td\">").Append(WebUtility.HtmlEncode(PyOps.Str(c))).Append("</span>");
                        sbm.Append("<span class=\"td\"></span></span>");
                    }
                    sbm.Append("</span>");
                    return sbm.ToString();
                }
            }
            bool allNum = items.Count > 0;
            foreach (var x in items) if (!PyOps.IsNumber(x)) { allNum = false; break; }
            if (!allNum || items.Count == 0)
            {
                var sb0 = new StringBuilder();
                sb0.Append(open);
                for (int i = 0; i < items.Count; i++) { if (i > 0) sb0.Append(", "); sb0.Append(WebUtility.HtmlEncode(PyOps.Repr(items[i]))); }
                sb0.Append(close);
                return WebUtility.HtmlEncode(sb0.ToString());
            }
            var sb = new StringBuilder();
            ClassicMatrixOpen(sb);
            sb.Append("<span class=\"tr\"><span class=\"td\"></span>");
            foreach (var x in items)
                sb.Append("<span class=\"td\">").Append(WebUtility.HtmlEncode(PyOps.Str(x))).Append("</span>");
            sb.Append("<span class=\"td\"></span></span></span>");
            return sb.ToString();
        }

        // Abre el markup de matriz CLÁSICO de Calcpad (.matrix inline-table, corchetes = borde de
        // celda vacía primera/última) — mismo estilo AJUSTADO que Calcpad Suite/VM/Symbolic.
        private static void ClassicMatrixOpen(StringBuilder sb) => sb.Append("<span class=\"matrix\">");

        // numpy.ndarray → matriz .eq (con truncado: matrices grandes muestran solo el shape).
        private static string NdToHtml(PyNdArray a)
        {
            const int MAXR = 16, MAXC = 16;
            if (a.Size > 400 || a.Rows > MAXR || a.Cols > MAXC)
                return WebUtility.HtmlEncode($"array(shape=({string.Join("×", a.Shape)}), dtype={(a.IsInt ? "int64" : "float64")})");
            string Cell(double v) => WebUtility.HtmlEncode(a.IsInt
                ? ((long)System.Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : PyOps.Str(v));
            var sb = new StringBuilder();
            ClassicMatrixOpen(sb);
            int rows = a.Ndim == 1 ? 1 : a.Rows, cols = a.Ndim == 1 ? a.Size : a.Cols;
            for (int i = 0; i < rows; i++)
            {
                sb.Append("<span class=\"tr\"><span class=\"td\"></span>");
                for (int j = 0; j < cols; j++)
                    sb.Append("<span class=\"td\">").Append(Cell(a.Data[i * cols + j])).Append("</span>");
                sb.Append("<span class=\"td\"></span></span>");
            }
            sb.Append("</span>");
            return sb.ToString();
        }

        private static string DictToHtml(PyDict d)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < d.Keys.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(PyOps.Repr(d.Keys[i])).Append(": ").Append(PyOps.Repr(d.Values[i]));
            }
            sb.Append('}');
            return WebUtility.HtmlEncode(sb.ToString());
        }

        // ── Reconstrucción de expresión a HTML (estilo math) ──
        public static string ExprToHtml(PyNode n)
        {
            switch (n)
            {
                case null: return "";
                case NumberLit num: return WebUtility.HtmlEncode(num.OrigText ?? PyOps.Str(num.IsInt ? (object)(long)num.Value : num.Value));
                case StringLit s: return WebUtility.HtmlEncode(PyOps.Repr(s.Value));
                case BoolLit b: return $"<b>{(b.Value ? "True" : "False")}</b>";
                case NoneLit: return "None";
                case NameRef nr: return VarName(nr.Name);
                case FStringLit: return "<i>f\"…\"</i>";
                case UnaryOp u: return (u.Op == "not" ? "not " : WebUtility.HtmlEncode(u.Op)) + ExprToHtml(u.Operand);
                case BinaryOp bin: return BinaryToHtml(bin);
                case CompareOp c:
                {
                    var sb = new StringBuilder(ExprToHtml(c.Left));
                    for (int i = 0; i < c.Ops.Count; i++)
                        sb.Append(" ").Append(WebUtility.HtmlEncode(c.Ops[i])).Append(" ").Append(ExprToHtml(c.Comparators[i]));
                    return sb.ToString();
                }
                case CallExpr call:
                {
                    var sb = new StringBuilder(ExprToHtml(call.Func));
                    sb.Append('(');
                    for (int i = 0; i < call.Args.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(ExprToHtml(call.Args[i])); }
                    sb.Append(')');
                    return sb.ToString();
                }
                case AttributeExpr at: return ExprToHtml(at.Target) + "." + WebUtility.HtmlEncode(at.Name);
                case IndexExpr ix: return ExprToHtml(ix.Target) + "[" + ExprToHtml(ix.Index) + "]";
                case SliceExpr sl: return (sl.Lower != null ? ExprToHtml(sl.Lower) : "") + ":" + (sl.Upper != null ? ExprToHtml(sl.Upper) : "") + (sl.Step != null ? ":" + ExprToHtml(sl.Step) : "");
                case ListLit l:
                {
                    var sb = new StringBuilder("[");
                    for (int i = 0; i < l.Elements.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(ExprToHtml(l.Elements[i])); }
                    return sb.Append(']').ToString();
                }
                case TupleLit t:
                {
                    var sb = new StringBuilder("(");
                    for (int i = 0; i < t.Elements.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(ExprToHtml(t.Elements[i])); }
                    return sb.Append(')').ToString();
                }
                // Nodo no soportado para eco (ternario, comprehensions, lambda, ...):
                // devolver "" → se omite la fórmula y se muestra solo "var = valor"
                // (antes volcaba el nombre del tipo, ej. "IfExp").
                default: return "";
            }
        }

        private static string BinaryToHtml(BinaryOp bin)
        {
            string l = ExprToHtml(bin.Left);
            string r = ExprToHtml(bin.Right);
            switch (bin.Op)
            {
                case "*": return $"{l} &middot; {r}";
                case "**": return $"{l}<sup>{r}</sup>";
                case "/": return $"<span class=\"dvc\"><span class=\"dvn\">{l}</span><span class=\"dvl\"></span><span class=\"dvd\">{r}</span></span>";
                case "and": return $"{l} and {r}";
                case "or": return $"{l} or {r}";
                default: return $"{l} {WebUtility.HtmlEncode(bin.Op)} {r}";
            }
        }

        // Nombre de variable: letras griegas por nombre (sigma→σ) + subíndice tras '_'.
        // El .py sigue siendo Python válido; el mapeo es SOLO de render (como Calcpad).
        private static string VarName(string name)
        {
            // `lambda_` (lambda es keyword Python) y `sigma_` → quitar el '_' final y mapear.
            string bare = name.Length > 1 && name.EndsWith("_") ? name.Substring(0, name.Length - 1) : name;
            int us = bare.IndexOf('_');
            if (us > 0 && us < bare.Length - 1)
            {
                string baseN = bare.Substring(0, us);
                var parts = bare.Substring(us + 1).Split('_');
                for (int i = 0; i < parts.Length; i++) parts[i] = GreekOrEscape(parts[i]);
                return $"<var>{GreekOrEscape(baseN)}<sub>{string.Join(",", parts)}</sub></var>";
            }
            return $"<var>{GreekOrEscape(bare)}</var>";
        }

        private static string GreekOrEscape(string s) => GreekLetterMap(s) ?? WebUtility.HtmlEncode(s);

        /// <summary>Nombre de letra griega → glifo unicode (como Calcpad). Null si no es griega.</summary>
        private static string GreekLetterMap(string name) => name switch
        {
            "alpha" => "α", "beta" => "β", "gamma" => "γ", "delta" => "δ",
            "epsilon" => "ε", "zeta" => "ζ", "eta" => "η", "theta" => "θ",
            "iota" => "ι", "kappa" => "κ", "lambda" => "λ", "mu" => "μ",
            "nu" => "ν", "xi" => "ξ", "omicron" => "ο", "pi" => "π",
            "rho" => "ρ", "sigma" => "σ", "tau" => "τ", "upsilon" => "υ",
            "phi" => "φ", "chi" => "χ", "psi" => "ψ", "omega" => "ω",
            "Alpha" => "Α", "Beta" => "Β", "Gamma" => "Γ", "Delta" => "Δ",
            "Epsilon" => "Ε", "Zeta" => "Ζ", "Eta" => "Η", "Theta" => "Θ",
            "Iota" => "Ι", "Kappa" => "Κ", "Lambda" => "Λ", "Mu" => "Μ",
            "Nu" => "Ν", "Xi" => "Ξ", "Pi" => "Π", "Rho" => "Ρ",
            "Sigma" => "Σ", "Tau" => "Τ", "Upsilon" => "Υ", "Phi" => "Φ",
            "Chi" => "Χ", "Psi" => "Ψ", "Omega" => "Ω",
            _ => null
        };
    }
}
