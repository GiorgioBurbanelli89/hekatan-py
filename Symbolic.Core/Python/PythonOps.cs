// =============================================================================
// Hekatan Python3 — Operaciones sobre valores Python (aritmética, comparación,
// igualdad, truthiness, str/repr, formato).
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Calcpad.Core.Python
{
    public static class PyOps
    {
        // ── Predicados de tipo ──
        public static bool IsInt(object o) => o is long || o is bool;
        public static bool IsFloat(object o) => o is double;
        public static bool IsNumber(object o) => o is long || o is double || o is bool;

        public static double ToDouble(object o) => o switch
        {
            long l => l,
            double d => d,
            bool b => b ? 1.0 : 0.0,
            _ => throw new PyRuntimeError("TypeError", $"no se puede convertir {TypeName(o)} a número")
        };

        public static long ToLong(object o) => o switch
        {
            long l => l,
            bool b => b ? 1L : 0L,
            double d => (long)d,
            _ => throw new PyRuntimeError("TypeError", $"no se puede convertir {TypeName(o)} a entero")
        };

        // ── Truthiness ──
        public static bool Truthy(object o) => o switch
        {
            null => false,
            bool b => b,
            long l => l != 0,
            double d => d != 0.0,
            string s => s.Length > 0,
            PyList l => l.Count > 0,
            PyTuple t => t.Count > 0,
            PyDict d => d.Count > 0,
            PySet st => st.Count > 0,
            PyRange r => r.Length > 0,
            _ => true
        };

        // ── Igualdad por valor ──
        public static bool Equal(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (IsNumber(a) && IsNumber(b)) return ToDouble(a) == ToDouble(b);
            if (a is string sa && b is string sb) return sa == sb;
            if (a is PyList la && b is PyList lb) return SeqEqual(la.Items, lb.Items);
            if (a is PyTuple ta && b is PyTuple tb) return SeqEqual(ta.Items, tb.Items);
            if (a is PySet sea && b is PySet seb)
            {
                if (sea.Count != seb.Count) return false;
                foreach (var x in sea.Items) if (!seb.Contains(x)) return false;
                return true;
            }
            if (a is PyDict da && b is PyDict db)
            {
                if (da.Count != db.Count) return false;
                for (int i = 0; i < da.Keys.Count; i++)
                    if (!db.TryGet(da.Keys[i], out var v) || !Equal(da.Values[i], v)) return false;
                return true;
            }
            return ReferenceEquals(a, b);
        }

        private static bool SeqEqual(List<object> a, List<object> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!Equal(a[i], b[i])) return false;
            return true;
        }

        // ── Comparación de orden (<,>,<=,>=) ──
        // ndarray de 1 elemento -> escalar (numpy permite comparar arrays de tamano 1 como escalar)
        private static object UnwrapScalar(object x) => (x is PyNdArray nd && nd.Data != null && nd.Data.Length == 1) ? (object)nd.Data[0] : x;

        public static int Compare(object a, object b)
        {
            a = UnwrapScalar(a); b = UnwrapScalar(b);
            if (IsNumber(a) && IsNumber(b)) return ToDouble(a).CompareTo(ToDouble(b));
            if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb);
            if (a is PyList la && b is PyList lb) return SeqCompare(la.Items, lb.Items);
            if (a is PyTuple ta && b is PyTuple tb) return SeqCompare(ta.Items, tb.Items);
            throw new PyRuntimeError("TypeError", $"'<' no soportado entre {TypeName(a)} y {TypeName(b)}");
        }

        private static int SeqCompare(List<object> a, List<object> b)
        {
            int n = Math.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++)
            {
                int c = Compare(a[i], b[i]);
                if (c != 0) return c;
            }
            return a.Count.CompareTo(b.Count);
        }

        // ── Aritmética binaria ──
        public static object Binary(string op, object a, object b)
        {
            switch (op)
            {
                case "+": return Add(a, b);
                case "-": return Sub(a, b);
                case "*": return Mul(a, b);
                case "/": return TrueDiv(a, b);
                case "//": return FloorDiv(a, b);
                case "%": return Mod(a, b);
                case "**": return Pow(a, b);
                case "&": return BitOp(op, a, b);
                case "|": return BitOp(op, a, b);
                case "^": return BitOp(op, a, b);
                case "<<": return ToLong(a) << (int)ToLong(b);
                case ">>": return ToLong(a) >> (int)ToLong(b);
                case "@": return PyNumpy.MatMul(a, b);
            }
            throw new PyRuntimeError("TypeError", $"operador binario desconocido '{op}'");
        }

        private static object Add(object a, object b)
        {
            if (a is PyNdArray || b is PyNdArray) return PyNumpy.Binary("+", a, b);
            if (IsNumber(a) && IsNumber(b)) return NumResult(a, b, (x, y) => x + y, (x, y) => x + y);
            if (a is string s1 && b is string s2) return s1 + s2;
            if (a is PyList l1 && b is PyList l2) { var r = new PyList(l1.Items); r.Items.AddRange(l2.Items); return r; }
            if (a is PyTuple t1 && b is PyTuple t2) { var r = new List<object>(t1.Items); r.AddRange(t2.Items); return new PyTuple(r); }
            throw new PyRuntimeError("TypeError", $"no se puede sumar {TypeName(a)} y {TypeName(b)}");
        }
        private static object Sub(object a, object b)
        {
            if (a is PyNdArray || b is PyNdArray) return PyNumpy.Binary("-", a, b);
            if (IsNumber(a) && IsNumber(b)) return NumResult(a, b, (x, y) => x - y, (x, y) => x - y);
            if (a is PySet s1 && b is PySet s2) { var r = new PySet(); foreach (var x in s1.Items) if (!s2.Contains(x)) r.Add(x); return r; }
            throw new PyRuntimeError("TypeError", $"no se puede restar {TypeName(a)} y {TypeName(b)}");
        }
        private static object Mul(object a, object b)
        {
            if (a is PyNdArray || b is PyNdArray) return PyNumpy.Binary("*", a, b);
            if (IsNumber(a) && IsNumber(b)) return NumResult(a, b, (x, y) => x * y, (x, y) => x * y);
            if (a is string s && IsInt(b)) return Repeat(s, ToLong(b));
            if (IsInt(a) && b is string s2) return Repeat(s2, ToLong(a));
            if (a is PyList l && IsInt(b)) return RepeatList(l.Items, ToLong(b), true);
            if (IsInt(a) && b is PyList l2) return RepeatList(l2.Items, ToLong(a), true);
            if (a is PyTuple t && IsInt(b)) return RepeatList(t.Items, ToLong(b), false);
            if (IsInt(a) && b is PyTuple t2) return RepeatList(t2.Items, ToLong(a), false);
            throw new PyRuntimeError("TypeError", $"no se puede multiplicar {TypeName(a)} y {TypeName(b)}");
        }
        private static string Repeat(string s, long n) { if (n <= 0) return ""; var sb = new StringBuilder(); for (long i = 0; i < n; i++) sb.Append(s); return sb.ToString(); }
        private static object RepeatList(List<object> items, long n, bool list)
        {
            var r = new List<object>();
            for (long i = 0; i < n; i++) r.AddRange(items);
            return list ? new PyList(r) : new PyTuple(r);
        }
        private static object TrueDiv(object a, object b)
        {
            if (a is PyNdArray || b is PyNdArray) return PyNumpy.Binary("/", a, b);
            double y = ToDouble(b);
            if (y == 0) throw new PyRuntimeError("ZeroDivisionError", "division by zero");
            return ToDouble(a) / y;
        }
        private static object FloorDiv(object a, object b)
        {
            if (IsInt(a) && IsInt(b))
            {
                long bb = ToLong(b);
                if (bb == 0) throw new PyRuntimeError("ZeroDivisionError", "integer division or modulo by zero");
                long aa = ToLong(a);
                long q = aa / bb;
                if ((aa % bb != 0) && ((aa < 0) != (bb < 0))) q--;  // floor
                return q;
            }
            double y = ToDouble(b);
            if (y == 0) throw new PyRuntimeError("ZeroDivisionError", "float floor division by zero");
            return Math.Floor(ToDouble(a) / y);
        }
        private static object Mod(object a, object b)
        {
            if (a is string fmt) return PyStringFormat.PercentFormat(fmt, b);
            if (IsInt(a) && IsInt(b))
            {
                long bb = ToLong(b);
                if (bb == 0) throw new PyRuntimeError("ZeroDivisionError", "integer division or modulo by zero");
                long r = ToLong(a) % bb;
                if (r != 0 && ((r < 0) != (bb < 0))) r += bb;  // signo del divisor
                return r;
            }
            double yb = ToDouble(b);
            if (yb == 0) throw new PyRuntimeError("ZeroDivisionError", "float modulo");
            double rr = ToDouble(a) % yb;
            if (rr != 0 && ((rr < 0) != (yb < 0))) rr += yb;
            return rr;
        }
        private static object Pow(object a, object b)
        {
            if (IsInt(a) && IsInt(b) && ToLong(b) >= 0)
            {
                long baseV = ToLong(a), exp = ToLong(b), res = 1;
                for (long i = 0; i < exp; i++) res *= baseV;
                return res;
            }
            return Math.Pow(ToDouble(a), ToDouble(b));
        }
        private static object BitOp(string op, object a, object b)
        {
            if (a is bool && b is bool)
            {
                bool x = (bool)a, y = (bool)b;
                return op switch { "&" => x & y, "|" => x | y, _ => x ^ y };
            }
            if (a is PySet s1 && b is PySet s2)
            {
                var r = new PySet();
                if (op == "&") { foreach (var x in s1.Items) if (s2.Contains(x)) r.Add(x); }
                else if (op == "|") { foreach (var x in s1.Items) r.Add(x); foreach (var x in s2.Items) r.Add(x); }
                else { foreach (var x in s1.Items) if (!s2.Contains(x)) r.Add(x); foreach (var x in s2.Items) if (!s1.Contains(x)) r.Add(x); }
                return r;
            }
            long la = ToLong(a), lb = ToLong(b);
            return op switch { "&" => la & lb, "|" => la | lb, _ => la ^ lb };
        }

        private static object NumResult(object a, object b, Func<long, long, long> fi, Func<double, double, double> fd)
        {
            if (IsInt(a) && IsInt(b)) return fi(ToLong(a), ToLong(b));
            return fd(ToDouble(a), ToDouble(b));
        }

        public static object Negate(object a)
        {
            if (a is PyNdArray nd) return PyNumpy.Negate(nd);
            if (IsInt(a)) return -ToLong(a);
            if (a is double d) return -d;
            throw new PyRuntimeError("TypeError", $"operador unario - no soportado para {TypeName(a)}");
        }

        // ── Nombre de tipo Python ──
        public static string TypeName(object o) => o switch
        {
            null => "NoneType",
            bool => "bool",
            long => "int",
            double => "float",
            string => "str",
            PyList => "list",
            PyTuple => "tuple",
            PyDict => "dict",
            PySet => "set",
            PyRange => "range",
            PyFunction => "function",
            PyBuiltin => "builtin_function_or_method",
            PyModule => "module",
            PyClass c => "type",
            PyInstance pi => pi.Class?.Name ?? "object",
            PyNdArray => "numpy.ndarray",
            _ => o.GetType().Name
        };

        // ── str() ──
        public static string Str(object o)
        {
            switch (o)
            {
                case null: return "None";
                case bool b: return b ? "True" : "False";
                case long l: return l.ToString(CultureInfo.InvariantCulture);
                case double d: return FormatFloat(d);
                case string s: return s;
                case PyList list: return "[" + JoinRepr(list.Items) + "]";
                case PyTuple t: return t.Count == 1 ? "(" + Repr(t.Items[0]) + ",)" : "(" + JoinRepr(t.Items) + ")";
                case PySet st: return st.Count == 0 ? "set()" : "{" + JoinRepr(st.Items) + "}";
                case PyDict dd: return DictStr(dd);
                case PyRange r: return r.Step == 1 ? $"range({r.Start}, {r.Stop})" : $"range({r.Start}, {r.Stop}, {r.Step})";
                case PyFunction f: return $"<function {f.Name}>";
                case PyBuiltin bf: return $"<built-in function {bf.Name}>";
                case PyModule m: return $"<module '{m.Name}'>";
                case PyClass c: return $"<class '{c.Name}'>";
                case PyInstance pi: return $"<{pi.Class?.Name} object>";
                case PyNdArray nd: return nd.ToString();
                default: return o.ToString();
            }
        }

        // ── repr() ──
        public static string Repr(object o)
        {
            switch (o)
            {
                case string s: return "'" + s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\t", "\\t") + "'";
                default: return Str(o);
            }
        }

        private static string JoinRepr(List<object> items)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(Repr(items[i])); }
            return sb.ToString();
        }

        private static string DictStr(PyDict d)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < d.Keys.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Repr(d.Keys[i])).Append(": ").Append(Repr(d.Values[i]));
            }
            return sb.Append('}').ToString();
        }

        public static string FormatFloat(double d)
        {
            if (double.IsNaN(d)) return "nan";
            if (double.IsPositiveInfinity(d)) return "inf";
            if (double.IsNegativeInfinity(d)) return "-inf";
            // Python: floats enteros muestran ".0"
            if (d == Math.Floor(d) && !double.IsInfinity(d) && Math.Abs(d) < 1e16)
                return ((long)d).ToString(CultureInfo.InvariantCulture) + ".0";
            string r = d.ToString("R", CultureInfo.InvariantCulture);
            return r;
        }
    }
}
