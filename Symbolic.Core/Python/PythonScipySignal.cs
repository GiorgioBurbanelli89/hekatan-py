// scipy.signal + scipy.spatial EMBEBIDOS — sin Python externo. Procesamiento de señales real
// (convolve, correlate, lfilter, butter, find_peaks, ventanas) y distancias espaciales.
using System;
using System.Collections.Generic;
using Cx = System.Numerics.Complex;

namespace Calcpad.Core.Python
{
    internal static class PythonScipySignal
    {
        private static PyNdArray Arr(object o) => PyNumpy.AsArr(o);

        public static PyModule SignalModule()
        {
            var s = new PyModule("scipy.signal");
            s.Attrs["convolve"] = new PyBuiltin("convolve", (a, kw) => Convolve(Arr(a[0]).Data, Arr(a[1]).Data));
            s.Attrs["correlate"] = new PyBuiltin("correlate", (a, kw) => { var v = (double[])Arr(a[1]).Data.Clone(); Array.Reverse(v); return Convolve(Arr(a[0]).Data, v); });
            s.Attrs["lfilter"] = new PyBuiltin("lfilter", (a, kw) => Lfilter(Arr(a[0]).Data, Arr(a[1]).Data, Arr(a[2]).Data));
            s.Attrs["filtfilt"] = new PyBuiltin("filtfilt", (a, kw) => { var y1 = Lfilter(Arr(a[0]).Data, Arr(a[1]).Data, Arr(a[2]).Data); var yr = (double[])y1.Data.Clone(); Array.Reverse(yr); var y2 = Lfilter(Arr(a[0]).Data, Arr(a[1]).Data, yr); var o = (double[])y2.Data.Clone(); Array.Reverse(o); return new PyNdArray(o, new[] { o.Length }); });
            s.Attrs["butter"] = new PyBuiltin("butter", (a, kw) => Butter((int)PyOps.ToLong(a[0]), PyOps.ToDouble(a[1]), a.Length > 2 && a[2] is string bt ? bt : (kw != null && kw.TryGet("btype", out var b2) ? PyOps.Str(b2) : "low")));
            s.Attrs["find_peaks"] = new PyBuiltin("find_peaks", (a, kw) => FindPeaks(Arr(a[0]).Data, kw));
            var win = new PyModule("scipy.signal.windows");
            win.Attrs["hann"] = new PyBuiltin("hann", (a, kw) => Window((int)PyOps.ToLong(a[0]), "hann"));
            win.Attrs["hamming"] = new PyBuiltin("hamming", (a, kw) => Window((int)PyOps.ToLong(a[0]), "hamming"));
            win.Attrs["blackman"] = new PyBuiltin("blackman", (a, kw) => Window((int)PyOps.ToLong(a[0]), "blackman"));
            s.Attrs["windows"] = win;
            s.Attrs["hann"] = win.Attrs["hann"]; s.Attrs["hamming"] = win.Attrs["hamming"];
            return s;
        }

        public static PyModule SpatialModule()
        {
            var sp = new PyModule("scipy.spatial");
            var dist = new PyModule("scipy.spatial.distance");
            dist.Attrs["euclidean"] = new PyBuiltin("euclidean", (a, kw) => { var u = Arr(a[0]).Data; var v = Arr(a[1]).Data; double s = 0; for (int i = 0; i < u.Length; i++) s += (u[i] - v[i]) * (u[i] - v[i]); return Math.Sqrt(s); });
            dist.Attrs["cityblock"] = new PyBuiltin("cityblock", (a, kw) => { var u = Arr(a[0]).Data; var v = Arr(a[1]).Data; double s = 0; for (int i = 0; i < u.Length; i++) s += Math.Abs(u[i] - v[i]); return s; });
            dist.Attrs["cdist"] = new PyBuiltin("cdist", (a, kw) => Cdist(Arr(a[0]), Arr(a[1])));
            dist.Attrs["pdist"] = new PyBuiltin("pdist", (a, kw) => Pdist(Arr(a[0])));
            sp.Attrs["distance"] = dist;
            return sp;
        }

        private static object Convolve(double[] a, double[] v)
        {
            int n = a.Length + v.Length - 1; var r = new double[n];
            for (int i = 0; i < a.Length; i++) for (int j = 0; j < v.Length; j++) r[i + j] += a[i] * v[j];
            return new PyNdArray(r, new[] { n });
        }
        private static PyNdArray Lfilter(double[] b, double[] a, double[] x)
        {
            // Direct Form II Transposed. Normaliza por a[0].
            int n = x.Length; var y = new double[n]; double a0 = a.Length > 0 ? a[0] : 1;
            for (int i = 0; i < n; i++)
            {
                double acc = 0;
                for (int j = 0; j < b.Length; j++) if (i - j >= 0) acc += b[j] * x[i - j];
                for (int j = 1; j < a.Length; j++) if (i - j >= 0) acc -= a[j] * y[i - j];
                y[i] = acc / a0;
            }
            return new PyNdArray(y, new[] { n });
        }
        private static object FindPeaks(double[] x, PyDict kw)
        {
            double? height = null;
            if (kw != null && kw.TryGet("height", out var h)) height = PyOps.ToDouble(h);
            var idx = new List<double>();
            for (int i = 1; i < x.Length - 1; i++)
                if (x[i] > x[i - 1] && x[i] > x[i + 1] && (height == null || x[i] >= height.Value)) idx.Add(i);
            return new PyTuple(new List<object> { new PyNdArray(idx.ToArray(), new[] { idx.Count }, true), new PyDict() });
        }
        private static object Window(int M, string type)
        {
            var w = new double[M];
            for (int i = 0; i < M; i++)
            {
                double t = 2 * Math.PI * i / (M - 1);
                w[i] = type == "hamming" ? 0.54 - 0.46 * Math.Cos(t)
                     : type == "blackman" ? 0.42 - 0.5 * Math.Cos(t) + 0.08 * Math.Cos(2 * t)
                     : 0.5 * (1 - Math.Cos(t));   // hann
            }
            return new PyNdArray(w, new[] { M });
        }

        // ---- Butterworth digital lowpass/highpass (analog prototipo + bilinear) ----
        private static object Butter(int N, double Wn, string btype)
        {
            double fs = 2.0, warped = 2 * fs * Math.Tan(Math.PI * Wn / fs);
            var poles = new Cx[N];
            for (int k = 0; k < N; k++) poles[k] = warped * Cx.FromPolarCoordinates(1, Math.PI / 2 + Math.PI * (2 * k + 1) / (2 * N));
            bool high = btype.StartsWith("high");
            if (high) for (int k = 0; k < N; k++) poles[k] = warped * warped / poles[k];
            // bilinear s->2fs(z-1)/(z+1): polos/ceros z
            var pz = new Cx[N]; for (int i = 0; i < N; i++) pz[i] = (2 * fs + poles[i]) / (2 * fs - poles[i]);
            var zz = new Cx[N]; for (int i = 0; i < N; i++) zz[i] = high ? Cx.One : -Cx.One;   // lp:z=-1, hp:z=+1
            var aC = Poly(pz); var bC = Poly(zz);
            var a = new double[aC.Length]; var b = new double[bC.Length];
            for (int i = 0; i < aC.Length; i++) a[i] = aC[i].Real;
            for (int i = 0; i < bC.Length; i++) b[i] = bC[i].Real;
            // normaliza ganancia: lowpass en z=1, highpass en z=-1
            double zev = high ? -1 : 1, num = 0, den = 0, zp = 1;
            for (int i = b.Length - 1; i >= 0; i--) { num += b[i] * zp; zp *= zev; }
            zp = 1; for (int i = a.Length - 1; i >= 0; i--) { den += a[i] * zp; zp *= zev; }
            double g = den / num; for (int i = 0; i < b.Length; i++) b[i] *= g;
            return new PyTuple(new List<object> { new PyNdArray(b, new[] { b.Length }), new PyNdArray(a, new[] { a.Length }) });
        }
        private static Cx[] Poly(Cx[] roots)
        {
            var c = new Cx[] { Cx.One };
            foreach (var r in roots)
            {
                var nc = new Cx[c.Length + 1];
                for (int i = 0; i < c.Length; i++) { nc[i] += c[i]; nc[i + 1] -= c[i] * r; }
                c = nc;
            }
            return c;
        }

        private static object Cdist(PyNdArray A, PyNdArray B)
        {
            int ma = A.Rows, mb = B.Rows, d = A.Cols; var r = new double[ma * mb];
            for (int i = 0; i < ma; i++) for (int j = 0; j < mb; j++) { double s = 0; for (int k = 0; k < d; k++) { double df = A.Data[i * d + k] - B.Data[j * d + k]; s += df * df; } r[i * mb + j] = Math.Sqrt(s); }
            return new PyNdArray(r, new[] { ma, mb });
        }
        private static object Pdist(PyNdArray A)
        {
            int m = A.Rows, d = A.Cols; var r = new List<double>();
            for (int i = 0; i < m; i++) for (int j = i + 1; j < m; j++) { double s = 0; for (int k = 0; k < d; k++) { double df = A.Data[i * d + k] - A.Data[j * d + k]; s += df * df; } r.Add(Math.Sqrt(s)); }
            return new PyNdArray(r.ToArray(), new[] { r.Count });
        }
    }
}
