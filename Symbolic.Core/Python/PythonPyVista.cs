// pyvista EMBEBIDO (2026-09-05, Jorge: "vamos a embeber pyvista en hekatan python").
// Subconjunto NATIVO de PyVista, sin Python externo: las mallas se construyen en C# y
// Plotter.show() las pinta con el visor 3D orbitable ya embebido (PythonViz.Solid3DViewer:
// THREE.js + OrbitControls, jet_r, hover con el valor). Lo que no esta aqui lanza
// PythonNotSupported -> el pipeline cae al Python real (que si tiene PyVista completo).
//
// Cubre lo que usan los guiones FEM:
//   pv.PolyData(points[, faces])           faces en formato VTK plano [n, i, j, k, ...]
//   pv.UnstructuredGrid(cells, celltypes, points)  |  pv.UnstructuredGrid({tipo: conn}, points)
//   pv.StructuredGrid(X, Y, Z)             mallas 2D (superficies z = f(x, y))
//   pv.Sphere / Cube / Plane / Cylinder    primitivas
//   mesh.points, .faces, .n_points, .n_cells, .point_data[...], .cell_data[...], .bounds, .center,
//   mesh.copy(), .triangulate(), .extract_surface(), .warp_by_vector(vec, factor), .plot(...)
//   pv.Plotter(...).add_mesh(mesh, scalars=..., ...) / .show() / .add_text / .show_grid ... (no-op)
using System;
using System.Collections.Generic;

namespace Calcpad.Core.Python
{
    internal static class PythonPyVista
    {
        /// <summary>Malla en C#: caras de superficie (triangulos) + celdas originales (para cell_data → nodo).</summary>
        private sealed class MeshData
        {
            public List<int[]> Tris = new();      // triangulos de la superficie (indices de puntos)
            public List<int> TriCell = new();     // celda de origen de cada triangulo (para cell_data plano)
            public List<int[]> Cells = new();     // celdas tal como las dio el usuario (caras o volumenes)
            public string Kind = "PolyData";
        }

        private sealed class MeshEntry
        {
            public PyInstance Mesh; public string ScalarName; public double[] Scalars; public bool IsCell;
        }

        private static PyClass _polyData, _ugrid, _sgrid, _plotter;

        public static PyModule CreateModule(Action<string> htmlOut, Func<string> newId)
        {
            var pv = new PyModule("pyvista");
            _htmlOut = htmlOut; _newId = newId;   // mesh.plot() sin Plotter previo (antes NullReference)
            _polyData ??= MakeClass("PolyData"); _ugrid ??= MakeClass("UnstructuredGrid");
            _sgrid ??= MakeClass("StructuredGrid"); _plotter ??= MakeClass("Plotter");
            pv.Attrs["OFF_SCREEN"] = false;
            pv.Attrs["__version__"] = "0.46-hekatan";
            pv.Attrs["set_plot_theme"] = new PyBuiltin("set_plot_theme", (a, kw) => null);
            pv.Attrs["global_theme"] = new PyModule("pyvista.global_theme");
            pv.Attrs["PolyData"] = new PyBuiltin("PolyData", (a, kw) => PolyData(a, kw));
            pv.Attrs["UnstructuredGrid"] = new PyBuiltin("UnstructuredGrid", (a, kw) => UnstructuredGrid(a, kw));
            pv.Attrs["StructuredGrid"] = new PyBuiltin("StructuredGrid", (a, kw) => StructuredGrid(a, kw));
            pv.Attrs["Sphere"] = new PyBuiltin("Sphere", (a, kw) => Sphere(a, kw));
            pv.Attrs["Cube"] = new PyBuiltin("Cube", (a, kw) => Cube(a, kw));
            pv.Attrs["Plane"] = new PyBuiltin("Plane", (a, kw) => Plane(a, kw));
            pv.Attrs["Cylinder"] = new PyBuiltin("Cylinder", (a, kw) => Cylinder(a, kw));
            pv.Attrs["Plotter"] = new PyBuiltin("Plotter", (a, kw) => Plotter(htmlOut, newId));
            // Tipos de celda VTK (pv.CellType.HEXAHEDRON, ...)
            var ct = new PyModule("pyvista.CellType");
            foreach (var (n, v) in new[] { ("TRIANGLE", 5L), ("QUAD", 9L), ("TETRA", 10L), ("HEXAHEDRON", 12L), ("WEDGE", 13L), ("PYRAMID", 14L), ("QUADRATIC_TETRA", 24L), ("QUADRATIC_HEXAHEDRON", 25L) })
                ct.Attrs[n] = v;
            pv.Attrs["CellType"] = ct;
            return pv;
        }

        private static PyClass MakeClass(string name)
        {
            var c = new PyClass { Name = name };
            c.Attrs["__pyvista__"] = true;   // el evaluador: atributo desconocido -> PythonNotSupported (cae a Python real)
            return c;
        }

        // ───────────────────────── helpers ─────────────────────────
        private static object Kw(PyDict kw, string name, object def) => kw != null && kw.TryGet(name, out var v) ? v : def;
        private static double D(object o) => PyOps.ToDouble(o);
        private static int I(object o) => (int)PyOps.ToLong(o);

        private static double[] Vec3(object o, double[] def)
        {
            if (o == null) return def;
            if (o is PyNdArray a && a.Data.Length >= 3) return new[] { a.Data[0], a.Data[1], a.Data[2] };
            var l = o is PyList pl ? pl.Items : o is PyTuple pt ? pt.Items : null;
            if (l == null || l.Count < 3) throw new PyRuntimeError("TypeError", "se esperaba (x, y, z)");
            return new[] { D(l[0]), D(l[1]), D(l[2]) };
        }

        /// <summary>Puntos n×3 desde ndarray o lista de listas.</summary>
        private static double[][] Points(object o)
        {
            var a = PyNumpy.AsArr(o);
            if (a.Ndim != 2 || a.Cols < 2) throw new PyRuntimeError("ValueError", "points debe ser un array n x 3");
            var p = new double[a.Rows][];
            for (int i = 0; i < a.Rows; i++)
                p[i] = new[] { a.Data[i * a.Cols], a.Data[i * a.Cols + 1], a.Cols > 2 ? a.Data[i * a.Cols + 2] : 0.0 };
            return p;
        }

        private static PyNdArray PointsArray(double[][] p)
        {
            var d = new double[p.Length * 3];
            for (int i = 0; i < p.Length; i++) { d[3 * i] = p[i][0]; d[3 * i + 1] = p[i][1]; d[3 * i + 2] = p[i][2]; }
            return new PyNdArray(d, new[] { p.Length, 3 });
        }

        private static long[] Ints(object o)
        {
            var a = PyNumpy.AsArr(o); var r = new long[a.Data.Length];
            for (int i = 0; i < r.Length; i++) r[i] = (long)Math.Round(a.Data[i]);
            return r;
        }

        /// <summary>Celdas en formato VTK plano [n, i, j, k, ...] → lista de celdas.</summary>
        private static List<int[]> VtkCells(long[] flat)
        {
            var cells = new List<int[]>(); int i = 0;
            while (i < flat.Length)
            {
                int n = (int)flat[i]; if (n <= 0 || i + n >= flat.Length + 1) break;
                var c = new int[n]; for (int k = 0; k < n; k++) c[k] = (int)flat[i + 1 + k];
                cells.Add(c); i += n + 1;
            }
            return cells;
        }

        private static void AddPoly(List<int[]> tris, int[] poly, List<int> triCell = null, int cell = -1)
        {
            int before = tris.Count;
            if (poly.Length == 3) tris.Add(new[] { poly[0], poly[1], poly[2] });
            else if (poly.Length >= 4) for (int k = 1; k + 1 < poly.Length; k++) tris.Add(new[] { poly[0], poly[k], poly[k + 1] });
            if (triCell != null) for (int i = before; i < tris.Count; i++) triCell.Add(cell);
        }
        private static void AddCellsAsFaces(MeshData md)
        {
            for (int c = 0; c < md.Cells.Count; c++) AddPoly(md.Tris, md.Cells[c], md.TriCell, c);
        }

        /// <summary>Caras de una celda de volumen (esquinas; los nodos medios de las cuadraticas se ignoran).</summary>
        private static List<int[]> CellFaces(int[] c, int type)
        {
            var f = new List<int[]>();
            switch (type)
            {
                case 10: case 24:   // tetra (4 esquinas)
                    f.Add(new[] { c[0], c[2], c[1] }); f.Add(new[] { c[0], c[1], c[3] }); f.Add(new[] { c[1], c[2], c[3] }); f.Add(new[] { c[0], c[3], c[2] }); break;
                case 12: case 25:   // hexaedro (8 esquinas)
                    f.Add(new[] { c[0], c[3], c[2], c[1] }); f.Add(new[] { c[4], c[5], c[6], c[7] }); f.Add(new[] { c[0], c[1], c[5], c[4] });
                    f.Add(new[] { c[1], c[2], c[6], c[5] }); f.Add(new[] { c[2], c[3], c[7], c[6] }); f.Add(new[] { c[3], c[0], c[4], c[7] }); break;
                case 13:            // wedge
                    f.Add(new[] { c[0], c[2], c[1] }); f.Add(new[] { c[3], c[4], c[5] }); f.Add(new[] { c[0], c[1], c[4], c[3] }); f.Add(new[] { c[1], c[2], c[5], c[4] }); f.Add(new[] { c[2], c[0], c[3], c[5] }); break;
                case 14:            // piramide
                    f.Add(new[] { c[0], c[3], c[2], c[1] }); f.Add(new[] { c[0], c[1], c[4] }); f.Add(new[] { c[1], c[2], c[4] }); f.Add(new[] { c[2], c[3], c[4] }); f.Add(new[] { c[3], c[0], c[4] }); break;
                case 5: case 9: case 7: f.Add(c); break;   // triangulo / cuadrilatero / poligono: la celda es la cara
                default: throw new PythonNotSupported($"pyvista: tipo de celda VTK {type}");
            }
            return f;
        }

        /// <summary>Piel exterior: caras que aparecen una sola vez.</summary>
        private static void Skin(MeshData md, List<int> types)
        {
            var count = new Dictionary<string, (int[] face, int n, int cell)>();
            for (int e = 0; e < md.Cells.Count; e++)
                foreach (var face in CellFaces(md.Cells[e], types[e]))
                {
                    var s = (int[])face.Clone(); Array.Sort(s); var key = string.Join(",", s);
                    count[key] = count.TryGetValue(key, out var v) ? (v.face, v.n + 1, v.cell) : (face, 1, e);
                }
            foreach (var v in count.Values) if (v.n == 1) AddPoly(md.Tris, v.face, md.TriCell, v.cell);
        }

        // ───────────────────────── mallas ─────────────────────────
        private static PyInstance NewMesh(PyClass cls, double[][] pts, MeshData md)
        {
            var inst = new PyInstance { Class = cls };
            inst.Attrs["points"] = PointsArray(pts);
            inst.Attrs["point_data"] = new PyDict();
            inst.Attrs["cell_data"] = new PyDict();
            inst.Attrs["__mesh"] = md;
            RefreshCounts(inst);
            inst.Attrs["copy"] = new PyBuiltin("copy", (a, kw) => Copy(inst));
            inst.Attrs["triangulate"] = new PyBuiltin("triangulate", (a, kw) => inst);
            inst.Attrs["extract_surface"] = new PyBuiltin("extract_surface", (a, kw) => inst);
            inst.Attrs["compute_normals"] = new PyBuiltin("compute_normals", (a, kw) => inst);
            inst.Attrs["cell_data_to_point_data"] = new PyBuiltin("cell_data_to_point_data", (a, kw) => inst);
            inst.Attrs["outline"] = new PyBuiltin("outline", (a, kw) => NewMesh(_polyData, Array.Empty<double[]>(), new MeshData()));
            inst.Attrs["warp_by_vector"] = new PyBuiltin("warp_by_vector", (a, kw) => WarpByVector(inst, a, kw));
            inst.Attrs["plot"] = new PyBuiltin("plot", (a, kw) => { var pl = Plotter(_htmlOut, _newId); AddMesh(pl, new object[] { inst }, kw); Show(pl); return null; });
            return inst;
        }

        private static Action<string> _htmlOut; private static Func<string> _newId;

        private static void RefreshCounts(PyInstance inst)
        {
            var md = (MeshData)inst.Attrs["__mesh"]; var pts = (PyNdArray)inst.Attrs["points"];
            inst.Attrs["n_points"] = (long)pts.Rows;
            inst.Attrs["n_cells"] = (long)md.Cells.Count;
            inst.Attrs["n_faces"] = (long)md.Cells.Count;
            // faces en formato VTK plano (como PyVista)
            var flat = new List<double>();
            foreach (var c in md.Cells) { flat.Add(c.Length); foreach (var k in c) flat.Add(k); }
            inst.Attrs["faces"] = new PyNdArray(flat.ToArray(), new[] { flat.Count }, true);
            var p = Points(pts);
            double[] lo = { double.MaxValue, double.MaxValue, double.MaxValue }, hi = { double.MinValue, double.MinValue, double.MinValue };
            foreach (var q in p) for (int d = 0; d < 3; d++) { lo[d] = Math.Min(lo[d], q[d]); hi[d] = Math.Max(hi[d], q[d]); }
            if (p.Length == 0) { lo = new double[3]; hi = new double[3]; }
            inst.Attrs["bounds"] = new PyTuple(new List<object> { lo[0], hi[0], lo[1], hi[1], lo[2], hi[2] });
            inst.Attrs["center"] = new PyTuple(new List<object> { (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2 });
        }

        private static PyInstance Copy(PyInstance src)
        {
            var md = (MeshData)src.Attrs["__mesh"];
            var nd = new MeshData { Kind = md.Kind }; nd.Tris.AddRange(md.Tris); nd.TriCell.AddRange(md.TriCell); nd.Cells.AddRange(md.Cells);
            var inst = NewMesh(src.Class, Points(src.Attrs["points"]), nd);
            foreach (var key in new[] { "point_data", "cell_data" })
            {
                var s = (PyDict)src.Attrs[key]; var d = (PyDict)inst.Attrs[key];
                for (int i = 0; i < s.Keys.Count; i++) d.Set(s.Keys[i], s.Values[i]);
            }
            return inst;
        }

        private static PyInstance WarpByVector(PyInstance src, object[] a, PyDict kw)
        {
            var name = a.Length > 0 ? a[0] : Kw(kw, "vectors", null);
            double factor = D(a.Length > 1 ? a[1] : Kw(kw, "factor", 1.0));
            var pts = Points(src.Attrs["points"]);
            object vecObj = name is string sn ? (((PyDict)src.Attrs["point_data"]).TryGet(sn, out var v) ? v : throw new PyRuntimeError("KeyError", sn)) : name;
            var vec = Points(vecObj);
            if (vec.Length != pts.Length) throw new PyRuntimeError("ValueError", "warp_by_vector: el vector debe ser n_points x 3");
            var np = new double[pts.Length][];
            for (int i = 0; i < pts.Length; i++) np[i] = new[] { pts[i][0] + factor * vec[i][0], pts[i][1] + factor * vec[i][1], pts[i][2] + factor * vec[i][2] };
            var c = Copy(src); c.Attrs["points"] = PointsArray(np); RefreshCounts(c);
            return c;
        }

        private static PyInstance PolyData(object[] a, PyDict kw)
        {
            var pts = a.Length > 0 ? Points(a[0]) : Array.Empty<double[]>();
            var md = new MeshData();
            var facesObj = a.Length > 1 ? a[1] : Kw(kw, "faces", null);
            if (facesObj != null)
            {
                var fa = PyNumpy.AsArr(facesObj);
                if (fa.Ndim == 2)   // n x k: cada fila una cara
                    for (int r = 0; r < fa.Rows; r++) { var c = new int[fa.Cols]; for (int k = 0; k < fa.Cols; k++) c[k] = (int)Math.Round(fa.Data[r * fa.Cols + k]); md.Cells.Add(c); }
                else md.Cells = VtkCells(Ints(facesObj));
                AddCellsAsFaces(md);
            }
            return NewMesh(_polyData, pts, md);
        }

        private static PyInstance UnstructuredGrid(object[] a, PyDict kw)
        {
            var md = new MeshData { Kind = "UnstructuredGrid" }; var types = new List<int>(); double[][] pts;
            if (a.Length == 2 && a[0] is PyDict dict)          // {tipo: conn n x k}
            {
                for (int i = 0; i < dict.Keys.Count; i++)
                {
                    int t = I(dict.Keys[i]); var conn = PyNumpy.AsArr(dict.Values[i]);
                    int k = conn.Ndim == 2 ? conn.Cols : conn.Data.Length; int n = conn.Ndim == 2 ? conn.Rows : 1;
                    for (int r = 0; r < n; r++) { var c = new int[k]; for (int j = 0; j < k; j++) c[j] = (int)Math.Round(conn.Data[r * k + j]); md.Cells.Add(c); types.Add(t); }
                }
                pts = Points(a[1]);
            }
            else if (a.Length >= 3)                             // (cells VTK plano, celltypes, points)
            {
                md.Cells = VtkCells(Ints(a[0])); foreach (var t in Ints(a[1])) types.Add((int)t); pts = Points(a[2]);
                if (types.Count != md.Cells.Count) throw new PyRuntimeError("ValueError", "UnstructuredGrid: celltypes no coincide con cells");
            }
            else throw new PythonNotSupported("pyvista.UnstructuredGrid: forma de llamada");
            Skin(md, types);
            var inst = NewMesh(_ugrid, pts, md); inst.Attrs["__types"] = types;
            return inst;
        }

        private static PyInstance StructuredGrid(object[] a, PyDict kw)
        {
            if (a.Length < 3) throw new PythonNotSupported("pyvista.StructuredGrid: se esperan X, Y, Z 2D");
            var X = PyNumpy.AsArr(a[0]); var Y = PyNumpy.AsArr(a[1]); var Z = PyNumpy.AsArr(a[2]);
            if (X.Ndim != 2 || Y.Ndim != 2 || Z.Ndim != 2) throw new PythonNotSupported("pyvista.StructuredGrid 3D");
            int r = X.Rows, c = X.Cols; var pts = new double[r * c][];
            for (int i = 0; i < r; i++) for (int j = 0; j < c; j++) pts[i * c + j] = new[] { X.Data[i * c + j], Y.Data[i * c + j], Z.Data[i * c + j] };
            var md = new MeshData { Kind = "StructuredGrid" };
            for (int i = 0; i + 1 < r; i++) for (int j = 0; j + 1 < c; j++) md.Cells.Add(new[] { i * c + j, i * c + j + 1, (i + 1) * c + j + 1, (i + 1) * c + j });
            AddCellsAsFaces(md);
            return NewMesh(_sgrid, pts, md);
        }

        // ───────────────────────── primitivas ─────────────────────────
        private static PyInstance Sphere(object[] a, PyDict kw)
        {
            double R = D(a.Length > 0 ? a[0] : Kw(kw, "radius", 0.5)); var ce = Vec3(a.Length > 1 ? a[1] : Kw(kw, "center", null), new double[3]);
            int nt = I(Kw(kw, "theta_resolution", 30L)), np = I(Kw(kw, "phi_resolution", 30L));
            var pts = new List<double[]> { new[] { ce[0], ce[1], ce[2] + R }, new[] { ce[0], ce[1], ce[2] - R } };
            for (int i = 1; i < np; i++)
            {
                double ph = Math.PI * i / np;
                for (int j = 0; j < nt; j++) { double th = 2 * Math.PI * j / nt; pts.Add(new[] { ce[0] + R * Math.Sin(ph) * Math.Cos(th), ce[1] + R * Math.Sin(ph) * Math.Sin(th), ce[2] + R * Math.Cos(ph) }); }
            }
            var md = new MeshData(); int Id(int i, int j) => 2 + (i - 1) * nt + (j % nt);
            for (int j = 0; j < nt; j++) { md.Cells.Add(new[] { 0, Id(1, j), Id(1, j + 1) }); md.Cells.Add(new[] { 1, Id(np - 1, j + 1), Id(np - 1, j) }); }
            for (int i = 1; i + 1 < np; i++) for (int j = 0; j < nt; j++) md.Cells.Add(new[] { Id(i, j), Id(i + 1, j), Id(i + 1, j + 1), Id(i, j + 1) });
            AddCellsAsFaces(md);
            return NewMesh(_polyData, pts.ToArray(), md);
        }

        private static PyInstance Cube(object[] a, PyDict kw)
        {
            var ce = Vec3(a.Length > 0 ? a[0] : Kw(kw, "center", null), new double[3]);
            double lx = D(Kw(kw, "x_length", 1.0)), ly = D(Kw(kw, "y_length", 1.0)), lz = D(Kw(kw, "z_length", 1.0));
            var pts = new double[8][]; int k = 0;
            foreach (var z in new[] { -1, 1 }) foreach (var (x, y) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                pts[k++] = new[] { ce[0] + x * lx / 2, ce[1] + y * ly / 2, ce[2] + z * lz / 2 };
            var md = new MeshData();
            foreach (var f in CellFaces(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, 12)) md.Cells.Add(f);
            AddCellsAsFaces(md);
            return NewMesh(_polyData, pts, md);
        }

        private static PyInstance Plane(object[] a, PyDict kw)
        {
            var ce = Vec3(a.Length > 0 ? a[0] : Kw(kw, "center", null), new double[3]);
            double si = D(Kw(kw, "i_size", 1.0)), sj = D(Kw(kw, "j_size", 1.0)); int ni = I(Kw(kw, "i_resolution", 10L)), nj = I(Kw(kw, "j_resolution", 10L));
            var pts = new double[(ni + 1) * (nj + 1)][];
            for (int j = 0; j <= nj; j++) for (int i = 0; i <= ni; i++) pts[j * (ni + 1) + i] = new[] { ce[0] - si / 2 + si * i / ni, ce[1] - sj / 2 + sj * j / nj, ce[2] };
            var md = new MeshData();
            for (int j = 0; j < nj; j++) for (int i = 0; i < ni; i++) md.Cells.Add(new[] { j * (ni + 1) + i, j * (ni + 1) + i + 1, (j + 1) * (ni + 1) + i + 1, (j + 1) * (ni + 1) + i });
            AddCellsAsFaces(md);
            return NewMesh(_polyData, pts, md);
        }

        private static PyInstance Cylinder(object[] a, PyDict kw)
        {
            var ce = Vec3(a.Length > 0 ? a[0] : Kw(kw, "center", null), new double[3]); var dir = Vec3(Kw(kw, "direction", null), new[] { 1.0, 0, 0 });
            double R = D(Kw(kw, "radius", 0.5)), H = D(Kw(kw, "height", 1.0)); int n = I(Kw(kw, "resolution", 100L)); bool cap = PyOps.Truthy(Kw(kw, "capping", true));
            double L = Math.Sqrt(dir[0] * dir[0] + dir[1] * dir[1] + dir[2] * dir[2]); if (L < 1e-12) L = 1; var d = new[] { dir[0] / L, dir[1] / L, dir[2] / L };
            var t = Math.Abs(d[0]) < 0.9 ? new[] { 1.0, 0, 0 } : new[] { 0, 1.0, 0 };
            var u = new[] { d[1] * t[2] - d[2] * t[1], d[2] * t[0] - d[0] * t[2], d[0] * t[1] - d[1] * t[0] }; double lu = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]); u = new[] { u[0] / lu, u[1] / lu, u[2] / lu };
            var v = new[] { d[1] * u[2] - d[2] * u[1], d[2] * u[0] - d[0] * u[2], d[0] * u[1] - d[1] * u[0] };
            var pts = new List<double[]>();
            for (int s = -1; s <= 1; s += 2) for (int j = 0; j < n; j++)
            {
                double th = 2 * Math.PI * j / n, cx = R * Math.Cos(th), sy = R * Math.Sin(th);
                pts.Add(new[] { ce[0] + s * H / 2 * d[0] + cx * u[0] + sy * v[0], ce[1] + s * H / 2 * d[1] + cx * u[1] + sy * v[1], ce[2] + s * H / 2 * d[2] + cx * u[2] + sy * v[2] });
            }
            var md = new MeshData();
            for (int j = 0; j < n; j++) md.Cells.Add(new[] { j, (j + 1) % n, n + (j + 1) % n, n + j });
            if (cap) { var b = new int[n]; var tcap = new int[n]; for (int j = 0; j < n; j++) { b[j] = n - 1 - j; tcap[j] = n + j; } md.Cells.Add(b); md.Cells.Add(tcap); }
            AddCellsAsFaces(md);
            return NewMesh(_polyData, pts.ToArray(), md);
        }

        // ───────────────────────── Plotter ─────────────────────────
        private static PyInstance Plotter(Action<string> htmlOut, Func<string> newId)
        {
            _htmlOut = htmlOut; _newId = newId;
            var pl = new PyInstance { Class = _plotter };
            pl.Attrs["__entries"] = new List<MeshEntry>();
            pl.Attrs["__title"] = "";
            pl.Attrs["camera_position"] = "iso";
            pl.Attrs["add_mesh"] = new PyBuiltin("add_mesh", (a, kw) => AddMesh(pl, a, kw));
            pl.Attrs["add_points"] = new PyBuiltin("add_points", (a, kw) => null);
            pl.Attrs["show"] = new PyBuiltin("show", (a, kw) => Show(pl));
            pl.Attrs["add_text"] = new PyBuiltin("add_text", (a, kw) => { if (a.Length > 0 && string.IsNullOrEmpty((string)pl.Attrs["__title"])) pl.Attrs["__title"] = PyOps.Str(a[0]); return null; });
            pl.Attrs["add_title"] = pl.Attrs["add_text"];
            foreach (var noop in new[] { "show_grid", "show_axes", "add_axes", "show_bounds", "set_background", "view_isometric", "view_xy", "view_xz", "view_yz",
                                         "add_scalar_bar", "remove_scalar_bar", "enable_parallel_projection", "subplot", "link_views", "close", "screenshot",
                                         "enable_anti_aliasing", "add_legend", "camera", "reset_camera", "render", "update" })
                pl.Attrs[noop] = new PyBuiltin(noop, (a, kw) => null);
            return pl;
        }

        private static object AddMesh(PyInstance pl, object[] a, PyDict kw)
        {
            if (a.Length < 1 || a[0] is not PyInstance mesh || !mesh.Attrs.ContainsKey("__mesh"))
                throw new PythonNotSupported("pyvista: add_mesh de algo que no es una malla nativa");
            var entries = (List<MeshEntry>)pl.Attrs["__entries"];
            var pd = (PyDict)mesh.Attrs["point_data"]; var cd = (PyDict)mesh.Attrs["cell_data"];
            var sc = Kw(kw, "scalars", null); string name = null; double[] vals = null; bool isCell = false;
            if (sc is string sname)
            {
                name = sname;
                if (pd.TryGet(sname, out var pv)) vals = PyNumpy.AsArr(pv).Data;
                else if (cd.TryGet(sname, out var cv)) { vals = PyNumpy.AsArr(cv).Data; isCell = true; }
                else throw new PyRuntimeError("KeyError", $"scalars '{sname}' no esta en point_data ni cell_data");
            }
            else if (sc != null) { vals = PyNumpy.AsArr(sc).Data; name = "scalars"; }
            else if (pd.Count > 0) { name = PyOps.Str(pd.Keys[pd.Count - 1]); vals = PyNumpy.AsArr(pd.Values[pd.Count - 1]).Data; }
            else if (cd.Count > 0) { name = PyOps.Str(cd.Keys[cd.Count - 1]); vals = PyNumpy.AsArr(cd.Values[cd.Count - 1]).Data; isCell = true; }
            if (vals != null && vals.Length != (isCell ? ((MeshData)mesh.Attrs["__mesh"]).Cells.Count : ((PyNdArray)mesh.Attrs["points"]).Rows))
            {
                int np = ((PyNdArray)mesh.Attrs["points"]).Rows, nc = ((MeshData)mesh.Attrs["__mesh"]).Cells.Count;
                if (vals.Length == nc) isCell = true; else if (vals.Length == np) isCell = false;
                else throw new PyRuntimeError("ValueError", $"scalars: {vals.Length} valores para {np} puntos / {nc} celdas");
            }
            entries.Add(new MeshEntry { Mesh = mesh, ScalarName = name, Scalars = vals, IsCell = isCell });
            return null;
        }

        private static object Show(PyInstance pl)
        {
            var entries = (List<MeshEntry>)pl.Attrs["__entries"];
            var nodes = new List<double[]>(); var tris = new List<int[]>(); var fields = new Dictionary<string, List<double>>(); var order = new List<string>();
            foreach (var e in entries)
            {
                var pts = Points(e.Mesh.Attrs["points"]); var md = (MeshData)e.Mesh.Attrs["__mesh"]; int off = nodes.Count;
                if (md.Tris.Count == 0) continue;                       // nubes de puntos / lineas: no se pintan
                double[] nodal = null;
                if (e.Scalars != null && e.IsCell && md.TriCell.Count == md.Tris.Count)
                {
                    // cell_data PLANO como PyVista: cada triangulo lleva sus 3 nodos propios con el valor de su celda
                    var dupPts = new List<double[]>(); var dupVal = new List<double>();
                    for (int ti = 0; ti < md.Tris.Count; ti++)
                    {
                        var t = md.Tris[ti]; int c = md.TriCell[ti]; double v = c >= 0 && c < e.Scalars.Length ? e.Scalars[c] : 0.0;
                        int b = off + dupPts.Count;
                        dupPts.Add(pts[t[0]]); dupPts.Add(pts[t[1]]); dupPts.Add(pts[t[2]]); dupVal.Add(v); dupVal.Add(v); dupVal.Add(v);
                        tris.Add(new[] { b, b + 1, b + 2 });
                    }
                    pts = dupPts.ToArray(); nodal = dupVal.ToArray(); nodes.AddRange(pts);
                }
                else
                {
                    nodes.AddRange(pts);
                    foreach (var t in md.Tris) tris.Add(new[] { t[0] + off, t[1] + off, t[2] + off });
                    if (e.Scalars != null)
                    {
                        nodal = new double[pts.Length];
                        if (!e.IsCell) Array.Copy(e.Scalars, nodal, Math.Min(pts.Length, e.Scalars.Length));
                        else
                        {
                            var cnt = new int[pts.Length];
                            for (int c = 0; c < md.Cells.Count && c < e.Scalars.Length; c++) foreach (var k in md.Cells[c]) if (k < pts.Length) { nodal[k] += e.Scalars[c]; cnt[k]++; }
                            for (int k = 0; k < pts.Length; k++) if (cnt[k] > 0) nodal[k] /= cnt[k];
                        }
                    }
                }
                string fname = e.ScalarName ?? "";
                if (!fields.ContainsKey(fname)) { fields[fname] = new List<double>(); order.Add(fname); }
                foreach (var f in fields) { while (f.Value.Count < off) f.Value.Add(0.0); }   // rellenar nodos de otras mallas
                for (int k = 0; k < pts.Length; k++) fields[fname].Add(nodal != null ? nodal[k] : 0.0);
            }
            if (tris.Count == 0) { _htmlOut("<p class=\"line\"><i>pyvista: nada que dibujar (sin caras)</i></p>\n"); return null; }
            foreach (var f in fields) while (f.Value.Count < nodes.Count) f.Value.Add(0.0);
            var names = new List<string>(); var vals = new List<double[]>();
            foreach (var n in order) { names.Add(n == "" ? "(sin campo)" : n); vals.Add(fields[n].ToArray()); }
            string title = (string)pl.Attrs["__title"]; if (string.IsNullOrEmpty(title)) title = "PyVista";
            _htmlOut(PythonViz.Solid3DViewer(nodes.ToArray(), tris.ToArray(), names.ToArray(), vals.ToArray(), title, _newId()));
            entries.Clear();
            return null;
        }
    }
}
