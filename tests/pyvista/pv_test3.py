import numpy as np
import pyvista as pv
#" Superficie z = sin(x) cos(y) y un cilindro
x = np.linspace(-3, 3, 30); y = np.linspace(-3, 3, 30)
X, Y = np.meshgrid(x, y); Z = np.sin(X) * np.cos(Y)
surf = pv.StructuredGrid(X, Y, Z)
surf.point_data["z"] = Z.ravel()
surf.plot(scalars="z")
cyl = pv.Cylinder(center=(0, 0, 0), direction=(0, 0, 1), radius=1.0, height=3.0, resolution=40)
cyl.plot()
print("puntos cilindro", cyl.n_points)
