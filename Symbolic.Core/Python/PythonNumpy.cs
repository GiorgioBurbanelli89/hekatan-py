// =============================================================================
// Hekatan Python3 — numpy EMBEBIDO (nativo en C#)
// =============================================================================
//   Implementa `import numpy as np` SIN Python externo: una clase matriz
//   (PyNdArray) + el módulo `numpy` con su API (array, zeros, eye, .T, @,
//   outer, trace, linalg.solve, indexado A[i,j], slices A[i,:]…), enchufada a
//   los motores que ya trae Suite Py:
//     • BlasInterop.MatMul  (OpenBLAS DGEMM)  → producto matricial A @ B
//     • LapackInterop.Solve (OpenBLAS DGESV)  → np.linalg.solve(K, F)
//   El SCRIPT no cambia: el mismo .py corre en IDLE con numpy real. Acá el
//   motor C# intercepta el import y provee un numpy compatible (subconjunto FEM).
//
//   Soporta arrays 1D y 2D (suficiente para el método de rigidez). Lo no
//   implementado lanza PythonNotSupported → el pipeline cae a python real.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Calcpad.Core.Python
{
    /// <summary>Espec de un índice por eje: entero `A[i]` o slice `A[i:j]` / `A[:]`.</summary>
    public struct NdSpec
    {
        public bool Slice;
        public long Idx;        // si !Slice
        public long? Lo, Hi;    // si Slice (null = abierto)
        public long Step;
    }

    /// <summary>Array N-dimensional (1D/2D) en row-major, estilo numpy.ndarray.</summary>
    // Resultado de np.ix_(rows, cols): usado en A[np.ix_(r,c)] para armar una submatriz.
    public sealed class PyIxGrid
    {
        public int[] Rows; public int[] Cols;
        public PyIxGrid(int[] r, int[] c) { Rows = r; Cols = c; }
    }

    public sealed class PyNdArray
    {
        public double[] Data;   // row-major (parte real)
        public double[] Imag;   // parte imaginaria (null = array real). Para FFT/complejos.
        public int[] Shape;     // longitud 1 (vector) o 2 (matriz)
        public bool IsInt;      // dtype entero (solo afecta display/lectura escalar)
        public bool IsComplex => Imag != null;

        public int Ndim => Shape.Length;
        public int Rows => Shape[0];
        public int Cols => Shape.Length > 1 ? Shape[1] : 1;
        public int Size => Data.Length;

        public PyNdArray(double[] data, int[] shape, bool isInt = false)
        { Data = data; Shape = shape; IsInt = isInt; }

        public static int Norm(long idx, int len)
        {
            long i = idx < 0 ? len + idx : idx;
            if (i < 0 || i >= len) throw new PyRuntimeError("IndexError", "index out of bounds");
            return (int)i;
        }

        private object Wrap(double v) => IsInt ? (object)(long)Math.Round(v) : v;

        public PyNdArray Copy() => new((double[])Data.Clone(), (int[])Shape.Clone(), IsInt);

        public PyNdArray Transpose()
        {
            if (Ndim == 1) return Copy();           // numpy: 1D.T == 1D
            int r = Rows, c = Cols;
            var d = new double[Data.Length];
            for (int i = 0; i < r; i++)
                for (int j = 0; j < c; j++)
                    d[j * r + i] = Data[i * c + j];
            return new PyNdArray(d, new[] { c, r }, IsInt);
        }

        // ── lectura A[...] ──
        public object Get(List<NdSpec> s)
        {
            if (Ndim == 1)
            {
                var sp = s[0];
                if (!sp.Slice) return Wrap(Data[Norm(sp.Idx, Shape[0])]);
                return SliceVec(sp);
            }
            // 2D
            if (s.Count == 1)
            {
                var sp = s[0];
                if (!sp.Slice) return Row(Norm(sp.Idx, Rows));
                return Copy();
            }
            var s0 = s[0]; var s1 = s[1];
            if (!s0.Slice && !s1.Slice) return Wrap(Data[Norm(s0.Idx, Rows) * Cols + Norm(s1.Idx, Cols)]);
            if (!s0.Slice && s1.Slice) return Row(Norm(s0.Idx, Rows));
            if (s0.Slice && !s1.Slice) return Col(Norm(s1.Idx, Cols));
            return Copy();
        }

        private PyNdArray Row(int r)
        {
            var d = new double[Cols];
            Array.Copy(Data, r * Cols, d, 0, Cols);
            return new PyNdArray(d, new[] { Cols }, IsInt);
        }
        private PyNdArray Col(int c)
        {
            var d = new double[Rows];
            for (int i = 0; i < Rows; i++) d[i] = Data[i * Cols + c];
            return new PyNdArray(d, new[] { Rows }, IsInt);
        }
        // ── indexado avanzado: A[intarray] (gather 1D) y A[np.ix_(r,c)] (submatriz 2D) ──
        public PyNdArray Gather(int[] idx)
        {
            // numpy: A[intarray] sobre una MATRIZ toma FILAS enteras (resultado len(idx) x Cols).
            // Antes se tomaban elementos sueltos del Data plano: `XY[nd]` devolvia basura
            // (medido con el talud Demo04: dJ=0 en todos los elementos).
            if (Ndim == 2) return GatherRows(idx);
            var d = new double[idx.Length];
            int n = Shape[0];
            for (int k = 0; k < idx.Length; k++) d[k] = Data[Norm(idx[k], n)];
            return new PyNdArray(d, new[] { idx.Length }, IsInt);
        }
        public PyNdArray GatherRows(int[] rows)
        {
            int R = rows.Length, C = Cols;
            var d = new double[R * C];
            for (int i = 0; i < R; i++)
            {
                int ri = Norm(rows[i], Rows);
                Array.Copy(Data, ri * C, d, i * C, C);
            }
            return new PyNdArray(d, new[] { R, C }, IsInt);
        }
        public PyNdArray Submatrix(int[] rows, int[] cols)
        {
            int R = rows.Length, C = cols.Length;
            var d = new double[R * C];
            for (int i = 0; i < R; i++)
            {
                int ri = Norm(rows[i], Rows);
                for (int j = 0; j < C; j++) d[i * C + j] = Data[ri * Cols + Norm(cols[j], Cols)];
            }
            return new PyNdArray(d, new[] { R, C }, IsInt);
        }
        private PyNdArray SliceVec(NdSpec sp)
        {
            int n = Shape[0];
            long step = sp.Step == 0 ? 1 : sp.Step;
            int lo = (int)(sp.Lo ?? (step > 0 ? 0 : n - 1));
            int hi = (int)(sp.Hi ?? (step > 0 ? n : -1));
            if (lo < 0) lo += n; if (hi < 0 && sp.Hi != null) hi += n;
            var res = new List<double>();
            if (step > 0) for (int i = lo; i < hi; i += (int)step) { if (i >= 0 && i < n) res.Add(Data[i]); }
            else for (int i = lo; i > hi; i += (int)step) { if (i >= 0 && i < n) res.Add(Data[i]); }
            return new PyNdArray(res.ToArray(), new[] { res.Count }, IsInt);
        }

        // ── escritura avanzada: A[np.ix_(r,c)] = block , A[intarray] = vec (scatter) ──
        public void SetSubmatrix(int[] rows, int[] cols, object value)
        {
            int nr = rows.Length, nc = cols.Length;
            if (value is PyNdArray b && b.Data.Length == nr * nc)
                for (int i = 0; i < nr; i++) for (int j = 0; j < nc; j++)
                    Data[Norm(rows[i], Rows) * Cols + Norm(cols[j], Cols)] = b.Data[i * nc + j];
            else { double v = PyOps.ToDouble(value); for (int i = 0; i < nr; i++) for (int j = 0; j < nc; j++) Data[Norm(rows[i], Rows) * Cols + Norm(cols[j], Cols)] = v; }
        }
        public void Scatter(int[] idx, object value)
        {
            int n = Shape[0];
            if (value is PyNdArray b && b.Data.Length == idx.Length)
                for (int k = 0; k < idx.Length; k++) Data[Norm(idx[k], n)] = b.Data[k];
            else { double v = PyOps.ToDouble(value); for (int k = 0; k < idx.Length; k++) Data[Norm(idx[k], n)] = v; }
        }

        // ── escritura A[...] = value ──
        public void Set(List<NdSpec> s, object value)
        {
            if (Ndim == 1)
            {
                var sp = s[0];
                if (!sp.Slice) { Data[Norm(sp.Idx, Shape[0])] = PyOps.ToDouble(value); return; }
                var vec = ToVec(value, Shape[0]);
                for (int i = 0; i < Shape[0]; i++) Data[i] = vec[i];
                return;
            }
            if (s.Count == 2)
            {
                var s0 = s[0]; var s1 = s[1];
                if (!s0.Slice && !s1.Slice) { Data[Norm(s0.Idx, Rows) * Cols + Norm(s1.Idx, Cols)] = PyOps.ToDouble(value); return; }
                if (!s0.Slice && s1.Slice) { SetRow(Norm(s0.Idx, Rows), value); return; }
                if (s0.Slice && !s1.Slice) { SetCol(Norm(s1.Idx, Cols), value); return; }
                FillAll(value); return;
            }
            // 2D con un solo índice
            var sp1 = s[0];
            if (!sp1.Slice) { SetRow(Norm(sp1.Idx, Rows), value); return; }
            FillAll(value);
        }

        private void SetRow(int r, object value)
        {
            var vec = ToVec(value, Cols);
            for (int j = 0; j < Cols; j++) Data[r * Cols + j] = vec[j];
        }
        private void SetCol(int c, object value)
        {
            var vec = ToVec(value, Rows);
            for (int i = 0; i < Rows; i++) Data[i * Cols + c] = vec[i];
        }
        private void FillAll(object value)
        {
            if (value is PyNdArray a) { Array.Copy(a.Data, Data, Math.Min(a.Data.Length, Data.Length)); return; }
            double v = PyOps.ToDouble(value);
            for (int i = 0; i < Data.Length; i++) Data[i] = v;
        }

        /// <summary>Convierte value (escalar broadcast / PyNdArray / PyList) a un vector de n.</summary>
        private static double[] ToVec(object value, int n)
        {
            var d = new double[n];
            switch (value)
            {
                case PyNdArray a:
                    if (a.Data.Length != n) throw new PyRuntimeError("ValueError", "could not broadcast input into row/col");
                    Array.Copy(a.Data, d, n); break;
                case PyList l:
                    if (l.Count != n) throw new PyRuntimeError("ValueError", "could not broadcast list into row/col");
                    for (int i = 0; i < n; i++) d[i] = PyOps.ToDouble(l.Items[i]); break;
                case PyTuple t:
                    for (int i = 0; i < n; i++) d[i] = PyOps.ToDouble(t.Items[i]); break;
                default:
                    double v = PyOps.ToDouble(value);
                    for (int i = 0; i < n; i++) d[i] = v; break;
            }
            return d;
        }

        // ── str / repr (con truncado para matrices grandes) ──
        public override string ToString()
        {
            if (Data.Length > 200)
                return $"array(shape=({string.Join(",", Shape)}), dtype={(IsInt ? "int64" : "float64")})";
            var sb = new StringBuilder("array(");
            if (Ndim == 1) { sb.Append('['); for (int i = 0; i < Cols * 0 + Data.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(Fmt(Data[i])); } sb.Append(']'); }
            else
            {
                sb.Append('[');
                for (int i = 0; i < Rows; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append('[');
                    for (int j = 0; j < Cols; j++) { if (j > 0) sb.Append(", "); sb.Append(Fmt(Data[i * Cols + j])); }
                    sb.Append(']');
                }
                sb.Append(']');
            }
            return sb.Append(')').ToString();
        }
        private string Fmt(double v) => IsInt ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
                                              : PyOps.FormatFloat(v);
    }

    // =========================================================================
    //  Módulo `numpy` + operadores
    // =========================================================================
    public static class PyNumpy
    {
        // ── construcción del módulo ──
        public static PyModule CreateModule()
        {
            var m = new PyModule("numpy");
            void Reg(string name, Func<object[], PyDict, object> fn) => m.Attrs[name] = new PyBuiltin(name, fn);

            m.Attrs["pi"] = Math.PI;
            m.Attrs["e"] = Math.E;
            m.Attrs["inf"] = double.PositiveInfinity;
            m.Attrs["nan"] = double.NaN;
            m.Attrs["newaxis"] = null;
            m.Attrs["float64"] = "float64"; m.Attrs["float_"] = "float64";
            m.Attrs["int64"] = "int64"; m.Attrs["int_"] = "int64";

            Reg("array", (a, kw) => FromObject(a[0], Dtype(a, 1, kw)));
            Reg("asarray", (a, kw) => a[0] is PyNdArray nd ? nd : FromObject(a[0], Dtype(a, 1, kw)));
            Reg("zeros", (a, kw) => Filled(ShapeOf(a[0]), 0.0, IntDtype(Dtype(a, 1, kw))));
            Reg("ones", (a, kw) => Filled(ShapeOf(a[0]), 1.0, IntDtype(Dtype(a, 1, kw))));
            Reg("full", (a, kw) => Filled(ShapeOf(a[0]), PyOps.ToDouble(a[1]), false));
            Reg("zeros_like", (a, kw) => Filled((int[])AsArr(a[0]).Shape.Clone(), 0.0, AsArr(a[0]).IsInt));
            Reg("ones_like", (a, kw) => Filled((int[])AsArr(a[0]).Shape.Clone(), 1.0, AsArr(a[0]).IsInt));
            Reg("eye", (a, kw) => Eye((int)PyOps.ToLong(a[0])));
            Reg("identity", (a, kw) => Eye((int)PyOps.ToLong(a[0])));
            Reg("outer", (a, kw) => Outer(AsArr(a[0]), AsArr(a[1])));
            Reg("dot", (a, kw) => MatMul(a[0], a[1]));
            Reg("matmul", (a, kw) => MatMul(a[0], a[1]));
            Reg("trace", (a, kw) => Trace(AsArr(a[0])));
            Reg("transpose", (a, kw) => AsArr(a[0]).Transpose());
            Reg("diag", (a, kw) => Diag(AsArr(a[0]), a.Length > 1 ? (int)PyOps.ToLong(a[1]) : 0));
            Reg("arange", (a, kw) => Arange(a));
            Reg("linspace", (a, kw) => Linspace(a, kw));
            Reg("sum", (a, kw) => Sum(AsArr(a[0])));
            Reg("abs", (a, kw) => CAbs(a[0]));
            Reg("absolute", (a, kw) => CAbs(a[0]));
            Reg("real", (a, kw) => { var z = AsArr(a[0]); return new PyNdArray((double[])z.Data.Clone(), (int[])z.Shape.Clone()); });
            Reg("imag", (a, kw) => { var z = AsArr(a[0]); return new PyNdArray(z.Imag != null ? (double[])z.Imag.Clone() : new double[z.Size], (int[])z.Shape.Clone()); });
            Reg("angle", (a, kw) => { var z = AsArr(a[0]); var r = new double[z.Size]; for (int i = 0; i < z.Size; i++) r[i] = Math.Atan2(z.Imag != null ? z.Imag[i] : 0, z.Data[i]); return new PyNdArray(r, (int[])z.Shape.Clone()); });
            Reg("conj", (a, kw) => { var z = AsArr(a[0]); var im = z.Imag != null ? new double[z.Size] : null; if (im != null) for (int i = 0; i < z.Size; i++) im[i] = -z.Imag[i]; return new PyNdArray((double[])z.Data.Clone(), (int[])z.Shape.Clone()) { Imag = im }; });
            Reg("sqrt", (a, kw) => UFunc(a[0], Math.Sqrt));
            Reg("exp", (a, kw) => UFunc(a[0], Math.Exp));
            Reg("log", (a, kw) => UFunc(a[0], Math.Log));
            Reg("sin", (a, kw) => UFunc(a[0], Math.Sin));
            Reg("cos", (a, kw) => UFunc(a[0], Math.Cos));
            Reg("tan", (a, kw) => UFunc(a[0], Math.Tan));
            Reg("floor", (a, kw) => UFunc(a[0], Math.Floor));
            Reg("ceil", (a, kw) => UFunc(a[0], Math.Ceiling));
            Reg("round", (a, kw) => RoundTo(a, kw));
            Reg("around", (a, kw) => RoundTo(a, kw));
            Reg("sign", (a, kw) => UFunc(a[0], v => (double)Math.Sign(v)));
            Reg("deg2rad", (a, kw) => UFunc(a[0], v => v * Math.PI / 180.0));
            Reg("rad2deg", (a, kw) => UFunc(a[0], v => v * 180.0 / Math.PI));
            Reg("radians", (a, kw) => UFunc(a[0], v => v * Math.PI / 180.0));   // alias numpy
            Reg("degrees", (a, kw) => UFunc(a[0], v => v * 180.0 / Math.PI));
            // reducciones
            Reg("max", (a, kw) => Reduce(AsArr(a[0]), Math.Max));
            Reg("amax", (a, kw) => Reduce(AsArr(a[0]), Math.Max));
            Reg("nanmax", (a, kw) => Reduce(AsArr(a[0]), Math.Max));
            Reg("min", (a, kw) => Reduce(AsArr(a[0]), Math.Min));
            Reg("amin", (a, kw) => Reduce(AsArr(a[0]), Math.Min));
            Reg("nanmin", (a, kw) => Reduce(AsArr(a[0]), Math.Min));
            Reg("mean", (a, kw) => { var x = AsArr(a[0]); double s = 0; foreach (var v in x.Data) s += v; return s / x.Data.Length; });
            Reg("prod", (a, kw) => { var x = AsArr(a[0]); double s = 1; foreach (var v in x.Data) s *= v; return s; });
            Reg("argmin", (a, kw) => ArgReduce(AsArr(a[0]), true));
            Reg("argmax", (a, kw) => ArgReduce(AsArr(a[0]), false));
            // elementwise binarias
            Reg("maximum", (a, kw) => EwiseBin(a[0], a[1], Math.Max));
            Reg("minimum", (a, kw) => EwiseBin(a[0], a[1], Math.Min));
            Reg("power", (a, kw) => EwiseBin(a[0], a[1], Math.Pow));
            Reg("clip", (a, kw) => Clip(AsArr(a[0]), a.Length > 1 ? a[1] : null, a.Length > 2 ? a[2] : null));
            // forma / combinación
            Reg("interp", (a, kw) => Interp(a[0], AsArr(a[1]), AsArr(a[2])));
            Reg("meshgrid", (a, kw) => Meshgrid(a, kw));
            Reg("column_stack", (a, kw) => ColumnStack(a[0]));
            Reg("vstack", (a, kw) => VStack(a[0]));
            Reg("hstack", (a, kw) => HStack(a[0]));
            Reg("concatenate", (a, kw) => HStack(a[0]));
            Reg("ravel", (a, kw) => { var x = AsArr(a[0]); return new PyNdArray((double[])x.Data.Clone(), new[] { x.Size }, x.IsInt); });
            Reg("flatten", (a, kw) => { var x = AsArr(a[0]); return new PyNdArray((double[])x.Data.Clone(), new[] { x.Size }, x.IsInt); });
            Reg("cumsum", (a, kw) => { var x = AsArr(a[0]); var d = new double[x.Size]; double s = 0; for (int i = 0; i < x.Size; i++) { s += x.Data[i]; d[i] = s; } return new PyNdArray(d, new[] { x.Size }); });
            Reg("diff", (a, kw) => { var x = AsArr(a[0]); int n = x.Size - 1; var d = new double[n < 0 ? 0 : n]; for (int i = 0; i < n; i++) d[i] = x.Data[i + 1] - x.Data[i]; return new PyNdArray(d, new[] { d.Length }); });
            Reg("isnan", (a, kw) => UFunc(a[0], v => double.IsNaN(v) ? 1.0 : 0.0));
            // Faltaban (medido con el talud Demo04 en el motor nativo, 2026-09-02):
            Reg("isfinite", (a, kw) => UFunc(a[0], v => double.IsFinite(v) ? 1.0 : 0.0));
            Reg("isinf", (a, kw) => UFunc(a[0], v => double.IsInfinity(v) ? 1.0 : 0.0));
            Reg("arctan", (a, kw) => UFunc(a[0], Math.Atan));
            Reg("arcsin", (a, kw) => UFunc(a[0], Math.Asin));
            Reg("arccos", (a, kw) => UFunc(a[0], Math.Acos));
            Reg("arctan2", (a, kw) => EwiseBin(a[0], a[1], Math.Atan2));
            Reg("hypot", (a, kw) => EwiseBin(a[0], a[1], (x, y) => Math.Sqrt(x * x + y * y)));
            Reg("all", (a, kw) => { var z = AsArr(a[0]); foreach (var v in z.Data) if (v == 0.0) return (object)false; return (object)true; });
            Reg("any", (a, kw) => { var z = AsArr(a[0]); foreach (var v in z.Data) if (v != 0.0) return (object)true; return (object)false; });
            Reg("unravel_index", (a, kw) => Unravel(a));
            // conjuntos e indexado avanzado (para ensamble FEM: free = setdiff1d(...), K[np.ix_(free,free)])
            Reg("setdiff1d", (a, kw) => Setdiff1d(AsArr(a[0]), AsArr(a[1])));
            Reg("unique", (a, kw) => { var set = new SortedSet<double>(AsArr(a[0]).Data); var d = new double[set.Count]; int k = 0; foreach (var x in set) d[k++] = x; return new PyNdArray(d, new[] { d.Length }, AsArr(a[0]).IsInt); });
            Reg("ix_", (a, kw) => new PyIxGrid(ToIntArr(AsArr(a[0])), ToIntArr(AsArr(a[1]))));
            // I/O de texto: cargar/guardar matrices numéricas (registros sísmicos, CSV, etc.)
            Reg("loadtxt", (a, kw) => Loadtxt(a, kw));
            Reg("genfromtxt", (a, kw) => Loadtxt(a, kw));
            Reg("savetxt", (a, kw) => { Savetxt(a, kw); return null; });

            // submódulo linalg
            var linalg = new PyModule("numpy.linalg");
            linalg.Attrs["solve"] = new PyBuiltin("solve", (a, kw) => Solve(AsArr(a[0]), AsArr(a[1])));
            // spsolve(rows, cols, vals, b): resuelve sistema DISPERSO simetrico A*x=b (COO, escala FEM
            // STKO/Abaqus) via Eigen SimplicialLDLT/SparseLU nativo. n = len(b).
            linalg.Attrs["spsolve"] = new PyBuiltin("spsolve", (a, kw) => SpSolve(AsArr(a[0]), AsArr(a[1]), AsArr(a[2]), AsArr(a[3])));
            linalg.Attrs["norm"] = new PyBuiltin("norm", (a, kw) => Norm(AsArr(a[0])));
            linalg.Attrs["inv"] = new PyBuiltin("inv", (a, kw) => Inv(AsArr(a[0])));
            linalg.Attrs["det"] = new PyBuiltin("det", (a, kw) => Det(AsArr(a[0])));
            linalg.Attrs["eigh"] = new PyBuiltin("eigh", (a, kw) => Eigh(AsArr(a[0])));
            linalg.Attrs["eigvalsh"] = new PyBuiltin("eigvalsh", (a, kw) => Eigvalsh(AsArr(a[0])));
            linalg.Attrs["eig"] = new PyBuiltin("eig", (a, kw) => Eigh(AsArr(a[0])));          // simétrica: eig ≡ eigh
            linalg.Attrs["eigvals"] = new PyBuiltin("eigvals", (a, kw) => Eigvalsh(AsArr(a[0])));
            m.Attrs["linalg"] = linalg;
            m.Attrs["fft"] = PythonFFT.Module("numpy.fft");   // FFT embebida
            return m;
        }

        private static int[] ToIntArr(PyNdArray a)
        {
            var r = new int[a.Data.Length];
            for (int i = 0; i < r.Length; i++) r[i] = (int)Math.Round(a.Data[i]);
            return r;
        }
        private static string KwStr(PyDict kw, string key)
        {
            if (kw != null && kw.TryGet(key, out var v) && v is string s) return s;
            return null;
        }
        private static object Loadtxt(object[] a, PyDict kw)
        {
            string path = a[0] as string ?? a[0]?.ToString();
            var dl = KwStr(kw, "delimiter");
            char[] sep = !string.IsNullOrEmpty(dl) ? dl.ToCharArray() : new[] { ' ', '\t', ',', ';' };
            var comments = KwStr(kw, "comments") ?? "#";
            var lines = System.IO.File.ReadAllLines(path);
            var rows = new List<double[]>(); int ncols = -1;
            foreach (var line in lines)
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith(comments)) continue;
                var toks = t.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                var row = new double[toks.Length]; bool ok = true;
                for (int k = 0; k < toks.Length; k++)
                    if (!double.TryParse(toks[k], NumberStyles.Any, CultureInfo.InvariantCulture, out row[k])) { ok = false; break; }
                if (!ok) continue;
                if (ncols < 0) ncols = row.Length;
                if (row.Length == ncols) rows.Add(row);
            }
            if (rows.Count == 0) return new PyNdArray(new double[0], new[] { 0 });
            if (ncols == 1) { var d = new double[rows.Count]; for (int i = 0; i < rows.Count; i++) d[i] = rows[i][0]; return new PyNdArray(d, new[] { rows.Count }); }
            if (rows.Count == 1) return new PyNdArray(rows[0], new[] { ncols });
            var data = new double[rows.Count * ncols];
            for (int i = 0; i < rows.Count; i++) for (int j = 0; j < ncols; j++) data[i * ncols + j] = rows[i][j];
            return new PyNdArray(data, new[] { rows.Count, ncols });
        }
        private static string FmtNum(double v, string fmt)
        {
            if (string.IsNullOrEmpty(fmt)) return v.ToString("G", CultureInfo.InvariantCulture);
            // traduce fmt estilo numpy "%.6f"/"%.4e"/"%g" a .NET
            var m = System.Text.RegularExpressions.Regex.Match(fmt, @"%\.?(\d+)?([feEgG])");
            if (m.Success)
            {
                int prec = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 6;
                char c = m.Groups[2].Value[0];
                string net = c == 'f' ? "F" + prec : (c == 'e' || c == 'E') ? "E" + prec : "G";
                return v.ToString(net, CultureInfo.InvariantCulture);
            }
            return v.ToString("G", CultureInfo.InvariantCulture);
        }
        private static void Savetxt(object[] a, PyDict kw)
        {
            string path = a[0] as string ?? a[0]?.ToString();
            var arr = AsArr(a[1]);
            string delim = KwStr(kw, "delimiter") ?? " ";
            string fmt = KwStr(kw, "fmt");
            int rows = arr.Ndim == 1 ? arr.Size : arr.Rows;
            int cols = arr.Ndim == 1 ? 1 : arr.Cols;
            var sb = new StringBuilder();
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (j > 0) sb.Append(delim);
                    double val = arr.Ndim == 1 ? arr.Data[i] : arr.Data[i * arr.Cols + j];
                    sb.Append(FmtNum(val, fmt));
                }
                sb.Append('\n');
            }
            System.IO.File.WriteAllText(path, sb.ToString());
        }
        private static PyNdArray Setdiff1d(PyNdArray a, PyNdArray b)
        {
            var setB = new HashSet<double>(b.Data);
            var diff = new SortedSet<double>();
            foreach (var x in a.Data) if (!setB.Contains(x)) diff.Add(x);
            var d = new double[diff.Count]; int k = 0; foreach (var x in diff) d[k++] = x;
            return new PyNdArray(d, new[] { d.Length }, true);   // índices → entero
        }

        // ── atributos/métodos de una PyNdArray (lo llama el evaluador en GetAttr) ──
        public static object GetAttr(PyNdArray arr, string name)
        {
            switch (name)
            {
                case "T": return arr.Transpose();
                case "shape": return new PyTuple(ShapeTuple(arr));
                case "ndim": return (long)arr.Ndim;
                case "size": return (long)arr.Size;
                case "dtype": return arr.IsInt ? "int64" : "float64";
                case "copy": return new PyBuiltin("copy", (a, kw) => arr.Copy());
                case "transpose": return new PyBuiltin("transpose", (a, kw) => arr.Transpose());
                case "ravel": case "flatten":
                    return new PyBuiltin(name, (a, kw) => new PyNdArray((double[])arr.Data.Clone(), new[] { arr.Size }, arr.IsInt));
                case "reshape": return new PyBuiltin("reshape", (a, kw) => Reshape(arr, a));
                case "sum": return new PyBuiltin("sum", (a, kw) => Sum(arr));
                case "max": return new PyBuiltin("max", (a, kw) => Reduce(arr, Math.Max));
                case "min": return new PyBuiltin("min", (a, kw) => Reduce(arr, Math.Min));
                case "mean": return new PyBuiltin("mean", (a, kw) => Sum(arr) is double sd ? sd / arr.Size : 0.0);
                case "tolist": return new PyBuiltin("tolist", (a, kw) => ToList(arr));
                case "copy_": return arr.Copy();
                case "astype": return new PyBuiltin("astype", (a, kw) =>
                    new PyNdArray((double[])arr.Data.Clone(), (int[])arr.Shape.Clone(), IntDtype(a.Length > 0 ? a[0] : null)));
                default:
                    throw new PyRuntimeError("AttributeError", $"'numpy.ndarray' object has no attribute '{name}'");
            }
        }

        private static List<object> ShapeTuple(PyNdArray a)
        {
            var l = new List<object>();
            foreach (var s in a.Shape) l.Add((long)s);
            return l;
        }

        // ── operadores (los llama PyOps) ──
        public static object Binary(string op, object a, object b)
        {
            bool aArr = a is PyNdArray, bArr = b is PyNdArray;
            if (aArr && bArr)
            {
                var x = (PyNdArray)a; var y = (PyNdArray)b;
                if (x.Data.Length != y.Data.Length || x.Ndim != y.Ndim)
                    throw new PyRuntimeError("ValueError", $"operands could not be broadcast together (shapes distintas)");
                var d = new double[x.Data.Length];
                for (int i = 0; i < d.Length; i++) d[i] = Apply(op, x.Data[i], y.Data[i]);
                return new PyNdArray(d, (int[])x.Shape.Clone());
            }
            if (aArr && PyOps.IsNumber(b))
            {
                var x = (PyNdArray)a; double s = PyOps.ToDouble(b);
                var d = new double[x.Data.Length];
                for (int i = 0; i < d.Length; i++) d[i] = Apply(op, x.Data[i], s);
                return new PyNdArray(d, (int[])x.Shape.Clone());
            }
            if (PyOps.IsNumber(a) && bArr)
            {
                var y = (PyNdArray)b; double s = PyOps.ToDouble(a);
                var d = new double[y.Data.Length];
                for (int i = 0; i < d.Length; i++) d[i] = Apply(op, s, y.Data[i]);
                return new PyNdArray(d, (int[])y.Shape.Clone());
            }
            throw new PyRuntimeError("TypeError", $"operación '{op}' no soportada con numpy.ndarray y {PyOps.TypeName(aArr ? b : a)}");
        }

        private static double Apply(string op, double x, double y) => op switch
        {
            "+" => x + y,
            "-" => x - y,
            "*" => x * y,
            "/" => y == 0 ? throw new PyRuntimeError("ZeroDivisionError", "division by zero") : x / y,
            _ => throw new PyRuntimeError("TypeError", $"operador '{op}' no soportado en ndarray")
        };

        public static object Negate(PyNdArray a)
        {
            var d = new double[a.Data.Length];
            for (int i = 0; i < d.Length; i++) d[i] = -a.Data[i];
            return new PyNdArray(d, (int[])a.Shape.Clone(), a.IsInt);
        }

        // ── producto matricial A @ B ──
        public static object MatMul(object A, object B)
        {
            var a = AsArr(A); var b = AsArr(B);
            if (a.Ndim == 2 && b.Ndim == 2)
            {
                int m = a.Rows, k = a.Cols, n = b.Cols;
                if (b.Rows != k) throw new PyRuntimeError("ValueError", $"matmul: {a.Cols} vs {b.Rows} no coinciden");
                var c = new double[m * n];
                Calcpad.Core.BlasInterop.MatMul(m, k, n, a.Data, b.Data, c);
                return new PyNdArray(c, new[] { m, n });
            }
            if (a.Ndim == 2 && b.Ndim == 1)
            {
                int m = a.Rows, k = a.Cols;
                if (b.Size != k) throw new PyRuntimeError("ValueError", "matmul dim mismatch");
                var c = new double[m];
                for (int i = 0; i < m; i++) { double s = 0; for (int j = 0; j < k; j++) s += a.Data[i * k + j] * b.Data[j]; c[i] = s; }
                return new PyNdArray(c, new[] { m });
            }
            if (a.Ndim == 1 && b.Ndim == 2)
            {
                int k = b.Rows, n = b.Cols;
                if (a.Size != k) throw new PyRuntimeError("ValueError", "matmul dim mismatch");
                var c = new double[n];
                for (int j = 0; j < n; j++) { double s = 0; for (int i = 0; i < k; i++) s += a.Data[i] * b.Data[i * n + j]; c[j] = s; }
                return new PyNdArray(c, new[] { n });
            }
            // 1D · 1D → escalar
            if (a.Size != b.Size) throw new PyRuntimeError("ValueError", "matmul dim mismatch");
            double dot = 0; for (int i = 0; i < a.Size; i++) dot += a.Data[i] * b.Data[i];
            return dot;
        }

        // ── linalg.solve(A, b) → enchufa a OpenBLAS (DGESV) con fallback Gauss ──
        // Resolver DISPERSO simetrico A*x=b desde COO (rows,cols,vals) via Eigen nativo (skyline ->
        // SimplicialLDLT, fallback SparseLU). Escala a FEM grande (STKO/Abaqus) sin matriz densa.
        public static PyNdArray SpSolve(PyNdArray rows, PyNdArray cols, PyNdArray vals, PyNdArray b)
        {
            int n = b.Size, nnz = rows.Size;
            if (cols.Size != nnz || vals.Size != nnz)
                throw new PyRuntimeError("ValueError", "spsolve: rows, cols, vals deben tener el mismo largo");
            // ensambla (suma contribuciones) el triangulo SUPERIOR por fila: col >= fila
            var rowUpper = new System.Collections.Generic.Dictionary<int, double>[n];
            for (int i = 0; i < n; i++) rowUpper[i] = new System.Collections.Generic.Dictionary<int, double>();
            for (int e = 0; e < nnz; e++)
            {
                int r = (int)rows.Data[e], c = (int)cols.Data[e];
                if (r < 0 || r >= n || c < 0 || c >= n) continue;
                if (r > c) continue;   // matriz simetrica: solo triangulo superior; el inferior es espejo (evita doblar)
                rowUpper[r][c] = rowUpper[r].TryGetValue(c, out var ex) ? ex + vals.Data[e] : vals.Data[e];
            }
            // formato skyline del solver: por fila i, rowSizes[i] entradas contiguas desde la diagonal
            var rowSizes = new int[n];
            var valsList = new System.Collections.Generic.List<double>();
            for (int i = 0; i < n; i++)
            {
                int maxc = i;
                foreach (var kv in rowUpper[i]) if (kv.Key > maxc) maxc = kv.Key;
                int size = maxc - i + 1; rowSizes[i] = size;
                for (int k = 0; k < size; k++) valsList.Add(rowUpper[i].TryGetValue(i + k, out var vv) ? vv : 0.0);
            }
            var rhs = new double[n]; for (int i = 0; i < n; i++) rhs[i] = b.Data[i];
            var sol = new double[n];
            int info = EigenInterop.skyline_cholesky_solve(n, rowSizes, valsList.ToArray(), rhs, sol);
            if (info < 0) throw new PyRuntimeError("RuntimeError", $"spsolve: el solver disperso fallo (info={info})");
            return new PyNdArray(sol, new[] { n });
        }

        public static object Solve(PyNdArray A, PyNdArray b)
        {
            if (A.Ndim != 2 || A.Rows != A.Cols)
                throw new PyRuntimeError("ValueError", "solve: A debe ser cuadrada");
            int n = A.Rows;
            // b vector (n) -> x vector; b matriz (n×m) -> X matriz (resolver columna por columna, como numpy)
            if (b.Ndim == 1 || b.Cols == 1)
            {
                if (b.Size != n) throw new PyRuntimeError("ValueError", "solve: dimensiones A·x=b no coinciden");
                return new PyNdArray(SolveVec(A.Data, n, (double[])b.Data.Clone()), new[] { n });
            }
            if (b.Rows != n) throw new PyRuntimeError("ValueError", "solve: dimensiones A·x=b no coinciden");
            int m = b.Cols;
            var X = new double[n * m];
            var col = new double[n];
            for (int j = 0; j < m; j++)
            {
                for (int i = 0; i < n; i++) col[i] = b.Data[i * m + j];
                var xj = SolveVec(A.Data, n, col);
                for (int i = 0; i < n; i++) X[i * m + j] = xj[i];
            }
            return new PyNdArray(X, new[] { n, m });
        }

        // Resuelve A·x=b para UN vector b (LAPACK DGESV o Gauss). Clona internamente (destructivo).
        private static double[] SolveVec(double[] Adata, int n, double[] bvec)
        {
            double[] x = null;
            if (Calcpad.Core.LapackInterop.Available)
            {
                try
                {
                    var xl = Calcpad.Core.LapackInterop.Solve(n, (double[])Adata.Clone(), (double[])bvec.Clone());
                    if (ResidualOk(Adata, n, xl, bvec)) x = xl;   // VERIFICA: LAPACK nativo dio resultados erroneos; validar ‖Ax-b‖
                }
                catch { x = null; }
            }
            x ??= GaussSolve(n, (double[])Adata.Clone(), (double[])bvec.Clone());   // fallback correcto (Gauss con pivoteo)
            return x;
        }

        /// <summary>Verifica ‖A·x − b‖ <= tol·(1+‖b‖). A es row-major (Adata[i*n+j]).</summary>
        private static bool ResidualOk(double[] A, int n, double[] x, double[] b)
        {
            if (x == null || x.Length != n) return false;
            double res = 0, nb = 0;
            for (int i = 0; i < n; i++)
            {
                double s = 0; for (int j = 0; j < n; j++) s += A[i * n + j] * x[j];
                double e = s - b[i]; res += e * e; nb += b[i] * b[i];
            }
            return Math.Sqrt(res) <= 1e-7 * (1.0 + Math.Sqrt(nb));
        }

        // Autovalores/autovectores de matriz simétrica (Eigen SelfAdjointEigenSolver).
        // Núcleo para np.linalg.eigh / eigvalsh (base del análisis modal).
        private static (double[] vals, double[] vecs, int n) EighCore(PyNdArray A)
        {
            if (A.Ndim != 2 || A.Rows != A.Cols)
                throw new PyRuntimeError("ValueError", "eigh: A debe ser cuadrada");
            int n = A.Rows;
            if (!Calcpad.Core.EigenInterop.IsAvailable())
                throw new PyRuntimeError("LinAlgError", "eigh: eigen_solver no disponible");
            var Adata = (double[])A.Data.Clone();
            var vals = new double[n];
            var vecs = new double[n * n];
            int rc = Calcpad.Core.EigenInterop.dense_eigenvalues(n, Adata, vals, vecs);
            if (rc != 0) throw new PyRuntimeError("LinAlgError", "eigh: fallo del solver (rc=" + rc + ")");
            return (vals, vecs, n);
        }

        // np.linalg.eigh(A) → (w, v) con w ascendente y v[:,i] = autovector de w[i].
        public static object Eigh(PyNdArray A)
        {
            var (vals, vecs, n) = EighCore(A);
            return new PyTuple(new object[] { new PyNdArray(vals, new[] { n }), new PyNdArray(vecs, new[] { n, n }) });
        }

        // np.linalg.eigvalsh(A) → solo los autovalores (ascendente).
        public static object Eigvalsh(PyNdArray A)
        {
            var (vals, _, n) = EighCore(A);
            return new PyNdArray(vals, new[] { n });
        }

        // Gauss con pivoteo parcial (fallback si OpenBLAS no está disponible).
        private static double[] GaussSolve(int n, double[] A, double[] b)
        {
            for (int col = 0; col < n; col++)
            {
                int piv = col; double best = Math.Abs(A[col * n + col]);
                for (int r = col + 1; r < n; r++) { double v = Math.Abs(A[r * n + col]); if (v > best) { best = v; piv = r; } }
                if (best == 0) throw new PyRuntimeError("LinAlgError", "Singular matrix");
                if (piv != col)
                {
                    for (int j = 0; j < n; j++) (A[col * n + j], A[piv * n + j]) = (A[piv * n + j], A[col * n + j]);
                    (b[col], b[piv]) = (b[piv], b[col]);
                }
                double d = A[col * n + col];
                for (int r = col + 1; r < n; r++)
                {
                    double f = A[r * n + col] / d;
                    if (f == 0) continue;
                    for (int j = col; j < n; j++) A[r * n + j] -= f * A[col * n + j];
                    b[r] -= f * b[col];
                }
            }
            var x = new double[n];
            for (int r = n - 1; r >= 0; r--)
            {
                double s = b[r];
                for (int j = r + 1; j < n; j++) s -= A[r * n + j] * x[j];
                x[r] = s / A[r * n + r];
            }
            return x;
        }

        // ── helpers de fábrica ──
        private static PyNdArray Filled(int[] shape, double val, bool isInt)
        {
            int total = 1; foreach (var s in shape) total *= s;
            var d = new double[total];
            if (val != 0) for (int i = 0; i < total; i++) d[i] = val;
            return new PyNdArray(d, shape, isInt);
        }
        private static PyNdArray Eye(int n)
        {
            var d = new double[n * n];
            for (int i = 0; i < n; i++) d[i * n + i] = 1.0;
            return new PyNdArray(d, new[] { n, n });
        }
        private static PyNdArray Outer(PyNdArray a, PyNdArray b)
        {
            int m = a.Size, n = b.Size;
            var d = new double[m * n];
            for (int i = 0; i < m; i++) for (int j = 0; j < n; j++) d[i * n + j] = a.Data[i] * b.Data[j];
            return new PyNdArray(d, new[] { m, n });
        }
        private static object Trace(PyNdArray a)
        {
            int n = Math.Min(a.Rows, a.Cols); double s = 0;
            for (int i = 0; i < n; i++) s += a.Data[i * a.Cols + i];
            return s;
        }
        private static PyNdArray Diag(PyNdArray a, int k = 0)
        {
            if (a.Ndim == 1)
            {
                // vector -> matriz con v en la k-esima diagonal (k>0 superior, k<0 inferior). Tamaño n+|k|.
                int n = a.Size, m = n + Math.Abs(k); var d = new double[m * m];
                for (int i = 0; i < n; i++)
                {
                    int row = k >= 0 ? i : i - k;
                    int col = k >= 0 ? i + k : i;
                    d[row * m + col] = a.Data[i];
                }
                return new PyNdArray(d, new[] { m, m });
            }
            // matriz -> extrae la k-esima diagonal
            var vals = new System.Collections.Generic.List<double>();
            for (int i = 0; ; i++)
            {
                int row = k >= 0 ? i : i - k;
                int col = k >= 0 ? i + k : i;
                if (row >= a.Rows || col >= a.Cols) break;
                vals.Add(a.Data[row * a.Cols + col]);
            }
            return new PyNdArray(vals.ToArray(), new[] { vals.Count });
        }
        private static object Sum(PyNdArray a) { double s = 0; foreach (var v in a.Data) s += v; return a.IsInt ? (object)(long)Math.Round(s) : s; }
        private static object Reduce(PyNdArray a, Func<double, double, double> f)
        { double acc = a.Data[0]; for (int i = 1; i < a.Data.Length; i++) acc = f(acc, a.Data[i]); return a.IsInt ? (object)(long)Math.Round(acc) : acc; }
        private static object Norm(PyNdArray a) { double s = 0; foreach (var v in a.Data) s += v * v; return Math.Sqrt(s); }

        private static PyNdArray Arange(object[] a)
        {
            double start = 0, stop, step = 1;
            if (a.Length == 1) stop = PyOps.ToDouble(a[0]);
            else { start = PyOps.ToDouble(a[0]); stop = PyOps.ToDouble(a[1]); if (a.Length > 2) step = PyOps.ToDouble(a[2]); }
            var list = new List<double>();
            if (step > 0) for (double v = start; v < stop - 1e-12; v += step) list.Add(v);
            else for (double v = start; v > stop + 1e-12; v += step) list.Add(v);
            bool allInt = start == Math.Floor(start) && step == Math.Floor(step);
            return new PyNdArray(list.ToArray(), new[] { list.Count }, allInt);
        }
        private static PyNdArray Linspace(object[] a, PyDict kw)
        {
            double s = PyOps.ToDouble(a[0]), e = PyOps.ToDouble(a[1]);
            int n = a.Length > 2 ? (int)PyOps.ToLong(a[2]) : 50;
            var d = new double[n];
            if (n == 1) d[0] = s;
            else for (int i = 0; i < n; i++) d[i] = s + (e - s) * i / (n - 1);
            return new PyNdArray(d, new[] { n });
        }
        private static PyNdArray Reshape(PyNdArray a, object[] args)
        {
            int[] shape = args.Length == 1 ? ShapeOf(args[0]) : ShapeOfArgs(args);
            int total = 1; foreach (var s in shape) total *= s;
            if (total != a.Size) throw new PyRuntimeError("ValueError", "cannot reshape: tamaño incompatible");
            return new PyNdArray((double[])a.Data.Clone(), shape, a.IsInt);
        }
        private static object ToList(PyNdArray a)
        {
            if (a.Ndim == 1)
            {
                var l = new PyList();
                foreach (var v in a.Data) l.Items.Add(a.IsInt ? (object)(long)Math.Round(v) : v);
                return l;
            }
            var outer = new PyList();
            for (int i = 0; i < a.Rows; i++)
            {
                var row = new PyList();
                for (int j = 0; j < a.Cols; j++) row.Items.Add(a.IsInt ? (object)(long)Math.Round(a.Data[i * a.Cols + j]) : a.Data[i * a.Cols + j]);
                outer.Items.Add(row);
            }
            return outer;
        }

        // np.round(a, decimals=0) — HONRA decimals (antes lo ignoraba, redondeaba a entero).
        private static object RoundTo(object[] a, PyDict kw)
        {
            int dec = 0;
            if (a.Length > 1 && a[1] != null) dec = (int)PyOps.ToLong(a[1]);
            else if (kw != null && kw.TryGet("decimals", out var dv)) dec = (int)PyOps.ToLong(dv);
            Func<double, double> f;
            if (dec >= 0 && dec <= 15) f = v => Math.Round(v, dec, MidpointRounding.ToEven);
            else { double p = Math.Pow(10.0, dec); f = v => Math.Round(v * p, MidpointRounding.ToEven) / p; }  // decimals negativos
            return UFunc(a[0], f);
        }

        private static object UFunc(object x, Func<double, double> f)
        {
            if (PyOps.IsNumber(x)) return f(PyOps.ToDouble(x));
            var a = AsArr(x);
            var d = new double[a.Data.Length];
            for (int i = 0; i < d.Length; i++) d[i] = f(a.Data[i]);
            return new PyNdArray(d, (int[])a.Shape.Clone());
        }
        // abs consciente de complejos: |z| = sqrt(re²+im²)
        private static object CAbs(object x)
        {
            if (PyOps.IsNumber(x)) return Math.Abs(PyOps.ToDouble(x));
            var a = AsArr(x); var d = new double[a.Size];
            for (int i = 0; i < a.Size; i++) d[i] = a.Imag != null ? Math.Sqrt(a.Data[i] * a.Data[i] + a.Imag[i] * a.Imag[i]) : Math.Abs(a.Data[i]);
            return new PyNdArray(d, (int[])a.Shape.Clone());
        }

        private static object ArgReduce(PyNdArray a, bool min)
        {
            int idx = 0; double best = a.Data[0];
            for (int i = 1; i < a.Data.Length; i++)
                if ((min && a.Data[i] < best) || (!min && a.Data[i] > best)) { best = a.Data[i]; idx = i; }
            return (long)idx;
        }
        private static object EwiseBin(object A, object B, Func<double, double, double> f)
        {
            bool aArr = A is PyNdArray, bArr = B is PyNdArray;
            if (aArr && bArr)
            {
                var x = (PyNdArray)A; var y = (PyNdArray)B;
                int n = Math.Min(x.Data.Length, y.Data.Length);
                var d = new double[n];
                for (int i = 0; i < n; i++) d[i] = f(x.Data[i], y.Data[i]);
                return new PyNdArray(d, (int[])x.Shape.Clone());
            }
            if (aArr) { var x = (PyNdArray)A; double s = PyOps.ToDouble(B); var d = new double[x.Data.Length]; for (int i = 0; i < d.Length; i++) d[i] = f(x.Data[i], s); return new PyNdArray(d, (int[])x.Shape.Clone()); }
            if (bArr) { var y = (PyNdArray)B; double s = PyOps.ToDouble(A); var d = new double[y.Data.Length]; for (int i = 0; i < d.Length; i++) d[i] = f(s, y.Data[i]); return new PyNdArray(d, (int[])y.Shape.Clone()); }
            return f(PyOps.ToDouble(A), PyOps.ToDouble(B));
        }
        private static PyNdArray Clip(PyNdArray a, object lo, object hi)
        {
            double l = lo == null ? double.NegativeInfinity : PyOps.ToDouble(lo);
            double h = hi == null ? double.PositiveInfinity : PyOps.ToDouble(hi);
            var d = new double[a.Data.Length];
            for (int i = 0; i < d.Length; i++) d[i] = Math.Min(h, Math.Max(l, a.Data[i]));
            return new PyNdArray(d, (int[])a.Shape.Clone(), a.IsInt);
        }
        private static double InterpOne(double x, double[] xp, double[] fp)
        {
            int n = xp.Length;
            if (n == 0) return 0;
            if (x <= xp[0]) return fp[0];
            if (x >= xp[n - 1]) return fp[n - 1];
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (xp[mid] <= x) lo = mid; else hi = mid; }
            double dx = xp[hi] - xp[lo];
            double t = dx == 0 ? 0 : (x - xp[lo]) / dx;
            return fp[lo] + t * (fp[hi] - fp[lo]);
        }
        private static object Interp(object X, PyNdArray xp, PyNdArray fp)
        {
            if (PyOps.IsNumber(X)) return InterpOne(PyOps.ToDouble(X), xp.Data, fp.Data);
            var x = AsArr(X);
            var d = new double[x.Data.Length];
            for (int i = 0; i < d.Length; i++) d[i] = InterpOne(x.Data[i], xp.Data, fp.Data);
            return new PyNdArray(d, (int[])x.Shape.Clone());
        }
        private static object Meshgrid(object[] a, PyDict kw)
        {
            var x = AsArr(a[0]); var y = AsArr(a[1]);
            bool ij = kw != null && kw.TryGet("indexing", out var iv) && iv is string s && s == "ij";
            int nx = x.Size, ny = y.Size;
            int r = ij ? nx : ny, c = ij ? ny : nx;
            var X = new double[r * c]; var Y = new double[r * c];
            for (int i = 0; i < r; i++)
                for (int j = 0; j < c; j++)
                    if (ij) { X[i * c + j] = x.Data[i]; Y[i * c + j] = y.Data[j]; }
                    else { X[i * c + j] = x.Data[j]; Y[i * c + j] = y.Data[i]; }
            return new PyList(new object[] { new PyNdArray(X, new[] { r, c }), new PyNdArray(Y, new[] { r, c }) });
        }
        private static List<PyNdArray> SeqArrays(object o)
        {
            var list = new List<PyNdArray>();
            var items = o is PyList l ? l.Items : o is PyTuple t ? t.Items : null;
            if (items == null) { list.Add(AsArr(o)); return list; }
            foreach (var it in items) list.Add(AsArr(it));
            return list;
        }
        private static PyNdArray ColumnStack(object o)
        {
            var arrs = SeqArrays(o);
            int rows = arrs[0].Size, k = arrs.Count;
            var d = new double[rows * k];
            for (int j = 0; j < k; j++) for (int i = 0; i < rows; i++) d[i * k + j] = arrs[j].Data[i];
            return new PyNdArray(d, new[] { rows, k });
        }
        private static PyNdArray HStack(object o)
        {
            var all = new List<double>();
            foreach (var ar in SeqArrays(o)) all.AddRange(ar.Data);
            return new PyNdArray(all.ToArray(), new[] { all.Count });
        }
        private static PyNdArray VStack(object o)
        {
            var arrs = SeqArrays(o);
            int cols = arrs[0].Ndim == 1 ? arrs[0].Size : arrs[0].Cols;
            int rows = 0; foreach (var ar in arrs) rows += ar.Ndim == 1 ? 1 : ar.Rows;
            var d = new double[rows * cols]; int p = 0;
            foreach (var ar in arrs) { Array.Copy(ar.Data, 0, d, p, ar.Data.Length); p += ar.Data.Length; }
            return new PyNdArray(d, new[] { rows, cols });
        }
        private static object Unravel(object[] a)
        {
            long flat = PyOps.ToLong(a[0]);
            var shape = ShapeOf(a[1]);
            var res = new List<object>();
            for (int k = shape.Length - 1; k >= 0; k--) { res.Insert(0, (long)(flat % shape[k])); flat /= shape[k]; }
            return new PyTuple(res);
        }
        private static PyNdArray Inv(PyNdArray A)
        {
            if (A.Ndim != 2 || A.Rows != A.Cols) throw new PyRuntimeError("LinAlgError", "inv: matriz no cuadrada");
            int n = A.Rows; var inv = new double[n * n];
            for (int col = 0; col < n; col++)
            {
                var e = new double[n]; e[col] = 1.0;
                var x = GaussSolve(n, (double[])A.Data.Clone(), e);
                for (int i = 0; i < n; i++) inv[i * n + col] = x[i];
            }
            return new PyNdArray(inv, new[] { n, n });
        }
        private static object Det(PyNdArray A)
        {
            if (A.Ndim != 2 || A.Rows != A.Cols) throw new PyRuntimeError("LinAlgError", "det: matriz no cuadrada");
            int n = A.Rows; var M = (double[])A.Data.Clone(); double det = 1;
            for (int col = 0; col < n; col++)
            {
                int piv = col; double best = Math.Abs(M[col * n + col]);
                for (int r = col + 1; r < n; r++) { double v = Math.Abs(M[r * n + col]); if (v > best) { best = v; piv = r; } }
                if (best == 0) return 0.0;
                if (piv != col) { for (int j = 0; j < n; j++) (M[col * n + j], M[piv * n + j]) = (M[piv * n + j], M[col * n + j]); det = -det; }
                det *= M[col * n + col];
                for (int r = col + 1; r < n; r++) { double f = M[r * n + col] / M[col * n + col]; for (int j = col; j < n; j++) M[r * n + j] -= f * M[col * n + j]; }
            }
            return det;
        }

        // ── conversión genérica object → PyNdArray ──
        public static PyNdArray AsArr(object o) => o switch
        {
            PyNdArray a => a,
            PyList or PyTuple or long or double or bool => FromObject(o, null),
            _ => throw new PyRuntimeError("TypeError", $"no se puede usar {PyOps.TypeName(o)} como ndarray")
        };

        public static PyNdArray FromObject(object o, object dtype)
        {
            bool isInt = IntDtype(dtype);
            if (o is PyNdArray nd) { var c = nd.Copy(); if (dtype != null) c.IsInt = isInt; return c; }
            if (PyOps.IsNumber(o)) return new PyNdArray(new[] { PyOps.ToDouble(o) }, new[] { 1 }, isInt);

            var rows = AsSeq(o);
            if (rows == null) throw new PyRuntimeError("TypeError", "np.array espera una lista/tupla o número");
            // ¿2D? (el primer elemento es a su vez secuencia)
            if (rows.Count > 0 && AsSeq(rows[0]) != null)
            {
                var first = AsSeq(rows[0]);
                int r = rows.Count, c = first.Count;
                var d = new double[r * c];
                for (int i = 0; i < r; i++)
                {
                    var row = AsSeq(rows[i]);
                    if (row == null || row.Count != c) throw new PyRuntimeError("ValueError", "filas de distinta longitud");
                    for (int j = 0; j < c; j++) d[i * c + j] = PyOps.ToDouble(row[j]);
                }
                if (dtype == null) isInt = AllInt(rows);
                return new PyNdArray(d, new[] { r, c }, isInt);
            }
            // 1D
            var v = new double[rows.Count];
            for (int i = 0; i < rows.Count; i++) v[i] = PyOps.ToDouble(rows[i]);
            if (dtype == null) isInt = AllInt(rows);
            return new PyNdArray(v, new[] { rows.Count }, isInt);
        }

        private static List<object> AsSeq(object o) =>
            o is PyList l ? l.Items : o is PyTuple t ? t.Items : null;

        private static bool AllInt(List<object> items)
        {
            foreach (var x in items)
            {
                var s = AsSeq(x);
                if (s != null) { if (!AllInt(s)) return false; }
                else if (!(x is long || x is bool)) return false;
            }
            return items.Count > 0;
        }

        // ── dtype / shape parsing ──
        private static object Dtype(object[] args, int posIdx, PyDict kw)
        {
            if (kw != null && kw.TryGet("dtype", out var dv)) return dv;
            if (args.Length > posIdx) return args[posIdx];
            return null;
        }
        private static bool IntDtype(object dt)
        {
            string n = dt switch { string s => s, PyBuiltin b => b.Name, PyClass c => c.Name, _ => null };
            return n != null && n.IndexOf("int", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static int[] ShapeOf(object o)
        {
            if (o is PyTuple t) { var s = new int[t.Count]; for (int i = 0; i < t.Count; i++) s[i] = (int)PyOps.ToLong(t.Items[i]); return s; }
            if (o is PyList l) { var s = new int[l.Count]; for (int i = 0; i < l.Count; i++) s[i] = (int)PyOps.ToLong(l.Items[i]); return s; }
            return new[] { (int)PyOps.ToLong(o) };
        }
        private static int[] ShapeOfArgs(object[] args)
        {
            var s = new int[args.Length];
            for (int i = 0; i < args.Length; i++) s[i] = (int)PyOps.ToLong(args[i]);
            return s;
        }
    }
}
