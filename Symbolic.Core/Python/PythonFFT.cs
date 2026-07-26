// FFT embebida (numpy.fft / scipy.fft) — sin Python externo. Radix-2 Cooley-Tukey para n potencia
// de 2, DFT O(n²) de respaldo para otros n. Entrada/salida compleja via PyNdArray.Imag.
using System;
using System.Collections.Generic;

namespace Calcpad.Core.Python
{
    internal static class PythonFFT
    {
        private static PyNdArray Arr(object o) => PyNumpy.AsArr(o);

        public static PyModule Module(string name)
        {
            var m = new PyModule(name);
            m.Attrs["fft"] = new PyBuiltin("fft", (a, kw) => Fft(Arr(a[0]), false));
            m.Attrs["ifft"] = new PyBuiltin("ifft", (a, kw) => Fft(Arr(a[0]), true));
            m.Attrs["rfft"] = new PyBuiltin("rfft", (a, kw) => Rfft(Arr(a[0])));
            m.Attrs["irfft"] = new PyBuiltin("irfft", (a, kw) => { var f = Fft(Arr(a[0]), true); return new PyNdArray(f.Data, f.Shape); });
            m.Attrs["fftfreq"] = new PyBuiltin("fftfreq", (a, kw) => FftFreq((int)PyOps.ToLong(a[0]), a.Length > 1 ? PyOps.ToDouble(a[1]) : 1.0));
            m.Attrs["rfftfreq"] = new PyBuiltin("rfftfreq", (a, kw) => RfftFreq((int)PyOps.ToLong(a[0]), a.Length > 1 ? PyOps.ToDouble(a[1]) : 1.0));
            return m;
        }

        public static PyNdArray Fft(PyNdArray x, bool inverse)
        {
            int n = x.Size;
            var re = new double[n]; var im = new double[n];
            Array.Copy(x.Data, re, n);
            if (x.Imag != null) Array.Copy(x.Imag, im, n);
            if ((n & (n - 1)) == 0 && n > 0) Radix2(re, im, inverse); else Dft(re, im, inverse);
            if (inverse) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
            return new PyNdArray(re, new[] { n }) { Imag = im };
        }
        private static PyNdArray Rfft(PyNdArray x)
        {
            var full = Fft(x, false); int nout = x.Size / 2 + 1;
            var re = new double[nout]; var im = new double[nout];
            Array.Copy(full.Data, re, nout); Array.Copy(full.Imag, im, nout);
            return new PyNdArray(re, new[] { nout }) { Imag = im };
        }
        private static object FftFreq(int n, double d)
        {
            var f = new double[n]; int half = (n - 1) / 2 + 1;
            for (int i = 0; i < half; i++) f[i] = i / (n * d);
            for (int i = half; i < n; i++) f[i] = (i - n) / (n * d);
            return new PyNdArray(f, new[] { n });
        }
        private static object RfftFreq(int n, double d)
        {
            int nout = n / 2 + 1; var f = new double[nout];
            for (int i = 0; i < nout; i++) f[i] = i / (n * d);
            return new PyNdArray(f, new[] { nout });
        }

        // radix-2 iterativo (bit-reversal + mariposas)
        private static void Radix2(double[] re, double[] im, bool inv)
        {
            int n = re.Length; double sign = inv ? 1 : -1;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = sign * 2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                        double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                    }
                }
            }
        }
        private static void Dft(double[] re, double[] im, bool inv)
        {
            int n = re.Length; var or_ = new double[n]; var oi = new double[n]; double sign = inv ? 1 : -1;
            for (int k = 0; k < n; k++)
            {
                double sr = 0, si = 0;
                for (int t = 0; t < n; t++)
                {
                    double ang = sign * 2 * Math.PI * k * t / n, c = Math.Cos(ang), s = Math.Sin(ang);
                    sr += re[t] * c - im[t] * s; si += re[t] * s + im[t] * c;
                }
                or_[k] = sr; oi[k] = si;
            }
            Array.Copy(or_, re, n); Array.Copy(oi, im, n);
        }
    }
}
