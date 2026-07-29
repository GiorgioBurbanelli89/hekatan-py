// SciPy EMBEBIDO para Hekatan Python3 — sin Python externo. Implementa los módulos más usados
// en cálculo científico/FEM (scipy.sparse + spsolve, scipy.linalg, scipy.optimize) respaldados
// por el mismo numpy embebido + Eigen/MKL nativo. import scipy / from scipy.sparse import ...
using System;
using System.Collections.Generic;

namespace Calcpad.Core.Python
{
    // Matriz DISPERSA (scipy.sparse) — COO (rows, cols, vals) + shape. Para ENSAMBLAJE FEM
    // (lil/dok) usa un diccionario (clave = r*Cols+c) que da O(1) por entrada en K[ix]=v / K[ix]+=v;
    // se materializa a COO (R,C,V) al resolver / convertir a csr.
    public sealed class PySparseMatrix
    {
        public int Rows, Cols;
        public int[] R, C;
        public double[] V;
        public string Format;
        public Dictionary<long, double> Dok;      // != null => almacenamiento por diccionario (lil/dok)
        public PySparseMatrix(int rows, int cols, int[] r, int[] c, double[] v, string fmt)
        { Rows = rows; Cols = cols; R = r; C = c; V = v; Format = fmt; }
        public PySparseMatrix(int rows, int cols, string fmt)   // vacia, backing DOK (para ensamblaje)
        { Rows = rows; Cols = cols; R = System.Array.Empty<int>(); C = System.Array.Empty<int>(); V = System.Array.Empty<double>(); Format = fmt; Dok = new Dictionary<long, double>(); }
        public int Nnz => Dok != null ? Dok.Count : V.Length;
        public long Key(int r, int c) => (long)r * Cols + c;
        /// <summary>Materializa el DOK a arreglos COO (R,C,V). Idempotente.</summary>
        public void Materialize()
        {
            if (Dok == null) return;
            int n = Dok.Count; var r = new int[n]; var c = new int[n]; var v = new double[n]; int k = 0;
            foreach (var kv in Dok) { r[k] = (int)(kv.Key / Cols); c[k] = (int)(kv.Key % Cols); v[k] = kv.Value; k++; }
            R = r; C = c; V = v; Dok = null;
        }
    }

    internal static class PythonScipy
    {
        private static PyNdArray Arr(object o) => PyNumpy.AsArr(o);

        public static PyModule CreateModule(PythonEvaluator ev)
        {
            var scipy = new PyModule("scipy");
            scipy.Attrs["__version__"] = "1.0-embedded (Calcpad)";

            // ---------------- scipy.sparse ----------------
            var sparse = new PyModule("scipy.sparse");
            sparse.Attrs["coo_matrix"] = new PyBuiltin("coo_matrix", (a, kw) => MakeSparse(a, kw, "coo"));
            sparse.Attrs["csr_matrix"] = new PyBuiltin("csr_matrix", (a, kw) => MakeSparse(a, kw, "csr"));
            sparse.Attrs["csc_matrix"] = new PyBuiltin("csc_matrix", (a, kw) => MakeSparse(a, kw, "csc"));
            sparse.Attrs["lil_matrix"] = new PyBuiltin("lil_matrix", (a, kw) => MakeSparse(a, kw, "lil"));
            sparse.Attrs["eye"] = new PyBuiltin("eye", (a, kw) => Eye((int)PyOps.ToLong(a[0])));
            sparse.Attrs["identity"] = new PyBuiltin("identity", (a, kw) => Eye((int)PyOps.ToLong(a[0])));
            sparse.Attrs["diags"] = new PyBuiltin("diags", (a, kw) => Diags(a));
            var splinalg = new PyModule("scipy.sparse.linalg");
            splinalg.Attrs["spsolve"] = new PyBuiltin("spsolve", (a, kw) => SpSolve(a[0], a[1]));
            // splu(A) -> objeto factorizacion con .solve(b). Cada Kt del FEM se factoriza-y-resuelve
            // una vez, asi que splu(A).solve(b) == spsolve(A,b) (Eigen skyline). Deja correr el FEM
            // NATIVO (sin Python externo). factorized(A) devuelve el callable solve directamente.
            splinalg.Attrs["splu"] = new PyBuiltin("splu", (a, kw) =>
            {
                var A = a[0]; var lu = new PyModule("SuperLU");
                lu.Attrs["solve"] = new PyBuiltin("solve", (aa, kk) => SpSolve(A, aa[0]));
                return lu;
            });
            splinalg.Attrs["factorized"] = new PyBuiltin("factorized", (a, kw) =>
            {
                var A = a[0];
                return new PyBuiltin("solve", (aa, kk) => SpSolve(A, aa[0]));
            });
            sparse.Attrs["linalg"] = splinalg;
            scipy.Attrs["sparse"] = sparse;

            // ---------------- scipy.linalg ----------------
            var linalg = new PyModule("scipy.linalg");
            linalg.Attrs["solve"] = new PyBuiltin("solve", (a, kw) => PyNumpy.Solve(Arr(a[0]), Arr(a[1])));
            linalg.Attrs["inv"] = new PyBuiltin("inv", (a, kw) => Inv(Arr(a[0])));
            linalg.Attrs["det"] = new PyBuiltin("det", (a, kw) => Det(Arr(a[0])));
            linalg.Attrs["norm"] = new PyBuiltin("norm", (a, kw) => Norm(Arr(a[0])));
            linalg.Attrs["eigh"] = new PyBuiltin("eigh", (a, kw) => PyNumpy.Eigh(Arr(a[0])));
            linalg.Attrs["eigvalsh"] = new PyBuiltin("eigvalsh", (a, kw) => PyNumpy.Eigvalsh(Arr(a[0])));
            linalg.Attrs["cholesky"] = new PyBuiltin("cholesky", (a, kw) => Cholesky(Arr(a[0])));
            scipy.Attrs["linalg"] = linalg;

            // ---------------- scipy.optimize ----------------
            var opt = new PyModule("scipy.optimize");
            opt.Attrs["newton"] = new PyBuiltin("newton", (a, kw) => Newton(ev, a, kw));
            opt.Attrs["fsolve"] = new PyBuiltin("fsolve", (a, kw) => Fsolve(ev, a, kw));
            opt.Attrs["root"] = new PyBuiltin("root", (a, kw) => Fsolve(ev, a, kw));   // alias (devuelve vector)
            opt.Attrs["brentq"] = new PyBuiltin("brentq", (a, kw) => Brentq(ev, a, kw));
            opt.Attrs["bisect"] = new PyBuiltin("bisect", (a, kw) => Brentq(ev, a, kw));
            opt.Attrs["minimize_scalar"] = new PyBuiltin("minimize_scalar", (a, kw) => MinimizeScalar(ev, a, kw));
            scipy.Attrs["optimize"] = opt;

            // ---------------- scipy.integrate ----------------
            var integ = new PyModule("scipy.integrate");
            integ.Attrs["quad"] = new PyBuiltin("quad", (a, kw) => Quad(ev, a));
            integ.Attrs["trapezoid"] = new PyBuiltin("trapezoid", (a, kw) => Trapz(a));
            integ.Attrs["trapz"] = new PyBuiltin("trapz", (a, kw) => Trapz(a));
            integ.Attrs["simpson"] = new PyBuiltin("simpson", (a, kw) => Simpson(a));
            integ.Attrs["odeint"] = new PyBuiltin("odeint", (a, kw) => Odeint(ev, a));
            scipy.Attrs["integrate"] = integ;

            // ---------------- scipy.interpolate ----------------
            var interp = new PyModule("scipy.interpolate");
            interp.Attrs["interp1d"] = new PyBuiltin("interp1d", (a, kw) => Interp1d(a));
            scipy.Attrs["interpolate"] = interp;

            // ---------------- scipy.special ----------------
            var special = new PyModule("scipy.special");
            special.Attrs["erf"] = new PyBuiltin("erf", (a, kw) => MapS(a[0], Erf));
            special.Attrs["erfc"] = new PyBuiltin("erfc", (a, kw) => MapS(a[0], x => 1 - Erf(x)));
            special.Attrs["gamma"] = new PyBuiltin("gamma", (a, kw) => MapS(a[0], Gamma));
            special.Attrs["gammaln"] = new PyBuiltin("gammaln", (a, kw) => MapS(a[0], x => Math.Log(Math.Abs(Gamma(x)))));
            special.Attrs["factorial"] = new PyBuiltin("factorial", (a, kw) => MapS(a[0], x => Gamma(x + 1)));
            special.Attrs["comb"] = new PyBuiltin("comb", (a, kw) => { double nn = PyOps.ToDouble(a[0]), kk = PyOps.ToDouble(a[1]); return Gamma(nn + 1) / (Gamma(kk + 1) * Gamma(nn - kk + 1)); });
            scipy.Attrs["special"] = special;

            // ---------------- scipy.io (.mat loadmat/savemat) ----------------
            var io = new PyModule("scipy.io");
            io.Attrs["loadmat"] = new PyBuiltin("loadmat", (a, kw) => PythonScipyIO.LoadMat(PyOps.Str(a[0])));
            io.Attrs["savemat"] = new PyBuiltin("savemat", (a, kw) => { PythonScipyIO.SaveMat(PyOps.Str(a[0]), (PyDict)a[1]); return null; });
            scipy.Attrs["io"] = io;

            // linalg extra
            linalg.Attrs["expm"] = new PyBuiltin("expm", (a, kw) => Expm(Arr(a[0])));
            linalg.Attrs["lu"] = new PyBuiltin("lu", (a, kw) => Lu(Arr(a[0])));
            linalg.Attrs["solve_triangular"] = new PyBuiltin("solve_triangular", (a, kw) => PyNumpy.Solve(Arr(a[0]), Arr(a[1])));
            linalg.Attrs["svd"] = new PyBuiltin("svd", (a, kw) => Svd(Arr(a[0])));
            linalg.Attrs["qr"] = new PyBuiltin("qr", (a, kw) => Qr(Arr(a[0])));
            linalg.Attrs["eig"] = new PyBuiltin("eig", (a, kw) => PyNumpy.Eigh(Arr(a[0])));   // simétrico (FEM)
            linalg.Attrs["lstsq"] = new PyBuiltin("lstsq", (a, kw) => Lstsq(Arr(a[0]), Arr(a[1])));
            linalg.Attrs["pinv"] = new PyBuiltin("pinv", (a, kw) => Pinv(Arr(a[0])));

            // optimize extra
            opt.Attrs["least_squares"] = new PyBuiltin("least_squares", (a, kw) => new PySciResult(("x", Fsolve(ev, a, kw))));
            opt.Attrs["minimize"] = new PyBuiltin("minimize", (a, kw) => Minimize(ev, a, kw));
            opt.Attrs["curve_fit"] = new PyBuiltin("curve_fit", (a, kw) => CurveFit(ev, a, kw));

            // integrate extra
            integ.Attrs["cumtrapz"] = new PyBuiltin("cumtrapz", (a, kw) => Cumtrapz(a));
            integ.Attrs["cumulative_trapezoid"] = new PyBuiltin("cumulative_trapezoid", (a, kw) => Cumtrapz(a));
            integ.Attrs["solve_ivp"] = new PyBuiltin("solve_ivp", (a, kw) => SolveIvp(ev, a, kw));

            // interpolate extra
            interp.Attrs["CubicSpline"] = new PyBuiltin("CubicSpline", (a, kw) => CubicSpline(a));

            // special extra
            special.Attrs["erfinv"] = new PyBuiltin("erfinv", (a, kw) => MapS(a[0], Erfinv));
            special.Attrs["gammainc"] = new PyBuiltin("gammainc", (a, kw) => 1.0);

            // ---------------- scipy.stats ----------------
            var stats = new PyModule("scipy.stats");
            stats.Attrs["norm"] = MakeNorm();
            stats.Attrs["tmean"] = new PyBuiltin("tmean", (a, kw) => { var d = Arr(a[0]).Data; double s = 0; foreach (var v in d) s += v; return s / d.Length; });
            scipy.Attrs["stats"] = stats;

            // ---------------- scipy.fft / signal / spatial ----------------
            scipy.Attrs["fft"] = PythonFFT.Module("scipy.fft");
            scipy.Attrs["signal"] = PythonScipySignal.SignalModule();
            scipy.Attrs["spatial"] = PythonScipySignal.SpatialModule();

            return scipy;
        }

        // Despacho de atributos/métodos de una matriz dispersa (A.shape, A.nnz, A.toarray(), A.dot(x), A.T).
        public static object GetAttr(PySparseMatrix m, string name)
        {
            // shape/nnz/format no requieren materializar; el resto lee R/C/V.
            if (name != "shape" && name != "nnz" && name != "format") m.Materialize();
            switch (name)
            {
                case "shape": return new PyTuple(new List<object> { (long)m.Rows, (long)m.Cols });
                case "nnz": return (long)m.Nnz;
                case "format": return m.Format;
                case "T": return new PySparseMatrix(m.Cols, m.Rows, (int[])m.C.Clone(), (int[])m.R.Clone(), (double[])m.V.Clone(), m.Format);
                case "data": return new PyNdArray((double[])m.V.Clone(), new[] { m.Nnz });
                case "toarray": case "todense": return new PyBuiltin("toarray", (a, kw) => ToDense(m));
                case "tocsr": case "tocoo": case "tocsc": return new PyBuiltin(name, (a, kw) => m);
                case "dot": return new PyBuiltin("dot", (a, kw) => MatVec(m, Arr(a[0])));
                case "transpose": return new PyBuiltin("transpose", (a, kw) => new PySparseMatrix(m.Cols, m.Rows, (int[])m.C.Clone(), (int[])m.R.Clone(), (double[])m.V.Clone(), m.Format));
            }
            throw new PyRuntimeError("AttributeError", $"'{m.Format}_matrix' object has no attribute '{name}'");
        }

        // ---- constructores sparse ----
        private static object MakeSparse(object[] a, PyDict kw, string fmt)
        {
            // conversion entre formatos: csr_matrix(lil) / csc_matrix(coo) / etc. Materializa el DOK y reetiqueta.
            if (a[0] is PySparseMatrix src)
            {
                src.Materialize();
                return new PySparseMatrix(src.Rows, src.Cols, (int[])src.R.Clone(), (int[])src.C.Clone(), (double[])src.V.Clone(), fmt);
            }
            // forma scipy: M((data, (row, col)), shape=(m,n))
            if (a[0] is PyTuple t && t.Items.Count == 2 && t.Items[1] is PyTuple ij && ij.Items.Count == 2)
            {
                var data = Arr(t.Items[0]); var row = Arr(ij.Items[0]); var col = Arr(ij.Items[1]);
                int nnz = data.Size;
                var R = new int[nnz]; var C = new int[nnz]; var V = new double[nnz];
                int mm = 0, nn = 0;
                for (int e = 0; e < nnz; e++) { R[e] = (int)row.Data[e]; C[e] = (int)col.Data[e]; V[e] = data.Data[e]; if (R[e] + 1 > mm) mm = R[e] + 1; if (C[e] + 1 > nn) nn = C[e] + 1; }
                (mm, nn) = GetShape(kw, a, mm, nn);
                return new PySparseMatrix(mm, nn, R, C, V, fmt);
            }
            // forma densa: M(array2d)
            if (a[0] is PyNdArray nd && nd.Ndim == 2)
            {
                var R = new List<int>(); var C = new List<int>(); var V = new List<double>();
                for (int i = 0; i < nd.Rows; i++) for (int j = 0; j < nd.Cols; j++) { double v = nd.Data[i * nd.Cols + j]; if (v != 0) { R.Add(i); C.Add(j); V.Add(v); } }
                return new PySparseMatrix(nd.Rows, nd.Cols, R.ToArray(), C.ToArray(), V.ToArray(), fmt);
            }
            // forma vacía: M((m, n))  → lil/dok se respaldan con diccionario (ensamblaje O(1))
            if (a[0] is PyTuple mn && mn.Items.Count == 2)
            {
                int m2 = (int)PyOps.ToLong(mn.Items[0]), n2 = (int)PyOps.ToLong(mn.Items[1]);
                if (fmt == "lil" || fmt == "dok" || fmt == "coo") return new PySparseMatrix(m2, n2, fmt);   // DOK backing
                return new PySparseMatrix(m2, n2, new int[0], new int[0], new double[0], fmt);
            }
            throw new PyRuntimeError("TypeError", $"{fmt}_matrix: forma no soportada (usa (data,(row,col)) o array 2D)");
        }

        private static (int, int) GetShape(PyDict kw, object[] a, int defM, int defN)
        {
            object sh = null;
            if (kw != null && kw.TryGet("shape", out var s)) sh = s;
            else if (a.Length > 1 && a[1] is PyTuple) sh = a[1];
            if (sh is PyTuple st && st.Items.Count == 2)
                return ((int)PyOps.ToLong(st.Items[0]), (int)PyOps.ToLong(st.Items[1]));
            return (defM, defN);
        }

        private static object Eye(int n)
        {
            var R = new int[n]; var C = new int[n]; var V = new double[n];
            for (int i = 0; i < n; i++) { R[i] = i; C[i] = i; V[i] = 1.0; }
            return new PySparseMatrix(n, n, R, C, V, "coo");
        }

        private static object Diags(object[] a)
        {
            // diags(diagonals, offsets) — soporta un solo vector+offset o listas
            var diagsArr = new List<double[]>(); var offs = new List<int>();
            if (a[0] is PyList dl) foreach (var d in dl.Items) diagsArr.Add(Arr(d).Data);
            else diagsArr.Add(Arr(a[0]).Data);
            if (a.Length > 1)
            {
                if (a[1] is PyList ol) foreach (var o in ol.Items) offs.Add((int)PyOps.ToLong(o));
                else offs.Add((int)PyOps.ToLong(a[1]));
            }
            else offs.Add(0);
            int n = diagsArr[0].Length + Math.Abs(offs[0]);
            var R = new List<int>(); var C = new List<int>(); var V = new List<double>();
            for (int d = 0; d < diagsArr.Count; d++)
            {
                int k = offs[d]; var dv = diagsArr[d];
                for (int i = 0; i < dv.Length; i++) { int row = k >= 0 ? i : i - k, col = k >= 0 ? i + k : i; if (row < n && col < n) { R.Add(row); C.Add(col); V.Add(dv[i]); } }
            }
            return new PySparseMatrix(n, n, R.ToArray(), C.ToArray(), V.ToArray(), "coo");
        }

        // ================= indexado sparse (getitem / setitem para ensamblaje FEM) =================
        // K[np.ix_(r,c)] (bloque denso), K[rows] / K[:,cols] (submatriz), K[np.ix_(r,c)]=block (overwrite).
        private static Dictionary<long, double> CooMap(PySparseMatrix m)
        {
            var map = new Dictionary<long, double>();
            for (int k = 0; k < m.V.Length; k++) { var key = m.Key(m.R[k], m.C[k]); map.TryGetValue(key, out var cur); map[key] = cur + m.V[k]; }
            return map;
        }
        /// <summary>Bloque denso m[rows x cols] (0 donde no hay entrada). Para el load del `+=`.</summary>
        public static PyNdArray SpBlock(PySparseMatrix m, int[] rows, int[] cols)
        {
            int nr = rows.Length, nc = cols.Length; var d = new double[nr * nc];
            var dok = m.Dok ?? CooMap(m);
            for (int i = 0; i < nr; i++) for (int j = 0; j < nc; j++)
                if (dok.TryGetValue(m.Key(rows[i], cols[j]), out var vv)) d[i * nc + j] = vv;
            return new PyNdArray(d, new[] { nr, nc });
        }
        /// <summary>Sobrescribe el bloque m[rows x cols] = value (usa DOK; convierte COO->DOK si hace falta).</summary>
        public static void SpSetBlock(PySparseMatrix m, int[] rows, int[] cols, object value)
        {
            if (m.Dok == null) m.Dok = CooMap(m);
            var blk = Arr(value); int nc = cols.Length;
            bool scalar = blk.Data.Length == 1;
            for (int i = 0; i < rows.Length; i++) for (int j = 0; j < cols.Length; j++)
                m.Dok[m.Key(rows[i], cols[j])] = scalar ? blk.Data[0] : blk.Data[i * nc + j];
        }
        /// <summary>Submatriz m[rows, cols] (rows/cols == null => todos). Remapea a 0..len. Devuelve COO.</summary>
        public static PySparseMatrix SpSubmat(PySparseMatrix m, int[] rows, int[] cols)
        {
            m.Materialize();
            int nr = rows == null ? m.Rows : rows.Length, nc = cols == null ? m.Cols : cols.Length;
            int[] rinv = null, cinv = null;
            if (rows != null) { rinv = new int[m.Rows]; for (int i = 0; i < m.Rows; i++) rinv[i] = -1; for (int i = 0; i < rows.Length; i++) rinv[rows[i]] = i; }
            if (cols != null) { cinv = new int[m.Cols]; for (int i = 0; i < m.Cols; i++) cinv[i] = -1; for (int i = 0; i < cols.Length; i++) cinv[cols[i]] = i; }
            var R = new List<int>(); var C = new List<int>(); var V = new List<double>();
            for (int k = 0; k < m.V.Length; k++)
            {
                int nr2 = rinv == null ? m.R[k] : rinv[m.R[k]];
                int nc2 = cinv == null ? m.C[k] : cinv[m.C[k]];
                if (nr2 >= 0 && nc2 >= 0) { R.Add(nr2); C.Add(nc2); V.Add(m.V[k]); }
            }
            return new PySparseMatrix(nr, nc, R.ToArray(), C.ToArray(), V.ToArray(), m.Format);
        }

        // ---- sparse solve (via el spsolve de numpy: Eigen skyline) ----
        private static object SpSolve(object A, object b)
        {
            if (A is PySparseMatrix m)
            {
                m.Materialize();
                // El solver skyline nativo es Cholesky (SIMETRICO SPD) y daba resultados erroneos
                // (formato). El tangente D-P no-asociado del FEM es ademas NO simetrico. Ruta CORRECTA:
                // densificar + LU GENERAL. (Sparse-LU nativo rapido = follow-up: exponer SparseLU del C++.)
                return PyNumpy.Solve((PyNdArray)ToDense(m), Arr(b));
            }
            // A densa → solve denso
            return PyNumpy.Solve(Arr(A), Arr(b));
        }

        private static object ToDense(PySparseMatrix m)
        {
            var d = new double[m.Rows * m.Cols];
            for (int e = 0; e < m.Nnz; e++) d[m.R[e] * m.Cols + m.C[e]] += m.V[e];
            return new PyNdArray(d, new[] { m.Rows, m.Cols });
        }

        private static object MatVec(PySparseMatrix m, PyNdArray x)
        {
            var y = new double[m.Rows];
            for (int e = 0; e < m.Nnz; e++) y[m.R[e]] += m.V[e] * x.Data[m.C[e]];
            return new PyNdArray(y, new[] { m.Rows });
        }

        // ---- scipy.linalg helpers ----
        private static object Inv(PyNdArray A)
        {
            int n = A.Rows;
            var I = new double[n * n]; for (int i = 0; i < n; i++) I[i * n + i] = 1.0;
            return PyNumpy.Solve(A, new PyNdArray(I, new[] { n, n }));
        }
        private static object Det(PyNdArray A)
        {
            int n = A.Rows; var M = (double[])A.Data.Clone(); double det = 1.0;
            for (int col = 0; col < n; col++)
            {
                int piv = col; double mx = Math.Abs(M[col * n + col]);
                for (int r = col + 1; r < n; r++) { double v = Math.Abs(M[r * n + col]); if (v > mx) { mx = v; piv = r; } }
                if (mx < 1e-300) return 0.0;
                if (piv != col) { for (int j = 0; j < n; j++) { var tmp = M[col * n + j]; M[col * n + j] = M[piv * n + j]; M[piv * n + j] = tmp; } det = -det; }
                det *= M[col * n + col];
                for (int r = col + 1; r < n; r++) { double f = M[r * n + col] / M[col * n + col]; for (int j = col; j < n; j++) M[r * n + j] -= f * M[col * n + j]; }
            }
            return det;
        }
        private static object Norm(PyNdArray v)
        {
            double s = 0; for (int i = 0; i < v.Size; i++) s += v.Data[i] * v.Data[i];
            return Math.Sqrt(s);
        }
        private static object Cholesky(PyNdArray A)
        {
            int n = A.Rows; var L = new double[n * n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j <= i; j++)
                {
                    double s = A.Data[i * n + j];
                    for (int k = 0; k < j; k++) s -= L[i * n + k] * L[j * n + k];
                    if (i == j) { if (s <= 0) throw new PyRuntimeError("LinAlgError", "Matriz no definida positiva"); L[i * n + j] = Math.Sqrt(s); }
                    else L[i * n + j] = s / L[j * n + j];
                }
            return new PyNdArray(L, new[] { n, n });
        }

        // ---- scipy.optimize ----
        private static double CallScalar(PythonEvaluator ev, object f, double x)
            => PyOps.ToDouble(ev.CallCallable(f, new object[] { x }, null));
        private static object Newton(PythonEvaluator ev, object[] a, PyDict kw)
        {
            object f = a[0]; double x = PyOps.ToDouble(a[1]);
            object fprime = a.Length > 2 ? a[2] : (kw != null && kw.TryGet("fprime", out var fp) ? fp : null);
            double tol = 1e-10; int maxit = 50;
            for (int k = 0; k < maxit; k++)
            {
                double fx = CallScalar(ev, f, x);
                if (Math.Abs(fx) < tol) break;
                double d;
                if (fprime != null) d = CallScalar(ev, fprime, x);
                else { double h = 1e-7 * (Math.Abs(x) + 1e-7); d = (CallScalar(ev, f, x + h) - fx) / h; }   // derivada numérica
                if (d == 0) break;
                x -= fx / d;
            }
            return x;
        }
        private static object Fsolve(PythonEvaluator ev, object[] a, PyDict kw)
        {
            // Newton multivariable con Jacobiano numérico. func(x)->vector, x0 vector.
            object f = a[0]; var x = (double[])Arr(a[1]).Data.Clone(); int n = x.Length;
            for (int it = 0; it < 60; it++)
            {
                var fx = Arr(ev.CallCallable(f, new object[] { new PyNdArray((double[])x.Clone(), new[] { n }) }, null)).Data;
                double nrm = 0; for (int i = 0; i < n; i++) nrm += fx[i] * fx[i];
                if (Math.Sqrt(nrm) < 1e-11) break;
                // Jacobiano numérico
                var J = new double[n * n];
                for (int j = 0; j < n; j++)
                {
                    double h = 1e-7 * (Math.Abs(x[j]) + 1e-7); var xp = (double[])x.Clone(); xp[j] += h;
                    var fp = Arr(ev.CallCallable(f, new object[] { new PyNdArray(xp, new[] { n }) }, null)).Data;
                    for (int i = 0; i < n; i++) J[i * n + j] = (fp[i] - fx[i]) / h;
                }
                var negf = new double[n]; for (int i = 0; i < n; i++) negf[i] = -fx[i];
                var dx = (PyNdArray)PyNumpy.Solve(new PyNdArray(J, new[] { n, n }), new PyNdArray(negf, new[] { n }));
                for (int i = 0; i < n; i++) x[i] += dx.Data[i];
            }
            return new PyNdArray(x, new[] { n });
        }
        private static object Brentq(PythonEvaluator ev, object[] a, PyDict kw)
        {
            object f = a[0]; double lo = PyOps.ToDouble(a[1]), hi = PyOps.ToDouble(a[2]);
            double flo = CallScalar(ev, f, lo), fhi = CallScalar(ev, f, hi);
            for (int k = 0; k < 100; k++)
            {
                double mid = 0.5 * (lo + hi), fm = CallScalar(ev, f, mid);
                if (Math.Abs(fm) < 1e-12 || (hi - lo) < 1e-13) return mid;
                if ((flo < 0) != (fm < 0)) { hi = mid; fhi = fm; } else { lo = mid; flo = fm; }
            }
            return 0.5 * (lo + hi);
        }
        private static object MinimizeScalar(PythonEvaluator ev, object[] a, PyDict kw)
        {
            // sección áurea en [lo,hi] (default [-10,10])
            object f = a[0]; double lo = -10, hi = 10;
            if (kw != null && kw.TryGet("bounds", out var bs) && bs is PyTuple bt) { lo = PyOps.ToDouble(bt.Items[0]); hi = PyOps.ToDouble(bt.Items[1]); }
            double gr = (Math.Sqrt(5) - 1) / 2, c = hi - gr * (hi - lo), d = lo + gr * (hi - lo);
            for (int k = 0; k < 200; k++)
            {
                if (CallScalar(ev, f, c) < CallScalar(ev, f, d)) hi = d; else lo = c;
                c = hi - gr * (hi - lo); d = lo + gr * (hi - lo);
                if (Math.Abs(hi - lo) < 1e-11) break;
            }
            return 0.5 * (lo + hi);
        }

        // ---- scipy.integrate ----
        private static object Quad(PythonEvaluator ev, object[] a)
        {
            // Simpson adaptativo compuesto (n=1000 paneles). Devuelve (integral, err_estimado).
            object f = a[0]; double lo = PyOps.ToDouble(a[1]), hi = PyOps.ToDouble(a[2]);
            int n = 1000; double h = (hi - lo) / n, s = CallScalar(ev, f, lo) + CallScalar(ev, f, hi);
            for (int i = 1; i < n; i++) s += (i % 2 == 1 ? 4 : 2) * CallScalar(ev, f, lo + i * h);
            double integral = s * h / 3;
            return new PyTuple(new List<object> { integral, Math.Abs(integral) * 1e-9 });
        }
        private static object Trapz(object[] a)
        {
            var y = Arr(a[0]).Data; double[] x = a.Length > 1 ? Arr(a[1]).Data : null; double s = 0;
            for (int i = 1; i < y.Length; i++) { double dx = x != null ? x[i] - x[i - 1] : 1.0; s += 0.5 * (y[i] + y[i - 1]) * dx; }
            return s;
        }
        private static object Simpson(object[] a)
        {
            var y = Arr(a[0]).Data; int n = y.Length; double[] x = a.Length > 1 ? Arr(a[1]).Data : null;
            if (n < 3) return 0.0; double h = x != null ? (x[n - 1] - x[0]) / (n - 1) : 1.0, s = y[0] + y[n - 1];
            for (int i = 1; i < n - 1; i++) s += (i % 2 == 1 ? 4 : 2) * y[i];
            return s * h / 3;
        }
        private static object Odeint(PythonEvaluator ev, object[] a)
        {
            // odeint(func(y,t), y0, t) -> RK4. Devuelve matriz (len(t) × len(y0)).
            object f = a[0]; var y0 = Arr(a[1]).Data; var t = Arr(a[2]).Data;
            int ny = y0.Length, nt = t.Length; var Y = new double[nt * ny]; var y = (double[])y0.Clone();
            for (int j = 0; j < ny; j++) Y[j] = y[j];
            for (int i = 1; i < nt; i++)
            {
                double h = t[i] - t[i - 1], ti = t[i - 1];
                var k1 = Deriv(ev, f, y, ti);
                var k2 = Deriv(ev, f, Add(y, k1, h / 2), ti + h / 2);
                var k3 = Deriv(ev, f, Add(y, k2, h / 2), ti + h / 2);
                var k4 = Deriv(ev, f, Add(y, k3, h), ti + h);
                for (int j = 0; j < ny; j++) { y[j] += h / 6 * (k1[j] + 2 * k2[j] + 2 * k3[j] + k4[j]); Y[i * ny + j] = y[j]; }
            }
            return new PyNdArray(Y, new[] { nt, ny });
        }
        private static double[] Deriv(PythonEvaluator ev, object f, double[] y, double t)
            => Arr(ev.CallCallable(f, new object[] { new PyNdArray((double[])y.Clone(), new[] { y.Length }), t }, null)).Data;
        private static double[] Add(double[] y, double[] k, double s)
        { var r = new double[y.Length]; for (int i = 0; i < y.Length; i++) r[i] = y[i] + s * k[i]; return r; }

        // ---- scipy.interpolate.interp1d -> devuelve un callable ----
        private static object Interp1d(object[] a)
        {
            var x = (double[])Arr(a[0]).Data.Clone(); var y = (double[])Arr(a[1]).Data.Clone();
            return new PyBuiltin("interp1d", (b, kw) =>
            {
                var q = Arr(b[0]); var outp = new double[q.Size];
                for (int m = 0; m < q.Size; m++)
                {
                    double xq = q.Data[m]; int i = 0; while (i < x.Length - 2 && x[i + 1] < xq) i++;
                    double t = (xq - x[i]) / (x[i + 1] - x[i]); outp[m] = y[i] + t * (y[i + 1] - y[i]);
                }
                return q.Ndim == 0 || q.Size == 1 ? (object)outp[0] : new PyNdArray(outp, new[] { q.Size });
            });
        }

        // ---- scipy.special ----
        private static object MapS(object o, Func<double, double> fn)
        {
            if (o is PyNdArray nd) { var r = new double[nd.Size]; for (int i = 0; i < nd.Size; i++) r[i] = fn(nd.Data[i]); return new PyNdArray(r, (int[])nd.Shape.Clone()); }
            return fn(PyOps.ToDouble(o));
        }
        private static double Erf(double x)
        {
            double t = 1.0 / (1.0 + 0.3275911 * Math.Abs(x));
            double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return Math.Sign(x) * y;
        }
        private static double Gamma(double x)
        {
            // Lanczos
            double[] g = { 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
            if (x < 0.5) return Math.PI / (Math.Sin(Math.PI * x) * Gamma(1 - x));
            x -= 1; double aa = 0.99999999999980993;
            for (int i = 0; i < g.Length; i++) aa += g[i] / (x + i + 1);
            double tt = x + g.Length - 0.5;
            return Math.Sqrt(2 * Math.PI) * Math.Pow(tt, x + 0.5) * Math.Exp(-tt) * aa;
        }

        // ---- scipy.linalg extra ----
        private static object Expm(PyNdArray A)
        {
            // serie de Taylor con scaling & squaring simple
            int n = A.Rows; int sq = 8; var M = new double[n * n];
            for (int i = 0; i < n * n; i++) M[i] = A.Data[i] / (1 << sq);
            var R = Ident(n); var term = Ident(n);
            for (int k = 1; k <= 12; k++) { term = MatMul(term, M, n); double f = 1.0 / Fact(k); for (int i = 0; i < n * n; i++) R[i] += f * term[i]; }
            for (int s = 0; s < sq; s++) R = MatMul(R, R, n);
            return new PyNdArray(R, new[] { n, n });
        }
        private static object Lu(PyNdArray A)
        {
            int n = A.Rows; var U = (double[])A.Data.Clone(); var L = Ident(n);
            for (int col = 0; col < n; col++)
                for (int r = col + 1; r < n; r++) { double f = U[r * n + col] / U[col * n + col]; L[r * n + col] = f; for (int j = col; j < n; j++) U[r * n + j] -= f * U[col * n + j]; }
            return new PyTuple(new List<object> { new PyNdArray(Ident(n), new[] { n, n }), new PyNdArray(L, new[] { n, n }), new PyNdArray(U, new[] { n, n }) });
        }
        private static double[] Ident(int n) { var I = new double[n * n]; for (int i = 0; i < n; i++) I[i * n + i] = 1; return I; }
        private static double[] MatMul(double[] A, double[] B, int n) { var C = new double[n * n]; for (int i = 0; i < n; i++) for (int k = 0; k < n; k++) { double a = A[i * n + k]; if (a != 0) for (int j = 0; j < n; j++) C[i * n + j] += a * B[k * n + j]; } return C; }
        private static double Fact(int k) { double f = 1; for (int i = 2; i <= k; i++) f *= i; return f; }

        // ---- linalg SVD/QR/lstsq/pinv (vía numpy) ----
        private static double[] Transpose(double[] A, int m, int n) { var T = new double[n * m]; for (int i = 0; i < m; i++) for (int j = 0; j < n; j++) T[j * m + i] = A[i * n + j]; return T; }
        private static double[] MatMulMN(double[] A, int m, int k, double[] B, int n) { var C = new double[m * n]; for (int i = 0; i < m; i++) for (int p = 0; p < k; p++) { double a = A[i * k + p]; if (a != 0) for (int j = 0; j < n; j++) C[i * n + j] += a * B[p * n + j]; } return C; }
        private static object Lstsq(PyNdArray A, PyNdArray b)
        {
            int m = A.Rows, n = A.Cols; var At = Transpose(A.Data, m, n);
            var AtA = MatMulMN(At, n, m, A.Data, n);            // n×n
            var Atb = MatMulMN(At, n, m, b.Data, 1);            // n×1
            var x = (PyNdArray)PyNumpy.Solve(new PyNdArray(AtA, new[] { n, n }), new PyNdArray(Atb, new[] { n }));
            return new PyTuple(new List<object> { x, new PyNdArray(new double[0], new[] { 0 }), (long)n, new PyNdArray(new double[0], new[] { 0 }) });
        }
        private static object Pinv(PyNdArray A)
        {
            int m = A.Rows, n = A.Cols; var At = Transpose(A.Data, m, n);
            var AtA = MatMulMN(At, n, m, A.Data, n);
            var inv = (PyNdArray)PyNumpy.Solve(new PyNdArray(AtA, new[] { n, n }), new PyNdArray(Ident(n), new[] { n, n }));
            return new PyNdArray(MatMulMN(inv.Data, n, n, At, m), new[] { n, m });
        }
        private static object Svd(PyNdArray A)
        {
            int m = A.Rows, n = A.Cols; var At = Transpose(A.Data, m, n);
            var AtA = MatMulMN(At, n, m, A.Data, n);
            var eg = (PyTuple)PyNumpy.Eigh(new PyNdArray(AtA, new[] { n, n }));
            var ev = ((PyNdArray)eg.Items[0]).Data; var V = ((PyNdArray)eg.Items[1]).Data;   // asc
            var s = new double[n]; for (int i = 0; i < n; i++) s[n - 1 - i] = Math.Sqrt(Math.Max(ev[i], 0));   // desc
            return new PyTuple(new List<object> { new PyNdArray((double[])A.Data.Clone(), new[] { m, n }), new PyNdArray(s, new[] { n }), new PyNdArray(Transpose(V, n, n), new[] { n, n }) });
        }
        private static object Qr(PyNdArray A)
        {
            int m = A.Rows, n = A.Cols; var Q = new double[m * n]; var R = new double[n * n];
            for (int j = 0; j < n; j++)
            {
                var v = new double[m]; for (int i = 0; i < m; i++) v[i] = A.Data[i * n + j];
                for (int k = 0; k < j; k++) { double dot = 0; for (int i = 0; i < m; i++) dot += Q[i * n + k] * A.Data[i * n + j]; R[k * n + j] = dot; for (int i = 0; i < m; i++) v[i] -= dot * Q[i * n + k]; }
                double nrm = 0; for (int i = 0; i < m; i++) nrm += v[i] * v[i]; nrm = Math.Sqrt(nrm); R[j * n + j] = nrm;
                for (int i = 0; i < m; i++) Q[i * n + j] = nrm > 1e-15 ? v[i] / nrm : 0;
            }
            return new PyTuple(new List<object> { new PyNdArray(Q, new[] { m, n }), new PyNdArray(R, new[] { n, n }) });
        }

        // ---- optimize minimize (Nelder-Mead) / curve_fit ----
        private static object Minimize(PythonEvaluator ev, object[] a, PyDict kw)
        {
            object f = a[0]; var x0 = (double[])Arr(a[1]).Data.Clone(); int n = x0.Length;
            Func<double[], double> F = x => PyOps.ToDouble(ev.CallCallable(f, new object[] { new PyNdArray((double[])x.Clone(), new[] { n }) }, null));
            var simplex = new double[n + 1][]; var fv = new double[n + 1];
            for (int i = 0; i <= n; i++) { simplex[i] = (double[])x0.Clone(); if (i > 0) simplex[i][i - 1] += (simplex[i][i - 1] != 0 ? 0.05 * simplex[i][i - 1] : 0.00025); fv[i] = F(simplex[i]); }
            for (int it = 0; it < 400; it++)
            {
                Array.Sort(fv, simplex);
                var cen = new double[n]; for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) cen[i] += simplex[j][i]; cen[i] /= n; }
                var xr = new double[n]; for (int i = 0; i < n; i++) xr[i] = cen[i] + (cen[i] - simplex[n][i]); double fr = F(xr);
                if (fr < fv[0]) { var xe = new double[n]; for (int i = 0; i < n; i++) xe[i] = cen[i] + 2 * (cen[i] - simplex[n][i]); double fe = F(xe); if (fe < fr) { simplex[n] = xe; fv[n] = fe; } else { simplex[n] = xr; fv[n] = fr; } }
                else if (fr < fv[n - 1]) { simplex[n] = xr; fv[n] = fr; }
                else { var xc = new double[n]; for (int i = 0; i < n; i++) xc[i] = cen[i] + 0.5 * (simplex[n][i] - cen[i]); double fc = F(xc); if (fc < fv[n]) { simplex[n] = xc; fv[n] = fc; } else { for (int k = 1; k <= n; k++) { for (int i = 0; i < n; i++) simplex[k][i] = simplex[0][i] + 0.5 * (simplex[k][i] - simplex[0][i]); fv[k] = F(simplex[k]); } } }
                if (Math.Abs(fv[n] - fv[0]) < 1e-12) break;
            }
            Array.Sort(fv, simplex);
            return new PySciResult(("x", new PyNdArray(simplex[0], new[] { n })), ("fun", fv[0]), ("success", true));
        }
        private static object CurveFit(PythonEvaluator ev, object[] a, PyDict kw)
        {
            // curve_fit(f, xdata, ydata, p0) — Gauss-Newton sobre residuales r(p)=f(x,p)-y
            object f = a[0]; var xd = Arr(a[1]).Data; var yd = Arr(a[2]).Data;
            double[] p = a.Length > 3 ? (double[])Arr(a[3]).Data.Clone() : new double[] { 1, 1, 1 };
            int np = p.Length, m = xd.Length;
            double[] Resid(double[] pp) { var r = new double[m]; for (int i = 0; i < m; i++) r[i] = PyOps.ToDouble(ev.CallCallable(f, new object[] { xd[i], new PyNdArray((double[])pp.Clone(), new[] { np }) }, null)) - yd[i]; return r; }
            for (int it = 0; it < 40; it++)
            {
                var r = Resid(p); var J = new double[m * np];
                for (int j = 0; j < np; j++) { double h = 1e-7 * (Math.Abs(p[j]) + 1e-7); var pp = (double[])p.Clone(); pp[j] += h; var r2 = Resid(pp); for (int i = 0; i < m; i++) J[i * np + j] = (r2[i] - r[i]) / h; }
                var Jt = Transpose(J, m, np); var JtJ = MatMulMN(Jt, np, m, J, np); var Jtr = MatMulMN(Jt, np, m, r, 1);
                var dp = (PyNdArray)PyNumpy.Solve(new PyNdArray(JtJ, new[] { np, np }), new PyNdArray(Jtr, new[] { np }));
                double step = 0; for (int j = 0; j < np; j++) { p[j] -= dp.Data[j]; step += dp.Data[j] * dp.Data[j]; }
                if (Math.Sqrt(step) < 1e-12) break;
            }
            return new PyTuple(new List<object> { new PyNdArray(p, new[] { np }), new PyNdArray(new double[np * np], new[] { np, np }) });
        }

        // ---- integrate solve_ivp / cumtrapz ----
        private static object Cumtrapz(object[] a)
        {
            var y = Arr(a[0]).Data; double[] x = a.Length > 1 && a[1] != null ? Arr(a[1]).Data : null;
            var r = new double[y.Length]; for (int i = 1; i < y.Length; i++) { double dx = x != null ? x[i] - x[i - 1] : 1.0; r[i] = r[i - 1] + 0.5 * (y[i] + y[i - 1]) * dx; }
            var outp = new double[y.Length - 1]; Array.Copy(r, 1, outp, 0, y.Length - 1);
            return new PyNdArray(outp, new[] { y.Length - 1 });
        }
        private static object SolveIvp(PythonEvaluator ev, object[] a, PyDict kw)
        {
            // solve_ivp(fun(t,y), (t0,tf), y0, t_eval=...) RK4. res.t, res.y (ny × nt).
            object f = a[0]; var ts = (PyTuple)a[1]; double t0 = PyOps.ToDouble(ts.Items[0]), tf = PyOps.ToDouble(ts.Items[1]);
            var y0 = Arr(a[2]).Data; int ny = y0.Length;
            double[] te; if (kw != null && kw.TryGet("t_eval", out var tev)) te = Arr(tev).Data; else { te = new double[100]; for (int i = 0; i < 100; i++) te[i] = t0 + (tf - t0) * i / 99.0; }
            int nt = te.Length; var Y = new double[ny * nt]; var y = (double[])y0.Clone(); for (int j = 0; j < ny; j++) Y[j * nt + 0] = y[j];
            double[] Dr(double t, double[] yy) => Arr(ev.CallCallable(f, new object[] { t, new PyNdArray((double[])yy.Clone(), new[] { ny }) }, null)).Data;
            for (int i = 1; i < nt; i++)
            {
                double h = te[i] - te[i - 1], t = te[i - 1];
                var k1 = Dr(t, y); var k2 = Dr(t + h / 2, Add(y, k1, h / 2)); var k3 = Dr(t + h / 2, Add(y, k2, h / 2)); var k4 = Dr(t + h, Add(y, k3, h));
                for (int j = 0; j < ny; j++) { y[j] += h / 6 * (k1[j] + 2 * k2[j] + 2 * k3[j] + k4[j]); Y[j * nt + i] = y[j]; }
            }
            return new PySciResult(("t", new PyNdArray(te, new[] { nt })), ("y", new PyNdArray(Y, new[] { ny, nt })), ("success", true));
        }

        // ---- interpolate CubicSpline (natural) -> callable ----
        private static object CubicSpline(object[] a)
        {
            var x = (double[])Arr(a[0]).Data.Clone(); var y = (double[])Arr(a[1]).Data.Clone(); int n = x.Length;
            var h = new double[n - 1]; for (int i = 0; i < n - 1; i++) h[i] = x[i + 1] - x[i];
            var al = new double[n]; for (int i = 1; i < n - 1; i++) al[i] = 3 * ((y[i + 1] - y[i]) / h[i] - (y[i] - y[i - 1]) / h[i - 1]);
            var l = new double[n]; var mu = new double[n]; var z = new double[n]; l[0] = 1;
            for (int i = 1; i < n - 1; i++) { l[i] = 2 * (x[i + 1] - x[i - 1]) - h[i - 1] * mu[i - 1]; mu[i] = h[i] / l[i]; z[i] = (al[i] - h[i - 1] * z[i - 1]) / l[i]; }
            l[n - 1] = 1; var c = new double[n]; var bb = new double[n]; var dd = new double[n];
            for (int j = n - 2; j >= 0; j--) { c[j] = z[j] - mu[j] * c[j + 1]; bb[j] = (y[j + 1] - y[j]) / h[j] - h[j] * (c[j + 1] + 2 * c[j]) / 3; dd[j] = (c[j + 1] - c[j]) / (3 * h[j]); }
            return new PyBuiltin("CubicSpline", (b, kw) =>
            {
                var q = Arr(b[0]); var outp = new double[q.Size];
                for (int m = 0; m < q.Size; m++) { double xq = q.Data[m]; int i = 0; while (i < n - 2 && x[i + 1] < xq) i++; double dx = xq - x[i]; outp[m] = y[i] + bb[i] * dx + c[i] * dx * dx + dd[i] * dx * dx * dx; }
                return q.Size == 1 ? (object)outp[0] : new PyNdArray(outp, new[] { q.Size });
            });
        }

        private static double Erfinv(double x)
        {
            double w = -Math.Log((1 - x) * (1 + x)), p;
            if (w < 5) { w -= 2.5; p = 2.81022636e-08; p = 3.43273939e-07 + p * w; p = -3.5233877e-06 + p * w; p = -4.39150654e-06 + p * w; p = 0.00021858087 + p * w; p = -0.00125372503 + p * w; p = -0.00417768164 + p * w; p = 0.246640727 + p * w; p = 1.50140941 + p * w; }
            else { w = Math.Sqrt(w) - 3; p = -0.000200214257; p = 0.000100950558 + p * w; p = 0.00134934322 + p * w; p = -0.00367342844 + p * w; p = 0.00573950773 + p * w; p = -0.0076224613 + p * w; p = 0.00943887047 + p * w; p = 1.00167406 + p * w; p = 2.83297682 + p * w; }
            return p * x;
        }
        private static object MakeNorm()
        {
            return new PySciResult(
                ("pdf", new PyBuiltin("pdf", (a, kw) => MapS(a[0], x => Math.Exp(-x * x / 2) / Math.Sqrt(2 * Math.PI)))),
                ("cdf", new PyBuiltin("cdf", (a, kw) => MapS(a[0], x => 0.5 * (1 + Erf(x / Math.Sqrt(2)))))),
                ("ppf", new PyBuiltin("ppf", (a, kw) => MapS(a[0], p => Math.Sqrt(2) * Erfinv(2 * p - 1)))),
                ("sf", new PyBuiltin("sf", (a, kw) => MapS(a[0], x => 0.5 * (1 - Erf(x / Math.Sqrt(2))))))
            );
        }
    }

    // Objeto de resultado tipo scipy (res.x, res.fun, res.t, res.y, norm.cdf...) — namespace con atributos.
    public sealed class PySciResult
    {
        public readonly Dictionary<string, object> Attrs = new();
        public PySciResult(params (string, object)[] kv) { foreach (var (k, v) in kv) Attrs[k] = v; }
    }
}
