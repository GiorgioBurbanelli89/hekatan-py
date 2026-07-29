// =============================================================================
// Hekatan Python3 — Python Evaluator (tree-walking interpreter)
// =============================================================================
//   Ejecuta el AST producido por PythonParser. Soporta un subset amplio de
//   Python: aritmética, control de flujo, funciones, clases, comprensiones,
//   f-strings, slicing, builtins comunes y el módulo `math`.
//   Lo no soportado lanza PythonNotSupported → el pipeline cae a python real.
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Calcpad.Core.Python
{
    public sealed class StatementResult
    {
        public bool Display;
        public List<(PyNode Target, object Value)> Assignments;  // assign / augassign
        public PyNode RhsExpr;   // expresión del lado derecho (para render "x = fórmula = valor")
        public PyNode Expr;      // expr stmt
        public object Value;     // valor del expr stmt
        public bool HasValue;    // expr produjo un valor != None
    }

    // ── Control de flujo vía excepciones ──
    internal sealed class BreakSignal : Exception { }
    internal sealed class ContinueSignal : Exception { }
    internal sealed class ReturnSignal : Exception { public object Value; }
    internal sealed class PyRaiseSignal : Exception { public object Value; public PyRaiseSignal(object v) { Value = v; } }

    /// <summary>Método ligado a un objeto (instance.method).</summary>
    public sealed class PyBoundMethod
    {
        public PyFunction Func;
        public object Self;
    }

    public sealed class PythonEvaluator
    {
        public PyScope Globals { get; }
        public Action<string> Output { get; set; } = _ => { };
        public Action<string> HtmlOut { get; set; } = _ => { };
        private int _vizId;
        private TclInterop _opensees;   // intérprete OpenSees nativo (MKL Pardiso+JIT), creado on-demand

        private static readonly HashSet<string> NativeModules = new(StringComparer.Ordinal)
        { "math", "cmath", "openseespy", "opensees", "openseespywin", "numpy", "scipy", "sys", "time", "os", "collections" };

        public PythonEvaluator()
        {
            Globals = new PyScope(null, null);
            RegisterBuiltins();
        }

        /// <summary>Indica si todos los imports del módulo son satisfacibles nativamente.</summary>
        public static bool CanRunNatively(List<PyNode> stmts, out string reason)
        {
            reason = null;
            foreach (var s in stmts)
            {
                if (!CheckImports(s, ref reason)) return false;
            }
            return true;
        }

        // ¿Existe <dir_script>/<name>.py? -> importable NATIVAMENTE como modulo local (no fuerza subprocess).
        private static bool IsLocalModule(string name)
        {
            var dir = RealPython.ScriptDirectory;
            if (string.IsNullOrEmpty(dir)) return false;
            try { return System.IO.File.Exists(System.IO.Path.Combine(dir, name + ".py")); } catch { return false; }
        }

        private static bool CheckImports(PyNode s, ref string reason)
        {
            switch (s)
            {
                case ImportStmt im:
                    foreach (var (mod, _) in im.Names)
                    {
                        var root = mod.Split('.')[0];
                        if (!NativeModules.Contains(root) && !IsLocalModule(root)) { reason = $"import {mod}"; return false; }
                    }
                    return true;
                case ImportFromStmt fr:
                    var r = fr.Module.TrimStart('.').Split('.')[0];
                    if (r.Length > 0 && !NativeModules.Contains(r) && !IsLocalModule(r)) { reason = $"from {fr.Module} import ..."; return false; }
                    return true;
                // Imports dentro de bloques de control (if/for/while/try/with/def/class) son CONDICIONALES
                // o GUARDADOS -> NO fuerzan fallback a Python externo; se resuelven en runtime (y si el
                // modulo no es nativo lanzan ImportError atrapable). Solo imports INCONDICIONALES top-level
                // fuerzan fallback. Esto arregla el idioma try/except-import (dependencias opcionales).
                case IfStmt: case ForStmt: case WhileStmt:
                case FuncDef: case ClassDef: case TryStmt: case WithStmt:
                default:
                    return true;
            }
        }

        // ===================================================================
        //  EJECUCIÓN DE STATEMENTS TOP-LEVEL (con StatementResult para render)
        // ===================================================================
        public StatementResult ExecuteOne(PyNode stmt, PyScope scope)
        {
            switch (stmt)
            {
                case AssignStmt a:
                {
                    var val = Eval(a.Value, scope);
                    var assigns = new List<(PyNode, object)>();
                    foreach (var tgt in a.Targets)
                    {
                        AssignTarget(tgt, val, scope);
                        assigns.Add((tgt, val));
                    }
                    return new StatementResult { Display = true, Assignments = assigns, RhsExpr = a.Value };
                }
                case AugAssignStmt ag:
                {
                    var cur = Eval(ag.Target, scope);
                    var rhs = Eval(ag.Value, scope);
                    var nv = PyOps.Binary(ag.Op, cur, rhs);
                    AssignTarget(ag.Target, nv, scope);
                    return new StatementResult { Display = true, Assignments = new() { (ag.Target, nv) } };
                }
                case ExprStmt e:
                {
                    var v = Eval(e.Expr, scope);
                    // String/f-string literal suelto = TEXTO (como ' o " de Calcpad), se muestra.
                    if (e.Expr is StringLit || e.Expr is FStringLit)
                        return new StatementResult { Display = true, Expr = e.Expr, Value = v, HasValue = true };
                    bool has = v != null;
                    return new StatementResult { Display = has, Expr = e.Expr, Value = v, HasValue = has };
                }
                case CommentStmt:
                    return new StatementResult { Display = true };
                default:
                    ExecStmt(stmt, scope);
                    return new StatementResult { Display = false };
            }
        }

        // ===================================================================
        //  EJECUCIÓN INTERNA (sin render)
        // ===================================================================
        public void ExecBlock(List<PyNode> body, PyScope scope)
        {
            foreach (var s in body) ExecStmt(s, scope);
        }

        private void ExecStmt(PyNode stmt, PyScope scope)
        {
            switch (stmt)
            {
                case AssignStmt a:
                {
                    var val = Eval(a.Value, scope);
                    foreach (var tgt in a.Targets) AssignTarget(tgt, val, scope);
                    break;
                }
                case AugAssignStmt ag:
                {
                    var cur = Eval(ag.Target, scope);
                    var rhs = Eval(ag.Value, scope);
                    AssignTarget(ag.Target, PyOps.Binary(ag.Op, cur, rhs), scope);
                    break;
                }
                case ExprStmt e: Eval(e.Expr, scope); break;
                case CommentStmt: break;
                case PassStmt: break;
                case BreakStmt: throw new BreakSignal();
                case ContinueStmt: throw new ContinueSignal();
                case ReturnStmt r: throw new ReturnSignal { Value = r.Value == null ? null : Eval(r.Value, scope) };
                case FuncDef fd: scope.Set(fd.Name, MakeFunction(fd, scope)); break;
                case ClassDef cd: ExecClassDef(cd, scope); break;
                case IfStmt iff: ExecIf(iff, scope); break;
                case ForStmt fo: ExecFor(fo, scope); break;
                case WhileStmt wh: ExecWhile(wh, scope); break;
                case TryStmt tr: ExecTry(tr, scope); break;
                case WithStmt wi: ExecWith(wi, scope); break;
                case ImportStmt im: ExecImport(im, scope); break;
                case ImportFromStmt fr: ExecImportFrom(fr, scope); break;
                case GlobalStmt g:
                    scope.GlobalNames ??= new HashSet<string>(StringComparer.Ordinal);
                    foreach (var nm in g.Names) scope.GlobalNames.Add(nm);
                    break;
                case NonlocalStmt: break; // tratado como global-ish (MVP)
                case DelStmt d:
                    foreach (var t in d.Targets) DelTarget(t, scope);
                    break;
                case AssertStmt asrt:
                    if (!PyOps.Truthy(Eval(asrt.Test, scope)))
                    {
                        var m = asrt.Message != null ? PyOps.Str(Eval(asrt.Message, scope)) : "";
                        throw new PyRuntimeError("AssertionError", m);
                    }
                    break;
                case RaiseStmt rs:
                    if (rs.Exc == null) throw new PyRuntimeError("RuntimeError", "No active exception to re-raise");
                    throw new PyRaiseSignal(Eval(rs.Exc, scope));
                default:
                    throw new PythonNotSupported($"statement {stmt.GetType().Name}");
            }
        }

        private void ExecIf(IfStmt iff, PyScope scope)
        {
            foreach (var (cond, body) in iff.Branches)
            {
                if (cond == null) { ExecBlock(body, scope); return; }
                if (PyOps.Truthy(Eval(cond, scope))) { ExecBlock(body, scope); return; }
            }
        }

        private void ExecFor(ForStmt fo, PyScope scope)
        {
            var iter = Eval(fo.Iter, scope);
            bool broke = false;
            foreach (var item in Iterate(iter))
            {
                AssignTarget(fo.Target, item, scope);
                try { ExecBlock(fo.Body, scope); }
                catch (BreakSignal) { broke = true; break; }
                catch (ContinueSignal) { }
            }
            if (!broke && fo.ElseBody != null) ExecBlock(fo.ElseBody, scope);
        }

        private void ExecWhile(WhileStmt wh, PyScope scope)
        {
            bool broke = false;
            while (PyOps.Truthy(Eval(wh.Cond, scope)))
            {
                try { ExecBlock(wh.Body, scope); }
                catch (BreakSignal) { broke = true; break; }
                catch (ContinueSignal) { }
            }
            if (!broke && wh.ElseBody != null) ExecBlock(wh.ElseBody, scope);
        }

        private void ExecTry(TryStmt tr, PyScope scope)
        {
            try
            {
                ExecBlock(tr.Body, scope);
                if (tr.ElseBody != null) ExecBlock(tr.ElseBody, scope);
            }
            catch (Exception ex) when (ex is PyRuntimeError || ex is PyRaiseSignal)
            {
                string excType = ex is PyRuntimeError pre ? pre.PyType : "Exception";
                object excVal = ex is PyRaiseSignal prs ? prs.Value : null;
                bool handled = false;
                foreach (var h in tr.Handlers)
                {
                    bool match = h.ExcType == null;
                    if (!match)
                    {
                        // Usa el NOMBRE del tipo de excepcion sintacticamente (except ImportError:)
                        // sin requerir que ImportError/NameError/... esten definidos como builtins.
                        string name = h.ExcType is NameRef nrx ? nrx.Name
                                    : (Eval(h.ExcType, scope) is object et
                                        ? (et is PyClass pc ? pc.Name : (et is PyBuiltin pb ? pb.Name : PyOps.Str(et)))
                                        : "");
                        if (name == excType || name == "Exception" || name == "BaseException") match = true;
                        // subclases: ModuleNotFoundError es-un ImportError
                        else if (name == "ImportError" && excType == "ModuleNotFoundError") match = true;
                    }
                    if (match)
                    {
                        if (h.Name != null) scope.Set(h.Name, excVal ?? new PyRuntimeError(excType, ex.Message));
                        ExecBlock(h.Body, scope);
                        handled = true;
                        break;
                    }
                }
                if (!handled) { if (tr.FinallyBody != null) ExecBlock(tr.FinallyBody, scope); throw; }
            }
            finally
            {
                if (tr.FinallyBody != null) ExecBlock(tr.FinallyBody, scope);
            }
        }

        private void ExecWith(WithStmt wi, PyScope scope)
        {
            // MVP: evalúa el contexto y ejecuta el cuerpo (sin __enter__/__exit__ reales).
            foreach (var (ctx, var) in wi.Items)
            {
                var v = Eval(ctx, scope);
                if (var != null) AssignTarget(var, v, scope);
            }
            ExecBlock(wi.Body, scope);
        }

        private void ExecImport(ImportStmt im, PyScope scope)
        {
            foreach (var (mod, alias) in im.Names)
            {
                var parts = mod.Split('.');
                var root = LoadNativeModule(parts[0]);
                var leaf = root;
                for (int i = 1; i < parts.Length; i++)
                {
                    if (leaf.Attrs.TryGetValue(parts[i], out var sub) && sub is PyModule subm) leaf = subm;
                    else throw new PythonNotSupported($"módulo {mod}");
                }
                // 'import a.b.c' liga el nombre RAÍZ 'a'; 'import a.b.c as x' liga x = hoja.
                if (alias != null) scope.Set(alias, leaf);
                else scope.Set(parts[0], root);
            }
        }

        private void ExecImportFrom(ImportFromStmt fr, PyScope scope)
        {
            var parts = fr.Module.TrimStart('.').Split('.');
            var m = LoadNativeModule(parts[0]);
            // navega submódulos: from scipy.sparse.linalg import spsolve → scipy→sparse→linalg
            for (int i = 1; i < parts.Length; i++)
            {
                if (m.Attrs.TryGetValue(parts[i], out var sub) && sub is PyModule subm) m = subm;
                else throw new PythonNotSupported($"módulo {fr.Module}");
            }
            if (fr.ImportStar)
            {
                foreach (var kv in m.Attrs) scope.Set(kv.Key, kv.Value);
                return;
            }
            foreach (var (name, alias) in fr.Names)
            {
                if (m.Attrs.TryGetValue(name, out var v)) scope.Set(alias ?? name, v);
                else throw new PythonNotSupported($"from {fr.Module} import {name}");
            }
        }

        private PyModule LoadNativeModule(string name)
        {
            if (name == "math" || name == "cmath") return PythonMath.Module;
            // openseespy / opensees → módulo OpenSees DINÁMICO (ops.node, ops.element, ...)
            // resuelto nativamente contra OpenSeesRT.dll (MKL Pardiso+JIT) vía TclInterop.
            if (name == "openseespy" || name == "opensees" || name == "openseespywin")
                return _opsModule ??= new PyModule("__opensees__");
            // numpy EMBEBIDO (PyNdArray + BlasInterop/LapackInterop) — sin Python externo.
            if (name == "numpy")
                return _numpyModule ??= PyNumpy.CreateModule();
            // scipy EMBEBIDO (scipy.sparse+spsolve, scipy.linalg, scipy.optimize) — sin Python externo.
            if (name == "scipy")
                return _scipyModule ??= PythonScipy.CreateModule(this);
            // stdlib EMBEBIDA: sys / time / os (os.path) — sin Python externo.
            if (name == "sys") return _sysModule ??= PythonStdlib.Sys(ScriptFilePath());
            if (name == "time") return _timeModule ??= PythonStdlib.Time();
            if (name == "os") return _osModule ??= PythonStdlib.Os(RealPython.ScriptDirectory);
            if (name == "collections") return _collectionsModule ??= BuildCollections();
            // Modulo .py LOCAL (hermano del script): parsear+ejecutar como modulo -> import fem_helper OK nativo.
            var local = LoadLocalModule(name);
            if (local != null) return local;
            // Modulo no nativo -> ImportError atrapable (como python real), para que
            // 'try: import X except ImportError:' funcione (dependencias opcionales).
            throw new PyRuntimeError("ModuleNotFoundError", $"No module named '{name}'");
        }

        private Dictionary<string, PyModule> _localModules;
        // Carga un modulo .py hermano del script (mismo directorio): lo corre en su PROPIO evaluador
        // (sus funciones cargan su propio globals como closure) y expone sus nombres como PyModule.
        private PyModule LoadLocalModule(string name)
        {
            _localModules ??= new Dictionary<string, PyModule>(StringComparer.Ordinal);
            if (_localModules.TryGetValue(name, out var cached)) return cached;
            var dir = RealPython.ScriptDirectory;
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return null;
            var file = System.IO.Path.Combine(dir, name + ".py");
            if (!System.IO.File.Exists(file)) return null;
            _localModules[name] = new PyModule(name);    // placeholder (evita recursion en imports circulares)
            try
            {
                var src = System.IO.File.ReadAllText(file);
                var toks = PythonTokenizer.Tokenize(src);
                var stmts = new PythonParser(toks).ParseModule();
                var sub = new PythonEvaluator();
                sub.ExecBlock(stmts, sub.Globals);
                var mod = new PyModule(name);
                foreach (var kv in sub.Globals.Vars) mod.Attrs[kv.Key] = kv.Value;
                _localModules[name] = mod;
                return mod;
            }
            catch (PyRuntimeError) { throw; }
            catch (Exception ex) { throw new PyRuntimeError("ImportError", $"no se pudo importar '{name}': {ex.Message}"); }
        }
        private PyModule _opsModule;
        private PyModule _numpyModule;
        private PyModule _scipyModule;
        private PyModule _sysModule, _timeModule, _osModule, _collectionsModule;

        // collections nativo: Counter (clave faltante->0, para d[k]+=1), defaultdict, OrderedDict.
        private PyModule BuildCollections()
        {
            var m = new PyModule("collections");
            m.Attrs["Counter"] = new PyBuiltin("Counter", (a, kw) =>
            {
                var d = new PyDict { DefaultZero = true };
                if (a.Length > 0 && a[0] != null)
                    foreach (var x in Iterate(a[0])) { d.TryGet(x, out var c); d.Set(x, (c is long l ? l : 0L) + 1L); }
                return d;
            });
            m.Attrs["defaultdict"] = new PyBuiltin("defaultdict", (a, kw) => new PyDict { DefaultZero = true });
            m.Attrs["OrderedDict"] = new PyBuiltin("OrderedDict", (a, kw) => new PyDict());
            return m;
        }

        // Ruta del script actual (para sys.argv[0] / __file__). Usa ScriptDirectory + un nombre estable.
        private static string ScriptFilePath()
        {
            var d = RealPython.ScriptDirectory;
            if (string.IsNullOrEmpty(d)) return "__main__.py";
            return System.IO.Directory.Exists(d) ? System.IO.Path.Combine(d, "__main__.py") : d;
        }

        private string _opsPatHeader;       // patrón de cargas abierto (replica el buffer de openseespy)
        private StringBuilder _opsPatBody;

        // ops.<comando>(*args) → "comando arg1 arg2 ..." → OpenSeesRT; parsea el resultado.
        internal object OpsCommand(string name, object[] args)
        {
            if (!TclInterop.Available)
                throw new PyRuntimeError("RuntimeError",
                    "OpenSees nativo no disponible (falta OpenSeesRT.dll / Tcl). " + (TclInterop.LastError ?? ""));
            _opensees ??= new TclInterop();
            string a = FormatOpsArgs(args);
            // OpenSeesRT exige el patrón con llaves: pattern Plain t ts { load ...; sp ... }.
            // openseespy lo bufferea en Python; lo replicamos: pattern ABRE, load/sp/eleLoad
            // se ACUMULAN, y cualquier otro comando CIERRA el patrón.
            if (name == "pattern")
            {
                FlushOpsPattern();
                _opsPatHeader = "pattern " + a;
                _opsPatBody = new StringBuilder();
                return null;
            }
            if (_opsPatHeader != null && (name == "load" || name == "sp" || name == "eleLoad"))
            {
                _opsPatBody.Append(name).Append(' ').Append(a).Append("; ");
                return null;
            }
            FlushOpsPattern();
            var res = _opensees.Eval(a.Length > 0 ? name + " " + a : name);
            // Solo las CONSULTAS devuelven valor (para asignar/print). Los comandos de
            // construcción (node/element/fix/...) son void → None → NO se auto-renderizan
            // (evita líneas basura tipo "ops.fix(...) = 0 1 2" en la hoja).
            return _opsQueries.Contains(name) ? ParseOpsResult(res) : null;
        }
        private static readonly System.Collections.Generic.HashSet<string> _opsQueries =
            new(StringComparer.Ordinal)
        {
            "nodeDisp","nodeReaction","nodeCoord","nodeVel","nodeAccel","nodeEigenvector","nodeBounds",
            "nodeMass","nodeDOFs","nodeResponse","nodeRotation","nodeUnbalance","setNodeDisp",
            "eleForce","eleResponse","eleNodes","eleDynamicalForce","eleType",
            "sectionForce","sectionDeformation","sectionStiffness","sectionFlexibility","sectionLocation","sectionWeight",
            "basicForce","basicDeformation","basicStiffness",
            "getNodeTags","getEleTags","getNDM","getNDF","getTime","getLoadFactor","getEleClassTags",
            "getEleLoadTags","getEleLoadClassTags","getFixedNodes","getFixedDOFs","getConstrainedNodes",
            "getConstrainedDOFs","getRetainedNodes","getRetainedDOFs","getParamTags","getParamValue","getNumElements",
            "eigen","modalProperties","responseSpectrum",
            "systemSize","numFact","numIter","testIter","testNorm","testIters","version","getPID","getNP","recorder","analyze",
        };
        private void FlushOpsPattern()
        {
            if (_opsPatHeader == null) return;
            var cmd = _opsPatHeader + " { " + _opsPatBody + " }";
            _opsPatHeader = null; _opsPatBody = null;
            _opensees.Eval(cmd);
        }
        private string FormatOpsArgs(object[] args)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < args.Length; i++) { if (i > 0) sb.Append(' '); AppendOpsArg(sb, args[i]); }
            return sb.ToString();
        }
        private void AppendOpsArg(StringBuilder sb, object a)
        {
            switch (a)
            {
                case PyList l:
                    for (int i = 0; i < l.Items.Count; i++) { if (i > 0) sb.Append(' '); AppendOpsArg(sb, l.Items[i]); }
                    break;
                case double d: sb.Append(d.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); break;
                case long lo: sb.Append(lo); break;
                case int ii: sb.Append(ii); break;
                case bool b: sb.Append(b ? 1 : 0); break;
                default: sb.Append(PyOps.Str(a)); break;
            }
        }
        private static object ParseOpsResult(string r)
        {
            if (string.IsNullOrWhiteSpace(r)) return null;
            var parts = r.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var nums = new System.Collections.Generic.List<object>();
            foreach (var p in parts)
                if (double.TryParse(p, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dv))
                    nums.Add(dv);
                else return r;                      // no todo numérico → string crudo
            if (nums.Count == 0) return r;
            if (nums.Count == 1) return nums[0];    // un número → double
            var pl = new PyList(); pl.Items.AddRange(nums); return pl;   // varios → lista
        }

        private void ExecClassDef(ClassDef cd, PyScope scope)
        {
            var cls = new PyClass { Name = cd.Name };
            foreach (var b in cd.Bases)
            {
                var bv = Eval(b, scope);
                if (bv is PyClass pc) cls.Bases.Add(pc);
            }
            var classScope = new PyScope(scope, scope.Globals);
            foreach (var s in cd.Body)
            {
                if (s is FuncDef fd)
                    cls.Attrs[fd.Name] = MakeFunction(fd, scope, cls);
                else if (s is AssignStmt a && a.Targets.Count == 1 && a.Targets[0] is NameRef nr)
                    cls.Attrs[nr.Name] = Eval(a.Value, classScope);
                else
                    ExecStmt(s, classScope);
            }
            scope.Set(cd.Name, cls);
        }

        private PyFunction MakeFunction(FuncDef fd, PyScope closure, PyClass owner = null)
            => new PyFunction { Name = fd.Name, Params = fd.Params, Body = fd.Body, Closure = closure, OwnerClass = owner };

        // ===================================================================
        //  ASIGNACIÓN A TARGETS
        // ===================================================================
        private void AssignTarget(PyNode target, object value, PyScope scope)
        {
            switch (target)
            {
                case NameRef nr: scope.Set(nr.Name, value); break;
                case TupleLit tup: UnpackInto(tup.Elements, value, scope); break;
                case ListLit lst: UnpackInto(lst.Elements, value, scope); break;
                case IndexExpr ix:
                {
                    var obj = Eval(ix.Target, scope);
                    if (obj is PyNdArray nd)
                    {
                        // indexado avanzado en asignacion: A[np.ix_(r,c)]=block , A[intarray]=vec
                        if (!(ix.Index is SliceExpr) && !(ix.Index is TupleLit))
                        {
                            var iv2 = Eval(ix.Index, scope);
                            if (iv2 is PyIxGrid gg) { nd.SetSubmatrix(gg.Rows, gg.Cols, value); break; }
                            if (iv2 is PyNdArray ia2 && ia2.Ndim == 1 && ia2.IsInt) { nd.Scatter(ToIntArr(ia2), value); break; }
                            nd.Set(new List<NdSpec> { new NdSpec { Slice = false, Idx = PyOps.ToLong(iv2) } }, value); break;
                        }
                        nd.Set(NdSpecs(ix.Index, scope), value); break;   // A[i,j]=, A[i,:]=
                    }
                    if (obj is PySparseMatrix sm)   // K[np.ix_(r,c)]=block , K[i,j]=v (ensamblaje FEM)
                    {
                        if (ix.Index is TupleLit stl && stl.Elements.Count == 2)
                        { PythonScipy.SpSetBlock(sm, SpAxis(stl.Elements[0], scope, sm.Rows), SpAxis(stl.Elements[1], scope, sm.Cols), value); break; }
                        var iv3 = Eval(ix.Index, scope);
                        if (iv3 is PyIxGrid gg3) { PythonScipy.SpSetBlock(sm, gg3.Rows, gg3.Cols, value); break; }
                        PythonScipy.SpSetBlock(sm, new[] { (int)PyOps.ToLong(iv3) }, AllIdx(sm.Cols), value); break;
                    }
                    var idx = Eval(ix.Index, scope);
                    SetItem(obj, idx, value);
                    break;
                }
                case AttributeExpr at:
                {
                    var obj = Eval(at.Target, scope);
                    SetAttr(obj, at.Name, value);
                    break;
                }
                case StarExpr se: AssignTarget(se.Value, value, scope); break;
                default:
                    throw new PythonNotSupported($"target de asignación {target.GetType().Name}");
            }
        }

        private void UnpackInto(List<PyNode> targets, object value, PyScope scope)
        {
            var items = new List<object>(Iterate(value));
            int starIdx = targets.FindIndex(t => t is StarExpr);
            if (starIdx < 0)
            {
                if (items.Count != targets.Count)
                    throw new PyRuntimeError("ValueError", $"not enough values to unpack (expected {targets.Count}, got {items.Count})");
                for (int i = 0; i < targets.Count; i++) AssignTarget(targets[i], items[i], scope);
            }
            else
            {
                int after = targets.Count - starIdx - 1;
                for (int i = 0; i < starIdx; i++) AssignTarget(targets[i], items[i], scope);
                int starCount = items.Count - starIdx - after;
                var mid = new PyList();
                for (int i = 0; i < starCount; i++) mid.Items.Add(items[starIdx + i]);
                AssignTarget(((StarExpr)targets[starIdx]).Value, mid, scope);
                for (int i = 0; i < after; i++) AssignTarget(targets[starIdx + 1 + i], items[starIdx + starCount + i], scope);
            }
        }

        private void DelTarget(PyNode target, PyScope scope)
        {
            switch (target)
            {
                case NameRef nr: scope.Vars.Remove(nr.Name); break;
                case IndexExpr ix:
                {
                    var obj = Eval(ix.Target, scope);
                    var idx = Eval(ix.Index, scope);
                    if (obj is PyList l) l.Items.RemoveAt(NormIndex(PyOps.ToLong(idx), l.Count));
                    else if (obj is PyDict d) d.Remove(idx);
                    else throw new PythonNotSupported("del de este tipo");
                    break;
                }
                default: throw new PythonNotSupported("del target");
            }
        }

        // ===================================================================
        //  EVALUACIÓN DE EXPRESIONES
        // ===================================================================
        public object Eval(PyNode node, PyScope scope)
        {
            switch (node)
            {
                case NumberLit n: return n.IsInt ? (object)(long)n.Value : n.Value;
                case StringLit s: return s.Value;
                case BoolLit b: return b.Value;
                case NoneLit: return null;
                case FStringLit f: return EvalFString(f, scope);
                case NameRef nr: return EvalName(nr, scope);
                case UnaryOp u: return EvalUnary(u, scope);
                case BinaryOp bin: return EvalBinary(bin, scope);
                case CompareOp c: return EvalCompare(c, scope);
                case CallExpr call: return EvalCall(call, scope);
                case IndexExpr ix: return EvalIndex(ix, scope);
                case AttributeExpr at: return GetAttr(Eval(at.Target, scope), at.Name);
                case ListLit l: return EvalList(l, scope);
                case TupleLit t: return EvalTuple(t, scope);
                case SetLit st: return EvalSet(st, scope);
                case DictLit d: return EvalDict(d, scope);
                case Comprehension comp: return EvalComprehension(comp, scope);
                case LambdaExpr lam: return new PyFunction { Name = "<lambda>", Params = lam.Params, Expr = lam.Body, Closure = scope };
                case IfExp ie: return PyOps.Truthy(Eval(ie.Cond, scope)) ? Eval(ie.TrueExpr, scope) : Eval(ie.FalseExpr, scope);
                case StarExpr se: return Eval(se.Value, scope);
                default:
                    throw new PythonNotSupported($"expresión {node.GetType().Name}");
            }
        }

        private object EvalName(NameRef nr, PyScope scope)
        {
            if (scope.TryGet(nr.Name, out var v)) return v;
            if (Globals.Vars.TryGetValue(nr.Name, out v)) return v;
            throw new PyRuntimeError("NameError", $"name '{nr.Name}' is not defined");
        }

        private object EvalUnary(UnaryOp u, PyScope scope)
        {
            var v = Eval(u.Operand, scope);
            return u.Op switch
            {
                "-" => PyOps.Negate(v),
                "+" => v,
                "not" => !PyOps.Truthy(v),
                "~" => ~PyOps.ToLong(v),
                _ => throw new PythonNotSupported($"unario {u.Op}")
            };
        }

        private object EvalBinary(BinaryOp bin, PyScope scope)
        {
            if (bin.Op == "and")
            {
                var l = Eval(bin.Left, scope);
                return PyOps.Truthy(l) ? Eval(bin.Right, scope) : l;
            }
            if (bin.Op == "or")
            {
                var l = Eval(bin.Left, scope);
                return PyOps.Truthy(l) ? l : Eval(bin.Right, scope);
            }
            if (bin.Op == ":=")
            {
                var v = Eval(bin.Right, scope);
                AssignTarget(bin.Left, v, scope);
                return v;
            }
            var a = Eval(bin.Left, scope);
            var b = Eval(bin.Right, scope);
            return PyOps.Binary(bin.Op, a, b);
        }

        private object EvalCompare(CompareOp c, PyScope scope)
        {
            var left = Eval(c.Left, scope);
            for (int i = 0; i < c.Ops.Count; i++)
            {
                var right = Eval(c.Comparators[i], scope);
                if (!CompareOnce(c.Ops[i], left, right)) return false;
                left = right;
            }
            return true;
        }

        private bool CompareOnce(string op, object a, object b)
        {
            switch (op)
            {
                case "==": return PyOps.Equal(a, b);
                case "!=": return !PyOps.Equal(a, b);
                case "<": return PyOps.Compare(a, b) < 0;
                case ">": return PyOps.Compare(a, b) > 0;
                case "<=": return PyOps.Compare(a, b) <= 0;
                case ">=": return PyOps.Compare(a, b) >= 0;
                case "is": return ReferenceEquals(a, b) || (a == null && b == null) || (PyOps.IsNumber(a) && PyOps.IsNumber(b) && PyOps.Equal(a, b) && a.GetType() == b.GetType());
                case "is not": return !CompareOnce("is", a, b);
                case "in": return Contains(b, a);
                case "not in": return !Contains(b, a);
                default: throw new PythonNotSupported($"comparador {op}");
            }
        }

        private bool Contains(object container, object item)
        {
            switch (container)
            {
                case string s when item is string sub: return s.Contains(sub);
                case PyDict d: return d.ContainsKey(item);
                case PySet st: return st.Contains(item);
                default:
                    foreach (var x in Iterate(container)) if (PyOps.Equal(x, item)) return true;
                    return false;
            }
        }

        private object EvalIndex(IndexExpr ix, PyScope scope)
        {
            var obj = Eval(ix.Target, scope);
            if (obj is PyNdArray nd)
            {
                // indexado avanzado: A[np.ix_(r,c)] (submatriz) y A[intarray] (gather)
                if (!(ix.Index is SliceExpr) && !(ix.Index is TupleLit))
                {
                    var iv = Eval(ix.Index, scope);
                    if (iv is PyIxGrid g) return nd.Submatrix(g.Rows, g.Cols);
                    if (iv is PyNdArray ia && ia.Ndim == 1 && ia.IsInt)
                    { var idxi = new int[ia.Data.Length]; for (int k = 0; k < idxi.Length; k++) idxi[k] = (int)Math.Round(ia.Data[k]); return nd.Gather(idxi); }
                    return nd.Get(new List<NdSpec> { new NdSpec { Slice = false, Idx = PyOps.ToLong(iv) } });
                }
                return nd.Get(NdSpecs(ix.Index, scope));   // A[i], A[i,j], A[i,:]
            }
            if (obj is PySparseMatrix sm)   // K[np.ix_(r,c)] (bloque) , K[rows] , K[:,cols] (submatriz)
            {
                if (ix.Index is TupleLit tl2 && tl2.Elements.Count == 2)
                    return PythonScipy.SpSubmat(sm, SpAxis(tl2.Elements[0], scope, sm.Rows), SpAxis(tl2.Elements[1], scope, sm.Cols));
                var iv = Eval(ix.Index, scope);
                if (iv is PyIxGrid g) return PythonScipy.SpBlock(sm, g.Rows, g.Cols);
                if (iv is PyNdArray ra) return PythonScipy.SpSubmat(sm, ToIntArr(ra), AllIdx(sm.Cols));
                return PythonScipy.SpSubmat(sm, new[] { (int)PyOps.ToLong(iv) }, AllIdx(sm.Cols));
            }
            if (ix.Index is SliceExpr sl) return GetSlice(obj, sl, scope);
            var idx = Eval(ix.Index, scope);
            return GetItem(obj, idx);
        }

        // Resuelve un eje de indice sparse: ':' -> todos; array int -> esos indices; escalar -> [i].
        private int[] SpAxis(PyNode node, PyScope scope, int dim)
        {
            if (node is SliceExpr) return AllIdx(dim);
            var v = Eval(node, scope);
            if (v is PyIxGrid) throw new PyRuntimeError("IndexError", "np.ix_ dentro de tupla no soportado");
            if (v is PyNdArray a) return ToIntArr(a);
            return new[] { (int)PyOps.ToLong(v) };
        }
        private static int[] AllIdx(int n) { var r = new int[n]; for (int i = 0; i < n; i++) r[i] = i; return r; }
        private static int[] ToIntArr(PyNdArray a) { var r = new int[a.Data.Length]; for (int k = 0; k < r.Length; k++) r[k] = (int)Math.Round(a.Data[k]); return r; }

        // Convierte el nodo índice (entero, tupla, slice) en specs para PyNdArray.
        private List<NdSpec> NdSpecs(PyNode node, PyScope scope)
        {
            var list = new List<NdSpec>();
            if (node is TupleLit t) foreach (var e in t.Elements) list.Add(NdOne(e, scope));
            else list.Add(NdOne(node, scope));
            return list;
        }
        private NdSpec NdOne(PyNode e, PyScope scope)
        {
            if (e is SliceExpr sl)
                return new NdSpec
                {
                    Slice = true,
                    Lo = sl.Lower == null ? null : PyOps.ToLong(Eval(sl.Lower, scope)),
                    Hi = sl.Upper == null ? null : PyOps.ToLong(Eval(sl.Upper, scope)),
                    Step = sl.Step == null ? 1 : PyOps.ToLong(Eval(sl.Step, scope))
                };
            return new NdSpec { Slice = false, Idx = PyOps.ToLong(Eval(e, scope)) };
        }

        private object EvalList(ListLit l, PyScope scope)
        {
            var r = new PyList();
            foreach (var e in l.Elements)
            {
                if (e is StarExpr se) foreach (var x in Iterate(Eval(se.Value, scope))) r.Items.Add(x);
                else r.Items.Add(Eval(e, scope));
            }
            return r;
        }
        private object EvalTuple(TupleLit t, PyScope scope)
        {
            var r = new List<object>();
            foreach (var e in t.Elements)
            {
                if (e is StarExpr se) foreach (var x in Iterate(Eval(se.Value, scope))) r.Add(x);
                else r.Add(Eval(e, scope));
            }
            return new PyTuple(r);
        }
        private object EvalSet(SetLit st, PyScope scope)
        {
            var r = new PySet();
            foreach (var e in st.Elements) r.Add(Eval(e, scope));
            return r;
        }
        private object EvalDict(DictLit d, PyScope scope)
        {
            var r = new PyDict();
            foreach (var (k, v) in d.Pairs)
            {
                if (k == null) // **unpack
                {
                    if (Eval(v, scope) is PyDict src)
                        for (int i = 0; i < src.Keys.Count; i++) r.Set(src.Keys[i], src.Values[i]);
                    continue;
                }
                r.Set(Eval(k, scope), Eval(v, scope));
            }
            return r;
        }

        private object EvalComprehension(Comprehension comp, PyScope scope)
        {
            var result = comp.Kind switch
            {
                "list" or "gen" => (object)new PyList(),
                "set" => new PySet(),
                "dict" => new PyDict(),
                _ => new PyList()
            };
            var inner = new PyScope(scope, scope.Globals);
            RunCompClauses(comp, 0, inner, result);
            return result;
        }

        private void RunCompClauses(Comprehension comp, int ci, PyScope scope, object result)
        {
            if (ci >= comp.Clauses.Count)
            {
                switch (result)
                {
                    case PyList l: l.Items.Add(Eval(comp.Element, scope)); break;
                    case PySet s: s.Add(Eval(comp.Element, scope)); break;
                    case PyDict d: d.Set(Eval(comp.Element, scope), Eval(comp.ValueElement, scope)); break;
                }
                return;
            }
            var clause = comp.Clauses[ci];
            foreach (var item in Iterate(Eval(clause.Iter, scope)))
            {
                AssignTarget(clause.Target, item, scope);
                bool ok = true;
                foreach (var cond in clause.Conditions) if (!PyOps.Truthy(Eval(cond, scope))) { ok = false; break; }
                if (ok) RunCompClauses(comp, ci + 1, scope, result);
            }
        }

        private string EvalFString(FStringLit f, PyScope scope)
        {
            var sb = new StringBuilder();
            foreach (var part in f.Parts)
            {
                if (part.Expr != null)
                {
                    var v = Eval(part.Expr, scope);
                    string s = part.Repr ? PyOps.Repr(v)
                             : !string.IsNullOrEmpty(part.FormatSpec) ? PyStringFormat.FormatSpec(v, ResolveSpec(part.FormatSpec, scope))
                             : PyOps.Str(v);
                    sb.Append(s);
                }
                else sb.Append(part.Text);
            }
            return sb.ToString();
        }

        // format-spec puede contener {expr} anidados (ej. {x:.{n}f})
        private string ResolveSpec(string spec, PyScope scope)
        {
            if (spec.IndexOf('{') < 0) return spec;
            var sb = new StringBuilder();
            for (int i = 0; i < spec.Length; i++)
            {
                if (spec[i] == '{')
                {
                    int j = spec.IndexOf('}', i);
                    if (j < 0) { sb.Append(spec.Substring(i)); break; }
                    var inner = spec.Substring(i + 1, j - i - 1);
                    var toks = PythonTokenizer.Tokenize(inner);
                    var v = new PythonParser(toks).ParseExpr();
                    sb.Append(PyOps.Str(Eval(v, scope)));
                    i = j;
                }
                else sb.Append(spec[i]);
            }
            return sb.ToString();
        }

        // ===================================================================
        //  LLAMADAS A FUNCIÓN
        // ===================================================================
        private object EvalCall(CallExpr call, PyScope scope)
        {
            var func = Eval(call.Func, scope);
            var args = new List<object>();
            foreach (var a in call.Args)
            {
                if (a is StarExpr se) foreach (var x in Iterate(Eval(se.Value, scope))) args.Add(x);
                else args.Add(Eval(a, scope));
            }
            PyDict kwargs = null;
            if (call.Kwargs.Count > 0 || call.DoubleStar != null)
            {
                kwargs = new PyDict();
                foreach (var (name, val) in call.Kwargs) kwargs.Set(name, Eval(val, scope));
                if (call.DoubleStar != null && Eval(call.DoubleStar, scope) is PyDict dd)
                    for (int i = 0; i < dd.Keys.Count; i++) kwargs.Set(dd.Keys[i], dd.Values[i]);
            }
            if (call.StarArgs != null)
                foreach (var x in Iterate(Eval(call.StarArgs, scope))) args.Add(x);
            return CallCallable(func, args.ToArray(), kwargs);
        }

        public object CallCallable(object func, object[] args, PyDict kwargs)
        {
            switch (func)
            {
                case PyBuiltin b:
                {
                    if (b.Self != null)
                    {
                        var arr = new object[args.Length + 1];
                        arr[0] = b.Self;
                        Array.Copy(args, 0, arr, 1, args.Length);
                        return b.Invoke(arr, kwargs);
                    }
                    return b.Invoke(args, kwargs);
                }
                case PyFunction f: return CallUserFunc(f, null, args, kwargs);
                case PyBoundMethod bm:
                {
                    var arr = new object[args.Length + 1];
                    arr[0] = bm.Self;
                    Array.Copy(args, 0, arr, 1, args.Length);
                    return CallUserFunc(bm.Func, null, arr, kwargs);
                }
                case PyClass cls: return Instantiate(cls, args, kwargs);
                default:
                    throw new PyRuntimeError("TypeError", $"'{PyOps.TypeName(func)}' object is not callable");
            }
        }

        private object CallUserFunc(PyFunction f, object self, object[] args, PyDict kwargs)
        {
            var local = new PyScope(f.Closure, f.Closure?.Globals ?? Globals);
            // Bind parámetros
            int ai = 0;
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            for (int pi = 0; pi < f.Params.Count; pi++)
            {
                var p = f.Params[pi];
                if (p.IsStar)
                {
                    var rest = new List<object>();
                    while (ai < args.Length) rest.Add(args[ai++]);
                    local.Vars[p.Name] = new PyTuple(rest);
                    assigned.Add(p.Name);
                }
                else if (p.IsDoubleStar)
                {
                    var dd = new PyDict();
                    if (kwargs != null) { for (int i = 0; i < kwargs.Keys.Count; i++) dd.Set(kwargs.Keys[i], kwargs.Values[i]); kwargs = null; }
                    local.Vars[p.Name] = dd;
                    assigned.Add(p.Name);
                }
                else if (ai < args.Length)
                {
                    local.Vars[p.Name] = args[ai++];
                    assigned.Add(p.Name);
                }
                else if (kwargs != null && kwargs.TryGet(p.Name, out var kv))
                {
                    local.Vars[p.Name] = kv;
                    kwargs.Remove(p.Name);
                    assigned.Add(p.Name);
                }
                else if (p.Default != null)
                {
                    local.Vars[p.Name] = Eval(p.Default, f.Closure);
                    assigned.Add(p.Name);
                }
                else
                    throw new PyRuntimeError("TypeError", $"{f.Name}() missing required argument: '{p.Name}'");
            }
            // kwargs sobrantes que matcheen params por nombre ya consumidos arriba
            if (kwargs != null)
            {
                for (int i = 0; i < kwargs.Keys.Count; i++)
                {
                    var kn = PyOps.Str(kwargs.Keys[i]);
                    if (!assigned.Contains(kn)) local.Vars[kn] = kwargs.Values[i];
                }
            }

            if (f.Expr != null) return Eval(f.Expr, local);
            try { ExecBlock(f.Body, local); }
            catch (ReturnSignal rs) { return rs.Value; }
            return null;
        }

        private object Instantiate(PyClass cls, object[] args, PyDict kwargs)
        {
            var inst = new PyInstance { Class = cls };
            if (cls.TryLookup("__init__", out var initObj) && initObj is PyFunction init)
            {
                var arr = new object[args.Length + 1];
                arr[0] = inst;
                Array.Copy(args, 0, arr, 1, args.Length);
                CallUserFunc(init, null, arr, kwargs);
            }
            return inst;
        }

        // ===================================================================
        //  ITERACIÓN / INDEXING / SLICING
        // ===================================================================
        public IEnumerable<object> Iterate(object o)
        {
            switch (o)
            {
                case PyList l: foreach (var x in l.Items) yield return x; break;
                case PyTuple t: foreach (var x in t.Items) yield return x; break;
                case PySet s: foreach (var x in s.Items) yield return x; break;
                case PyRange r: foreach (var x in r) yield return x; break;
                case string str: foreach (var ch in str) yield return ch.ToString(); break;
                case PyDict d: foreach (var k in d.Keys) yield return k; break;
                case PyNdArray nd:
                    // numpy: iterar recorre el PRIMER eje -> 1D da escalares, 2D da filas (sub-arrays 1D)
                    if (nd.Ndim == 1)
                        for (int i = 0; i < nd.Shape[0]; i++)
                            yield return nd.IsInt ? (object)(long)System.Math.Round(nd.Data[i]) : nd.Data[i];
                    else
                    {
                        int c = nd.Cols;
                        for (int i = 0; i < nd.Rows; i++)
                        {
                            var row = new double[c];
                            System.Array.Copy(nd.Data, i * c, row, 0, c);
                            yield return new PyNdArray(row, new[] { c }, nd.IsInt);
                        }
                    }
                    break;
                case IEnumerable<object> en: foreach (var x in en) yield return x; break;
                case null: throw new PyRuntimeError("TypeError", "'NoneType' object is not iterable");
                default: throw new PyRuntimeError("TypeError", $"'{PyOps.TypeName(o)}' object is not iterable");
            }
        }

        public static int NormIndex(long idx, int len)
        {
            long i = idx < 0 ? len + idx : idx;
            if (i < 0 || i >= len) throw new PyRuntimeError("IndexError", "index out of range");
            return (int)i;
        }

        private object GetItem(object obj, object idx)
        {
            switch (obj)
            {
                case PyList l: return l.Items[NormIndex(PyOps.ToLong(idx), l.Count)];
                case PyTuple t: return t.Items[NormIndex(PyOps.ToLong(idx), t.Count)];
                case string s: { int i = NormIndex(PyOps.ToLong(idx), s.Length); return s[i].ToString(); }
                case PyDict d:
                    if (d.TryGet(idx, out var v)) return v;
                    if (d.DefaultZero) return 0L;   // Counter: clave faltante -> 0
                    throw new PyRuntimeError("KeyError", PyOps.Repr(idx));
                case PyRange r:
                {
                    var items = new List<object>(r);
                    return items[NormIndex(PyOps.ToLong(idx), items.Count)];
                }
                default: throw new PyRuntimeError("TypeError", $"'{PyOps.TypeName(obj)}' object is not subscriptable");
            }
        }

        private void SetItem(object obj, object idx, object value)
        {
            switch (obj)
            {
                case PyList l: l.Items[NormIndex(PyOps.ToLong(idx), l.Count)] = value; break;
                case PyDict d: d.Set(idx, value); break;
                default: throw new PyRuntimeError("TypeError", $"'{PyOps.TypeName(obj)}' object does not support item assignment");
            }
        }

        private object GetSlice(object obj, SliceExpr sl, PyScope scope)
        {
            long? lo = sl.Lower == null ? null : PyOps.ToLong(Eval(sl.Lower, scope));
            long? hi = sl.Upper == null ? null : PyOps.ToLong(Eval(sl.Upper, scope));
            long step = sl.Step == null ? 1 : PyOps.ToLong(Eval(sl.Step, scope));
            if (step == 0) throw new PyRuntimeError("ValueError", "slice step cannot be zero");

            if (obj is string str)
            {
                var chars = SliceList(new List<object>(StrChars(str)), lo, hi, step);
                var sb = new StringBuilder();
                foreach (var c in chars) sb.Append((string)c);
                return sb.ToString();
            }
            if (obj is PyList l) return new PyList(SliceList(l.Items, lo, hi, step));
            if (obj is PyTuple t) return new PyTuple(SliceList(t.Items, lo, hi, step));
            if (obj is PyRange r) return new PyList(SliceList(new List<object>(r), lo, hi, step));
            throw new PyRuntimeError("TypeError", $"'{PyOps.TypeName(obj)}' no soporta slicing");
        }

        private static IEnumerable<object> StrChars(string s) { foreach (var c in s) yield return c.ToString(); }

        private static List<object> SliceList(List<object> items, long? lo, long? hi, long step)
        {
            int n = items.Count;
            int start, stop;
            if (step > 0)
            {
                start = (int)Clamp(lo ?? 0, n, false);
                stop = (int)Clamp(hi ?? n, n, false);
            }
            else
            {
                start = (int)Clamp(lo ?? (n - 1), n, true);
                stop = (int)Clamp(hi ?? (-n - 1), n, true);
            }
            var res = new List<object>();
            if (step > 0) for (int i = start; i < stop; i += (int)step) { if (i >= 0 && i < n) res.Add(items[i]); }
            else for (int i = start; i > stop; i += (int)step) { if (i >= 0 && i < n) res.Add(items[i]); }
            return res;
        }

        private static long Clamp(long idx, int n, bool neg)
        {
            long i = idx < 0 ? n + idx : idx;
            if (neg) { if (i < -1) i = -1; if (i > n - 1) i = n - 1; }
            else { if (i < 0) i = 0; if (i > n) i = n; }
            return i;
        }

        // ===================================================================
        //  ATRIBUTOS Y MÉTODOS
        // ===================================================================
        public object GetAttr(object obj, string name)
        {
            switch (obj)
            {
                case PyModule m:
                    if (m.Attrs.TryGetValue(name, out var mv)) return mv;
                    if (m.Name == "__opensees__")   // módulo OpenSees DINÁMICO: ops.<comando>(*args)
                        return new PyBuiltin(name, (a, kw) => OpsCommand(name, a));
                    throw new PyRuntimeError("AttributeError", $"module '{m.Name}' has no attribute '{name}'");
                case PyInstance inst:
                {
                    if (inst.Attrs.TryGetValue(name, out var iv)) return iv;
                    if (inst.Class != null && inst.Class.TryLookup(name, out var cv))
                    {
                        if (cv is PyFunction pf) return new PyBoundMethod { Func = pf, Self = inst };
                        return cv;
                    }
                    throw new PyRuntimeError("AttributeError", $"'{inst.Class?.Name}' object has no attribute '{name}'");
                }
                case PyClass cls:
                    if (cls.TryLookup(name, out var clv)) return clv;
                    throw new PyRuntimeError("AttributeError", $"type object '{cls.Name}' has no attribute '{name}'");
                case PyRuntimeError err:
                    if (name == "args") return new PyTuple(new List<object> { err.Message });
                    return err.Message;
                case PyNdArray arr:
                    return PyNumpy.GetAttr(arr, name);   // .T, .shape, .copy(), .sum(), ...
                case PySparseMatrix spm:
                    return PythonScipy.GetAttr(spm, name);   // .shape, .nnz, .toarray(), .dot(x), .T
                case PySciResult sr:                          // res.x/.fun/.t/.y, norm.cdf/.pdf/.ppf
                    if (sr.Attrs.TryGetValue(name, out var srv)) return srv;
                    throw new PyRuntimeError("AttributeError", $"result object has no attribute '{name}'");
            }
            // Métodos builtin de tipos nativos
            var method = PythonBuiltinMethods.GetMethod(obj, name, this);
            if (method != null) return method;
            throw new PyRuntimeError("AttributeError", $"'{PyOps.TypeName(obj)}' object has no attribute '{name}'");
        }

        private void SetAttr(object obj, string name, object value)
        {
            switch (obj)
            {
                case PyInstance inst: inst.Attrs[name] = value; break;
                case PyClass cls: cls.Attrs[name] = value; break;
                case PyModule m: m.Attrs[name] = value; break;
                default: throw new PyRuntimeError("AttributeError", $"no se puede asignar atributo a {PyOps.TypeName(obj)}");
            }
        }

        // ===================================================================
        //  BUILTINS
        // ===================================================================
        // Eliminacion gaussiana con pivoteo parcial (fallback si LAPACK no esta).
        private static double[] GaussSolve(int n, double[] A, double[] b)
        {
            var M = (double[])A.Clone();
            var x = (double[])b.Clone();
            for (int k = 0; k < n; k++)
            {
                int piv = k; double best = Math.Abs(M[k * n + k]);
                for (int i = k + 1; i < n; i++)
                {
                    double v = Math.Abs(M[i * n + k]);
                    if (v > best) { best = v; piv = i; }
                }
                if (piv != k)
                {
                    for (int j = 0; j < n; j++) { var t = M[k * n + j]; M[k * n + j] = M[piv * n + j]; M[piv * n + j] = t; }
                    var tb = x[k]; x[k] = x[piv]; x[piv] = tb;
                }
                double d = M[k * n + k];
                for (int i = k + 1; i < n; i++)
                {
                    double f = M[i * n + k] / d;
                    if (f == 0.0) continue;
                    for (int j = k; j < n; j++) M[i * n + j] -= f * M[k * n + j];
                    x[i] -= f * x[k];
                }
            }
            for (int i = n - 1; i >= 0; i--)
            {
                double s = x[i];
                for (int j = i + 1; j < n; j++) s -= M[i * n + j] * x[j];
                x[i] = s / M[i * n + i];
            }
            return x;
        }

        // Jacobi ciclico (Numerical Recipes) para matriz SIMETRICA n x n.
        // d = autovalores ; V row-major con V[i*n+k] = i-esima componente del
        // autovector k. (Eigen estandar A·v = lambda·v.)
        private static void JacobiEig(int n, double[] Ain, out double[] d, out double[] V)
        {
            var a = (double[])Ain.Clone();
            V = new double[n * n];
            for (int i = 0; i < n; i++) V[i * n + i] = 1.0;
            d = new double[n];
            var b = new double[n]; var z = new double[n];
            for (int i = 0; i < n; i++) { d[i] = a[i * n + i]; b[i] = d[i]; }
            void Rot(double[] m, int i, int j, int k, int l, double s, double tau)
            { double g = m[i * n + j]; double h = m[k * n + l]; m[i * n + j] = g - s * (h + g * tau); m[k * n + l] = h + s * (g - h * tau); }
            for (int iter = 0; iter < 100; iter++)
            {
                double sm = 0;
                for (int p = 0; p < n - 1; p++) for (int q = p + 1; q < n; q++) sm += Math.Abs(a[p * n + q]);
                if (sm == 0.0) break;
                double thresh = iter < 3 ? 0.2 * sm / (n * n) : 0.0;
                for (int p = 0; p < n - 1; p++)
                {
                    for (int q = p + 1; q < n; q++)
                    {
                        double g = 100.0 * Math.Abs(a[p * n + q]);
                        if (iter > 3 && Math.Abs(d[p]) + g == Math.Abs(d[p]) && Math.Abs(d[q]) + g == Math.Abs(d[q]))
                            a[p * n + q] = 0.0;
                        else if (Math.Abs(a[p * n + q]) > thresh)
                        {
                            double h = d[q] - d[p]; double t;
                            if (Math.Abs(h) + g == Math.Abs(h)) t = a[p * n + q] / h;
                            else { double theta = 0.5 * h / a[p * n + q]; t = 1.0 / (Math.Abs(theta) + Math.Sqrt(1.0 + theta * theta)); if (theta < 0.0) t = -t; }
                            double c = 1.0 / Math.Sqrt(1.0 + t * t); double s = t * c; double tau = s / (1.0 + c);
                            h = t * a[p * n + q];
                            z[p] -= h; z[q] += h; d[p] -= h; d[q] += h; a[p * n + q] = 0.0;
                            for (int j = 0; j < p; j++) Rot(a, j, p, j, q, s, tau);
                            for (int j = p + 1; j < q; j++) Rot(a, p, j, j, q, s, tau);
                            for (int j = q + 1; j < n; j++) Rot(a, p, j, q, j, s, tau);
                            for (int j = 0; j < n; j++) Rot(V, j, p, j, q, s, tau);
                        }
                    }
                }
                for (int p = 0; p < n; p++) { b[p] += z[p]; d[p] = b[p]; z[p] = 0.0; }
            }
        }

        private void RegisterBuiltins()
        {
            void Reg(string name, Func<object[], PyDict, object> fn) => Globals.Vars[name] = new PyBuiltin(name, fn);

            // eig_sym(A): autovalores/autovectores de A simetrica n×n (lista de listas).
            // Devuelve [vals, vecs] con vals ASCENDENTE y vecs[k] = autovector de vals[k]
            // (lista de n). Motor NATIVO (Jacobi), sin numpy. Para modal: A = D^-1/2 K D^-1/2.
            Reg("eig_sym", (a, kw) =>
            {
                if (a.Length < 1 || a[0] is not PyList A || A.Count == 0 || A.Items[0] is not PyList)
                    throw new PyRuntimeError("TypeError", "eig_sym(A): A debe ser matriz simetrica (lista de listas)");
                int n = A.Count;
                var Ar = new double[n * n];
                for (int i = 0; i < n; i++)
                {
                    if (A.Items[i] is not PyList rowL || rowL.Count != n)
                        throw new PyRuntimeError("ValueError", "eig_sym: A no es cuadrada");
                    for (int j = 0; j < n; j++) Ar[i * n + j] = PyOps.ToDouble(rowL.Items[j]);
                }
                JacobiEig(n, Ar, out var d, out var V);
                var order = new int[n];
                for (int i = 0; i < n; i++) order[i] = i;
                Array.Sort(order, (x, y) => d[x].CompareTo(d[y]));
                var vals = new PyList();
                for (int k = 0; k < n; k++) vals.Items.Add(d[order[k]]);
                var vecs = new PyList();
                for (int k = 0; k < n; k++)
                {
                    var vk = new PyList();
                    int col = order[k];
                    for (int i = 0; i < n; i++) vk.Items.Add(V[i * n + col]);
                    vecs.Items.Add(vk);
                }
                var res = new PyList(); res.Items.Add(vals); res.Items.Add(vecs);
                return res;
            });

            // solve(A, b): resuelve el sistema lineal A·x = b en el motor NATIVO.
            // A = matriz n×n (lista de listas), b = vector n. Usa el solver LAPACK
            // nativo (libopenblas / DGESV); si no esta disponible, eliminacion
            // gaussiana managed. Permite FEM denso sin numpy ni python real.
            Reg("solve", (a, kw) =>
            {
                if (a.Length < 2)
                    throw new PyRuntimeError("TypeError", "solve(A, b)");
                if (a[0] is not PyList A || A.Count == 0 || A.Items[0] is not PyList)
                    throw new PyRuntimeError("TypeError", "solve: A debe ser una matriz (lista de listas)");
                int n = A.Count;
                var Arow = new double[n * n];
                for (int i = 0; i < n; i++)
                {
                    if (A.Items[i] is not PyList rowL || rowL.Count != n)
                        throw new PyRuntimeError("ValueError", "solve: A no es cuadrada");
                    var row = rowL.Items;
                    for (int j = 0; j < n; j++) Arow[i * n + j] = PyOps.ToDouble(row[j]);
                }
                if (a[1] is not PyList bl || bl.Count != n)
                    throw new PyRuntimeError("ValueError", "solve: dim(b) != n");
                var bv = new double[n];
                for (int i = 0; i < n; i++) bv[i] = PyOps.ToDouble(bl.Items[i]);

                double[] x = Calcpad.Core.LapackInterop.Available
                    ? Calcpad.Core.LapackInterop.Solve(n, Arow, bv)
                    : GaussSolve(n, Arow, bv);

                var res = new PyList();
                for (int i = 0; i < n; i++) res.Items.Add(x[i]);
                return res;
            });

            // OpenSees NATIVO (OpenSeesRT.dll vía Tcl, MKL Pardiso + JIT). Sin openseespy/CPython.
            //   opensees("node 1 0 0")            → ejecuta un comando OpenSees
            //   opensees("element ...", "...")    → batch (varios comandos, ensamble en C++)
            //   uy = float(opensees("nodeDisp 2 2"))
            Reg("opensees", (a, kw) =>
            {
                if (!TclInterop.Available)
                    throw new PyRuntimeError("RuntimeError",
                        "OpenSees nativo no disponible (falta OpenSeesRT.dll / Tcl). " + (TclInterop.LastError ?? ""));
                string cmd;
                if (a.Length == 1 && a[0] is string s0) cmd = s0;
                else
                {
                    var b = new StringBuilder();
                    for (int i = 0; i < a.Length; i++) { if (i > 0) b.Append(' '); b.Append(PyOps.Str(a[i])); }
                    cmd = b.ToString();
                }
                _opensees ??= new TclInterop();
                return _opensees.Eval(cmd);
            });

            Reg("print", (a, kw) =>
            {
                string sep = " ", end = "\n";
                if (kw != null)
                {
                    if (kw.TryGet("sep", out var s) && s != null) sep = PyOps.Str(s);
                    if (kw.TryGet("end", out var e) && e != null) end = PyOps.Str(e);
                }
                var sb = new StringBuilder();
                for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(sep); sb.Append(PyOps.Str(a[i])); }
                sb.Append(end);
                Output(sb.ToString());
                return null;
            });
            // mesh3d(w, Mxy, Mx, My, a=6, b=6, H=4): visor interactivo de la mesa
            // (2D canvas jet + crosshair, 3D three.js de la mesa deformada + hover).
            // Builtin nativo de Calcpad-Py: se llama como funcion Python; el JS vive en el motor.
            Reg("mesh3d", (a, kw) =>
            {
                if (a.Length < 4)
                    throw new PyRuntimeError("TypeError", "mesh3d(w, Mxy, Mx, My, a=6, b=6, H=4)");
                double KwD(string k, double d) =>
                    (kw != null && kw.TryGet(k, out var v) && v != null) ? PyOps.ToDouble(v) : d;
                (double[] f, int r, int c) Flat(object o, string nm)
                {
                    if (o is not PyList pl || pl.Items.Count == 0 || pl.Items[0] is not PyList)
                        throw new PyRuntimeError("TypeError", $"mesh3d: '{nm}' debe ser una matriz (lista de listas)");
                    int R = pl.Items.Count, C = ((PyList)pl.Items[0]).Items.Count;
                    var fa = new double[R * C];
                    for (int i = 0; i < R; i++)
                    {
                        var row = ((PyList)pl.Items[i]).Items;
                        for (int j = 0; j < C; j++) fa[i * C + j] = PyOps.ToDouble(row[j]);
                    }
                    return (fa, R, C);
                }
                var (w, R, C) = Flat(a[0], "w");
                var (mxy, _, _) = Flat(a[1], "Mxy");
                var (mx, _, _) = Flat(a[2], "Mx");
                var (my, _, _) = Flat(a[3], "My");
                string html = PythonViz.MesaViewer(w, mxy, mx, my, R - 1, C - 1,
                    KwD("a", 6), KwD("b", 6), KwD("H", 4), "rv" + (++_vizId));
                HtmlOut(html);
                return null;
            });
            // mesh_viewer(nodes, elements, fields, title=""): visor FEM GENÉRICO interactivo.
            // nodes=lista de [x,y]; elements=lista de [i,j,k(,l)] (tri o quad);
            // fields=dict nombre->lista de valores nodales. Heatmap jet + HOVER (valor en
            // el cursor) + selector de campo. Builtin nativo → WebView2 (sin python real).
            Reg("mesh_viewer", (a, kw) =>
            {
                if (a.Length < 3)
                    throw new PyRuntimeError("TypeError", "mesh_viewer(nodes, elements, fields, title='')");
                var nl = (PyList)a[0];
                var nodes = new double[nl.Count][];
                for (int i = 0; i < nodes.Length; i++)
                {
                    var p = (PyList)nl.Items[i];
                    nodes[i] = new[] { PyOps.ToDouble(p.Items[0]), PyOps.ToDouble(p.Items[1]) };
                }
                var el = (PyList)a[1];
                var tris = new List<int[]>();
                foreach (var eo in el.Items)
                {
                    var e = (PyList)eo;
                    var ix = new int[e.Count];
                    for (int k = 0; k < ix.Length; k++) ix[k] = (int)PyOps.ToLong(e.Items[k]);
                    if (ix.Length >= 4) { tris.Add(new[] { ix[0], ix[1], ix[2] }); tris.Add(new[] { ix[0], ix[2], ix[3] }); }
                    else if (ix.Length == 3) tris.Add(new[] { ix[0], ix[1], ix[2] });
                }
                var fd = (PyDict)a[2];
                var names = new string[fd.Count];
                var vals = new double[fd.Count][];
                for (int i = 0; i < fd.Count; i++)
                {
                    names[i] = PyOps.Str(fd.Keys[i]);
                    var vl = (PyList)fd.Values[i];
                    var va = new double[vl.Count];
                    for (int k = 0; k < va.Length; k++) va[k] = PyOps.ToDouble(vl.Items[k]);
                    vals[i] = va;
                }
                string title = a.Length >= 4 ? PyOps.Str(a[3]) : "Campo FEM";
                string html = PythonViz.MeshViewer(nodes, tris.ToArray(), names, vals, title, "mv" + (++_vizId));
                HtmlOut(html);
                return null;
            });
            // mesh3d_viewer(nodes, faces, fields, title=""): visor 3D ORBIT genérico para SÓLIDOS.
            // nodes=lista de [x,y,z]; faces=lista de caras de superficie [i,j,k] o [i,j,k,l];
            // fields=dict nombre->valores nodales. THREE.js OrbitControls (arrastra/zoom/hover), jet_r.
            Reg("mesh3d_viewer", (a, kw) =>
            {
                if (a.Length < 3)
                    throw new PyRuntimeError("TypeError", "mesh3d_viewer(nodes, faces, fields, title='')");
                var nl = (PyList)a[0];
                var nodes = new double[nl.Count][];
                for (int i = 0; i < nodes.Length; i++)
                {
                    var p = (PyList)nl.Items[i];
                    nodes[i] = new[] { PyOps.ToDouble(p.Items[0]), PyOps.ToDouble(p.Items[1]), PyOps.ToDouble(p.Items[2]) };
                }
                var el = (PyList)a[1];
                var tris = new List<int[]>();
                foreach (var eo in el.Items)
                {
                    var e = (PyList)eo;
                    var ix = new int[e.Count];
                    for (int k = 0; k < ix.Length; k++) ix[k] = (int)PyOps.ToLong(e.Items[k]);
                    if (ix.Length >= 4) { tris.Add(new[] { ix[0], ix[1], ix[2] }); tris.Add(new[] { ix[0], ix[2], ix[3] }); }
                    else if (ix.Length == 3) tris.Add(new[] { ix[0], ix[1], ix[2] });
                }
                var fd = (PyDict)a[2];
                var names = new string[fd.Count];
                var vals = new double[fd.Count][];
                for (int i = 0; i < fd.Count; i++)
                {
                    names[i] = PyOps.Str(fd.Keys[i]);
                    var vl = (PyList)fd.Values[i];
                    var va = new double[vl.Count];
                    for (int k = 0; k < va.Length; k++) va[k] = PyOps.ToDouble(vl.Items[k]);
                    vals[i] = va;
                }
                string title = a.Length >= 4 ? PyOps.Str(a[3]) : "Campo FEM 3D";
                string html = PythonViz.Solid3DViewer(nodes, tris.ToArray(), names, vals, title, "m3" + (++_vizId));
                HtmlOut(html);
                return null;
            });
            // solid3d_viewer(nodes, elems, fields, title=""): SÓLIDO MACIZO desde elementos de
            // VOLUMEN (tetraedros [i,j,k,l] o hexaedros [8 idx]). Extrae la PIEL exterior automatica-
            // mente (caras que aparecen 1 vez) -> cuerpo solido cerrado. Al filtrar elementos, el
            // corte queda RELLENO con la seccion interna (sin huecos). Orbit 3D, jet_r.
            Reg("solid3d_viewer", (a, kw) =>
            {
                if (a.Length < 3)
                    throw new PyRuntimeError("TypeError", "solid3d_viewer(nodes, elems, fields, title='')");
                var nl = (PyList)a[0];
                var nodes = new double[nl.Count][];
                for (int i = 0; i < nodes.Length; i++)
                {
                    var p = (PyList)nl.Items[i];
                    nodes[i] = new[] { PyOps.ToDouble(p.Items[0]), PyOps.ToDouble(p.Items[1]), PyOps.ToDouble(p.Items[2]) };
                }
                var el = (PyList)a[1];
                var elems = new List<int[]>();
                foreach (var eo in el.Items)
                {
                    var e = (PyList)eo;
                    var ix = new int[e.Count];
                    for (int k = 0; k < ix.Length; k++) ix[k] = (int)PyOps.ToLong(e.Items[k]);
                    elems.Add(ix);
                }
                var fd = (PyDict)a[2];
                var names = new string[fd.Count];
                var vals = new double[fd.Count][];
                for (int i = 0; i < fd.Count; i++)
                {
                    names[i] = PyOps.Str(fd.Keys[i]);
                    var vl = (PyList)fd.Values[i];
                    var va = new double[vl.Count];
                    for (int k = 0; k < va.Length; k++) va[k] = PyOps.ToDouble(vl.Items[k]);
                    vals[i] = va;
                }
                string title = a.Length >= 4 ? PyOps.Str(a[3]) : "Sólido FEM 3D";
                // pasa el VOLUMEN al visor: extrae piel en JS + SLIDER de corte en vivo (relleno)
                string html = PythonViz.SolidClipViewer(nodes, elems.ToArray(), names, vals, title, "s3" + (++_vizId));
                HtmlOut(html);
                return null;
            });
            Reg("len", (a, kw) => (long)PyLen(a[0]));
            Reg("range", (a, kw) =>
            {
                long start = 0, stop, step = 1;
                if (a.Length == 1) stop = PyOps.ToLong(a[0]);
                else { start = PyOps.ToLong(a[0]); stop = PyOps.ToLong(a[1]); if (a.Length >= 3) step = PyOps.ToLong(a[2]); }
                return new PyRange(start, stop, step);
            });
            Reg("abs", (a, kw) => a[0] is double d ? Math.Abs(d) : (object)Math.Abs(PyOps.ToLong(a[0])));
            Reg("min", (a, kw) => MinMax(a, kw, true));
            Reg("max", (a, kw) => MinMax(a, kw, false));
            Reg("sum", (a, kw) =>
            {
                object acc = a.Length > 1 ? a[1] : (object)0L;
                foreach (var x in Iterate(a[0])) acc = PyOps.Binary("+", acc, x);
                return acc;
            });
            Reg("round", (a, kw) =>
            {
                double d = PyOps.ToDouble(a[0]);
                if (a.Length >= 2 && a[1] != null)
                {
                    int nd = (int)PyOps.ToLong(a[1]);
                    return Math.Round(d, nd, MidpointRounding.ToEven);
                }
                return (long)Math.Round(d, MidpointRounding.ToEven);
            });
            Reg("int", (a, kw) =>
            {
                if (a.Length == 0) return 0L;
                if (a[0] is string s)
                {
                    int bas = a.Length >= 2 ? (int)PyOps.ToLong(a[1]) : 10;
                    return Convert.ToInt64(s.Trim(), bas);
                }
                return PyOps.ToLong(a[0] is double dd ? (object)Math.Truncate(dd) : a[0]);
            });
            Reg("float", (a, kw) => a.Length == 0 ? 0.0 : (a[0] is string s ? double.Parse(s.Trim(), CultureInfo.InvariantCulture) : PyOps.ToDouble(a[0])));
            Reg("str", (a, kw) => a.Length == 0 ? "" : PyOps.Str(a[0]));
            Reg("repr", (a, kw) => PyOps.Repr(a[0]));
            Reg("bool", (a, kw) => a.Length != 0 && PyOps.Truthy(a[0]));
            Reg("list", (a, kw) => a.Length == 0 ? new PyList() : new PyList(Iterate(a[0])));
            Reg("tuple", (a, kw) => a.Length == 0 ? new PyTuple() : new PyTuple(Iterate(a[0])));
            Reg("set", (a, kw) => { var s = new PySet(); if (a.Length > 0) foreach (var x in Iterate(a[0])) s.Add(x); return s; });
            Reg("frozenset", (a, kw) => { var s = new PySet(); if (a.Length > 0 && a[0] != null) foreach (var x in Iterate(a[0])) s.Add(x); return s; });
            Reg("dict", (a, kw) =>
            {
                var d = new PyDict();
                if (a.Length > 0) foreach (var pair in Iterate(a[0])) { var it = new List<object>(Iterate(pair)); d.Set(it[0], it[1]); }
                if (kw != null) for (int i = 0; i < kw.Keys.Count; i++) d.Set(kw.Keys[i], kw.Values[i]);
                return d;
            });
            Reg("sorted", (a, kw) =>
            {
                var items = new List<object>(Iterate(a[0]));
                bool rev = kw != null && kw.TryGet("reverse", out var rv) && PyOps.Truthy(rv);
                object keyFn = null; kw?.TryGet("key", out keyFn);
                items.Sort((x, y) =>
                {
                    object kx = keyFn != null ? CallCallable(keyFn, new[] { x }, null) : x;
                    object ky = keyFn != null ? CallCallable(keyFn, new[] { y }, null) : y;
                    return PyOps.Compare(kx, ky);
                });
                if (rev) items.Reverse();
                return new PyList(items);
            });
            Reg("reversed", (a, kw) => { var items = new List<object>(Iterate(a[0])); items.Reverse(); return new PyList(items); });
            Reg("enumerate", (a, kw) =>
            {
                long start = a.Length >= 2 ? PyOps.ToLong(a[1]) : 0;
                var r = new PyList();
                long i = start;
                foreach (var x in Iterate(a[0])) r.Items.Add(new PyTuple(new List<object> { i++, x }));
                return r;
            });
            Reg("zip", (a, kw) =>
            {
                var iters = new List<List<object>>();
                foreach (var it in a) iters.Add(new List<object>(Iterate(it)));
                var r = new PyList();
                if (iters.Count == 0) return r;
                int min = int.MaxValue;
                foreach (var it in iters) min = Math.Min(min, it.Count);
                for (int i = 0; i < min; i++)
                {
                    var tup = new List<object>();
                    foreach (var it in iters) tup.Add(it[i]);
                    r.Items.Add(new PyTuple(tup));
                }
                return r;
            });
            Reg("map", (a, kw) =>
            {
                var fn = a[0];
                var iters = new List<List<object>>();
                for (int i = 1; i < a.Length; i++) iters.Add(new List<object>(Iterate(a[i])));
                var r = new PyList();
                int min = int.MaxValue;
                foreach (var it in iters) min = Math.Min(min, it.Count);
                for (int i = 0; i < min; i++)
                {
                    var ar = new object[iters.Count];
                    for (int k = 0; k < iters.Count; k++) ar[k] = iters[k][i];
                    r.Items.Add(CallCallable(fn, ar, null));
                }
                return r;
            });
            Reg("filter", (a, kw) =>
            {
                var fn = a[0];
                var r = new PyList();
                foreach (var x in Iterate(a[1]))
                {
                    bool keep = fn == null ? PyOps.Truthy(x) : PyOps.Truthy(CallCallable(fn, new[] { x }, null));
                    if (keep) r.Items.Add(x);
                }
                return r;
            });
            Reg("any", (a, kw) => { foreach (var x in Iterate(a[0])) if (PyOps.Truthy(x)) return true; return false; });
            Reg("all", (a, kw) => { foreach (var x in Iterate(a[0])) if (!PyOps.Truthy(x)) return false; return true; });
            Reg("type", (a, kw) => PyOps.TypeName(a[0]));
            Reg("isinstance", (a, kw) => IsInstance(a[0], a[1]));
            Reg("abs", (a, kw) => a[0] is double d ? Math.Abs(d) : (object)Math.Abs(PyOps.ToLong(a[0])));
            Reg("ord", (a, kw) => (long)((string)a[0])[0]);
            Reg("chr", (a, kw) => ((char)PyOps.ToLong(a[0])).ToString());
            Reg("divmod", (a, kw) => new PyTuple(new List<object> { PyOps.Binary("//", a[0], a[1]), PyOps.Binary("%", a[0], a[1]) }));
            Reg("pow", (a, kw) => a.Length >= 3 ? PyOps.Binary("%", PyOps.Binary("**", a[0], a[1]), a[2]) : PyOps.Binary("**", a[0], a[1]));
            Reg("format", (a, kw) => PyStringFormat.FormatSpec(a[0], a.Length >= 2 ? PyOps.Str(a[1]) : ""));
            Reg("input", (a, kw) => { if (a.Length > 0) Output(PyOps.Str(a[0])); return ""; });
            Reg("hex", (a, kw) => "0x" + PyOps.ToLong(a[0]).ToString("x"));
            Reg("oct", (a, kw) => "0o" + Convert.ToString(PyOps.ToLong(a[0]), 8));
            Reg("bin", (a, kw) => "0b" + Convert.ToString(PyOps.ToLong(a[0]), 2));
            Reg("isinstance", (a, kw) => IsInstance(a[0], a[1]));
            Globals.Vars["True"] = true;
            Globals.Vars["False"] = false;
            Globals.Vars["None"] = null;
            // El script de entrada corre como programa principal, igual que `python script.py`,
            // para que `if __name__ == "__main__":` ejecute su bloque en el motor nativo.
            Globals.Vars["__name__"] = "__main__";
            Globals.Vars["__file__"] = ScriptFilePath();   // os.path.dirname(os.path.abspath(__file__)) -> carpeta del script
        }

        private bool IsInstance(object obj, object cls)
        {
            string want = cls switch
            {
                PyClass pc => pc.Name,
                PyBuiltin pb => pb.Name,
                string s => s,
                PyTuple t => null,
                _ => PyOps.TypeName(cls)
            };
            if (cls is PyTuple tup)
            {
                foreach (var c in tup.Items) if (IsInstance(obj, c)) return true;
                return false;
            }
            if (obj is PyInstance inst)
            {
                var c = inst.Class;
                while (c != null) { if (c.Name == want) return true; c = c.Bases.Count > 0 ? c.Bases[0] : null; }
                return cls is PyClass pcc && inst.Class != null && InheritsFrom(inst.Class, pcc);
            }
            string actual = PyOps.TypeName(obj);
            if (want == "int") return obj is long || obj is bool;
            if (want == "float") return obj is double;
            if (want == "number") return PyOps.IsNumber(obj);
            return actual == want;
        }

        private bool InheritsFrom(PyClass c, PyClass target)
        {
            if (c == target) return true;
            foreach (var b in c.Bases) if (InheritsFrom(b, target)) return true;
            return false;
        }

        private object MinMax(object[] a, PyDict kw, bool min)
        {
            IEnumerable<object> seq = a.Length == 1 ? Iterate(a[0]) : a;
            object keyFn = null; kw?.TryGet("key", out keyFn);
            object best = null, bestKey = null; bool first = true;
            foreach (var x in seq)
            {
                object k = keyFn != null ? CallCallable(keyFn, new[] { x }, null) : x;
                if (first || (min ? PyOps.Compare(k, bestKey) < 0 : PyOps.Compare(k, bestKey) > 0))
                { best = x; bestKey = k; first = false; }
            }
            if (first)
            {
                if (kw != null && kw.TryGet("default", out var def)) return def;
                throw new PyRuntimeError("ValueError", $"{(min ? "min" : "max")}() arg is an empty sequence");
            }
            return best;
        }

        public int PyLen(object o) => o switch
        {
            string s => s.Length,
            PyList l => l.Count,
            PyTuple t => t.Count,
            PyDict d => d.Count,
            PySet st => st.Count,
            PyRange r => (int)r.Length,
            PyNdArray nd => nd.Shape[0],          // numpy: len(array) = tamaño del primer eje
            _ => throw new PyRuntimeError("TypeError", $"object of type '{PyOps.TypeName(o)}' has no len()")
        };

        // Extrae la PIEL (contorno) de una malla de VOLUMEN (tetraedros 4 idx / hexaedros 8 idx):
        // caras compartidas por 2 elementos = internas (se descartan); las que aparecen 1 vez = piel
        // exterior. Al filtrar elementos, la seccion cortada queda expuesta -> corte RELLENO sin huecos.
        private static int[][] ExtractVolumeBoundary(List<int[]> elems)
        {
            int[][] TetF = { new[] { 0, 1, 2 }, new[] { 0, 1, 3 }, new[] { 0, 2, 3 }, new[] { 1, 2, 3 } };
            int[][] HexF = { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
            var count = new Dictionary<string, int>();
            var repr = new Dictionary<string, int[]>();
            foreach (var el in elems)
            {
                int[][] fdefs = el.Length == 4 ? TetF : el.Length == 8 ? HexF : null;
                if (fdefs == null) continue;
                foreach (var fd in fdefs)
                {
                    var nodes = new int[fd.Length];
                    for (int k = 0; k < fd.Length; k++) nodes[k] = el[fd[k]];
                    var sorted = (int[])nodes.Clone(); System.Array.Sort(sorted);
                    string key = string.Join("_", sorted);
                    if (count.ContainsKey(key)) count[key]++;
                    else { count[key] = 1; repr[key] = nodes; }
                }
            }
            var tris = new List<int[]>();
            foreach (var kv in count)
                if (kv.Value == 1)
                {
                    var nd = repr[kv.Key];
                    tris.Add(new[] { nd[0], nd[1], nd[2] });
                    if (nd.Length == 4) tris.Add(new[] { nd[0], nd[2], nd[3] });   // quad -> 2 tris
                }
            return tris.ToArray();
        }
    }
}
