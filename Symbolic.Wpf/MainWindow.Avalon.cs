using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Xml;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Indentation;

namespace Calcpad.Wpf
{
    /// <summary>
    /// EL EDITOR DE CODIGO DE HEKATAN PYTHON3, sobre AvalonEdit.
    ///
    /// El RichTextBox de siempre SIGUE AHI, oculto, y se le escribe con <c>SetInputText</c> en
    /// cada tecla. Asi todo lo demas de la app —autorun, calcular, guardar, los botones de
    /// insertar, el teclado, MathCanvas— funciona igual que siempre, sin tocar sus llamadas al
    /// RichTextBox. Es el mismo patron que ya usa MathCanvas para ser un segundo editor
    /// (OnMathCanvasTextChanged/_syncingFromMathCanvas), y el mismo que Hekatan Lab.
    ///
    /// Lo que AvalonEdit aporta: CARPETAS PLEGABLES (+/-) en def, class, for, if, while, with,
    /// try y celdas <c>#%%</c>, autocompletado con los builtins REALES del motor, y sangria
    /// automatica de Python. Quien sabe donde estan los bloques es el MOTOR
    /// (<see cref="Calcpad.Core.Python.PythonBlocks"/>).
    ///
    /// La casilla "Plegado" alterna entre este editor y el clasico, para compararlos.
    /// </summary>
    public partial class MainWindow
    {
        private FoldingManager _foldingManager;
        private CompletionWindow _avalonCompletion;
        private readonly PythonSemanticColorizer _colorizadorSemantico = new();
        private bool _desdeAvalon;      // el cambio de texto lo origino AvalonEdit
        private bool _haciaAvalon;      // estamos escribiendo EN AvalonEdit desde la app
        private bool _avalonListo;

        /// <summary>Llamar una vez, cuando la ventana ya cargo.</summary>
        private void PrepararAvalon()
        {
            if (_avalonListo) return;
            _avalonListo = true;

            CargarResaltadoPython();
            _foldingManager = FoldingManager.Install(AvalonEditor.TextArea);
            AvalonEditor.TextArea.TextView.LineTransformers.Add(_colorizadorSemantico);

            // Python se escribe con ESPACIOS: un tabulador de verdad mezclado con espacios es
            // un TabError en el motor y en Python real.
            AvalonEditor.Options.ConvertTabsToSpaces = true;
            AvalonEditor.Options.IndentationSize = 4;
            AvalonEditor.TextArea.IndentationStrategy = new SangriaPython();

            AvalonEditor.TextChanged += AvalonEditor_TextChanged;
            AvalonEditor.TextArea.TextEntered += AvalonEditor_TextEntered;
            AvalonEditor.PreviewKeyDown += AvalonEditor_PreviewKeyDown;

            AplicarModoEditor();
            SincronizarHaciaAvalon();

            // --plegar [--cshot <png>]: arrancar con todo plegado, para revisar EN PNG que las
            // carpetas salen donde tienen que salir. Con retardo: el archivo entra al editor
            // DESPUES, al final de GetInputTextFromFile.
            var argv = Environment.GetCommandLineArgs();
            if (argv.Any(a => a == "--plegar"))
                TrasUnRato(1400, () =>
                {
                    PlegarTodo_Click(null, null);
                    var png = ValorDe(argv, "--cshot");
                    if (png is null) return;
                    TrasUnRato(900, () => { CapturarPantalla(png); Environment.Exit(0); });
                });

            // --clasico [--cshot <png>]: desmarca la casilla, para comprobar en PNG que el
            // editor de siempre (RichTextBox) sigue vivo detras.
            if (argv.Any(a => a == "--clasico"))
                TrasUnRato(1400, () =>
                {
                    EditorPlegableChk.IsChecked = false;
                    var png = ValorDe(argv, "--cshot");
                    if (png is null) return;
                    TrasUnRato(900, () => { CapturarPantalla(png); Environment.Exit(0); });
                });

            PrepararCapturaAutocompletado();
            PrepararCapturaBusqueda();
            PrepararCapturaInsercion();
            PrepararCapturaMarcado();
        }

        /// <summary><c>--insertar &lt;texto&gt; [--cshot &lt;png&gt;]</c>: mete texto por el MISMO camino
        /// que los botones de la barra (InsertManager), para comprobar en PNG que acaba en el
        /// editor plegable y no en el RichTextBox oculto.</summary>
        private void PrepararCapturaInsercion()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "--insertar");
            if (i < 0 || i + 1 >= args.Length) return;
            var texto = args[i + 1];
            var png = ValorDe(args, "--cshot");

            TrasUnRato(1400, () =>
            {
                try
                {
                    AvalonEditor.Focus();
                    AvalonEditor.CaretOffset = AvalonEditor.Document.TextLength;
                    _insertManager.InsertLine();
                    _insertManager.InsertText(texto);
                    AvalonEditor.ScrollToEnd();      // para que se VEA en el PNG
                }
                catch { }
                if (png is null) return;
                TrasUnRato(900, () => { CapturarPantalla(png); Environment.Exit(0); });
            });
        }

        /// <summary><c>--buscar &lt;texto&gt; [--cshot &lt;png&gt;]</c>: abre el buscador del editor con
        /// ese texto, para revisar en PNG que resalta las coincidencias.</summary>
        private void PrepararCapturaBusqueda()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "--buscar");
            if (i < 0 || i + 1 >= args.Length) return;
            var texto = args[i + 1];
            var png = ValorDe(args, "--cshot");

            TrasUnRato(1400, () =>
            {
                try
                {
                    AvalonEditor.Focus();
                    BuscarEnAvalon(false);
                    if (_panelBuscar is not null) _panelBuscar.SearchPattern = texto;
                }
                catch { }
                if (png is null) return;
                TrasUnRato(900, () => { CapturarPantalla(png); Environment.Exit(0); });
            });
        }

        /// <summary>
        /// <c>--completar &lt;prefijo&gt; [--aceptar] [--cshot &lt;png&gt;]</c>: escribe el prefijo al
        /// final del codigo y abre el popup de autocompletado, para poder REVISARLO en un PNG.
        ///
        /// Por que no vale <c>--shot</c>: ese dibuja el WebViewer (la hoja). El popup de
        /// AvalonEdit es OTRA ventana encima de esta, asi que hay que dibujar el arbol visual de
        /// la ventana Y el de sus ventanas hijas — que es lo que hace <see cref="CapturarPantalla"/>.
        /// </summary>
        private void PrepararCapturaAutocompletado()
        {
            var args = Environment.GetCommandLineArgs();
            var iPre = Array.IndexOf(args, "--completar");
            if (iPre < 0 || iPre + 1 >= args.Length) return;
            var prefijo = args[iPre + 1];
            var png = ValorDe(args, "--cshot");

            TrasUnRato(1400, () =>
            {
                try
                {
                    WindowState = WindowState.Normal;
                    AvalonEditor.Focus();
                    AvalonEditor.AppendText("\n" + prefijo);
                    AvalonEditor.CaretOffset = AvalonEditor.Document.TextLength;
                    AvalonEditor.ScrollToEnd();      // sin esto el popup nace fuera de la vista
                    // `--completar np.` = calificador `np` y prefijo vacio: el filtro es lo que
                    // va DESPUES del punto, no la linea entera.
                    var punto = prefijo.LastIndexOf('.');
                    MostrarAutocompletado(punto >= 0 ? prefijo[(punto + 1)..] : prefijo);
                    // --aceptar: ademas, mete el item seleccionado (para ver el SNIPPET ya
                    // insertado con sus huecos, que es lo que hace Tab).
                    if (args.Any(a => a == "--aceptar"))
                        _avalonCompletion?.CompletionList.RequestInsertion(EventArgs.Empty);
                }
                catch { }

                // Junto al PNG, un .defs.txt con lo que el motor detecto: si el popup sale vacio
                // hay que poder VER si el problema es la deteccion o el filtro.
                if (png is null) return;
                VolcarDeclaradas(png + ".defs.txt");
                TrasUnRato(900, () =>
                {
                    CapturarPantalla(png);
                    // Salida DURA: Shutdown() dispara el "File not saved. Save?" (el snippet
                    // ensucio el documento) y en headless ese dialogo se queda ahi para siempre.
                    Environment.Exit(0);
                });
            });
        }

        private static string ValorDe(string[] args, string bandera)
        {
            var i = Array.IndexOf(args, bandera);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>Ejecutar algo cuando la ventana ya termino de montarse.</summary>
        private void TrasUnRato(int ms, Action accion)
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (_, _) => { t.Stop(); accion(); };
            t.Start();
        }

        /// <summary>Dibuja esta ventana Y las ventanas hijas que tenga encima (el popup del
        /// autocompletado), pegando cada una en su sitio.
        ///
        /// Se dibuja el ARBOL VISUAL de WPF (RenderTargetBitmap), no la pantalla ni el handle:
        ///   - <c>CopyFromScreen</c> traia lo que hubiera delante en el monitor (otra terminal).
        ///   - <c>PrintWindow</c> depende del escritorio: con la ventana tapada salio en BLANCO.
        /// El arbol visual siempre esta ahi. Lo unico que no sale es el WebView2 (superficie
        /// nativa): el panel Output queda vacio, que es lo esperado en estas capturas.</summary>
        private void CapturarPantalla(string ruta)
        {
            try
            {
                UpdateLayout();
                int w = (int)Math.Ceiling(ActualWidth), h = (int)Math.Ceiling(ActualHeight);
                if (w <= 0 || h <= 0) return;

                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawImage(Render(this), new Rect(0, 0, w, h));

                    foreach (Window otra in Application.Current.Windows)
                    {
                        if (ReferenceEquals(otra, this) || !otra.IsVisible) continue;
                        // De pantalla a MIS coordenadas en una sola operacion: mezclar pixeles
                        // fisicos con unidades WPF descoloca el popup con la pantalla al 150 %.
                        var p = PointFromScreen(otra.PointToScreen(new Point(0, 0)));
                        dc.DrawImage(Render(otra), new Rect(p.X, p.Y, otra.ActualWidth, otra.ActualHeight));
                    }
                }

                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(dv);

                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                var dir = System.IO.Path.GetDirectoryName(ruta);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                using var fs = System.IO.File.Create(ruta);
                enc.Save(fs);
            }
            catch { }

            static System.Windows.Media.Imaging.RenderTargetBitmap Render(Window v)
            {
                v.UpdateLayout();
                var b = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    Math.Max(1, (int)Math.Ceiling(v.ActualWidth)),
                    Math.Max(1, (int)Math.Ceiling(v.ActualHeight)),
                    96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                b.Render(v);
                return b;
            }
        }

        // ---------- alternar editor plegable / clasico ----------

        private bool EditorPlegableActivo => EditorPlegableChk?.IsChecked == true;

        private void EditorPlegable_Changed(object sender, RoutedEventArgs e)
        {
            if (!_avalonListo) return;
            AplicarModoEditor();
            if (EditorPlegableActivo) SincronizarHaciaAvalon();
        }

        private void AplicarModoEditor()
        {
            var plegable = EditorPlegableActivo;
            AvalonEditor.Visibility = plegable ? Visibility.Visible : Visibility.Collapsed;
            PlegarTodoBtn.Visibility = plegable ? Visibility.Visible : Visibility.Collapsed;
            DesplegarTodoBtn.Visibility = plegable ? Visibility.Visible : Visibility.Collapsed;
            // El canal de numeros de linea clasico estorba: AvalonEdit trae el suyo.
            LineNumbers.Visibility = plegable ? Visibility.Collapsed : Visibility.Visible;
            LineNumbersBorder.Visibility = plegable ? Visibility.Collapsed : Visibility.Visible;
            if (plegable) AvalonEditor.Focus();
        }

        // ---------- sincronizacion en los dos sentidos ----------

        /// <summary>AvalonEdit -> RichTextBox oculto. Dispara la cadena normal de la app
        /// (RichTextBox_TextChanged: autorun, resaltado, guardado sucio...).</summary>
        private void AvalonEditor_TextChanged(object sender, EventArgs e)
        {
            if (_haciaAvalon || !EditorPlegableActivo) return;
            _desdeAvalon = true;
            try { SetInputText(AvalonEditor.Text); }
            catch { }
            finally { _desdeAvalon = false; }
            ActualizarPlegado();
            ActualizarSemantica();
        }

        /// <summary>RichTextBox -> AvalonEdit. Se llama al abrir archivo, al limpiar, y tras
        /// cualquier cambio que NO haya salido de AvalonEdit (botones de insertar, teclado de
        /// simbolos, MathCanvas).</summary>
        private void SincronizarHaciaAvalon()
        {
            if (!_avalonListo || _desdeAvalon || AvalonEditor is null) return;
            string codigo;
            try { codigo = GetInputText(); }
            catch { return; }
            if (codigo == AvalonEditor.Text) return;

            var caret = AvalonEditor.CaretOffset;
            _haciaAvalon = true;
            try
            {
                AvalonEditor.Text = codigo;
                AvalonEditor.CaretOffset = Math.Min(caret, AvalonEditor.Document.TextLength);
            }
            finally { _haciaAvalon = false; }
            ActualizarPlegado();
            ActualizarSemantica();
        }

        // ---------- botones de insertar ----------

        /// <summary>Lo que insertan los botones y el teclado de simbolos va al editor que el
        /// usuario esta VIENDO. Si fuera al RichTextBox oculto, el texto apareceria donde esta
        /// SU cursor, no donde el usuario tiene el suyo.
        /// Devuelve false si el editor plegable no esta activo (entonces sigue el de siempre).</summary>
        private bool InsertarEnAvalon(string texto)
        {
            if (!EditorPlegableActivo || !_avalonListo || AvalonEditor is null) return false;

            var doc = AvalonEditor.Document;
            using (doc.RunUpdate())
            {
                if (texto == "\b")                                  // borrar un caracter
                {
                    if (AvalonEditor.SelectionLength > 0)
                        doc.Remove(AvalonEditor.SelectionStart, AvalonEditor.SelectionLength);
                    else if (AvalonEditor.CaretOffset > 0)
                        doc.Remove(AvalonEditor.CaretOffset - 1, 1);
                    AvalonEditor.Focus();
                    return true;
                }

                if (AvalonEditor.SelectionLength > 0)
                    doc.Remove(AvalonEditor.SelectionStart, AvalonEditor.SelectionLength);

                var inicio = AvalonEditor.CaretOffset;
                var t = texto == "\n" ? Environment.NewLine : texto;
                doc.Insert(inicio, t);
                SeleccionarHueco(inicio, t);
            }
            AvalonEditor.Focus();
            return true;
        }

        /// <summary>Deja seleccionado el hueco de la plantilla recien insertada, igual que hacia
        /// <c>SelectInsertedText</c>: lo de dentro de <c>{ }</c> (o hasta el <c>@</c>) y, si no
        /// hay llaves, el primer argumento entre <c>( )</c>.</summary>
        private void SeleccionarHueco(int inicio, string texto)
        {
            var i1 = texto.IndexOf('{') + 1;
            var i2 = i1 > 0 ? texto.IndexOfAny(['@', '}'], i1) : -1;
            if (i1 <= 0)
            {
                i1 = texto.IndexOf('(') + 1;
                i2 = i1 > 0 ? texto.IndexOfAny([';', ')'], i1) : -1;
            }
            if (i1 > 0 && i2 > 0)
                AvalonEditor.Select(inicio + i1, i2 - i1);
            else
                AvalonEditor.CaretOffset = inicio + texto.Length;
        }

        // ---------- buscar y deshacer ----------

        /// <summary>Abre el buscador propio de AvalonEdit (resalta TODAS las coincidencias en el
        /// margen y en el texto). Devuelve false si el editor plegable no esta activo, para que
        /// siga funcionando el buscador clasico del RichTextBox.</summary>
        private bool BuscarEnAvalon(bool conReemplazo)
        {
            if (!EditorPlegableActivo || !_avalonListo) return false;

            _panelBuscar ??= ICSharpCode.AvalonEdit.Search.SearchPanel.Install(AvalonEditor);

            var sel = AvalonEditor.SelectedText;
            if (!string.IsNullOrEmpty(sel) && !sel.Contains('\n'))
                _panelBuscar.SearchPattern = sel;

            _panelBuscar.Open();
            // El panel de AvalonEdit sale sin caja de reemplazo; con Ctrl+H se pide igual, asi
            // que al menos se deja el foco puesto para escribir la busqueda.
            Dispatcher.InvokeAsync(() => _panelBuscar.Reactivate(),
                System.Windows.Threading.DispatcherPriority.Input);
            return true;
        }
        private ICSharpCode.AvalonEdit.Search.SearchPanel _panelBuscar;

        /// <summary>Deshacer/rehacer en el editor plegable. false = no esta activo.</summary>
        private bool DeshacerEnAvalon(bool rehacer)
        {
            if (!EditorPlegableActivo || !_avalonListo) return false;
            if (rehacer) { if (AvalonEditor.CanRedo) AvalonEditor.Redo(); }
            else if (AvalonEditor.CanUndo) AvalonEditor.Undo();
            return true;
        }

        // ---------- plegado ----------

        /// <summary>Quien SABE donde estan los bloques es el motor
        /// (<see cref="Calcpad.Core.Python.PythonBlocks"/>); aqui solo se traducen sus tramos al
        /// +/- de AvalonEdit. Asi otra piel (Avalonia) pliega identico. UpdateFoldings conserva
        /// los que ya estaban cerrados: plegar no se deshace al escribir.</summary>
        private void ActualizarPlegado()
        {
            if (_foldingManager is null) return;
            try
            {
                var tramos = Calcpad.Core.Python.PythonBlocks.Find(AvalonEditor.Text);
                var pliegues = tramos
                    .Select(t => new NewFolding(t.Start, t.End) { Name = t.Label })
                    .ToList();
                _foldingManager.UpdateFoldings(pliegues, -1);
            }
            catch { /* con un bloque a medio escribir puede no cuadrar: no es critico */ }
        }

        /// <summary>Relee que declaro el usuario y repinta. Va con retardo: al escribir no hace
        /// falta rehacerlo en cada tecla, y en un archivo largo recorrerlo entero si se nota.</summary>
        private void ActualizarSemantica()
        {
            _repintado ??= new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(350) };
            _repintado.Tick -= Repintar;
            _repintado.Tick += Repintar;
            _repintado.Stop();
            _repintado.Start();

            void Repintar(object s, EventArgs e)
            {
                _repintado.Stop();
                var texto = AvalonEditor.Text;
                _colorizadorSemantico.Actualizar(
                    LoDeclarado(), Calcpad.Core.Python.PythonSymbols.LineasDeCadena(texto));
                AvalonEditor.TextArea.TextView.Redraw();
            }
        }
        private System.Windows.Threading.DispatcherTimer _repintado;

        private void PlegarTodo_Click(object sender, RoutedEventArgs e)
        {
            ActualizarPlegado();
            if (_foldingManager is null) return;
            foreach (var f in _foldingManager.AllFoldings) f.IsFolded = true;
        }

        private void DesplegarTodo_Click(object sender, RoutedEventArgs e)
        {
            if (_foldingManager is null) return;
            foreach (var f in _foldingManager.AllFoldings) f.IsFolded = false;
        }

        // ---------- resaltado ----------

        private void CargarResaltadoPython()
        {
            try
            {
                using var stream = typeof(MainWindow).Assembly
                    .GetManifestResourceStream("Calcpad.Wpf.Python.xshd");
                if (stream is null) return;
                using var reader = XmlReader.Create(stream);
                AvalonEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
            catch { /* sin resaltado no es critico */ }
        }

        // ---------- autocompletado ----------

        private void AvalonEditor_TextEntered(object sender, TextCompositionEventArgs e)
        {
            if (_avalonCompletion is not null) return;    // ya abierto: AvalonEdit filtra solo
            if (e.Text == ".")
            {
                // En Python casi todo se escribe con punto (np.zeros): al teclearlo ya se puede
                // ofrecer lo del modulo, sin esperar a que escriba dos letras.
                if (Calificador() is not null) MostrarAutocompletado("");
                return;
            }
            if (e.Text.Length == 1 && (char.IsLetter(e.Text[0]) || e.Text[0] == '_'))
            {
                var prefijo = PalabraActual();
                if (prefijo.Length >= 2) MostrarAutocompletado(prefijo);
            }
        }

        private void AvalonEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                MostrarAutocompletado(PalabraActual());
                e.Handled = true;
            }
        }

        private string PalabraActual()
        {
            var doc = AvalonEditor.Document;
            int caret = AvalonEditor.CaretOffset, ini = caret;
            while (ini > 0)
            {
                var c = doc.GetCharAt(ini - 1);
                if (char.IsLetterOrDigit(c) || c == '_') ini--;
                else break;
            }
            return doc.GetText(ini, caret - ini);
        }

        /// <summary>Lo que va antes del punto: en <c>np.zer|</c> devuelve <c>np</c>. null si no
        /// hay punto (entonces se ofrece todo).</summary>
        private string Calificador()
        {
            var doc = AvalonEditor.Document;
            var i = AvalonEditor.CaretOffset - PalabraActual().Length;
            if (i <= 0 || doc.GetCharAt(i - 1) != '.') return null;
            var fin = i - 1;
            var ini = fin;
            while (ini > 0)
            {
                var c = doc.GetCharAt(ini - 1);
                if (char.IsLetterOrDigit(c) || c == '_') ini--;
                else break;
            }
            return ini < fin ? doc.GetText(ini, fin - ini) : null;
        }

        private void MostrarAutocompletado(string prefijo)
        {
            var items = PythonLang.Items(prefijo, Calificador(), LoDeclarado(), LineaDelCursor()).ToList();
            if (items.Count == 0) return;

            _avalonCompletion = new CompletionWindow(AvalonEditor.TextArea) { CloseWhenCaretAtBeginning = true };
            _avalonCompletion.StartOffset = AvalonEditor.CaretOffset - prefijo.Length;
            _avalonCompletion.FontFamily = AvalonEditor.FontFamily;
            _avalonCompletion.FontSize = AvalonEditor.FontSize;
            foreach (var it in items) _avalonCompletion.CompletionList.CompletionData.Add(it);
            if (!string.IsNullOrEmpty(prefijo)) _avalonCompletion.CompletionList.SelectItem(prefijo);
            _avalonCompletion.Closed += (_, _) => _avalonCompletion = null;
            _avalonCompletion.Show();
        }

        /// <summary>Diagnostico de <c>--completar</c>: que simbolos vio el motor en el archivo.</summary>
        private void VolcarDeclaradas(string ruta)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"linea del cursor: {LineaDelCursor()}");
                sb.AppendLine($"calificador: {Calificador() ?? "(ninguno)"}");
                var d = LoDeclarado();
                if (d is null) sb.AppendLine("PythonSymbols = null (excepcion al leer)");
                else
                    foreach (var s in d)
                        sb.AppendLine($"   {s.Tipo,-9} {s.Nombre}  (linea {s.Linea}) {s.Firma}");
                System.IO.File.WriteAllText(ruta, sb.ToString());
            }
            catch { }
        }

        private int LineaDelCursor() =>
            AvalonEditor.Document.GetLineByOffset(AvalonEditor.CaretOffset).LineNumber;

        /// <summary>Que declaro el usuario (variables, funciones, clases, argumentos y modulos)
        /// y en que linea, segun el MOTOR. No se usa el <c>UserDefined</c> de Calcpad: alli el
        /// <c>#</c> es una directiva y el <c>;</c> final significa "la linea sigue", asi que en
        /// Python veia mal casi todo.</summary>
        private System.Collections.Generic.IReadOnlyList<Calcpad.Core.Python.Simbolo> LoDeclarado()
        {
            try { return Calcpad.Core.Python.PythonSymbols.Find(AvalonEditor.Text); }
            catch { return null; }
        }
    }

    /// <summary>
    /// La sangria de Python: al pulsar Enter, la linea nueva arranca con la MISMA sangria que la
    /// anterior, y con 4 espacios mas si la anterior terminaba en <c>:</c> (que es justo lo que
    /// abre bloque). Sin esto, cada linea nueva empieza pegada al margen y hay que sangrar a
    /// mano; en MATLAB daba igual porque alli el bloque lo cierra un <c>end</c>.
    /// </summary>
    internal sealed class SangriaPython : DefaultIndentationStrategy
    {
        public override void IndentLine(TextDocument document, DocumentLine linea)
        {
            var anterior = linea?.PreviousLine;
            if (document is null || anterior is null) return;

            var texto = document.GetText(anterior);
            var sangria = texto[..(texto.Length - texto.TrimStart().Length)];
            var codigo = Calcpad.Core.Python.PythonBlocks.SoloCodigo(texto).TrimEnd();

            if (codigo.EndsWith(':')) sangria += "    ";
            // return / pass / break / continue / raise cierran lo que se estaba haciendo:
            // la linea siguiente sale un nivel MAS AFUERA, como en cualquier IDE de Python.
            else if (SacaUnNivel(codigo.TrimStart()) && sangria.Length >= 4)
                sangria = sangria[..^4];

            document.Replace(linea.Offset, ObtenerSangriaActual(document, linea), sangria);
        }

        private static bool SacaUnNivel(string codigo) =>
            codigo == "pass" || codigo == "break" || codigo == "continue" ||
            codigo.StartsWith("return", StringComparison.Ordinal) ||
            codigo.StartsWith("raise", StringComparison.Ordinal);

        private static int ObtenerSangriaActual(TextDocument document, DocumentLine linea)
        {
            var texto = document.GetText(linea);
            return texto.Length - texto.TrimStart().Length;
        }
    }
}
