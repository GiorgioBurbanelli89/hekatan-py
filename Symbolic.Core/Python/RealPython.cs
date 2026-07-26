// =============================================================================
// Hekatan Python3 — Puente al intérprete python real (subprocess)
// =============================================================================
//   Ejecuta un script en el `python` del sistema cuando el motor nativo C# no
//   alcanza (numpy, sympy, matplotlib, imports no nativos, etc.). Reusa el
//   patrón ya usado en Calcpad para bloques #python.
// =============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Calcpad.Core.Python
{
    public static class RealPython
    {
        /// <summary>Comando del intérprete (python / python3 / py). Configurable.
        /// Lo fija el selector de entornos (PythonEnvironments) o queda en el python del PATH.</summary>
        public static string Interpreter { get; set; } = DefaultInterpreter();

        /// <summary>Override POR-SCRIPT: si el .py declara su entorno con `#venv`/`#env`,
        /// el pipeline pone aquí el python.exe de ese entorno SOLO para esa ejecución.
        /// Tiene prioridad sobre Interpreter. null/"" → se usa Interpreter (la elección del menú).</summary>
        public static string OverrideInterpreter { get; set; }

        /// <summary>Intérprete efectivo: el override por-script si existe, si no el del selector.</summary>
        public static string Effective =>
            string.IsNullOrEmpty(OverrideInterpreter) ? Interpreter : OverrideInterpreter;

        public static int TimeoutMs { get; set; } = 60000;

        /// <summary>Carpeta del documento actual (.py). Se usa como WorkingDirectory del
        /// subproceso y se agrega a PYTHONPATH para que los `import &lt;modulo_hermano&gt;`
        /// (ej. `import fem_numpy`) y los `open("archivo_relativo")` encuentren los
        /// archivos que están JUNTO al script. Sin esto, el script corre en %TEMP%
        /// y falla con ModuleNotFoundError. La pone el WPF/CLI antes de ejecutar.</summary>
        public static string ScriptDirectory { get; set; }

        private static string DefaultInterpreter()
            => OperatingSystem.IsWindows() ? "python" : "python3";

        /// <summary>Carpeta de trabajo efectiva: ScriptDirectory si es válida, si no el CWD actual.</summary>
        private static string WorkDir()
        {
            if (!string.IsNullOrEmpty(ScriptDirectory) && Directory.Exists(ScriptDirectory))
                return ScriptDirectory;
            return Directory.GetCurrentDirectory();
        }

        /// <summary>Escribe el motor 3D nativo (glplot.js/GL3) a un temp fijo, para que la
        /// intercepción de PyVista lo inyecte inline y muestre el 3D INTERACTIVO embebido.</summary>
        public static void EnsureGlPlot()
        {
            try
            {
                var p = Path.Combine(Path.GetTempPath(), "cpspy_glplot.min.js");
                if (!File.Exists(p) || new FileInfo(p).Length != GlPlotJs.Min.Length)
                    File.WriteAllText(p, GlPlotJs.Min, new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>Aplica WorkingDirectory + PYTHONPATH (carpeta del documento) al subproceso,
        /// para que los imports de módulos hermanos y los open() relativos funcionen.</summary>
        private static void ApplyScriptLocation(ProcessStartInfo psi)
        {
            EnsureGlPlot();
            var workDir = WorkDir();
            try { psi.WorkingDirectory = workDir; } catch { }
            var existing = Environment.GetEnvironmentVariable("PYTHONPATH");
            psi.Environment["PYTHONPATH"] = string.IsNullOrEmpty(existing)
                ? workDir
                : workDir + Path.PathSeparator + existing;
        }

        public static (string Stdout, string Stderr, bool Ok) Execute(string code)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "calcpad_py_" + Guid.NewGuid().ToString("N") + ".py");
            try
            {
                // El .py corre desde un temp, así que os.path.dirname(__file__) apuntaría al temp.
                // Fijamos __file__ a la carpeta REAL del script para que open(join(dirname(__file__),...)) resuelva.
                string preamble = "";
                if (!string.IsNullOrEmpty(ScriptDirectory) && Directory.Exists(ScriptDirectory))
                    preamble = "__file__ = r\"\"\"" + Path.Combine(ScriptDirectory, "__main__.py") + "\"\"\"\n";
                File.WriteAllText(tempFile, preamble + code, new UTF8Encoding(false));
                var psi = new ProcessStartInfo
                {
                    FileName = Effective,
                    Arguments = $"-X utf8 \"{tempFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                psi.Environment["PYTHONIOENCODING"] = "utf-8";
                ApplyScriptLocation(psi);

                using var proc = Process.Start(psi);
                if (proc == null)
                    return ("", "No se pudo iniciar el intérprete de Python. ¿Está instalado y en el PATH?", false);

                var so = proc.StandardOutput.ReadToEnd();
                var se = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(TimeoutMs))
                {
                    try { proc.Kill(true); } catch { }
                    return (so, "Timeout: el script Python excedió el tiempo límite.", false);
                }
                return (so, se, proc.ExitCode == 0);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return ("", $"No se encontró el intérprete '{Interpreter}'. Instalá Python o configurá RealPython.Interpreter.", false);
            }
            catch (Exception ex)
            {
                return ("", $"Error ejecutando Python: {ex.Message}", false);
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        /// <summary>Ejecuta el script y entrega cada línea de stdout EN VIVO (a medida que
        /// Python la imprime), para mostrar el output línea por línea en el WebView2.
        /// Usa `-u` (unbuffered) + ReadLine en bucle. Devuelve (stderr, ok) al terminar.</summary>
        public static (string Stderr, bool Ok) ExecuteStreaming(string code, Action<string> onStdoutLine)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "calcpad_py_" + Guid.NewGuid().ToString("N") + ".py");
            try
            {
                // El .py corre desde un temp, así que os.path.dirname(__file__) apuntaría al temp.
                // Fijamos __file__ a la carpeta REAL del script para que open(join(dirname(__file__),...)) resuelva.
                string preamble = "";
                if (!string.IsNullOrEmpty(ScriptDirectory) && Directory.Exists(ScriptDirectory))
                    preamble = "__file__ = r\"\"\"" + Path.Combine(ScriptDirectory, "__main__.py") + "\"\"\"\n";
                File.WriteAllText(tempFile, preamble + code, new UTF8Encoding(false));
                var psi = new ProcessStartInfo
                {
                    FileName = Effective,
                    Arguments = $"-X utf8 -u \"{tempFile}\"",   // -u = stdout sin buffer → llega línea a línea
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                psi.Environment["PYTHONIOENCODING"] = "utf-8";
                psi.Environment["PYTHONUNBUFFERED"] = "1";
                ApplyScriptLocation(psi);

                using var proc = Process.Start(psi);
                if (proc == null)
                    return ("No se pudo iniciar el intérprete de Python. ¿Está instalado y en el PATH?", false);

                var stderr = new StringBuilder();
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                proc.BeginErrorReadLine();

                string line;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                    onStdoutLine(line);                          // ← cada línea, EN VIVO

                if (!proc.WaitForExit(TimeoutMs))
                {
                    try { proc.Kill(true); } catch { }
                    return (stderr + "\nTimeout: el script Python excedió el tiempo límite.", false);
                }
                proc.WaitForExit();
                return (stderr.ToString(), proc.ExitCode == 0);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return ($"No se encontró el intérprete '{Interpreter}'. Instalá Python o configurá RealPython.Interpreter.", false);
            }
            catch (Exception ex)
            {
                return ($"Error ejecutando Python: {ex.Message}", false);
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        /// <summary>Ejecuta un script GUI (PyQt/PySide/PyVista/tkinter) de forma DESACOPLADA:
        /// arranca el proceso, NO redirige stdout ni espera a que termine, y devuelve
        /// enseguida. Así la ventana nativa (Qt/VTK) se abre y vive por su cuenta — el
        /// cuaderno no la embebe ni se bloquea por el timeout. El .py temporal queda en
        /// %TEMP% (lo limpia el SO) porque el proceso hijo lo necesita mientras corre.</summary>
        public static (string Info, bool Ok) ExecuteDetached(string code)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "calcpad_pygui_" + Guid.NewGuid().ToString("N") + ".py");
            try
            {
                File.WriteAllText(tempFile, code, new UTF8Encoding(false));
                var psi = new ProcessStartInfo
                {
                    FileName = Effective,
                    Arguments = $"-X utf8 \"{tempFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,        // sin consola; la ventana GUI sí aparece
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                };
                psi.Environment["PYTHONIOENCODING"] = "utf-8";
                ApplyScriptLocation(psi);
                var proc = Process.Start(psi);   // NO using/await: vive independiente
                if (proc == null)
                    return ("No se pudo iniciar el intérprete de Python.", false);
                return ("🪟 Interfaz gráfica (Qt/PyVista) abierta en una ventana aparte.", true);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return ($"No se encontró el intérprete '{Interpreter}'.", false);
            }
            catch (Exception ex)
            {
                return ($"Error lanzando la interfaz Python: {ex.Message}", false);
            }
        }

        /// <summary>Comprueba si hay un intérprete de Python disponible.</summary>
        public static bool IsAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Effective,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit(5000);
                return true;
            }
            catch { return false; }
        }
    }
}
