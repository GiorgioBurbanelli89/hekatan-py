// =============================================================================
// Hekatan Python3 — Embed NATIVO del solver OpenSees (OpenSeesRT.dll) vía Tcl
// =============================================================================
//   OpenSeesRT.dll (xara) es una extensión Tcl con el motor OpenSees C++
//   (MKL Pardiso + JIT GEMM, multi-thread). Se maneja por la C-API de Tcl 8.6:
//     Tcl_CreateInterp → Tcl_Eval("load OpenSeesRT.dll") → Tcl_Eval("node 1 0 0")
//   SIN openseespy / CPython. El ensamble + solve corre en C++ (cero bucle Python).
// =============================================================================
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Calcpad.Core.Python
{
    public sealed class TclInterop : IDisposable
    {
        private const string TCL = "tcl86t";   // tcl86t.dll (debe estar en el dll-search-path)

        [DllImport(TCL, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Tcl_CreateInterp();
        [DllImport(TCL, CallingConvention = CallingConvention.Cdecl)] private static extern int Tcl_Init(IntPtr interp);
        [DllImport(TCL, CallingConvention = CallingConvention.Cdecl)] private static extern int Tcl_Eval(IntPtr interp, [MarshalAs(UnmanagedType.LPUTF8Str)] string script);
        [DllImport(TCL, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Tcl_GetStringResult(IntPtr interp);
        [DllImport(TCL, CallingConvention = CallingConvention.Cdecl)] private static extern void Tcl_DeleteInterp(IntPtr interp);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr AddDllDirectory(string path);
        [DllImport("kernel32")] private static extern int SetDefaultDllDirectories(uint flags);

        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;

        private IntPtr _interp;
        private static bool _setup;
        private static string _rtDll, _tclDir, _mklDir;

        /// <summary>True si se encontraron las DLLs (Tcl + OpenSeesRT) en el sistema.</summary>
        public static bool Available => Locate();
        public static string LastError { get; private set; }

        // ---- localizar las DLLs (Tcl, OpenSeesRT, MKL) en la instalación de Python/xara ----
        private static bool Locate()
        {
            if (_rtDll != null) return _rtDll.Length > 0;
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string[] siteRoots = {
                    // 1º: junto a la app (si en el futuro se bundlea el "OpenSees pack")
                    Path.Combine(AppContext.BaseDirectory, "opensees"),
                    AppContext.BaseDirectory,
                    // 2º: instalación de Python/xara del sistema (pip install opensees)
                    Path.Combine(roaming, "Python"),
                    Path.Combine(home, "AppData", "Local", "Programs", "Python"),
                    @"C:\Program Files\Python312", @"C:\Program Files\Python311",
                };
                // OpenSeesRT.dll  (paquete 'opensees' = xara)
                _rtDll = "";
                foreach (var root in siteRoots)
                    if (Directory.Exists(root))
                        foreach (var f in SafeEnum(root, "OpenSeesRT.dll"))
                            { _rtDll = f; break; }
                // tcl86t.dll  (DLLs de Python)
                _tclDir = "";
                foreach (var root in siteRoots)
                    if (Directory.Exists(root))
                        foreach (var f in SafeEnum(root, "tcl86t.dll"))
                            { _tclDir = Path.GetDirectoryName(f); break; }
                // si no se encontró Tcl junto a opensees, probar Python\DLLs
                if (_tclDir.Length == 0)
                    foreach (var root in siteRoots)
                    {
                        var d = Path.Combine(root, "DLLs");
                        if (File.Exists(Path.Combine(d, "tcl86t.dll"))) { _tclDir = d; break; }
                    }
                // mkl_rt.2.dll  (dependencia de OpenSeesRT — suele estar en ...\Python\Library\bin)
                _mklDir = "";
                foreach (var root in siteRoots)
                    if (Directory.Exists(root))
                        foreach (var f in SafeEnum(root, "mkl_rt.2.dll"))
                            { _mklDir = Path.GetDirectoryName(f); break; }
                return _rtDll.Length > 0 && _tclDir.Length > 0;
            }
            catch (Exception ex) { LastError = ex.Message; _rtDll = ""; return false; }
        }

        private static System.Collections.Generic.IEnumerable<string> SafeEnum(string root, string name)
        {
            string[] dirs;
            try { dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories); }
            catch { yield break; }
            foreach (var d in dirs)
            {
                string p = Path.Combine(d, name);
                if (File.Exists(p)) yield return p;
            }
        }

        // ---- preparar el dll-search-path + precargar deps (MKL/iomp/svml) ----
        private static void Setup()
        {
            if (_setup) return;
            _setup = true;
            SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
            var rtDir = Path.GetDirectoryName(_rtDll);
            foreach (var d in new[] { _tclDir, rtDir, _mklDir })
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) { try { AddDllDirectory(d); } catch { } }
            // precargar las deps en orden (para que el load de OpenSeesRT las resuelva)
            string[] deps = { "mkl_rt.2.dll", "libiomp5md.dll", "svml_dispmd.dll", "libmmd.dll", "libifcoremd.dll" };
            foreach (var dep in deps)
                foreach (var baseDir in new[] { _mklDir, rtDir })
                {
                    if (string.IsNullOrEmpty(baseDir)) continue;
                    var p = Path.Combine(baseDir, dep);
                    if (File.Exists(p)) { try { LoadLibrary(p); } catch { } break; }
                }
        }

        public TclInterop()
        {
            if (!Locate()) throw new InvalidOperationException("OpenSeesRT/Tcl no encontrados. " + LastError);
            Setup();
            _interp = Tcl_CreateInterp();
            if (_interp == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear el intérprete Tcl.");
            Tcl_Init(_interp);
            // cargar OpenSeesRT → registra los comandos OpenSees (node, element, eigen, ...)
            int rc = Tcl_Eval(_interp, "load {" + _rtDll.Replace("\\", "/") + "} OpenSeesRT");
            if (rc != 0) throw new InvalidOperationException("load OpenSeesRT falló: " + Result());
        }

        /// <summary>Ejecuta uno o varios comandos OpenSees/Tcl y devuelve el resultado (string).
        /// Para batch, pasar todas las líneas juntas (el loop arma el string, C++ ensambla).</summary>
        public string Eval(string cmd)
        {
            int rc = Tcl_Eval(_interp, cmd);
            var res = Result();
            if (rc != 0) throw new Exception("OpenSees: " + res);
            return res;
        }

        private string Result()
        {
            var p = Tcl_GetStringResult(_interp);
            return p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
        }

        public void Dispose()
        {
            if (_interp != IntPtr.Zero) { try { Tcl_DeleteInterp(_interp); } catch { } _interp = IntPtr.Zero; }
        }
    }
}
