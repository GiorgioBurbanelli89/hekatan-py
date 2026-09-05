import numpy as np
import pyvista as pv
#" Esfera con un campo escalar
sphere = pv.Sphere(radius=1.0, theta_resolution=40, phi_resolution=40)
sphere.point_data["z"] = sphere.points[:, 2]
pl = pv.Plotter()
pl.add_mesh(sphere, scalars="z", cmap="jet_r", show_edges=True)
pl.show()
print("listo")
