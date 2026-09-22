# Referencia SOLO para Python real: replica el visor THREE.js de Suite Py con matplotlib
# (misma extraccion de piel, jet_r, misma camara ~ THREE.js default). Suite Py NO importa esto
# (usa su builtin solid3d_viewer).
import numpy as np, matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt, matplotlib.cm as cm
from matplotlib.colors import Normalize
from mpl_toolkits.mplot3d.art3d import Poly3DCollection
from collections import defaultdict

def solid3d_viewer(nodes, elems, fields, title=""):
    ND = np.array(nodes, dtype=float)
    TetF = [[0,1,2],[0,1,3],[0,2,3],[1,2,3]]
    HexF = [[0,1,2,3],[4,5,6,7],[0,1,5,4],[1,2,6,5],[2,3,7,6],[3,0,4,7]]
    cnt = defaultdict(int); rep = {}
    for el in elems:
        F = TetF if len(el) == 4 else HexF
        for fd in F:
            nds = [el[q] for q in fd]; key = tuple(sorted(nds))
            cnt[key] += 1
            if key not in rep: rep[key] = nds
    tris = []
    for key, c in cnt.items():
        if c == 1:
            nd = rep[key]; tris.append(nd[:3])
            if len(nd) == 4: tris.append([nd[0], nd[2], nd[3]])
    tris = np.array(tris)
    name = list(fields.keys())[0]; V = np.array(fields[name], dtype=float)
    vn, vx = V.min(), V.max()
    fc = V[tris].mean(1)
    cols = cm.jet(1 - Normalize(vn, vx)(fc))         # jet_r = como el visor THREE.js (jt(1-t))
    fig = plt.figure(figsize=(5.6, 4.8)); ax = fig.add_subplot(111, projection='3d')
    ax.add_collection3d(Poly3DCollection(ND[tris], facecolors=cols, edgecolors='none'))
    mn = ND.min(0); mx = ND.max(0)
    ax.set_xlim(mn[0], mx[0]); ax.set_ylim(mn[1], mx[1]); ax.set_zlim(mn[2], mx[2]); ax.set_box_aspect((1,1,1))
    ax.view_init(elev=28, azim=-54); ax.set_axis_off(); ax.set_title(title)
    plt.savefig(r'C:\tmp\realpy_solid.png', dpi=95, bbox_inches='tight')
    print('realpy_solid.png guardado (Python real)')
