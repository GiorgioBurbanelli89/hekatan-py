using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Calcpad.Wpf
{
    /// <summary>
    /// EL CANAL DE CONTROL <c>--ctl &lt;carpeta&gt;</c>: manejar la ventana VIVA desde la terminal.
    ///
    /// Es una cola de archivos, no un socket: la terminal escribe <c>cmd-0001.json</c> en la
    /// carpeta y la ventana contesta <c>resp-0001.json</c>. Sin puertos, sin permisos, y se lee
    /// con cualquier cosa. Al arrancar, la ventana deja <c>ready.txt</c> con su PID: eso es lo
    /// que espera el test antes de mandar el primer comando.
    ///
    /// Ops del MOTOR (las del EDITOR estan en MainWindow.Pruebas.cs):
    ///   {"op":"run"}                     recalcular y esperar a que termine
    ///   {"op":"settext","text":"..."}    escribir el codigo y recalcular
    ///   {"op":"getoutput"}               el TEXTO del reporte -> {"output":"..."}
    ///   {"op":"js","code":"..."}         ejecutar JS en el WebView2
    ///   {"op":"capture","path":"x.png"}  PNG de la ventana entera (PrintWindow: SI coge el
    ///                                    WebView2; RenderTargetBitmap lo deja en negro)
    ///   {"op":"quit"}                    cerrar
    /// </summary>
    public partial class MainWindow
    {
        internal static bool IsControlMode =
            Environment.GetCommandLineArgs().Any(a => a == "--ctl");
        private string _ctlDir;
        private DispatcherTimer _ctlTimer;
        private readonly HashSet<string> _ctlDone = new(StringComparer.OrdinalIgnoreCase);
        private bool _ctlBusy;

        /// <summary>Con que motor se calculo lo ultimo: "python" (el motor de Python) o
        /// "calcpad" (el parser heredado). Lo lee la op "state".</summary>
        private string _ctlUltimoMotor;

        private const uint PW_RENDERFULLCONTENT = 0x00000002;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NRECT lpRect);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NRECT { public int Left, Top, Right, Bottom; }

        /// <summary>La VENTANA COMPLETA, incluida la superficie nativa del WebView2
        /// (RenderTargetBitmap no puede con ella; PrintWindow con PW_RENDERFULLCONTENT si).</summary>
        private void CaptureWindowNative(string path)
        {
            try
            {
                UpdateLayout();
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
                if (!GetWindowRect(hwnd, out var r)) return;
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                if (w <= 0 || h <= 0) return;
                using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    try { PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT); }
                    finally { g.ReleaseHdc(hdc); }
                }
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch { }
        }

        private void StartControlServer()
        {
            // Ventana a tamaño NORMAL: una minimizada da un PNG minusculo.
            try
            {
                WindowState = WindowState.Normal;
                if (Width < 1200) Width = 1400;
                if (Height < 700) Height = 900;
                Activate();
            }
            catch { }
            try { Directory.CreateDirectory(_ctlDir); } catch { }
            try { File.WriteAllText(Path.Combine(_ctlDir, "ready.txt"), Environment.ProcessId.ToString()); } catch { }
            _ctlTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _ctlTimer.Tick += async (s, e) => await CtlPoll();
            _ctlTimer.Start();
        }

        private async Task CtlPoll()
        {
            if (_ctlBusy) return;
            string[] cmds;
            try { cmds = Directory.GetFiles(_ctlDir, "cmd-*.json"); }
            catch { return; }
            Array.Sort(cmds, StringComparer.Ordinal);
            foreach (var f in cmds)
            {
                if (_ctlDone.Contains(f)) continue;
                _ctlDone.Add(f);
                _ctlBusy = true;
                try { await CtlExecute(f); } catch { }
                _ctlBusy = false;
            }
        }

        private async Task CtlWaitCalc()
        {
            for (var t = 0; t < 400 && _isParsing; t++) await Task.Delay(80);
            await Task.Delay(700);   // settle del render (graficas)
        }

        private async Task CtlExecute(string cmdFile)
        {
            string json; try { json = File.ReadAllText(cmdFile); } catch { return; }
            var id = Path.GetFileNameWithoutExtension(cmdFile);
            if (id.StartsWith("cmd-")) id = id[4..];
            var resp = "{\"ok\":true}";
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var op = root.GetProperty("op").GetString();
                switch (op)
                {
                    case "run":
                        IsCalculated = true;
                        CalculateAsync();
                        await CtlWaitCalc();
                        break;
                    case "settext":
                        SetInputText(root.GetProperty("text").GetString());
                        ForceHighlight();
                        SincronizarHaciaAvalon();
                        IsCalculated = true;
                        CalculateAsync();
                        await CtlWaitCalc();
                        break;
                    case "capture":
                        await Task.Delay(300);
                        CaptureWindowNative(root.GetProperty("path").GetString());
                        break;
                    case "js":
                        var jr = "null";
                        try { jr = await WebViewer.ExecuteScriptAsync(root.GetProperty("code").GetString()); }
                        catch { }
                        resp = "{\"ok\":true,\"result\":" + (string.IsNullOrEmpty(jr) ? "null" : jr) + "}";
                        break;
                    case "getoutput":
                        var outText = "\"\"";
                        try
                        {
                            outText = await WebViewer.ExecuteScriptAsync(
                                "(document.getElementById('matlab-output')||document.body).innerText");
                        }
                        catch { }
                        resp = "{\"ok\":true,\"output\":" + (string.IsNullOrEmpty(outText) ? "\"\"" : outText) + "}";
                        break;
                    case "quit":
                        try { File.WriteAllText(Path.Combine(_ctlDir, "resp-" + id + ".json"), resp); } catch { }
                        Environment.Exit(0);
                        break;
                    default:
                        // Ops del EDITOR (MainWindow.Pruebas.cs): null = tampoco es de alli.
                        resp = CtlEditorOp(op, root) ?? "{\"ok\":false,\"error\":\"op desconocida\"}";
                        break;
                }
            }
            catch (Exception ex)
            {
                resp = "{\"ok\":false,\"error\":" + JsonSerializer.Serialize(ex.Message) + "}";
            }
            try { File.WriteAllText(Path.Combine(_ctlDir, "resp-" + id + ".json"), resp); } catch { }
        }
    }
}
