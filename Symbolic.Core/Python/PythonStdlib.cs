// Stdlib EMBEBIDA para Hekatan Python3 — sys / time / os (os.path) nativos, sin Python externo.
// Cubre lo que usan los scripts FEM: sys.argv, time.time/perf_counter, os.path.dirname/abspath/join/exists, os.environ/getenv.
using System;
using System.Collections.Generic;
using System.IO;

namespace Calcpad.Core.Python
{
    internal static class PythonStdlib
    {
        // reloj monotono base (perf_counter): ticks desde el arranque del proceso
        private static readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

        public static PyModule Sys(string scriptPath)
        {
            var m = new PyModule("sys");
            var argv = new PyList();
            argv.Items.Add(scriptPath ?? "");            // argv[0] = ruta del script (argv[1:] vacio -> defaults)
            m.Attrs["argv"] = argv;
            m.Attrs["executable"] = "HekatanPython3 (nativo)";
            m.Attrs["platform"] = OperatingSystem.IsWindows() ? "win32" : "linux";
            m.Attrs["version"] = "3.12 (Hekatan nativo)";
            m.Attrs["maxsize"] = (long)long.MaxValue;
            var mods = new PyDict();
            m.Attrs["modules"] = mods;
            m.Attrs["path"] = new PyList();
            m.Attrs["stdout"] = new PyModule("__stdout__");
            m.Attrs["stderr"] = new PyModule("__stderr__");
            m.Attrs["exit"] = new PyBuiltin("exit", (a, kw) => throw new PyRuntimeError("SystemExit", a.Length > 0 ? PyOps.Str(a[0]) : ""));
            return m;
        }

        public static PyModule Time()
        {
            var m = new PyModule("time");
            m.Attrs["time"] = new PyBuiltin("time", (a, kw) => (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
            m.Attrs["perf_counter"] = new PyBuiltin("perf_counter", (a, kw) => _sw.Elapsed.TotalSeconds);
            m.Attrs["monotonic"] = new PyBuiltin("monotonic", (a, kw) => _sw.Elapsed.TotalSeconds);
            m.Attrs["sleep"] = new PyBuiltin("sleep", (a, kw) =>
            {
                try { int ms = (int)(PyOps.ToDouble(a[0]) * 1000); if (ms > 0) System.Threading.Thread.Sleep(Math.Min(ms, 60000)); } catch { }
                return null;
            });
            return m;
        }

        public static PyModule Os(string scriptDir)
        {
            var os = new PyModule("os");
            os.Attrs["sep"] = Path.DirectorySeparatorChar.ToString();
            os.Attrs["linesep"] = Environment.NewLine;
            os.Attrs["name"] = OperatingSystem.IsWindows() ? "nt" : "posix";
            os.Attrs["getcwd"] = new PyBuiltin("getcwd", (a, kw) => scriptDir ?? Directory.GetCurrentDirectory());
            os.Attrs["listdir"] = new PyBuiltin("listdir", (a, kw) =>
            {
                var dir = a.Length > 0 ? PyOps.Str(a[0]) : (scriptDir ?? Directory.GetCurrentDirectory());
                var l = new PyList();
                try { foreach (var e in Directory.GetFileSystemEntries(dir)) l.Items.Add(Path.GetFileName(e)); } catch { }
                return l;
            });
            // os.environ / os.getenv (2026-09-04): el talud GEO5 leia variables (GEO5_SRFROUND, HK_DUMP) y el
            // motor nativo no las tenia -> "module 'os' has no attribute 'environ'". Copia del entorno del proceso.
            var env = new PyDict();
            foreach (System.Collections.DictionaryEntry de in Environment.GetEnvironmentVariables())
                if (de.Key is string ek) env.Set(ek, de.Value?.ToString() ?? "");
            os.Attrs["environ"] = env;
            os.Attrs["getenv"] = new PyBuiltin("getenv", (a, kw) =>
            {
                var k = PyOps.Str(a[0]); var v = Environment.GetEnvironmentVariable(k);
                return v ?? (a.Length > 1 ? a[1] : null);
            });
            os.Attrs["putenv"] = new PyBuiltin("putenv", (a, kw) => { try { Environment.SetEnvironmentVariable(PyOps.Str(a[0]), PyOps.Str(a[1])); env.Set(PyOps.Str(a[0]), PyOps.Str(a[1])); } catch { } return null; });
            os.Attrs["makedirs"] = new PyBuiltin("makedirs", (a, kw) => { try { Directory.CreateDirectory(PyOps.Str(a[0])); } catch { } return null; });
            os.Attrs["mkdir"] = new PyBuiltin("mkdir", (a, kw) => { try { Directory.CreateDirectory(PyOps.Str(a[0])); } catch { } return null; });

            var path = new PyModule("os.path");
            path.Attrs["sep"] = Path.DirectorySeparatorChar.ToString();
            path.Attrs["dirname"] = new PyBuiltin("dirname", (a, kw) => Path.GetDirectoryName(PyOps.Str(a[0])) ?? "");
            path.Attrs["basename"] = new PyBuiltin("basename", (a, kw) => Path.GetFileName(PyOps.Str(a[0])) ?? "");
            path.Attrs["abspath"] = new PyBuiltin("abspath", (a, kw) => { try { return Path.GetFullPath(PyOps.Str(a[0])); } catch { return PyOps.Str(a[0]); } });
            path.Attrs["realpath"] = new PyBuiltin("realpath", (a, kw) => { try { return Path.GetFullPath(PyOps.Str(a[0])); } catch { return PyOps.Str(a[0]); } });
            path.Attrs["exists"] = new PyBuiltin("exists", (a, kw) => { var p = PyOps.Str(a[0]); return File.Exists(p) || Directory.Exists(p); });
            path.Attrs["isfile"] = new PyBuiltin("isfile", (a, kw) => File.Exists(PyOps.Str(a[0])));
            path.Attrs["isdir"] = new PyBuiltin("isdir", (a, kw) => Directory.Exists(PyOps.Str(a[0])));
            path.Attrs["join"] = new PyBuiltin("join", (a, kw) =>
            {
                var parts = new string[a.Length]; for (int i = 0; i < a.Length; i++) parts[i] = PyOps.Str(a[i]);
                return parts.Length == 0 ? "" : Path.Combine(parts);
            });
            path.Attrs["splitext"] = new PyBuiltin("splitext", (a, kw) =>
            {
                var p = PyOps.Str(a[0]); var ext = Path.GetExtension(p);
                return new PyTuple(new List<object> { p.Substring(0, p.Length - ext.Length), ext });
            });
            path.Attrs["split"] = new PyBuiltin("split", (a, kw) =>
            {
                var p = PyOps.Str(a[0]);
                return new PyTuple(new List<object> { Path.GetDirectoryName(p) ?? "", Path.GetFileName(p) ?? "" });
            });
            os.Attrs["path"] = path;
            return os;
        }
    }
}
