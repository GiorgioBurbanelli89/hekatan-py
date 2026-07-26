// =============================================================================
// Hekatan Python3 — Módulo nativo `math`
// =============================================================================
using System;
using System.Collections.Generic;

namespace Calcpad.Core.Python
{
    public static class PythonMath
    {
        public static readonly PyModule Module = Build();

        private static PyModule Build()
        {
            var m = new PyModule("math");
            void Fn(string name, Func<double, double> f) =>
                m.Attrs[name] = new PyBuiltin("math." + name, (a, kw) => f(PyOps.ToDouble(a[0])));
            void Fn2(string name, Func<double, double, double> f) =>
                m.Attrs[name] = new PyBuiltin("math." + name, (a, kw) => f(PyOps.ToDouble(a[0]), PyOps.ToDouble(a[1])));

            m.Attrs["pi"] = Math.PI;
            m.Attrs["e"] = Math.E;
            m.Attrs["tau"] = Math.PI * 2;
            m.Attrs["inf"] = double.PositiveInfinity;
            m.Attrs["nan"] = double.NaN;

            Fn("sqrt", Math.Sqrt);
            Fn("sin", Math.Sin); Fn("cos", Math.Cos); Fn("tan", Math.Tan);
            Fn("asin", Math.Asin); Fn("acos", Math.Acos); Fn("atan", Math.Atan);
            Fn("sinh", Math.Sinh); Fn("cosh", Math.Cosh); Fn("tanh", Math.Tanh);
            Fn("asinh", Math.Asinh); Fn("acosh", Math.Acosh); Fn("atanh", Math.Atanh);
            Fn("exp", Math.Exp); Fn("expm1", x => Math.Exp(x) - 1);
            Fn("log10", Math.Log10); Fn("log2", Math.Log2);
            Fn("log1p", x => Math.Log(1 + x));
            Fn("degrees", x => x * 180.0 / Math.PI);
            Fn("radians", x => x * Math.PI / 180.0);
            Fn("fabs", Math.Abs);
            Fn2("atan2", Math.Atan2);
            Fn2("hypot", (x, y) => Math.Sqrt(x * x + y * y));
            Fn2("copysign", Math.CopySign);
            Fn2("fmod", (x, y) => x % y);

            m.Attrs["log"] = new PyBuiltin("math.log", (a, kw) =>
                a.Length >= 2 ? Math.Log(PyOps.ToDouble(a[0]), PyOps.ToDouble(a[1])) : Math.Log(PyOps.ToDouble(a[0])));
            m.Attrs["pow"] = new PyBuiltin("math.pow", (a, kw) => Math.Pow(PyOps.ToDouble(a[0]), PyOps.ToDouble(a[1])));
            m.Attrs["floor"] = new PyBuiltin("math.floor", (a, kw) => (long)Math.Floor(PyOps.ToDouble(a[0])));
            m.Attrs["ceil"] = new PyBuiltin("math.ceil", (a, kw) => (long)Math.Ceiling(PyOps.ToDouble(a[0])));
            m.Attrs["trunc"] = new PyBuiltin("math.trunc", (a, kw) => (long)Math.Truncate(PyOps.ToDouble(a[0])));
            m.Attrs["isnan"] = new PyBuiltin("math.isnan", (a, kw) => double.IsNaN(PyOps.ToDouble(a[0])));
            m.Attrs["isinf"] = new PyBuiltin("math.isinf", (a, kw) => double.IsInfinity(PyOps.ToDouble(a[0])));
            m.Attrs["isfinite"] = new PyBuiltin("math.isfinite", (a, kw) => double.IsFinite(PyOps.ToDouble(a[0])));
            m.Attrs["factorial"] = new PyBuiltin("math.factorial", (a, kw) =>
            {
                long n = PyOps.ToLong(a[0]); long r = 1; for (long i = 2; i <= n; i++) r *= i; return r;
            });
            m.Attrs["gcd"] = new PyBuiltin("math.gcd", (a, kw) =>
            {
                long g = 0; foreach (var x in a) g = Gcd(g, Math.Abs(PyOps.ToLong(x))); return g;
            });
            m.Attrs["lcm"] = new PyBuiltin("math.lcm", (a, kw) =>
            {
                long l = 1; foreach (var x in a) { long v = Math.Abs(PyOps.ToLong(x)); if (v == 0) return 0L; l = l / Gcd(l, v) * v; } return l;
            });
            m.Attrs["dist"] = new PyBuiltin("math.dist", (a, kw) =>
            {
                var p = new List<object>(); var q = new List<object>();
                foreach (var x in (System.Collections.IEnumerable)a[0]) p.Add(x);
                foreach (var x in (System.Collections.IEnumerable)a[1]) q.Add(x);
                double s = 0; for (int i = 0; i < p.Count; i++) { double d = PyOps.ToDouble(p[i]) - PyOps.ToDouble(q[i]); s += d * d; }
                return Math.Sqrt(s);
            });
            m.Attrs["prod"] = new PyBuiltin("math.prod", (a, kw) =>
            {
                object acc = 1L;
                foreach (var x in (System.Collections.IEnumerable)a[0]) acc = PyOps.Binary("*", acc, x);
                return acc;
            });
            return m;
        }

        private static long Gcd(long a, long b) { while (b != 0) { (a, b) = (b, a % b); } return a; }
    }
}
