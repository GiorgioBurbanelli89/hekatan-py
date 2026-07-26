// =============================================================================
// Hekatan Python3 — Selector de ENTORNOS de Python (venv / conda / sistema)
// =============================================================================
//   Patrón estándar de Python: las librerías (numpy, scipy, matplotlib) NO se
//   instalan globalmente, sino dentro de un *entorno* (carpeta aislada con su
//   propio python.exe). Este gestor:
//     • DESCUBRE los entornos disponibles en la máquina (PATH, py launcher,
//       venvs junto al script, instalaciones conda).
//     • Permite AGREGAR un venv existente (carpeta) o CREAR uno nuevo.
//     • SELECCIONA cuál usa Suite Py → fija RealPython.Interpreter al python.exe
//       de ese entorno, así los `import numpy` salen de ESE entorno.
//     • INSTALA numpy/scipy/matplotlib en el entorno activo (pip), sin tocar el
//       Python del sistema.
//   La selección y los entornos agregados se PERSISTEN en
//   %LOCALAPPDATA%\HekatanPython3\pyenv.json.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Calcpad.Core.Python
{
    /// <summary>Un entorno de Python detectado o agregado por el usuario.</summary>
    public sealed class PythonEnv
    {
        public string DisplayName { get; init; }   // texto para el menú
        public string PythonExe { get; init; }      // ruta absoluta al python.exe
        public string Kind { get; init; }           // "Sistema" | "venv" | "conda"
        public string Folder { get; init; }         // carpeta raíz del entorno (si aplica)

        public override bool Equals(object obj) =>
            obj is PythonEnv e && string.Equals(NormExe(e.PythonExe), NormExe(PythonExe),
                StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode() => NormExe(PythonExe).ToLowerInvariant().GetHashCode();

        public static string NormExe(string p)
        {
            try { return string.IsNullOrEmpty(p) ? "" : Path.GetFullPath(p); }
            catch { return p ?? ""; }
        }
    }

    public static class PythonEnvironments
    {
        private sealed class Persisted
        {
            public string Selected { get; set; }            // python.exe activo
            public List<string> Folders { get; set; } = new(); // venvs agregados por el usuario
        }

        private static readonly object _lock = new();
        private static Persisted _state;

        private static string ConfigDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "HekatanPython3");
        private static string ConfigFile => Path.Combine(ConfigDir, "pyenv.json");

        /// <summary>python.exe activo (o null = usar "python" del PATH por defecto).</summary>
        public static string ActiveInterpreter { get; private set; }

        /// <summary>Etiqueta corta del entorno activo para mostrar en la UI.</summary>
        public static string ActiveLabel { get; private set; } = "Python del sistema (PATH)";

        // ─────────────────────────────────────────────────────────────────────
        //  Arranque: cargar la selección guardada y aplicarla a RealPython.
        //  La llama el WPF (App) y el CLI antes de ejecutar cualquier .py.
        // ─────────────────────────────────────────────────────────────────────
        public static void Initialize()
        {
            lock (_lock)
            {
                Load();
                if (!string.IsNullOrEmpty(_state.Selected) && File.Exists(_state.Selected))
                    Apply(_state.Selected, LabelFor(_state.Selected));
            }
        }

        private static void Load()
        {
            if (_state != null) return;
            try
            {
                if (File.Exists(ConfigFile))
                    _state = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(ConfigFile)) ?? new Persisted();
                else
                    _state = new Persisted();
            }
            catch { _state = new Persisted(); }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ConfigFile,
                    JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* persistencia best-effort */ }
        }

        private static void Apply(string pythonExe, string label)
        {
            ActiveInterpreter = pythonExe;
            ActiveLabel = label;
            RealPython.Interpreter = string.IsNullOrEmpty(pythonExe)
                ? (OperatingSystem.IsWindows() ? "python" : "python3")
                : pythonExe;
        }

        /// <summary>Selecciona un entorno como activo y lo persiste.
        /// pythonExe == null o "" → vuelve al "python" del PATH del sistema.</summary>
        public static void Select(string pythonExe, string label = null)
        {
            lock (_lock)
            {
                Load();
                _state.Selected = string.IsNullOrEmpty(pythonExe) ? null : PythonEnv.NormExe(pythonExe);
                Save();
                Apply(_state.Selected, label ?? LabelFor(_state.Selected));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Descubrimiento de entornos
        // ─────────────────────────────────────────────────────────────────────
        public static List<PythonEnv> Discover()
        {
            lock (_lock) { Load(); }
            var list = new List<PythonEnv>();
            var seen = new HashSet<PythonEnv>();

            void Add(PythonEnv e)
            {
                if (e == null || string.IsNullOrEmpty(e.PythonExe)) return;
                if (!File.Exists(e.PythonExe)) return;
                if (seen.Add(e)) list.Add(e);
            }

            // 1) Pythons del sistema (py launcher + PATH)
            foreach (var exe in SystemPythons())
                Add(new PythonEnv { DisplayName = "Sistema — " + Path.GetDirectoryName(exe),
                                    PythonExe = exe, Kind = "Sistema" });

            // 2) venvs junto al script y en el cwd (.venv / venv / env)
            foreach (var root in CandidateScriptRoots())
                foreach (var name in new[] { ".venv", "venv", "env" })
                {
                    var f = Path.Combine(root, name);
                    if (IsVenv(f))
                        Add(new PythonEnv { DisplayName = name + " — junto al script", Kind = "venv",
                                            Folder = f, PythonExe = VenvPython(f) });
                }

            // 3) venvs agregados por el usuario
            foreach (var f in _state.Folders.ToArray())
                if (IsVenv(f))
                    Add(new PythonEnv { DisplayName = Path.GetFileName(f.TrimEnd(Path.DirectorySeparatorChar)) + " — agregado",
                                        Kind = "venv", Folder = f, PythonExe = VenvPython(f) });

            // 4) entornos conda (base + envs/*)
            foreach (var (name, exe, folder) in CondaEnvs())
                Add(new PythonEnv { DisplayName = "conda — " + name, Kind = "conda",
                                    Folder = folder, PythonExe = exe });

            return list;
        }

        private static IEnumerable<string> CandidateScriptRoots()
        {
            var roots = new List<string>();
            if (!string.IsNullOrEmpty(RealPython.ScriptDirectory) && Directory.Exists(RealPython.ScriptDirectory))
                roots.Add(RealPython.ScriptDirectory);
            try { roots.Add(Directory.GetCurrentDirectory()); } catch { }
            return roots.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> SystemPythons()
        {
            var found = new List<string>();
            // py launcher: lista rutas de todas las versiones instaladas
            foreach (var line in RunLines("py", "-0p"))
            {
                // formato: " -V:3.12 *        C:\...\python.exe"  (la ruta es lo último)
                var idx = line.IndexOf(':');
                var path = line.Trim();
                var sp = path.LastIndexOf("  ", StringComparison.Ordinal);
                if (sp >= 0) path = path.Substring(sp).Trim();
                if (path.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                    found.Add(path);
                _ = idx;
            }
            // where python / where python3 (PATH)
            foreach (var cmd in new[] { "python", "python3" })
                foreach (var p in RunLines("where", cmd))
                {
                    var path = p.Trim();
                    if (path.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)
                        && !path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) // alias de la Store: no real
                        found.Add(path);
                }
            return found.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<(string Name, string Exe, string Folder)> CondaEnvs()
        {
            var roots = new List<string>();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var b in new[] { "anaconda3", "miniconda3", "miniforge3", "mambaforge" })
            {
                roots.Add(Path.Combine(home, b));
                roots.Add(Path.Combine(local, b));
                roots.Add(Path.Combine(@"C:\", b));
                roots.Add(Path.Combine(@"C:\ProgramData", b));
            }
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root)) continue;
                var baseExe = Path.Combine(root, "python.exe");
                if (File.Exists(baseExe)) yield return ("base (" + Path.GetFileName(root) + ")", baseExe, root);
                var envsDir = Path.Combine(root, "envs");
                if (Directory.Exists(envsDir))
                    foreach (var d in SafeDirs(envsDir))
                    {
                        var exe = Path.Combine(d, "python.exe");
                        if (File.Exists(exe)) yield return (Path.GetFileName(d), exe, d);
                    }
            }
        }

        private static IEnumerable<string> SafeDirs(string root)
        {
            try { return Directory.GetDirectories(root); }
            catch { return Array.Empty<string>(); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Directiva POR-SCRIPT:  #venv <ruta>   /   #env <ruta-o-python.exe>
        //  Permite que el propio .py declare en qué entorno corre, sin tocar el
        //  menú. La ruta puede ser relativa (a la carpeta del script) o absoluta,
        //  apuntar a la CARPETA del venv o directo a un python.exe.
        //    #venv .venv                  → <script>\.venv\Scripts\python.exe
        //    #env  C:\envs\fem            → C:\envs\fem\Scripts\python.exe
        //    #env  C:\Python312\python.exe→ ese intérprete tal cual
        //  Devuelve el python.exe a usar, o null si no hay directiva (o no resuelve).
        // ─────────────────────────────────────────────────────────────────────
        public static string ResolveScriptEnv(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            string raw = null;
            foreach (var line in source.Replace("\r\n", "\n").Split('\n'))
            {
                var t = line.TrimStart();
                if (t.StartsWith("#venv", StringComparison.OrdinalIgnoreCase)) { raw = t.Substring(5).Trim(); break; }
                if (t.StartsWith("#env", StringComparison.OrdinalIgnoreCase) &&
                    (t.Length == 4 || char.IsWhiteSpace(t[4]))) { raw = t.Substring(4).Trim(); break; }
            }
            if (string.IsNullOrEmpty(raw)) return null;
            raw = raw.Trim('"', '\'');

            // resolver relativo a la carpeta del script
            string path = raw;
            try
            {
                if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(RealPython.ScriptDirectory))
                    path = Path.GetFullPath(Path.Combine(RealPython.ScriptDirectory, raw));
            }
            catch { }

            // ¿apunta directo a un python(.exe)?
            if (File.Exists(path) &&
                Path.GetFileName(path).StartsWith("python", StringComparison.OrdinalIgnoreCase))
                return path;
            // ¿es una carpeta de venv?
            if (IsVenv(path)) return VenvPython(path);
            // último intento: carpeta cualquiera con Scripts\python.exe o bin/python
            var vp = VenvPython(path);
            if (File.Exists(vp)) return vp;
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers de venv
        // ─────────────────────────────────────────────────────────────────────
        public static string VenvPython(string folder) =>
            OperatingSystem.IsWindows()
                ? Path.Combine(folder, "Scripts", "python.exe")
                : Path.Combine(folder, "bin", "python");

        /// <summary>¿La carpeta es un entorno virtual válido? (pyvenv.cfg + python.exe).</summary>
        public static bool IsVenv(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
                return File.Exists(Path.Combine(folder, "pyvenv.cfg")) && File.Exists(VenvPython(folder));
            }
            catch { return false; }
        }

        /// <summary>Agrega un venv existente (por carpeta) a la lista persistida.
        /// Devuelve el PythonEnv o null si la carpeta no es un venv válido.</summary>
        public static PythonEnv AddVenvFolder(string folder)
        {
            if (!IsVenv(folder)) return null;
            lock (_lock)
            {
                Load();
                var full = Path.GetFullPath(folder);
                if (!_state.Folders.Any(f => string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase)))
                {
                    _state.Folders.Add(full);
                    Save();
                }
                return new PythonEnv
                {
                    DisplayName = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)) + " — agregado",
                    Kind = "venv", Folder = full, PythonExe = VenvPython(full)
                };
            }
        }

        /// <summary>Crea un venv nuevo en <paramref name="folder"/> usando <paramref name="basePython"/>
        /// (o el python del PATH). Devuelve (ok, mensaje). NO instala paquetes.</summary>
        public static (bool Ok, string Message) CreateVenv(string folder, string basePython = null)
        {
            try
            {
                var py = string.IsNullOrEmpty(basePython)
                    ? (OperatingSystem.IsWindows() ? "python" : "python3") : basePython;
                var (so, se, ok) = RunCapture(py, $"-m venv \"{folder}\"", 120000);
                if (ok && IsVenv(folder)) { AddVenvFolder(folder); return (true, "Entorno creado en " + folder); }
                return (false, string.IsNullOrWhiteSpace(se) ? so : se);
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        /// <summary>Instala (o actualiza) numpy/scipy/matplotlib en el python dado, transmitiendo
        /// la salida de pip línea a línea. Devuelve (ok, stderr).</summary>
        public static (bool Ok, string Stderr) InstallScientificStack(string pythonExe, Action<string> onLine,
            string[] packages = null)
        {
            packages ??= new[] { "numpy", "scipy", "matplotlib" };
            var args = "-m pip install --upgrade " + string.Join(" ", packages);
            return RunStreaming(pythonExe, args, onLine, 600000);
        }

        /// <summary>Comprueba qué paquetes científicos están presentes en el python dado.</summary>
        public static Dictionary<string, bool> CheckModules(string pythonExe, string[] modules = null)
        {
            modules ??= new[] { "numpy", "scipy", "matplotlib" };
            var result = new Dictionary<string, bool>();
            foreach (var m in modules)
            {
                var (_, _, ok) = RunCapture(pythonExe, $"-c \"import {m}\"", 15000);
                result[m] = ok;
            }
            return result;
        }

        /// <summary>Versión del intérprete (ej. "Python 3.12.1"), o "" si falla.</summary>
        public static string Version(string pythonExe)
        {
            var (so, se, ok) = RunCapture(pythonExe, "--version", 8000);
            if (!ok) return "";
            var v = (so + se).Trim();
            return v.Length > 40 ? v.Substring(0, 40) : v;
        }

        private static string LabelFor(string pythonExe)
        {
            if (string.IsNullOrEmpty(pythonExe)) return "Python del sistema (PATH)";
            var dir = Path.GetDirectoryName(pythonExe) ?? "";
            // venv → Scripts\python.exe : el nombre útil es la carpeta del venv
            if (dir.EndsWith("Scripts", StringComparison.OrdinalIgnoreCase) || dir.EndsWith("bin", StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(Path.GetDirectoryName(dir)?.TrimEnd(Path.DirectorySeparatorChar) ?? dir);
            return Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Utilidades de proceso
        // ─────────────────────────────────────────────────────────────────────
        private static IEnumerable<string> RunLines(string file, string args)
        {
            var (so, _, ok) = RunCapture(file, args, 8000);
            if (!ok || string.IsNullOrEmpty(so)) return Array.Empty<string>();
            return so.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }

        private static (string Stdout, string Stderr, bool Ok) RunCapture(string file, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                psi.Environment["PYTHONIOENCODING"] = "utf-8";
                using var p = Process.Start(psi);
                if (p == null) return ("", "no se pudo iniciar " + file, false);
                var so = p.StandardOutput.ReadToEnd();
                var se = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (so, "timeout", false); }
                return (so, se, p.ExitCode == 0);
            }
            catch (Exception ex) { return ("", ex.Message, false); }
        }

        private static (bool Ok, string Stderr) RunStreaming(string file, string args, Action<string> onLine, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                psi.Environment["PYTHONIOENCODING"] = "utf-8";
                psi.Environment["PYTHONUNBUFFERED"] = "1";
                using var p = Process.Start(psi);
                if (p == null) return (false, "no se pudo iniciar " + file);
                var err = new StringBuilder();
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) { err.AppendLine(e.Data); onLine?.Invoke(e.Data); } };
                p.BeginErrorReadLine();
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null) onLine?.Invoke(line);
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (false, err + "\ntimeout"); }
                p.WaitForExit();
                return (p.ExitCode == 0, err.ToString());
            }
            catch (Exception ex) { return (false, ex.Message); }
        }
    }
}
