using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Calcpad.Wpf
{
    /// <summary>
    /// EL TEMA DE HEKATAN (Oscuro / Oro) Y LOS BOTONES PROPIOS DE LA VENTANA.
    ///
    /// Hekatan Python3 llevaba la ventana de Calcpad (blanca, con Numbers/Angles/unidades que
    /// aquí no pintan nada). Ahora lleva la MISMA ventana que Hekatan Lab, y esta clase trae lo
    /// que esa ventana necesita y que en Calcpad no existía:
    ///
    ///   - los brushes del tema (un juego Oscuro y otro Oro) y el interruptor que los cambia
    ///     EN VIVO (por eso el XAML los pide con DynamicResource, no StaticResource),
    ///   - ⛶ Output: el editor se pliega y el reporte ocupa toda la ventana,
    ///   - PNG: exporta el reporte entero a imagen (no solo lo que se ve),
    ///   - los menús desplegables de la barra (disp▾, graf▾…),
    ///   - el modo texto: mientras está activo, cada línea nueva arranca con <c>#'</c>.
    ///
    /// Es el mismo código de Hekatan Lab, con dos cambios: el comentario de hoja es <c>#'</c>
    /// (no <c>%'</c>) y todavía NO se re-tiñe el REPORTE (eso en el Lab depende de su caché de
    /// HTML, que aquí no existe): el reporte sigue en blanco en los dos temas.
    /// </summary>
    public partial class MainWindow
    {
        private bool _isDarkTheme = true;    // tema activo (Oscuro por defecto; Oro = claro cálido)
        private bool _webOnlyMode;
        private WindowState _savedWinState = WindowState.Normal;
        private bool _textMode;

        /// <summary>(clave del recurso, color en Oscuro, color en Oro). Misma tabla que el Lab:
        /// los dos programas tienen que verse iguales.</summary>
        private static readonly (string Key, string Dark, string Gold)[] _themeBrushes =
        {
            ("ThemeWindowBg",     "#14110A", "#EDE4CE"),
            ("ThemePanelBg",      "#1F1B14", "#E7DCC2"),
            ("ThemeEditorBg",     "#1A1712", "#F8F2E4"),
            ("ThemeText",         "#E8E2D4", "#2B2416"),
            ("ThemeTextMuted",    "#A89F8C", "#6B5E45"),
            ("ThemeAccentRed",    "#E5382B", "#C0392B"),
            ("ThemeAccentGold",   "#E6C463", "#A9820C"),
            ("ThemeButtonBg",     "#262016", "#EFE7D2"),
            ("ThemeButtonBorder", "#3A3226", "#CBBD98"),
            ("ThemeGutterBg",     "#211D16", "#E4DAC0"),
            ("ThemeHoverBg",      "#33E5382B", "#33C0392B"),
        };

        /// <summary>Reasigna los brushes del tema (DynamicResource → se actualiza en vivo).</summary>
        private void SwapThemeBrushes(bool dark)
        {
            var conv = new BrushConverter();
            foreach (var (key, d, g) in _themeBrushes)
            {
                var b = (SolidColorBrush)conv.ConvertFromString(dark ? d : g);
                b.Freeze();
                Resources[key] = b;
            }
        }

        private void SetTheme(bool dark)
        {
            _isDarkTheme = dark;
            SwapThemeBrushes(dark);
            ApplyWebViewBackground(dark);
            HighLighter.ApplyTheme(dark);
            AplicarColoresAvalon(dark);            // EDITOR PLEGABLE: sigue el tema
            // FORZAR re-resaltado de TODO el documento: los Run existentes conservan el brush del
            // tema anterior (en Oro, Const y Function son NEGROS → invisibles al pasar a Oscuro).
            try { _forceHighlight = true; ForceHighlight(); } catch { }
            if (ThemeToggleMenuItem != null)
                ThemeToggleMenuItem.Header = dark ? "Theme: Dark  →  Gold" : "Theme: Gold  →  Dark";
            UpdateThemeToggleVisual();
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e) => SetTheme(!_isDarkTheme);
        private void ThemeSetDark_Click(object sender, RoutedEventArgs e) => SetTheme(true);
        private void ThemeSetGold_Click(object sender, RoutedEventArgs e) => SetTheme(false);

        private static readonly Brush _pillActiveBg = FrzBrush(0xE6, 0xC4, 0x63);   // oro
        private static readonly Brush _pillActiveFg = FrzBrush(0x14, 0x11, 0x09);   // texto sobre oro

        private static Brush FrzBrush(byte r, byte g, byte b)
        { var s = new SolidColorBrush(Color.FromRgb(r, g, b)); s.Freeze(); return s; }

        /// <summary>Resalta el segmento activo del interruptor Oscuro | Oro.</summary>
        private void UpdateThemeToggleVisual()
        {
            if (ThemeDarkBtn == null || ThemeGoldBtn == null) return;
            ThemeDarkBtn.Background = _isDarkTheme ? _pillActiveBg : Brushes.Transparent;
            ThemeDarkBtn.Foreground = _isDarkTheme ? _pillActiveFg : (Brush)FindResource("ThemeTextMuted");
            ThemeGoldBtn.Background = !_isDarkTheme ? _pillActiveBg : Brushes.Transparent;
            ThemeGoldBtn.Foreground = !_isDarkTheme ? _pillActiveFg : (Brush)FindResource("ThemeTextMuted");
        }

        /// <summary>El fondo del WebView2. Va aparte del CSS porque es lo que se ve MIENTRAS
        /// carga: sin esto, cada cálculo daba un fogonazo blanco en el tema Oscuro.</summary>
        private void ApplyWebViewBackground(bool dark)
        {
            try
            {
                WebViewer.DefaultBackgroundColor = dark
                    ? System.Drawing.Color.FromArgb(0x1A, 0x17, 0x12)   // carbón
                    : System.Drawing.Color.FromArgb(0xED, 0xE4, 0xCE);  // crema
                // help.html se tematiza con @media (prefers-color-scheme); como se navega directo,
                // no recibe ninguna clase, así que se le dice al WebView qué esquema prefiere.
                var core = WebViewer.CoreWebView2;
                if (core?.Profile != null)
                    core.Profile.PreferredColorScheme = dark
                        ? CoreWebView2PreferredColorScheme.Dark
                        : CoreWebView2PreferredColorScheme.Light;
            }
            catch { }
        }

        // ---------- ⛶ Output: el reporte a pantalla completa ----------

        private void MaximizeOutput_Click(object sender, RoutedEventArgs e) => ToggleWebOnlyMode();

        private void ToggleWebOnlyMode()
        {
            _webOnlyMode = !_webOnlyMode;
            if (_webOnlyMode)
            {
                _savedWinState = WindowState;
                InputFrame.Visibility = Visibility.Collapsed;
                MainSplitter.Visibility = Visibility.Collapsed;
                EditorCol.Width = new GridLength(0);
                SplitterCol.Width = new GridLength(0);
                WebCol.Width = new GridLength(1, GridUnitType.Star);
                WindowState = WindowState.Maximized;
                if (MaximizeOutputBtn != null) MaximizeOutputBtn.Content = "⛶ Editor";
            }
            else
            {
                // Se vuelve SIEMPRE al 50/50 limpio: guardar el ancho anterior en píxeles
                // absolutos acababa aplastando el Output.
                InputFrame.Visibility = Visibility.Visible;
                MainSplitter.Visibility = Visibility.Visible;
                EditorCol.Width = new GridLength(120, GridUnitType.Star);
                SplitterCol.Width = GridLength.Auto;
                WebCol.Width = new GridLength(120, GridUnitType.Star);
                WindowState = _savedWinState;
                if (MaximizeOutputBtn != null) MaximizeOutputBtn.Content = "⛶ Output";
            }
        }

        // ---------- modo texto ----------

        /// <summary>Mientras está activo, cada línea nueva arranca con <c>#'</c> (texto que SÍ
        /// sale en la hoja). En el Lab la marca es <c>%'</c>; aquí es la de Python.</summary>
        private void TextModeToggle_Click(object sender, RoutedEventArgs e)
        {
            _textMode = (sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked ?? !_textMode;
            if (_textMode)
            {
                try
                {
                    if (InsertarEnAvalon("#'")) return;      // con el editor plegable delante
                    var p = RichTextBox.Selection?.End.Paragraph;
                    if (p != null && p.ContentStart.GetOffsetToPosition(p.ContentEnd) == 0)
                    {
                        RichTextBox.BeginChange();
                        try { _insertManager.InsertText("#'"); }
                        finally { RichTextBox.EndChange(); }
                    }
                }
                catch { }
            }
        }

        // ---------- PNG del reporte ----------

        /// <summary>Guarda el Output entero como PNG. No es una captura de pantalla: se le pide
        /// al navegador la página COMPLETA (captureBeyondViewport), así que sale también lo que
        /// hay que bajar con el scroll.</summary>
        private async void ExportPngButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (WebViewer?.CoreWebView2 == null) await WebViewer.EnsureCoreWebView2Async();
                var suggested = "salida.png";
                if (!string.IsNullOrEmpty(CurrentFileName))
                    suggested = Path.GetFileNameWithoutExtension(CurrentFileName) + ".png";
                var dlg = new SaveFileDialog
                {
                    FileName = suggested,
                    DefaultExt = ".png",
                    Filter = "PNG (*.png)|*.png",
                    Title = "Exportar el Output como PNG"
                };
                if (dlg.ShowDialog() != true) return;

                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var wStr = await WebViewer.CoreWebView2.ExecuteScriptAsync(
                    "Math.max(document.body.scrollWidth,document.documentElement.scrollWidth)");
                var hStr = await WebViewer.CoreWebView2.ExecuteScriptAsync(
                    "Math.max(document.body.scrollHeight,document.documentElement.scrollHeight)");
                int w = (int)double.Parse(wStr, ci), h = (int)double.Parse(hStr, ci);
                var prm = "{\"format\":\"png\",\"captureBeyondViewport\":true,\"clip\":{\"x\":0,\"y\":0,\"width\":"
                          + w + ",\"height\":" + h + ",\"scale\":1}}";
                var res = await WebViewer.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", prm);
                using var jd = System.Text.Json.JsonDocument.Parse(res);
                File.WriteAllBytes(dlg.FileName, Convert.FromBase64String(jd.RootElement.GetProperty("data").GetString()));
                try { Title = AppInfo.Title + "  [PNG guardado: " + Path.GetFileName(dlg.FileName) + "]"; } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo exportar el PNG:\n" + ex.Message, "Exportar PNG",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---------- menús desplegables de la barra ----------

        /// <summary>Un botón de la barra que abre su propio menú (disp▾, graf▾…): el menú se
        /// cuelga DEBAJO del botón, no donde esté el ratón.</summary>
        private void DispMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.ContextMenu is not null)
            {
                b.ContextMenu.PlacementTarget = b;
                b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                b.ContextMenu.IsOpen = true;
            }
        }
    }
}
