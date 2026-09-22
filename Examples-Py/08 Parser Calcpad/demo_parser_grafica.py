#"Superficie 3D con parser Calcpad + visor orbit
#'Combina markup Calcpad (`#'`, `#cp`) con computo numpy y un **visor 3D orbitable**.
import numpy as np

#"Funcion
#'Superficie de una membrana con carga puntual central (forma de campana):
#cp w = A*exp(-(x^2 + y^2)/(2*s^2))

A = 1.0      # amplitud
s = 1.2      # dispersion
n = 21       # nodos por lado
L = 4.0      # semilado del dominio

# malla estructurada
xs = np.linspace(-L, L, n)
ys = np.linspace(-L, L, n)
nodes = []
w = []
for j in range(n):
    for i in range(n):
        x = float(xs[i]); y = float(ys[j])
        z = A * np.exp(-(x*x + y*y) / (2*s*s))
        nodes.append([x, y, float(z)])
        w.append(float(z))

# caras quad (0-based) de la malla estructurada
faces = []
for j in range(n - 1):
    for i in range(n - 1):
        a = j*n + i
        faces.append([a, a + 1, a + n + 1, a + n])

wmax = max(w)
print("nodos =", len(nodes), " caras =", len(faces), " w_max =", round(wmax, 4))

#'Flecha maxima en el centro:
#cp w_max = A

#hide
# ── Visor 3D que funciona en LOS DOS entornos ────────────────────────────────
# Hekatan Py trae el builtin mesh3d_viewer (canvas orbitable, jet_r). CPython no
# lo tiene, asi que ahi se dibuja la MISMA malla y el MISMO campo con matplotlib
# 3D. El mismo .py corre y GRAFICA en los dos.
def show_mesh3d(nodes, faces, fields, title=""):
    try:
        mesh3d_viewer(nodes, faces, fields, title)
        return
    except NameError:
        pass
    import numpy as _np
    import matplotlib.pyplot as _plt
    from matplotlib import cm as _cm
    from matplotlib import colors as _colors
    from mpl_toolkits.mplot3d.art3d import Poly3DCollection as _Poly3D
    P = _np.asarray(nodes, dtype=float)
    name = list(fields.keys())[0]
    W = _np.asarray(fields[name], dtype=float)
    polys = []
    vals = []
    for fc in faces:
        idx = [int(i) for i in fc]
        polys.append([[P[i][0], P[i][1], P[i][2]] for i in idx])
        s = 0.0
        for i in idx:
            s += W[i]
        vals.append(s / len(idx))
    vals = _np.asarray(vals)
    norm = _colors.Normalize(vals.min(), vals.max())
    fig = _plt.figure(figsize=(7.5, 5.5))
    ax = fig.add_subplot(111, projection='3d')
    ax.add_collection3d(_Poly3D(polys, facecolors=_cm.jet_r(norm(vals)),
                                edgecolor='#00000033', linewidths=0.15))
    lo = P.min(axis=0)
    hi = P.max(axis=0)
    d = hi - lo
    pad = d * 0.05 + 1e-9
    ax.set_xlim(lo[0] - pad[0], hi[0] + pad[0])
    ax.set_ylim(lo[1] - pad[1], hi[1] + pad[1])
    if d[2] < 1e-12:
        ax.set_zlim(-1.0, 1.0)      # malla plana (mapa de color):
        ax.set_zticks([])           # se mira en planta, como el mapa 2D
        ax.view_init(elev=90, azim=-90)
    else:
        ax.set_zlim(lo[2] - pad[2], hi[2] + pad[2])
    ax.set_box_aspect((max(d[0], 1e-9), max(d[1], 1e-9),
                       max(d[2], (d[0] + d[1]) * 0.15)))
    ax.set_xlabel('x [m]')
    ax.set_ylabel('y [m]')
    ax.set_title(title)
    sm = _cm.ScalarMappable(norm=norm, cmap='jet_r')
    sm.set_array(vals)
    fig.colorbar(sm, ax=ax, shrink=0.7, pad=0.12).set_label(name)
    ax._surf3d = (P[:, 0], P[:, 1], P[:, 2], W)   # datatip de _hover() si existe
    _plt.show()
#show
show_mesh3d(nodes, faces, {"w": w}, "Membrana - campana gaussiana (w_max %.3f)" % wmax)
