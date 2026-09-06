# -*- coding: utf-8 -*-
# GRAFICA de referencia (Python puro + matplotlib) del talud Demo04: campo d_x [mm] del ultimo
# peldano convergido de cada etapa (el que GEO5 tabula), bandas y paleta de GEO5, y la geometria
# encima. El calculo lo hace talud_geo5_hek.py (se importa y corre) y se guarda en talud_U.npz.
# Uso: python talud_geo5_plot.py [nstages]      -> calcula + dibuja  (talud_py_e1..3.png)
#      python talud_geo5_plot.py --plot-only    -> solo dibuja desde talud_U.npz (segundos)
import sys, os
import numpy as np
from talud_plot_lib import plot_stage, SC, names, gfs

NPZ = os.path.join(SC, "talud_U.npz")
if "--plot-only" in sys.argv:
    D = np.load(NPZ); U = list(D["U"]); FS = list(D["FS"]); U1 = list(D["U1"])
else:
    import talud_geo5_hek as TG
    U = TG.U_STAGES; FS = TG.FS; U1 = TG.U_WORK
    np.savez(NPZ, U=np.array(U), FS=np.array(FS), U1=np.array(TG.U_WORK)); print("guardado", NPZ)
for si in range(len(U)):
    plot_stage(si, U[si] - U1[si], FS[si], gfs[si], names[si], os.path.join(SC, "talud_py_e%d.png" % (si + 1)))   # u(FS) - u_elastica, como GEO5
    for campo in ("dz", "d"):   # 2026-09-04: tambien d_z (asiento +) y la resultante, con la escala de GEO5
        plot_stage(si, U[si] - U1[si], FS[si], gfs[si], names[si], os.path.join(SC, "talud_py_e%d_%s.png" % (si + 1, campo)), campo)
