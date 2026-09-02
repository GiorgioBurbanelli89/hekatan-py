// =============================================================================
// Hekatan Python3 — Python Pipeline: fachada Tokenizer + Parser + Evaluator
//                    + HtmlWriter, con fallback al intérprete python real.
// =============================================================================
//   Entry-point único para ejecutar un script Python y obtener HTML.
//   Estrategia (decisión del usuario): lo que el motor C# nativo sabe hacer se
//   ejecuta en C# (rápido, sin proceso externo); lo que no (numpy, sympy,
//   matplotlib, imports no nativos, constructos no soportados) cae al `python`
//   real del sistema (subprocess), reusando el patrón ya existente en Calcpad.
// =============================================================================
using System;
using Markdig;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Calcpad.Core.Python
{
    public sealed class PythonPipeline
    {
        private PythonEvaluator _evaluator = new();
        public PyScope GlobalScope => _evaluator.Globals;

        /// <summary>Si true, fuerza el uso del intérprete python real del sistema
        /// (sin intentar el motor nativo). Útil para depurar paridad.</summary>
        public bool ForceRealPython { get; set; }

        /// <summary>Si true (default), permite caer a python real cuando el motor
        /// nativo no soporta algo. Si false, los no-soportados producen error.</summary>
        public bool AllowRealPythonFallback { get; set; } = true;

        public bool StreamingMode { get; set; }
        public event Action<int> StatementStarting;
        public event Action<int, string> StatementCompleted;
        public event Action<string> ScriptFinished;

        /// <summary>Última ejecución usó python real (subprocess).</summary>
        public bool LastRanWithRealPython { get; private set; }

        private bool _noAutoNative;   // #noauto/#solografica en el motor nativo: no auto-render de variables
        private HashSet<int> _hiddenLines = new HashSet<int>();   // líneas dentro de un bloque #hide…#show

        public string Run(string source)
        {
            LastRanWithRealPython = false;
            _noAutoNative = source.Contains("#noauto") || source.Contains("#solografica");

            // Bloques #hide … #show: se calculan por NÚMERO DE LÍNEA desde el source crudo.
            // Robusto: un `#show` tras un `for`/`def`/`if` lo absorbe el parser dentro del cuerpo
            // (no aparece como statement suelto), así que detectarlo en el AST fallaba (tragaba
            // el heading siguiente). Por línea es inmune a esa absorción.
            _hiddenLines = new HashSet<int>();
            {
                var _sl = source.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                bool _hid = false;
                for (int _i = 0; _i < _sl.Length; _i++)
                {
                    var _t = _sl[_i].Trim();
                    if (_t == "#hide") { _hid = true; continue; }
                    if (_t == "#show") { _hid = false; continue; }
                    if (_hid) _hiddenLines.Add(_i + 1);   // líneas 1-based (como PyToken.Line)
                }
            }

            // Directiva por-script `#venv`/`#env`: si el .py declara su entorno,
            // ese python.exe tiene prioridad sobre la elección del menú, SOLO para
            // esta ejecución. Si no hay directiva, queda null → se usa el del menú.
            try { RealPython.OverrideInterpreter = PythonEnvironments.ResolveScriptEnv(source); }
            catch { RealPython.OverrideInterpreter = null; }

            List<PyNode> stmts;
            try
            {
                var tokens = PythonTokenizer.Tokenize(source);
                var parser = new PythonParser(tokens);
                stmts = parser.ParseModule();
            }
            catch (Exception) when (ForceRealPython || AllowRealPythonFallback)
            {
                // No parseable nativamente → python real.
                return FinishRealPython(source);
            }

            if (ForceRealPython)
                return FinishRealPython(source);

            // ¿Imports satisfacibles nativamente?
            if (!PythonEvaluator.CanRunNatively(stmts, out var reason))
            {
                if (AllowRealPythonFallback) return FinishRealPython(source);
                return ErrorHtml($"Construcción no soportada por el motor nativo: {reason}");
            }

            try
            {
                return RunNative(stmts);
            }
            catch (PythonNotSupported) when (AllowRealPythonFallback)
            {
                // Algo dinámico no soportado: re-ejecutar todo en python real.
                _evaluator = new PythonEvaluator(); // limpiar estado parcial
                return FinishRealPython(source);
            }
        }

        public (string Html, string Error, int ErrorLine) RunLine(string source, int lineOffset = 0)
        {
            try
            {
                return (Run(source), null, 0);
            }
            catch (PythonParseException pe) { return (null, pe.Message, pe.Line + lineOffset); }
            catch (PythonTokenizeException te) { return (null, te.Message, te.Line + lineOffset); }
            catch (PyRuntimeError re) { return (null, $"{re.PyType}: {re.Message}", lineOffset); }
            catch (Exception ex) { return (null, $"Internal: {ex.GetType().Name}: {ex.Message}", lineOffset); }
        }

        public void Reset() => _evaluator = new PythonEvaluator();

        // ===================================================================
        //  EJECUCIÓN NATIVA
        // ===================================================================
        private string RunNative(List<PyNode> stmts)
        {
            _htmlBlock = false;   // estado limpio de bloque HTML al arrancar el render
            var sb = new StringBuilder();
            var dispBuffer = new StringBuilder();
            _evaluator.Output = msg => dispBuffer.Append(msg);
            _evaluator.HtmlOut = html => sb.Append(html);

            string LineLink(int line) => $"[<a href=\"#0\" data-text=\"{line}\">{line}</a>]";

            int lastLine = -1;
            int pendingStart = sb.Length, pendingLine = -1;

            void FlushDisp(int line)
            {
                if (dispBuffer.Length == 0) return;
                var raw = dispBuffer.ToString();
                dispBuffer.Clear();
                // Procesa marcadores __CPSPY_HTML__ / __CPSPY_IMG__ (HTML/imagen crudos)
                // igual que el fallback, para que print() pueda emitir HTML (ej. visores 3D).
                sb.Append(RenderStdout(raw));
            }

            for (int idx = 0; idx < stmts.Count; idx++)
            {
                var stmt = stmts[idx];

                // Comentario INLINE = directiva del statement anterior (ya consumida) → no renderizar.
                if (stmt is CommentStmt ics && ics.IsInline)
                    continue;

                // Bloque #hide … #show (por número de línea): ejecuta lo ejecutable y descarta
                // todo el render (echo + prints). Inmune a la absorción del `#show` por un for/def.
                if (_hiddenLines.Contains(stmt?.Line ?? -1))
                {
                    if (stmt is not CommentStmt)
                    {
                        try { _evaluator.ExecuteOne(stmt, _evaluator.Globals); }
                        catch (PythonNotSupported) { throw; }
                        catch { /* en bloque oculto no rompemos el reporte por un error de una línea */ }
                        dispBuffer.Clear();   // descartar prints emitidos dentro del bloque oculto
                    }
                    continue;
                }

                // Agrupar #' (Markdown) consecutivos → un bloque (listas/tablas multilínea) — EMBEBIDO, sin python real.
                if (stmt is CommentStmt mcs && !mcs.IsInline
                    && PythonHtmlWriter.CommentKind(mcs.Text, out _) == "text")
                {
                    var mdLines = new List<string>();
                    int j = idx;
                    while (j < stmts.Count && stmts[j] is CommentStmt c2 && !c2.IsInline
                           && PythonHtmlWriter.CommentKind(c2.Text, out var cc) == "text")
                    { mdLines.Add(cc.Trim()); j++; }
                    FlushDisp(mcs.Line);
                    string mdHtml = PythonHtmlWriter.RenderMarkdown(string.Join("\n", mdLines));
                    if (!string.IsNullOrEmpty(mdHtml))
                        sb.Append($"<div class=\"line\" id=\"line-{mcs.Line}\">").Append(mdHtml).Append("</div>\n");
                    idx = j - 1;   // el for hará idx++
                    continue;
                }

                // ¿Directiva inline para ESTE statement? (comentario en la misma línea, a continuación)
                string directive = null, directiveText = null;
                if (idx + 1 < stmts.Count && stmts[idx + 1] is CommentStmt nc && nc.IsInline)
                    directive = PythonHtmlWriter.CommentKind(nc.Text, out directiveText);

                int stmtLine = stmt?.Line ?? 0;

                if (StreamingMode && pendingLine != -1 && pendingLine != stmtLine
                    && sb.Length > pendingStart && StatementCompleted != null)
                {
                    StatementCompleted.Invoke(pendingLine, sb.ToString(pendingStart, sb.Length - pendingStart));
                    pendingStart = sb.Length;
                }
                pendingLine = stmtLine;
                StatementStarting?.Invoke(stmtLine);

                StatementResult result;
                try
                {
                    result = _evaluator.ExecuteOne(stmt, _evaluator.Globals);
                }
                catch (PythonNotSupported) { throw; } // sube al fallback
                catch (PyRuntimeError ex)
                {
                    FlushDisp(stmtLine);
                    sb.Append($"<p class=\"err\" id=\"line-{stmtLine}\">{ex.PyType}: {WebUtility.HtmlEncode(ex.Message)} (line {LineLink(stmtLine)})</p>\n");
                    continue;
                }
                catch (PyRaiseSignal rs)
                {
                    FlushDisp(stmtLine);
                    string m = rs.Value is PyRuntimeError pe ? $"{pe.PyType}: {pe.Message}" : PyOps.Str(rs.Value);
                    sb.Append($"<p class=\"err\" id=\"line-{stmtLine}\">{WebUtility.HtmlEncode(m)} (line {LineLink(stmtLine)})</p>\n");
                    continue;
                }

                // Volcar print() antes del render del statement.
                FlushDisp(stmtLine);

                // #noauto → NO auto-renderizar variables: ejecuta pero solo muestra texto
                // (#'/#" comentarios, strings sueltos), prints, figuras y lo marcado con #show.
                bool _isText = stmt is CommentStmt
                               || (stmt is ExprStmt _es && (_es.Expr is StringLit || _es.Expr is FStringLit));
                bool _explicit = directive == "show" || directive == "text" || directive == "heading";
                bool _autoSuppressed = _noAutoNative && !_isText && !_explicit;

                // #hide → se ejecuta (ya se ejecutó) pero NO se muestra nada.
                if (result.Display && directive != "hide" && !_autoSuppressed)
                {
                    try
                    {
                        var html = PythonHtmlWriter.RenderStatement(stmt, result, directive, directiveText);
                        if (!string.IsNullOrEmpty(html))
                        {
                            sb.Append($"<p class=\"line\" id=\"line-{stmtLine}\">");
                            sb.Append(html);
                            sb.Append("</p>\n");
                        }
                    }
                    catch (PythonNotSupported) { throw; }
                    catch (Exception rex)
                    {
                        sb.Append($"<p class=\"err\" id=\"line-{stmtLine}\">Render error: {WebUtility.HtmlEncode(rex.Message)}</p>\n");
                    }
                }
                lastLine = stmtLine;
            }

            // Flush final de print pendiente.
            FlushDisp(lastLine < 0 ? 0 : lastLine);

            if (StreamingMode && pendingLine != -1 && sb.Length > pendingStart && StatementCompleted != null)
                StatementCompleted.Invoke(pendingLine, sb.ToString(pendingStart, sb.Length - pendingStart));

            var full = sb.ToString();
            ScriptFinished?.Invoke(full);
            return full;
        }

        // ===================================================================
        //  FALLBACK: PYTHON REAL (subprocess)
        // ===================================================================
        // Detecta scripts de INTERFAZ GRÁFICA (PyQt5/6, PySide2/6, PyVista, tkinter mainloop).
        // Estos abren su propia ventana nativa y bloquean en exec_()/mainloop(): se lanzan
        // DESACOPLADOS (sin timeout, sin capturar stdout) para que la interfaz se vea, igual
        // que al correr `python script.py` a mano.
        private static bool IsGuiScript(string source)
        {
            // OJO: 'pyvista' (a secas) NO va aquí — se intercepta su show() y se EMBEBE
            // como PNG (ver _pvPreamble). Solo los full-app Qt/tk/visores propios se detachan.
            string[] markers = {
                "PyQt5", "PyQt6", "PySide2", "PySide6", "pyvistaqt", "QtInteractor",
                ".exec_()", ".exec()", ".mainloop(", "QApplication", "vedo",
                "mayavi", "glfw", "pyglet"
            };
            foreach (var m in markers)
                if (source.Contains(m, StringComparison.Ordinal)) return true;
            return false;
        }

        private string FinishRealPython(string source)
        {
            LastRanWithRealPython = true;

            // ── INTERFAZ GRÁFICA (Qt/PyVista/tk): abrir ventana nativa desacoplada ──
            if (IsGuiScript(source))
            {
                var (info, okGui) = RealPython.ExecuteDetached(source);
                var cls = okGui ? "line" : "err";
                var html = $"<p class=\"{cls}\"><span class=\"eq\"><span style=\"white-space:pre-wrap\">" +
                           WebUtility.HtmlEncode(info) + "</span></span></p>\n";
                ScriptFinished?.Invoke(html);
                if (StreamingMode && StatementCompleted != null) StatementCompleted.Invoke(0, html);
                return html;
            }

            var instrumented = InstrumentForRender(source);

            // STREAMING (WPF): el subproceso de Python transmite su stdout LÍNEA POR LÍNEA
            // y cada una se manda al WebView2 apenas llega → output progresivo, no todo de golpe.
            if (StreamingMode && StatementCompleted != null)
            {
                var acc = new StringBuilder();
                var (stderrS, okS) = RealPython.ExecuteStreaming(instrumented, ln =>
                {
                    var chunk = RenderStdoutLine(ln);
                    if (chunk.Length == 0) return;
                    acc.Append(chunk);
                    StatementCompleted.Invoke(0, chunk);   // ← chunk vivo al panel de output
                });
                if (!okS && !string.IsNullOrEmpty(stderrS))
                {
                    var errHtml = "<p class=\"err\"><span style=\"white-space:pre-wrap\">" +
                        WebUtility.HtmlEncode(stderrS.TrimEnd('\n')) + "</span></p>\n";
                    acc.Append(errHtml);
                    StatementCompleted.Invoke(0, errHtml);
                }
                var fullS = acc.Length > 0 ? acc.ToString()
                    : "<p class=\"line\"><span class=\"eq\"><i>(sin salida)</i></span></p>\n";
                ScriptFinished?.Invoke(fullS);
                return fullS;
            }

            // BATCH (CLI / no streaming): todo de una.
            var (stdout, stderr, ok) = RealPython.Execute(instrumented);
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(stdout))
                sb.Append(RenderStdout(stdout));
            if (!ok && !string.IsNullOrEmpty(stderr))
            {
                sb.Append("<p class=\"err\"><span style=\"white-space:pre-wrap\">");
                sb.Append(WebUtility.HtmlEncode(stderr.TrimEnd('\n')));
                sb.Append("</span></p>\n");
            }
            if (sb.Length == 0)
                sb.Append("<p class=\"line\"><span class=\"eq\"><i>(sin salida)</i></span></p>\n");
            var full = sb.ToString();
            ScriptFinished?.Invoke(full);
            return full;
        }

        /// <summary>Renderiza UNA línea de stdout de python (para streaming): __CPSPY_IMG__→&lt;img&gt;,
        /// __CPSPY_HTML__→HTML crudo, resto→texto pre-wrap. Líneas en blanco → "".</summary>
        private string RenderStdoutLine(string ln)
        {
            // Bloque HTML MULTI-LÍNEA (visor WebGL/canvas 3D): estado persistente entre líneas del stream.
            if (_htmlBlock)
            {
                if (ln.StartsWith(HtmlEnd, StringComparison.Ordinal)) { _htmlBlock = false; return ""; }
                return ln + "\n";   // crudo
            }
            if (ln.StartsWith(HtmlBegin, StringComparison.Ordinal)) { _htmlBlock = true; return ""; }
            if (ln.StartsWith(ImgMarker, StringComparison.Ordinal))
            {
                var b64 = ln.Substring(ImgMarker.Length).Trim();
                return b64.Length == 0 ? "" : ImgTag(b64, "png");
            }
            if (ln.StartsWith(GifMarker, StringComparison.Ordinal))
            {
                var b64 = ln.Substring(GifMarker.Length).Trim();
                return b64.Length == 0 ? "" : ImgTag(b64, "gif");
            }
            if (ln.StartsWith(HtmlMarker, StringComparison.Ordinal))
                return ln.Substring(HtmlMarker.Length) + "\n";
            if (ln.TrimEnd().Length == 0) return "";
            return "<p class=\"line\"><span class=\"eq\"><span style=\"white-space:pre-wrap\">" +
                WebUtility.HtmlEncode(ln) + "</span></span></p>\n";
        }

        // Marcadores que el script Python puede imprimir para enriquecer el reporte:
        //   print("__CPSPY_IMG__:"  + base64png)   → imagen PNG embebida
        //   print("__CPSPY_HTML__:" + htmlCrudo)    → HTML con markup Calcpad
        //                                             (.eq, var, .mat, <table>...) SIN escapar
        // Así los valores/tablas se ven como worksheet Calcpad (no texto plano).
        private const string ImgMarker = "__CPSPY_IMG__:";
        private const string GifMarker = "__CPSPY_GIF__:";   // animación → GIF embebido
        private const string HtmlMarker = "__CPSPY_HTML__:";
        private const string HtmlBegin = "__CPSPY_HTML_BEGIN__";   // bloque HTML MULTI-LÍNEA (visor WebGL, etc.)
        private const string HtmlEnd = "__CPSPY_HTML_END__";

        /// <summary>&lt;img&gt; centrado con data-uri base64 del tipo dado (png/gif).</summary>
        private static string ImgTag(string b64, string kind) =>
            "<p class=\"line\" style=\"text-align:center\"><img src=\"data:image/" + kind +
            ";base64," + b64 + "\" style=\"max-width:100%;height:auto\"/></p>\n";

        // Estado del bloque HTML multi-línea (__CPSPY_HTML_BEGIN__..._END__). Es CAMPO (no local) para
        // que PERSISTA entre llamadas: el motor embebido hace FlushDisp por cada print() y el streaming
        // WPF procesa línea por línea, así que el estado debe sobrevivir a cada chunk.
        private bool _htmlBlock;

        /// <summary>Renderiza el stdout de python: texto plano como bloque pre-wrap,
        /// __CPSPY_IMG__ como &lt;img&gt;, y __CPSPY_HTML__ como HTML crudo (markup Calcpad).</summary>
        private string RenderStdout(string stdout)
        {
            var sb = new StringBuilder();
            var lines = stdout.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var text = new StringBuilder();
            void FlushText()
            {
                if (text.Length == 0) return;
                var t = text.ToString().TrimEnd('\n');
                if (t.Length > 0)
                    sb.Append("<p class=\"line\"><span class=\"eq\"><span style=\"white-space:pre-wrap\">")
                      .Append(WebUtility.HtmlEncode(t))
                      .Append("</span></span></p>\n");
                text.Clear();
            }
            foreach (var ln in lines)
            {
                // Bloque HTML MULTI-LÍNEA: entre __CPSPY_HTML_BEGIN__ y __CPSPY_HTML_END__ todo va CRUDO
                // (para visores WebGL/canvas 3D con saltos de línea, que el single-line __CPSPY_HTML__ escapaba).
                if (_htmlBlock)
                {
                    if (ln.StartsWith(HtmlEnd, StringComparison.Ordinal)) { _htmlBlock = false; }
                    else sb.Append(ln).Append('\n');
                    continue;
                }
                if (ln.StartsWith(HtmlBegin, StringComparison.Ordinal)) { FlushText(); _htmlBlock = true; continue; }
                if (ln.StartsWith(ImgMarker, StringComparison.Ordinal))
                {
                    FlushText();
                    var b64 = ln.Substring(ImgMarker.Length).Trim();
                    if (b64.Length > 0) sb.Append(ImgTag(b64, "png"));
                }
                else if (ln.StartsWith(GifMarker, StringComparison.Ordinal))
                {
                    FlushText();
                    var b64 = ln.Substring(GifMarker.Length).Trim();
                    if (b64.Length > 0) sb.Append(ImgTag(b64, "gif"));
                }
                else if (ln.StartsWith(HtmlMarker, StringComparison.Ordinal))
                {
                    FlushText();
                    sb.Append(ln.Substring(HtmlMarker.Length)).Append('\n');  // HTML crudo (markup Calcpad)
                }
                else
                {
                    text.Append(ln).Append('\n');
                }
            }
            FlushText();
            return sb.ToString();
        }

        private static string ErrorHtml(string msg) =>
            $"<p class=\"err\">{WebUtility.HtmlEncode(msg)}</p>\n";

        // ===================================================================
        //  INSTRUMENTACIÓN PARA RENDER EN EL FALLBACK (python real)
        //  El motor nativo auto-renderiza cada asignación; el python real solo
        //  da stdout. Para que las variables se VEAN (como worksheet Calcpad),
        //  añadimos un footer que vuelca los nombres top-level como markup
        //  via el marcador __CPSPY_HTML__ (lo recoge RenderStdout).
        // ===================================================================
        private static readonly HashSet<string> _pyKw = new()
        {
            "if","elif","else","for","while","def","class","return","import","from","as",
            "in","not","and","or","None","True","False","pass","break","continue","with",
            "try","except","finally","raise","lambda","global","nonlocal","del","assert",
            "yield","print","async","await"
        };

        private static string InstrumentForRender(string source)
        {
            var names = new List<string>();
            var seen = new HashSet<string>();
            foreach (var raw in source.Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.Length == 0 || char.IsWhiteSpace(raw[0])) continue; // indentado/blank → no top-level
                var m = System.Text.RegularExpressions.Regex.Match(
                    raw, @"^([A-Za-z_][A-Za-z0-9_]*)\s*(?::[^=]+)?=(?!=)");
                if (!m.Success) continue;
                var n = m.Groups[1].Value;
                if (_pyKw.Contains(n)) continue;
                if (n.StartsWith("_")) continue;   // privados/internos → no volcar
                if (seen.Add(n)) names.Add(n);
            }
            bool usesMpl = source.Contains("matplotlib");
            bool usesPv = source.Contains("pyvista") && !source.Contains("pyvistaqt") && !source.Contains("QtInteractor");
            bool hasVisibleComments = source.Contains("#'") || source.Contains("#\"");
            // #noauto / #solografica → NO auto-renderizar variables (solo prints/figuras explícitos,
            // como en Python real). Son comentarios válidos de Python (no rompen).
            // #show <var> en cualquier línea → modo OPT-IN: por defecto NO se auto-renderiza
            // nada; SOLO lo marcado con #show se muestra (inline). #noauto/#solografica también
            // activan opt-in (sin mostrar nada salvo prints/figuras explícitos).
            bool hasShow = System.Text.RegularExpressions.Regex.IsMatch(source, @"(?m)^\s*#show\s+[A-Za-z_]");
            bool noAuto = hasShow || source.Contains("#noauto") || source.Contains("#solografica");
            // #noprint → Suite-Py NO ejecuta los print() del usuario (en Python real SÍ corren,
            // porque #noprint es solo un comentario). Asi no hay que comentar cada print.
            bool noPrint = source.Contains("#noprint");
            // #nofig → Suite-Py NO embebe NINGUNA figura de matplotlib (en Python real igual abren ventana).
            bool noFig = usesMpl && source.Contains("#nofig");
            // pandas.DataFrame se auto-renderiza como tabla HTML (estilo notebook) → inyectar helpers.
            bool usesTable = source.Contains("DataFrame") || source.Contains("read_csv") || source.Contains("read_excel");
            bool needsTransform = hasVisibleComments || hasShow || noPrint || usesTable || source.Contains("#cp") || source.Contains("#nosuite");
            if ((names.Count == 0 || noAuto) && !usesMpl && !usesPv && !needsTransform) return source;
            var sb = new StringBuilder();
            sb.Append("_realprint = print\n");      // print REAL para uso interno (markers/figuras)
            if (usesMpl) sb.Append(_mplPreamble);   // captura figuras matplotlib (Agg + patch show)
            if (usesPv) sb.Append(_pvPreamble);     // captura PyVista (Plotter.show → screenshot off-screen)
            sb.Append(_pyHelpers);                  // defs de _cpspy_* ANTES (para #show inline)
            if (noPrint) sb.Append("print = lambda *a, **k: None  # #noprint: Suite-Py omite prints del usuario\n");
            if (noFig) sb.Append("_cpspy_flush_figs = lambda: None  # #nofig: Suite-Py no embebe figuras\n");
            sb.Append(TransformVisibleComments(source));   // #'/#" texto + #show <var> inline + #nosuite
            if (!noAuto)                            // modo clásico: volcar TODAS las variables al final
                foreach (var n in names)
                    sb.Append("try:\n    _cpspy_emit('").Append(n).Append("', ").Append(n)
                      .Append(")\nexcept Exception:\n    pass\n");
            if (usesMpl) sb.Append("try:\n    _cpspy_flush_figs()\nexcept Exception:\n    pass\n");
            return sb.ToString();
        }

        // Transforma comentarios VISIBLES (estilo Ned) en prints que emiten HTML, para que
        // se vean también en scripts de Python REAL (no solo en el motor nativo):
        //   #'texto  → párrafo visible        #"titulo → encabezado h3
        // El resto de comentarios (# ...) quedan invisibles, como en Python.
        private static string TransformVisibleComments(string source)
        {
            var lines = source.Replace("\r\n", "\n").Split('\n');
            var outp = new StringBuilder();
            bool insideCp = false;   // dentro de un bloque  #cp[ … #cp]
            System.Collections.Generic.List<string> cpBlock = null;
            System.Collections.Generic.List<string> mdBuf = null;   // líneas #' consecutivas → Markdown multilínea
            string mdIndent = "";
            foreach (var raw in lines)
            {
                int i = 0;
                while (i < raw.Length && (raw[i] == ' ' || raw[i] == '\t')) i++;
                string rest = raw.Substring(i);
                // Al salir de un grupo de líneas #' consecutivas, volcar el Markdown acumulado (listas, tablas).
                if (mdBuf != null && !insideCp && !rest.StartsWith("#'"))
                { outp.Append(FlushMarkdown(mdBuf, mdIndent)); mdBuf = null; }
                // --- Calcpad (#cp): inline = simbólico; bloque #cp[ … #cp] = Calcpad completo evaluado ---
                if (rest.StartsWith("#cp["))                     // abre bloque
                { insideCp = true; cpBlock = new System.Collections.Generic.List<string>(); continue; }
                if (rest.StartsWith("#cp]"))                     // cierra bloque → Calcpad completo ($Sum, #for, #noc, sqrt)
                {
                    insideCp = false;
                    EmitCalcpadBlock(outp, raw.Substring(0, i), string.Join("\n", cpBlock ?? new System.Collections.Generic.List<string>()));
                    cpBlock = null;
                    continue;
                }
                if (insideCp)                                    // acumular la línea (quitar UN '#')
                {
                    cpBlock?.Add(rest.StartsWith("#") ? rest.Substring(1) : rest);
                    continue;
                }
                if (rest.StartsWith("#cp ") || rest.StartsWith("#cp\t"))   // inline: simbólico (#sym)
                {
                    EmitCalcpadSymbolic(outp, raw.Substring(0, i), rest.Substring(3).Trim());
                    continue;
                }
                // línea de CÓDIGO con marcador trailing #nosuite → NO se ejecuta en Suite-Py
                // (se vuelve 'pass'); en Python real (IDLE) la línea corre normal porque el
                // #nosuite es solo un comentario al final. Ideal para prints de depuración.
                if (!rest.StartsWith("#") && rest.Contains("#nosuite"))
                {
                    outp.Append(raw.Substring(0, i)).Append("pass\n");
                    continue;
                }
                // #show <variable> → mostrar esa variable INLINE en este punto (opt-in)
                if (rest.StartsWith("#show"))
                {
                    string after = rest.Length > 5 ? rest.Substring(5).Trim() : "";
                    if (after.Length > 0 &&
                        System.Text.RegularExpressions.Regex.IsMatch(after, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                    {
                        string ind = raw.Substring(0, i);
                        outp.Append(ind).Append("try:\n")
                            .Append(ind).Append("    _cpspy_emit('").Append(after).Append("', ").Append(after).Append(")\n")
                            .Append(ind).Append("except Exception:\n")
                            .Append(ind).Append("    pass\n");
                        continue;
                    }
                    outp.Append(raw).Append('\n');   // "#show" sin nombre → comentario normal
                    continue;
                }
                if (rest.StartsWith("#\""))          // #" → título h3 (encabezado destacado)
                {
                    string content = rest.Substring(2).Trim();
                    if (content.Length == 0) { outp.Append('\n'); continue; }
                    string enc = WebUtility.HtmlEncode(content).Replace("'", "&#39;");
                    outp.Append(raw.Substring(0, i))
                        .Append("_realprint('__CPSPY_HTML__:").Append(("<h3>" + enc + "</h3>").Replace("\\", "\\\\")).Append("')\n");
                }
                else if (rest.StartsWith("#'"))       // #' → texto MARKDOWN (acumular para multilínea: listas, tablas)
                {
                    if (mdBuf == null) { mdBuf = new System.Collections.Generic.List<string>(); mdIndent = raw.Substring(0, i); }
                    mdBuf.Add(rest.Substring(2).TrimStart());
                }
                else outp.Append(raw).Append('\n');
            }
            if (mdBuf != null) outp.Append(FlushMarkdown(mdBuf, mdIndent));   // grupo #' final
            return outp.ToString();
        }

        // Vuelca un grupo de líneas #' consecutivas como un solo bloque Markdown (para listas/tablas multilínea).
        private static string FlushMarkdown(System.Collections.Generic.List<string> mdLines, string indent)
        {
            string html = RenderMarkdown(string.Join("\n", mdLines));
            string pyStr = html.Replace("\\", "\\\\").Replace("'", "&#39;").Replace("\r", " ").Replace("\n", " ");
            return indent + "_realprint('__CPSPY_HTML__:" + pyStr + "')\n";
        }

        // #' → Markdown (Markdig, GFM: negrita **, cursiva *, tachado ~~, listas, tablas |, task lists, links).
        private static Markdig.MarkdownPipeline _mdPipe;
        private static string RenderMarkdown(string md)
        {
            try
            {
                _mdPipe ??= new Markdig.MarkdownPipelineBuilder()
                    .UseEmphasisExtras().UseListExtras().UsePipeTables().UseGridTables().UseAutoLinks().UseTaskLists().Build();
                string html = Markdig.Markdown.ToHtml(md, _mdPipe).Trim();
                // Un solo párrafo (línea inline) → envolver en el estilo de línea de Suite Py; listas/tablas/headings van tal cual.
                if (html.StartsWith("<p>", StringComparison.Ordinal) && html.EndsWith("</p>", StringComparison.Ordinal)
                    && html.IndexOf("<p>", 3, StringComparison.Ordinal) < 0)
                    return "<p class=\"line\"><span class=\"eq\">" + html.Substring(3, html.Length - 7) + "</span></p>";
                return html;
            }
            catch { return "<p class=\"line\"><span class=\"eq\">" + WebUtility.HtmlEncode(md) + "</span></p>"; }
        }

        // #cp — renderiza una expresión como fórmula de Calcpad (símbolos, SIN evaluar números)
        // y la emite como HTML en su posición. En Python real la línea #cp es un comentario.
        private static void EmitCalcpadSymbolic(StringBuilder outp, string indent, string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) { outp.Append('\n'); return; }
            string html = RenderCalcpadSymbolic(expr);
            string pyStr = html.Replace("\\", "\\\\").Replace("'", "&#39;").Replace("\r", " ").Replace("\n", " ");
            outp.Append(indent).Append("_realprint('__CPSPY_HTML__:").Append(pyStr).Append("')\n");
        }

        [ThreadStatic] private static Calcpad.Core.ExpressionParser _cpEp;
        private static string RenderCalcpadSymbolic(string expr)
        {
            try
            {
                _cpEp ??= new Calcpad.Core.ExpressionParser();
                _cpEp.Parse("#sym " + expr, calculate: true, getXml: true);   // #sym = simbólico (vars=símbolos, sqrt→√, $sum→Σ)
                string html = _cpEp.HtmlResult ?? "";
                int b = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);   // quedarnos con el cuerpo si vino documento completo
                if (b >= 0)
                {
                    int s = html.IndexOf('>', b) + 1;
                    int e = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                    if (e > s) html = html.Substring(s, e - s);
                }
                return html.Trim().Replace("<i>∂</i>", "<i style=\"color:#C080F0\">∂</i>");
            }
            catch { return System.Net.WebUtility.HtmlEncode(expr); }
        }

        // #cp[ … #cp] bloque → Calcpad COMPLETO evaluado: $Sum{f @ k=a:b}, $Product, #for/#loop, #noc, sqrt, subíndices.
        private static void EmitCalcpadBlock(StringBuilder outp, string indent, string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            string html = RenderCalcpadBlock(code);
            string pyStr = html.Replace("\\", "\\\\").Replace("'", "&#39;").Replace("\r", " ").Replace("\n", " ");
            outp.Append(indent).Append("_realprint('__CPSPY_HTML__:").Append(pyStr).Append("')\n");
        }

        private static string RenderCalcpadBlock(string code)
        {
            try
            {
                _cpEp ??= new Calcpad.Core.ExpressionParser();
                _cpEp.Parse(code, calculate: true, getXml: true);   // bloque completo, modo evaluación (NO #sym)
                string html = _cpEp.HtmlResult ?? "";
                int b = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
                if (b >= 0)
                {
                    int s = html.IndexOf('>', b) + 1;
                    int e = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                    if (e > s) html = html.Substring(s, e - s);
                }
                return html.Trim().Replace("<i>∂</i>", "<i style=\"color:#C080F0\">∂</i>");
            }
            catch { return System.Net.WebUtility.HtmlEncode(code); }
        }

        // Captura de figuras matplotlib: backend Agg + plt.show() -> PNG base64 (__CPSPY_IMG__).
        // Asi el .py es Python PURO (plt.show()) y la figura aparece embebida en Calcpad-Py;
        // ejecutado con python real, plt.show() abre la ventana normal.
        //
        // ANIMACIONES: FuncAnimation/ArtistAnimation se interceptan (se registran al crearse)
        // y al flush se guardan como GIF (PillowWriter) embebido via __CPSPY_GIF__. La figura
        // de cada animación NO se vuelca también como PNG estático (se evita el duplicado).
        // Si Pillow no está, cae a guardar el primer frame como PNG.
        private const string _mplPreamble = @"
try:
    import matplotlib as _mpl
    _mpl.use(""Agg"")
    import matplotlib.pyplot as _pltcap
    import matplotlib.animation as _animcap
    import io as _iocap, base64 as _b64cap, os as _oscap, tempfile as _tmpcap
    import numpy as _npm
    # --- 3D INTERACTIVO: interceptar plot_surface(X,Y,Z) y mandarlo al canvas GL3
    #     (girar/mover/hover), en vez de PNG estatico. Igual idea que PyVista. ---
    try:
        from mpl_toolkits.mplot3d import Axes3D as _Ax3
        _cpspy_surfs = {}
        _orig_psurf = _Ax3.plot_surface
        def _cpspy_psurf(self, X, Y, Z, *a, **k):
            try: _cpspy_surfs.setdefault(id(self.figure), []).append((self, _npm.asarray(X, float), _npm.asarray(Y, float), _npm.asarray(Z, float)))
            except Exception: pass
            return _orig_psurf(self, X, Y, Z, *a, **k)
        _Ax3.plot_surface = _cpspy_psurf
        _cpspy_lines3 = {}
        _orig_plot3 = _Ax3.plot
        def _cpspy_plot3(self, *a, **k):   # captura líneas 3D (ej. columnas) → GL3.line3
            try:
                if len(a) >= 3 and hasattr(a[2], '__len__') and not isinstance(a[2], str):
                    _cpspy_lines3.setdefault(id(self.figure), []).append((_npm.asarray(a[0], float), _npm.asarray(a[1], float), _npm.asarray(a[2], float)))
            except Exception: pass
            return _orig_plot3(self, *a, **k)
        _Ax3.plot = _cpspy_plot3
    except Exception:
        _cpspy_surfs = {}
        _cpspy_lines3 = {}
    # Render unificado a GL3: superficie 3D (_flat=False) o campo plano 2D estilo
    # contorno visto desde arriba (_flat=True). Ambos con hover (datapoint) del valor.
    def _cpspy_gl3(_fid, X, Y, Z, _flat, _title, _lines=None, _wval=None):
        _q = chr(34)
        nr, nc = Z.shape
        _st = max(1, int(round(max(nr, nc) / 40.0)))   # ~40×40 celdas (ágil + suave)
        X = X[::_st, ::_st]; Y = Y[::_st, ::_st]; Z = Z[::_st, ::_st]
        C = Z if _wval is None else _wval[::_st, ::_st]   # escalar de COLOR/VALOR (deflexión si se pasa)
        nr, nc = Z.shape
        _zmin = float(_npm.nanmin(Z)); _zmax = float(_npm.nanmax(Z))           # geometría (eje Z)
        _cmin = float(_npm.nanmin(C)); _cmax = float(_npm.nanmax(C)); _rng = (_cmax - _cmin) or 1.0
        def _tv(v): return (float(v) - _cmin) / _rng
        def _zc(v): return 0.0 if _flat else float(v)
        _calls = []
        for i in range(nr - 1):
            for j in range(nc - 1):
                _arr = '[%.4f,%.4f,%.4f,%.4f,%.4f,%.4f,%.4f,%.4f,%.4f,%.4f,%.4f,%.4f]' % (X[i,j],Y[i,j],_zc(Z[i,j]),X[i+1,j],Y[i+1,j],_zc(Z[i+1,j]),X[i+1,j+1],Y[i+1,j+1],_zc(Z[i+1,j+1]),X[i,j+1],Y[i,j+1],_zc(Z[i,j+1]))
                _calls.append('GL3.fill3(%s,%.4f,%.4f,%.4f,%.4f);' % (_arr, _tv(C[i,j]), _tv(C[i+1,j]), _tv(C[i+1,j+1]), _tv(C[i,j+1])))
        _lcalls = []; _lzmin = _zmin
        if _lines and not _flat:           # columnas u otras líneas 3D → GL3.line3
            for (_lx, _ly, _lz) in _lines:
                for _m in range(len(_lx) - 1):
                    _lcalls.append('GL3.line3(%.4f,%.4f,%.4f,%.4f,%.4f,%.4f,%s);' % (float(_lx[_m]),float(_ly[_m]),float(_lz[_m]),float(_lx[_m+1]),float(_ly[_m+1]),float(_lz[_m+1]), _q + '#333333' + _q))
                try: _lzmin = min(_lzmin, float(_npm.min(_lz)))
                except Exception: pass
        _dps = []; _stp = max(1, (nr*nc)//4500); _kk = 0   # denso → hover continuo en toda la superficie
        for i in range(nr):
            for j in range(nc):
                if _kk % _stp == 0: _dps.append('GL3.datapoint(%.4f,%.4f,%.4f,%.6g);' % (X[i,j],Y[i,j],_zc(Z[i,j]),C[i,j]))
                _kk += 1
        if _flat: _zlo = -0.5; _zhi = 0.5; _vw = 'GL3.view3(0,89);'
        else: _zlo = min(_zmin, _lzmin); _zhi = _zmax; _vw = 'GL3.view3(40,22);'
        _gljs = open(_oscap.path.join(_tmpcap.gettempdir(), 'cpspy_glplot.min.js'), encoding='utf-8').read()
        _js = ('GL3.figure3(' + _q + _fid + _q + ',780,520);GL3.etabs=false;' + _vw   # etabs=false → jet_r
               + 'GL3.axis3(%.4f,%.4f,%.4f,%.4f,%.4f,%.4f);' % (float(X.min()),float(X.max()),float(Y.min()),float(Y.max()),_zlo,_zhi)
               + ''.join(_calls) + ''.join(_lcalls) + 'GL3.datatip(' + _q + 'valor' + _q + ');' + ''.join(_dps)
               + 'GL3.render3();GL3.colorbar3(%.4f,%.4f,320);' % (_cmin, _cmax))
        _cap = ('<div style=font-weight:bold;color:#1a4f7a;margin-top:6px>' + _title + '</div>') if _title else ''
        _html = '<div style=text-align:center>' + _cap + '<script>if(!window.GL3){' + _gljs + '}</script><script>(function(){' + _js + '})();</script></div>'
        _realprint('__CPSPY_HTML__:' + _html.replace(chr(10), ' ').replace(chr(13), ' '))
    def _cpspy_emit_gl3(_fig, _surfs):
        _ax, X, Y, Z = _surfs[0]
        _w = None
        try:                       # campo escalar de color/valor (deflexión w) si el script lo guardó
            _s3 = getattr(_ax, '_cpspy_surf3d', None)
            if _s3 is not None and len(_s3) >= 4: _w = _npm.asarray(_s3[3], float)
        except Exception: _w = None
        _cpspy_gl3('mpl', X, Y, Z, False, '', _cpspy_lines3.get(id(_fig), []), _w)
    # --- 2D INTERACTIVO: interceptar contourf(X,Y,Z) → campo plano GL3 con hover ---
    _cpspy_contours = {}
    try:
        import matplotlib.axes as _maxes
        _orig_cf = _maxes.Axes.contourf
        def _cpspy_cf(self, *a, **k):
            try:
                if len(a) >= 3 and hasattr(a[2], 'shape'):
                    _Xc, _Yc, _Zc = a[0], a[1], a[2]
                elif len(a) >= 1:
                    _Zc = _npm.asarray(a[0], float); _ny, _nx = _Zc.shape
                    _Yc, _Xc = _npm.meshgrid(range(_ny), range(_nx), indexing='ij')
                else:
                    return _orig_cf(self, *a, **k)
                _cpspy_contours.setdefault(id(self.figure), []).append((self, _npm.asarray(_Xc, float), _npm.asarray(_Yc, float), _npm.asarray(_Zc, float)))
            except Exception: pass
            return _orig_cf(self, *a, **k)
        _maxes.Axes.contourf = _cpspy_cf
    except Exception:
        pass
    # Canvas 2D de MAPA DE CALOR (heatmap jet_r + colorbar + hover por píxel).
    _CPMAPJS = '''window.CPMAP={plot:function(id,d){var cv=document.getElementById(id);if(!cv)return;var ctx=cv.getContext('2d');var W=cv.width,H=cv.height,mL=44,mR=66,mT=22,mB=30;var pw=W-mL-mR,ph=H-mT-mB;var nr=d.nr,nc=d.nc,z=d.z,zmin=d.zmin,zmax=d.zmax,rng=(zmax-zmin)||1;var x0=d.x0,x1=d.x1,y0=d.y0,y1=d.y1;function jetr(t){t=Math.max(0,Math.min(1,t));var u=1-t;function c(q){return Math.max(0,Math.min(1,1.5-Math.abs(4*u-q)));}return 'rgb('+Math.round(c(3)*255)+','+Math.round(c(2)*255)+','+Math.round(c(1)*255)+')';}function draw(hv){ctx.clearRect(0,0,W,H);ctx.fillStyle='#fff';ctx.fillRect(0,0,W,H);ctx.fillStyle='#1a4f7a';ctx.font='bold 12px sans-serif';ctx.textAlign='center';ctx.fillText(d.title,mL+pw/2,14);var cw=pw/nc,chh=ph/nr,r,c2,t;for(r=0;r<nr;r++){for(c2=0;c2<nc;c2++){if(z[r*nc+c2]==null)continue;t=(z[r*nc+c2]-zmin)/rng;ctx.fillStyle=jetr(t);ctx.fillRect(mL+c2*cw,mT+ph-(r+1)*chh,cw+0.7,chh+0.7);}}ctx.strokeStyle='#444';ctx.strokeRect(mL,mT,pw,ph);ctx.fillStyle='#555';ctx.font='9px sans-serif';ctx.textAlign='center';var i,vx,vy;for(i=0;i<=5;i++){vx=x0+(x1-x0)*i/5;ctx.fillText(vx.toFixed(1),mL+pw*i/5,mT+ph+12);}ctx.textAlign='right';for(i=0;i<=5;i++){vy=y0+(y1-y0)*i/5;ctx.fillText(vy.toFixed(1),mL-4,mT+ph-ph*i/5+3);}var cbx=mL+pw+12,cbw=13;for(i=0;i<ph;i++){ctx.fillStyle=jetr(i/ph);ctx.fillRect(cbx,mT+ph-i,cbw,1.6);}ctx.strokeStyle='#444';ctx.strokeRect(cbx,mT,cbw,ph);ctx.fillStyle='#333';ctx.textAlign='left';ctx.font='8px sans-serif';for(i=0;i<=4;i++){ctx.fillText((zmin+rng*i/4).toFixed(2),cbx+cbw+2,mT+ph-ph*i/4+3);}if(hv){ctx.strokeStyle='#000';ctx.lineWidth=1.2;ctx.beginPath();ctx.arc(hv.px,hv.py,4,0,6.2832);ctx.stroke();var s='x='+hv.x.toFixed(2)+' y='+hv.y.toFixed(2)+'  '+hv.v.toFixed(4);ctx.font='10px sans-serif';var tw=ctx.measureText(s).width;var tx=hv.px+8,ty=hv.py-8;if(tx+tw+8>mL+pw)tx=tx-tw-18;if(ty<mT+10)ty=hv.py+18;ctx.fillStyle='rgba(255,255,224,0.97)';ctx.fillRect(tx-4,ty-11,tw+8,15);ctx.strokeStyle='#888';ctx.strokeRect(tx-4,ty-11,tw+8,15);ctx.fillStyle='#111';ctx.textAlign='left';ctx.fillText(s,tx,ty);}}draw(null);cv.onmousemove=function(e){var rb=cv.getBoundingClientRect();var mx=e.clientX-rb.left,my=e.clientY-rb.top;if(mx<mL||mx>mL+pw||my<mT||my>mT+ph){draw(null);return;}var dx=x0+(mx-mL)/pw*(x1-x0),dy=y1-(my-mT)/ph*(y1-y0);var c2=Math.min(nc-1,Math.max(0,Math.round((dx-x0)/((x1-x0)||1)*(nc-1))));var r=Math.min(nr-1,Math.max(0,Math.round((dy-y0)/((y1-y0)||1)*(nr-1))));if(z[r*nc+c2]==null){draw(null);return;}draw({px:mx,py:my,x:dx,y:dy,v:z[r*nc+c2]});};cv.onmouseleave=function(){draw(null);};}};'''
    def _cpspy_emit_contours(_fig, _flds):
        _Q = chr(39)
        _parts = ['<script>if(!window.CPMAP){' + _CPMAPJS + '}</script>']
        for _ix in range(len(_flds)):
            _ax, X, Y, Z = _flds[_ix]
            try: _ax0x = abs(float(X[-1, 0] - X[0, 0])) >= abs(float(Y[-1, 0] - Y[0, 0]))
            except Exception: _ax0x = True
            if _ax0x: _xs = X[:, 0]; _ys = Y[0, :]; _Zm = Z.T
            else: _xs = X[0, :]; _ys = Y[:, 0]; _Zm = Z
            _nr, _nc = _Zm.shape
            _st = max(1, int(round(max(_nr, _nc) / 60.0)))
            _Zm = _Zm[::_st, ::_st]; _nr, _nc = _Zm.shape
            _x0 = float(_xs.min()); _x1 = float(_xs.max()); _y0 = float(_ys.min()); _y1 = float(_ys.max())
            _zmin = float(_npm.nanmin(_Zm)); _zmax = float(_npm.nanmax(_Zm))
            _zflat = ','.join(('null' if v != v else '%.5g' % v) for v in _Zm.ravel())   # NaN (fuera del dominio) -> null, no 'nan' (rompia el JS)
            _ttl = (_ax.get_title() or '').replace(_Q, ' ')
            _cid = 'cpmap_' + str(_ix)
            _d = ('{title:' + _Q + _ttl + _Q + ',nr:%d,nc:%d,x0:%.4f,x1:%.4f,y0:%.4f,y1:%.4f,zmin:%.6g,zmax:%.6g,z:[%s]}'
                  % (_nr, _nc, _x0, _x1, _y0, _y1, _zmin, _zmax, _zflat))
            _parts.append('<canvas id=' + _cid + ' width=420 height=320 style=margin:4px;vertical-align:top></canvas>')
            _parts.append('<script>CPMAP.plot(' + _Q + _cid + _Q + ',' + _d + ');</script>')
        _realprint('__CPSPY_HTML__:<div style=text-align:center>' + ''.join(_parts).replace(chr(10), ' ') + '</div>')
    # --- CANVAS 2D de LINEAS (diagramas) con hover propio (sin librerías) ---
    _CP2DJS = '''window.CP2D={plot:function(id,d){var cv=document.getElementById(id);if(!cv)return;var ctx=cv.getContext('2d');var W=cv.width,H=cv.height,mL=50,mR=12,mT=26,mB=34;var pw=W-mL-mR,ph=H-mT-mB;var x0=d.xmin,x1=d.xmax,y0=d.ymin,y1=d.ymax;if(x1==x0)x1=x0+1;if(y1==y0)y1=y0+1;function sx(x){return mL+(x-x0)/(x1-x0)*pw;}function sy(y){return mT+ph-(y-y0)/(y1-y0)*ph;}function draw(hv){ctx.clearRect(0,0,W,H);ctx.fillStyle='#fff';ctx.fillRect(0,0,W,H);ctx.fillStyle='#1a4f7a';ctx.font='bold 12px sans-serif';ctx.textAlign='center';ctx.fillText(d.title,W/2,15);ctx.strokeStyle='#ececec';ctx.fillStyle='#555';ctx.font='9px sans-serif';var i,g,v;ctx.textAlign='center';for(i=0;i<=5;i++){v=x0+(x1-x0)*i/5;g=sx(v);ctx.beginPath();ctx.moveTo(g,mT);ctx.lineTo(g,mT+ph);ctx.stroke();ctx.fillText(v.toFixed(1),g,mT+ph+12);}ctx.textAlign='right';for(i=0;i<=5;i++){v=y0+(y1-y0)*i/5;g=sy(v);ctx.beginPath();ctx.moveTo(mL,g);ctx.lineTo(mL+pw,g);ctx.stroke();ctx.fillText(v.toFixed(1),mL-5,g+3);}ctx.fillStyle='#555';ctx.textAlign='center';ctx.font='10px sans-serif';ctx.fillText(d.xlabel,mL+pw/2,H-4);if(y0<0&&y1>0){ctx.strokeStyle='#999';ctx.beginPath();var z=sy(0);ctx.moveTo(mL,z);ctx.lineTo(mL+pw,z);ctx.stroke();}var s,k,se;for(s=0;s<d.series.length;s++){se=d.series[s];ctx.strokeStyle=se.c;ctx.fillStyle=se.c;ctx.lineWidth=1.8;ctx.beginPath();for(k=0;k<se.x.length;k++){if(k==0)ctx.moveTo(sx(se.x[k]),sy(se.y[k]));else ctx.lineTo(sx(se.x[k]),sy(se.y[k]));}ctx.stroke();for(k=0;k<se.x.length;k++){ctx.beginPath();ctx.arc(sx(se.x[k]),sy(se.y[k]),2.3,0,6.2832);ctx.fill();}}ctx.font='9px sans-serif';ctx.textAlign='left';var ly=mT+5;for(s=0;s<d.series.length;s++){if(d.series[s].l){ctx.fillStyle=d.series[s].c;ctx.fillRect(mL+5,ly+s*12,9,3);ctx.fillStyle='#333';ctx.fillText(d.series[s].l,mL+18,ly+4+s*12);}}if(hv){ctx.strokeStyle='#222';ctx.beginPath();ctx.arc(sx(hv.x),sy(hv.y),4,0,6.2832);ctx.stroke();var tx=sx(hv.x)+8,ty=sy(hv.y)-8,t=hv.l+' x='+hv.x.toFixed(2)+'  y='+hv.y.toFixed(3);ctx.font='10px sans-serif';var tw=ctx.measureText(t).width;if(tx+tw+8>W)tx=tx-tw-18;ctx.fillStyle='rgba(255,255,224,0.96)';ctx.fillRect(tx-4,ty-11,tw+8,15);ctx.strokeStyle='#999';ctx.strokeRect(tx-4,ty-11,tw+8,15);ctx.fillStyle='#222';ctx.textAlign='left';ctx.fillText(t,tx,ty);}}draw(null);cv.onmousemove=function(e){var r=cv.getBoundingClientRect();var mx=e.clientX-r.left,my=e.clientY-r.top;if(mx<mL||mx>mL+pw||my<mT||my>mT+ph){draw(null);return;}var xd=x0+(mx-mL)/pw*(x1-x0);var b=null,bd=1e18,s,k,se,yi;for(s=0;s<d.series.length;s++){se=d.series[s];if(se.x.length<2||xd<se.x[0]||xd>se.x[se.x.length-1])continue;yi=se.y[se.y.length-1];for(k=0;k<se.x.length-1;k++){if(xd>=se.x[k]&&xd<=se.x[k+1]){var t=(xd-se.x[k])/((se.x[k+1]-se.x[k])||1);yi=se.y[k]+t*(se.y[k+1]-se.y[k]);break;}}var dy=sy(yi)-my,dd=dy*dy;if(dd<bd){bd=dd;b={x:xd,y:yi,l:se.l||''};}}if(b)draw(b);else draw(null);};cv.onmouseleave=function(){draw(null);};}};'''
    def _cpspy_is_drawing(_fig):
        # Un contourf acompanado de geometria (LineCollection, patches, text, lineas, aspecto fijo)
        # es un DIBUJO (p.ej. el talud con su malla, suelos y cargas): se embebe el PNG FIEL de
        # matplotlib, igual que Python puro. El canvas con hover queda para el contourf solo.
        try: from matplotlib.contour import ContourSet as _CSet
        except Exception: _CSet = ()
        for _axc in _fig.get_axes():
            try:
                if len(_axc.patches) > 0 or len(_axc.texts) > 0 or len(_axc.get_lines()) > 0: return True
                for _cc in _axc.collections:
                    if not isinstance(_cc, _CSet): return True
                if _axc.get_aspect() != 'auto': return True
            except Exception: pass
        return False
    def _cpspy_emit_lines(_fig):
        import matplotlib.colors as _mcol
        _Q = chr(39)
        # CP2D es SOLO para gráficas de línea x-y. Si la figura es un dibujo de
        # malla/geometría (polígonos ax.fill→patches, scatter→collections, o aspecto
        # fijo set_aspect('equal')) NO se convierte → cae a PNG fiel (igual que Python).
        for _axc in _fig.get_axes():
            try:
                if len(_axc.patches) > 0 or len(_axc.collections) > 0: return False
                if _axc.get_aspect() != 'auto': return False
            except Exception: pass
        _axes = [ax for ax in _fig.get_axes() if len(ax.get_lines()) > 0]
        if not _axes: return False
        _parts = ['<script>if(!window.CP2D){' + _CP2DJS + '}</script>']
        _any = False
        for _ai in range(len(_axes)):
            ax = _axes[_ai]; _ser = []
            _xmn = 1e30; _xmx = -1e30; _ymn = 1e30; _ymx = -1e30
            for ln in ax.get_lines():
                xd = _npm.asarray(ln.get_xdata(), float); yd = _npm.asarray(ln.get_ydata(), float)
                if xd.size < 2: continue
                if xd.size == 2 and yd[0] == yd[1]: continue   # axhline (línea de cero)
                try: _c = _mcol.to_hex(ln.get_color())
                except Exception: _c = '#1f77b4'
                _lbl = ln.get_label(); _lbl = '' if _lbl.startswith('_') else _lbl.replace(_Q, ' ')
                _ser.append('{x:[' + ','.join('%.4f' % v for v in xd) + '],y:[' + ','.join('%.4f' % v for v in yd) + '],c:' + _Q + _c + _Q + ',l:' + _Q + _lbl + _Q + '}')
                _xmn = min(_xmn, float(xd.min())); _xmx = max(_xmx, float(xd.max()))
                _ymn = min(_ymn, float(yd.min())); _ymx = max(_ymx, float(yd.max()))
            if not _ser: continue
            _pad = (_ymx - _ymn) * 0.08 or 1.0
            _ttl = (ax.get_title() or '').replace(_Q, ' '); _xl = (ax.get_xlabel() or '').replace(_Q, ' ')
            _cid = 'cp2d_' + str(_ai)
            _d = ('{title:' + _Q + _ttl + _Q + ',xlabel:' + _Q + _xl + _Q
                  + ',xmin:%.4f,xmax:%.4f,ymin:%.4f,ymax:%.4f,series:[' % (_xmn, _xmx, _ymn - _pad, _ymx + _pad)
                  + ','.join(_ser) + ']}')
            _parts.append('<canvas id=' + _cid + ' width=380 height=300 style=margin:4px;vertical-align:top></canvas>')
            _parts.append('<script>CP2D.plot(' + _Q + _cid + _Q + ',' + _d + ');</script>')
            _any = True
        if not _any: return False
        _realprint('__CPSPY_HTML__:<div style=text-align:center>' + ''.join(_parts).replace(chr(10), ' ') + '</div>')
        return True
    _cpspy_anims = []
    def _cpspy_track_anim(_an):
        _cpspy_anims.append(_an); return _an
    _Orig_FuncAnim = _animcap.FuncAnimation
    class _CpspyFuncAnimation(_Orig_FuncAnim):
        def __init__(self, *a, **k):
            super().__init__(*a, **k); _cpspy_track_anim(self)
    _animcap.FuncAnimation = _CpspyFuncAnimation
    _pltcap.matplotlib.animation.FuncAnimation = _CpspyFuncAnimation
    _Orig_ArtistAnim = _animcap.ArtistAnimation
    class _CpspyArtistAnimation(_Orig_ArtistAnim):
        def __init__(self, *a, **k):
            super().__init__(*a, **k); _cpspy_track_anim(self)
    _animcap.ArtistAnimation = _CpspyArtistAnimation
    def _cpspy_anim_fps(_an):
        _iv = getattr(_an, '_interval', None)
        try: return max(1, round(1000.0 / float(_iv))) if _iv else 20
        except Exception: return 20
    def _cpspy_flush_figs():
        _anim_figs = set()
        for _an in _cpspy_anims:
            _fig = getattr(_an, '_fig', None)
            _gp = None
            try:
                # PillowWriter exige una RUTA de archivo (no acepta BytesIO) → temp .gif
                _fd, _gp = _tmpcap.mkstemp(suffix="".gif"", prefix=""cpspy_anim_""); _oscap.close(_fd)
                _an.save(_gp, writer=_animcap.PillowWriter(fps=_cpspy_anim_fps(_an)))
                with open(_gp, ""rb"") as _gf:
                    _realprint(""__CPSPY_GIF__:"" + _b64cap.b64encode(_gf.read()).decode())
                if _fig is not None: _anim_figs.add(id(_fig))
            except Exception:
                pass  # sin Pillow → la figura cae como PNG estático abajo
            finally:
                try:
                    if _gp and _oscap.path.exists(_gp): _oscap.remove(_gp)
                except Exception: pass
        _cpspy_anims.clear()
        for _n in _pltcap.get_fignums():
            _f = _pltcap.figure(_n)
            if id(_f) in _anim_figs: continue
            if id(_f) in _cpspy_surfs:    # figura con superficie 3D → canvas GL3 interactivo
                try: _cpspy_emit_gl3(_f, _cpspy_surfs[id(_f)])
                except Exception as _e3: _realprint('__CPSPY_HTML__:<p class=err>GL3 3D: ' + str(_e3) + '</p>')
                continue
            if id(_f) in _cpspy_contours and not _cpspy_is_drawing(_f):  # solo contornos → canvas 2D con hover
                try: _cpspy_emit_contours(_f, _cpspy_contours[id(_f)])
                except Exception as _e4: _realprint('__CPSPY_HTML__:<p class=err>GL3 2D: ' + str(_e4) + '</p>')
                continue
            try:                            # figura de líneas → canvas 2D propio con hover
                if _cpspy_emit_lines(_f): continue
            except Exception: pass
            _bf = _iocap.BytesIO(); _f.savefig(_bf, format=""png"", dpi=110, bbox_inches=""tight""); _bf.seek(0)
            _realprint(""__CPSPY_IMG__:"" + _b64cap.b64encode(_bf.read()).decode())
        _pltcap.close(""all"")
        try: _cpspy_surfs.clear(); _cpspy_contours.clear(); _cpspy_lines3.clear()   # evita reuso de id(fig)
        except Exception: pass
    _pltcap.show = lambda *a, **k: _cpspy_flush_figs()
except Exception:
    def _cpspy_flush_figs(): pass
";

        // Captura de PyVista: patchea Plotter.show() → convierte las mallas al motor 3D
        // nativo GL3 (glplot.js) → 3D INTERACTIVO embebido (rotar + hover + bandas ETABS),
        // misma visualización que PyVista pero dentro del worksheet (sin ventana externa).
        // Si la malla es enorme (>40k triángulos) cae a screenshot PNG estático.
        private const string _pvPreamble = @"
try:
    import pyvista as _pvcap
    _pvcap.OFF_SCREEN = True
    import numpy as _nppv, os as _ospv, tempfile as _tmppv, io as _iopv, base64 as _b64pv
    _orig_ss = _pvcap.Plotter.screenshot
    def _cpspy_pv_tris(tm):
        fa = getattr(tm, 'faces', None); out = []
        if fa is not None and len(fa):
            fa = _nppv.asarray(fa); i = 0
            while i < len(fa):
                n = int(fa[i])
                if n == 3: out.append((int(fa[i+1]), int(fa[i+2]), int(fa[i+3])))
                elif n == 4: out.append((int(fa[i+1]), int(fa[i+2]), int(fa[i+3]))); out.append((int(fa[i+1]), int(fa[i+3]), int(fa[i+4])))
                i += n + 1
        return out
    def _cpspy_pv_show(self, *a, **k):
        try:
            meshes = list(getattr(self, 'meshes', []) or [])
            smin = smax = None
            for m in meshes:
                sc = getattr(m, 'active_scalars', None)
                if sc is not None and len(sc) and _nppv.asarray(sc).ndim == 1:
                    lo = float(_nppv.nanmin(sc)); hi = float(_nppv.nanmax(sc))
                    smin = lo if smin is None else min(smin, lo); smax = hi if smax is None else max(smax, hi)
            if smin is None: smin, smax = 0.0, 1.0
            rng = (smax - smin) or 1.0
            calls = []; dps = []; lo3 = [1e30,1e30,1e30]; hi3 = [-1e30,-1e30,-1e30]; ntri = 0
            for m in meshes:
                try:
                    tm = m.extract_surface()                 # FEM volumen (hexaedros/tetras) → superficie con faces
                    try: tm = tm.cell_data_to_point_data()   # scalar por elemento (ej. DAMAGEC) → nodo: colorear + hover
                    except Exception: pass
                    tm = tm.triangulate()
                except Exception:
                    try: tm = m.triangulate()
                    except Exception: tm = m
                pts = _nppv.asarray(tm.points, float)
                if len(pts) == 0: continue
                for d in range(3): lo3[d] = min(lo3[d], float(pts[:,d].min())); hi3[d] = max(hi3[d], float(pts[:,d].max()))
                sc = getattr(tm, 'active_scalars', None)
                sc = _nppv.asarray(sc).ravel() if (sc is not None and len(sc)) else None
                if len(dps) < 6000:                          # hover: registrar nudos+valor (datatip)
                    _st = max(1, len(pts)//6000)
                    for _ii in range(0, len(pts), _st):
                        _v = float(sc[_ii]) if sc is not None else 0.0
                        dps.append('GL3.datapoint(%.4f,%.4f,%.4f,%.6g);' % (pts[_ii,0],pts[_ii,1],pts[_ii,2],_v))
                for (a0,b0,c0) in _cpspy_pv_tris(tm):
                    p0,p1,p2 = pts[a0],pts[b0],pts[c0]
                    if sc is not None: t0=(float(sc[a0])-smin)/rng; t1=(float(sc[b0])-smin)/rng; t2=(float(sc[c0])-smin)/rng
                    else: t0=t1=t2=0.5
                    arr = '[' + ','.join('%.4f'%v for v in (p0[0],p0[1],p0[2],p1[0],p1[1],p1[2],p2[0],p2[1],p2[2],p2[0],p2[1],p2[2])) + ']'
                    calls.append('GL3.fill3(%s,%.4f,%.4f,%.4f,%.4f);' % (arr,t0,t1,t2,t2)); ntri += 1
            if ntri == 0 or ntri > 40000:
                from PIL import Image as _Impv
                _img = _orig_ss(self, return_img=True); _bf = _iopv.BytesIO()
                _Impv.fromarray(_img).save(_bf, format='PNG'); _bf.seek(0)
                _realprint('__CPSPY_IMG__:' + _b64pv.b64encode(_bf.read()).decode()); return
            _gljs = open(_ospv.path.join(_tmppv.gettempdir(),'cpspy_glplot.min.js'), encoding='utf-8').read()
            js = ('GL3.figure3(""pv"",860,620);GL3.etabs=true;GL3.view3(40,20);'
                + 'GL3.axis3(%.4f,%.4f,%.4f,%.4f,%.4f,%.4f);' % (lo3[0],hi3[0],lo3[1],hi3[1],lo3[2],hi3[2])
                + ''.join(calls) + 'GL3.datatip(""valor"");' + ''.join(dps)
                + 'GL3.render3();GL3.colorbar3(%.4f,%.4f,340);' % (smin,smax))
            html = '<div><script>if(!window.GL3){' + _gljs + '}</script><script>(function(){' + js + '})();</script></div>'
            _realprint('__CPSPY_HTML__:' + html.replace(chr(10),' ').replace(chr(13),' '))
        except Exception as _e:
            _realprint('__CPSPY_HTML__:<p class=""err"">PyVista 3D: ' + str(_e) + '</p>')
    def _cpspy_pv_screenshot(self, *a, **k):
        # Suite Py: los scripts batch llaman screenshot() (no show()) → embebemos IGUAL el
        # modelo 3D interactivo con hover, y luego hacemos el screenshot real que pide el script.
        if getattr(self, '_cpspy_inss', False):
            return _orig_ss(self, *a, **k)
        self._cpspy_inss = True
        try: _cpspy_pv_show(self)
        except Exception: pass
        finally: self._cpspy_inss = False
        return _orig_ss(self, *a, **k)
    _pvcap.Plotter.show = _cpspy_pv_show
    _pvcap.Plotter.screenshot = _cpspy_pv_screenshot
except Exception:
    pass
";

        private const string _pyHelpers = @"
import html as _cpspy_html
def _cpspy_n(x):
    if isinstance(x, float): return ('%.6g' % x)
    s = str(x)
    if len(s) > 120: s = s[:120] + '…'           # truncar celdas/escalares largos
    return _cpspy_html.escape(s)
def _cpspy_fmt(v):
    try:
        import pandas as _pd
        if isinstance(v, _pd.DataFrame):
            return _cpspy_table_html(v.values.tolist(), list(v.columns))
    except Exception:
        pass
    try:
        import numpy as _np
        if isinstance(v, _np.ndarray):
            if v.size > 200: return _cpspy_html.escape('ndarray shape=' + str(v.shape) + ' dtype=' + str(v.dtype))
            v = v.tolist()
    except Exception:
        pass
    if isinstance(v, dict):                        # dicts (ej. resultados FEM): solo las claves, compacto
        ks = list(v.keys())
        return _cpspy_html.escape('{' + ', '.join(str(k) for k in ks[:20]) + ('' if len(ks)<=20 else ', …') + '}')
    if isinstance(v, (list, tuple)):
        flat = (len(v) > 0 and isinstance(v[0], (list, tuple)))
        # markup NATIVO de Calcpad: <span class=""matrix""><span class=""tr""><span class=""td"">…
        # Los <td> vacíos de los extremos de cada fila son los corchetes que dibuja el CSS de Calcpad.
        def _tr(cells):
            return '<span class=""tr""><span class=""td""></span>' + ''.join('<span class=""td"">' + _cpspy_n(c) + '</span>' for c in cells) + '<span class=""td""></span></span>'
        if flat:
            trs = ''.join(_tr(r[:50]) for r in v[:200])       # matriz: una fila (tr) por cada sublista
        else:
            trs = _tr(v[:200])                                # vector: una sola fila
        return '<span class=""matrix"">' + trs + '</span>'
    return _cpspy_n(v)
def _cpspy_emit(name, val):
    import types as _t
    if callable(val) or isinstance(val, _t.ModuleType): return
    try:
        import pandas as _pdE
        if isinstance(val, _pdE.DataFrame): return   # DataFrame se muestra con print(df), no se duplica en auto-emit
    except Exception:
        pass
    _h = '<p class=""line""><span class=""eq""><var>' + name + '</var> = ' + _cpspy_fmt(val) + '</span></p>'
    _realprint('__CPSPY_HTML__:' + _h.replace(chr(10),' ').replace(chr(13),' '))   # marcador es POR-LINEA → 1 sola linea
def _cpspy_table_html(rows, headers=None, title=None):
    _stt = 'border-collapse:collapse;margin:8px 0;font-size:0.95em'
    _stc = 'border:1px solid #b0b0b0;padding:3px 10px;text-align:right'
    _sth = 'border:1px solid #808080;padding:4px 10px;text-align:center;background:#e8edf7;font-weight:bold'
    def _row(r):
        try:
            import numpy as _np
            if isinstance(r, _np.ndarray): return list(r.tolist())
        except Exception:
            pass
        return list(r) if isinstance(r, (list, tuple)) else [r]
    _h = '<table style=""' + _stt + '"">'
    if title is not None:
        _h += '<caption style=""font-weight:bold;padding:4px 0;text-align:left"">' + _cpspy_html.escape(str(title)) + '</caption>'
    if headers is not None:
        _h += '<tr>' + ''.join('<th style=""' + _sth + '"">' + _cpspy_html.escape(str(c)) + '</th>' for c in _row(headers)) + '</tr>'
    for r in rows:
        _h += '<tr>' + ''.join('<td style=""' + _stc + '"">' + _cpspy_n(c) + '</td>' for c in _row(r)) + '</tr>'
    return _h + '</table>'
_cpspy_print0 = print
def print(*_a, **_k):
    # print(DataFrame) → tabla HTML embebida en Suite Py; en Python real es print normal (texto).
    if len(_a) == 1:
        try:
            import pandas as _pdP
            if isinstance(_a[0], _pdP.DataFrame):
                _realprint('__CPSPY_HTML__:' + _cpspy_table_html(_a[0].values.tolist(), list(_a[0].columns)))
                return
        except Exception:
            pass
    return _cpspy_print0(*_a, **_k)
";
    }
}
