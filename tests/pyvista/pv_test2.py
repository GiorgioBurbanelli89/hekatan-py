import numpy as np
import pyvista as pv
#" Dos hexaedros con dano por celda y deformada
pts = np.array([[0,0,0],[1,0,0],[1,1,0],[0,1,0],[0,0,1],[1,0,1],[1,1,1],[0,1,1],
                [2,0,0],[2,1,0],[2,0,1],[2,1,1]], dtype=float)
cells = np.array([8, 0,1,2,3,4,5,6,7,  8, 1,8,9,2,5,10,11,6])
types = np.array([pv.CellType.HEXAHEDRON, pv.CellType.HEXAHEDRON])
grid = pv.UnstructuredGrid(cells, types, pts)
grid.cell_data["dano"] = np.array([0.2, 0.8])
u = np.zeros((12, 3)); u[:, 0] = 0.1 * pts[:, 2]
grid.point_data["u"] = u
warped = grid.warp_by_vector("u", factor=2.0)
pl = pv.Plotter()
pl.add_mesh(warped, scalars="dano", show_edges=True)
pl.add_text("Hexaedros deformados")
pl.show()
print("n_points", grid.n_points, "n_cells", grid.n_cells)
