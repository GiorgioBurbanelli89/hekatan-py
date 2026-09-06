# -*- coding: utf-8 -*-
# Libreria del dibujo del talud Demo04 (matplotlib): campo d_x [mm] en bandas GEO5 + geometria
# (malla, contorno, interfaz de suelos, cargas). La usan talud_geo5_plot.py (nuestro campo) y
# plot_geo5_field.py (el campo del solver de GEO5 headless), para comparar con el MISMO dibujo.
import os, math
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap, BoundaryNorm
from matplotlib.collections import LineCollection
import demo04_geo5_fixture as FX

SC = os.path.dirname(os.path.abspath(__file__))
names = ["etapa1 (peso propio)", "etapa2 (+sobrecarga)", "etapa3 (+ancla)"]; gfs = [1.69, 1.48, 1.69]
X = np.array(FX.X); Y = np.array(FX.Y); ELE = np.array(FX.ELE); EMAT = np.array(FX.EMAT)
Fs = np.array(FX.Fs); Fa = np.array(FX.Fa); MAT = np.array(FX.MAT)
nn = len(X); ne = len(ELE)
# paleta de GEO5 (12 colores, azul -> rojo)
G12 = [[30, 60, 230], [0, 0, 190], [0, 120, 120], [0, 150, 0], [0, 190, 0], [0, 255, 0],
       [130, 255, 0], [255, 255, 0], [255, 150, 0], [255, 80, 0], [255, 0, 0], [180, 0, 0]]
CM = ListedColormap([[c[0] / 255., c[1] / 255., c[2] / 255.] for c in G12])
NX = 220; NZ = 170

# ---- geometria de la malla: aristas (contorno, interfaz de suelos, malla) ----
edges = {}
for e in range(ne):
    c = ELE[e]
    for a, b in ((0, 1), (1, 2), (2, 0)):
        k = (min(c[a], c[b]), max(c[a], c[b]))
        if k not in edges: edges[k] = [0, set()]
        edges[k][0] += 1; edges[k][1].add(int(EMAT[e]))
MESH = [((X[a], Y[a]), (X[b], Y[b])) for (a, b) in edges]
BORDE = [((X[a], Y[a]), (X[b], Y[b])) for (a, b), v in edges.items() if v[0] == 1]
INTERF = [((X[a], Y[a]), (X[b], Y[b])) for (a, b), v in edges.items() if len(v[1]) == 2]
def centroid(mat):
    xs = []; ys = []
    for e in range(ne):
        if EMAT[e] == mat: xs.append(X[ELE[e][:3]].mean()); ys.append(Y[ELE[e][:3]].mean())
    return float(np.mean(xs)), float(np.mean(ys))

def grid_field(vals):
    """Interpola un campo nodal (esquinas del T6) a una rejilla regular por coordenadas
    baricentricas, triangulo a triangulo. Fuera del talud queda NaN. Mismo algoritmo que en el .m."""
    xmin = float(np.min(X)); xmax = float(np.max(X)); zmin = float(np.min(Y)); zmax = float(np.max(Y))
    xg = np.linspace(xmin, xmax, NX); zg = np.linspace(zmin, zmax, NZ)
    Z = np.full((NZ, NX), np.nan)
    dxg = (xmax - xmin) / (NX - 1); dzg = (zmax - zmin) / (NZ - 1)
    for e in range(ne):
        n0, n1, n2 = ELE[e][0], ELE[e][1], ELE[e][2]
        ax, ay = X[n0], Y[n0]; bx, by = X[n1], Y[n1]; cx, cy = X[n2], Y[n2]
        det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        i0 = max(int(math.floor((min(ax, bx, cx) - xmin) / dxg)), 0); i1 = min(int(math.ceil((max(ax, bx, cx) - xmin) / dxg)), NX - 1)
        j0 = max(int(math.floor((min(ay, by, cy) - zmin) / dzg)), 0); j1 = min(int(math.ceil((max(ay, by, cy) - zmin) / dzg)), NZ - 1)
        v0, v1, v2 = vals[n0], vals[n1], vals[n2]
        for j in range(j0, j1 + 1):
            zz = zg[j]
            for i in range(i0, i1 + 1):
                xx = xg[i]
                L1 = ((by - cy) * (xx - cx) + (cx - bx) * (zz - cy)) / det
                L2 = ((cy - ay) * (xx - cx) + (ax - cx) * (zz - cy)) / det
                L3 = 1.0 - L1 - L2
                if L1 >= -1e-9 and L2 >= -1e-9 and L3 >= -1e-9:
                    Z[j, i] = L1 * v0 + L2 * v1 + L3 * v2
    return xg, zg, Z

def geo5_step(vmin, vmax):
    """Paso de banda de GEO5, MEDIDO en 6 barras de su GUI (d_x y d_z de las 3 etapas, capturas
    'Resultados colormap talud' del 3-sep): paso = (max - min)/11 con la MANTISA redondeada al
    0.5 mas cercano.  18.0/11=1.636->1.5 ; 16.7/11=1.52->1.5 ; 110.7/11=10.06->10 ;
    107.7/11=9.79->10 ; 5.4/11=0.49->0.5 ; 5.9/11=0.536->0.55 (este ultimo descartaba la regla
    anterior de 'valor bonito' vmax/12)."""
    rng = vmax - vmin
    if rng <= 0: return 1.0
    raw = rng / 11.0
    p = 10 ** math.floor(math.log10(raw)); m = raw / p
    return round(m * 2.0) / 2.0 * p

def geo5_levels(vmin, vmax):
    """Bordes de banda de GEO5: [min, multiplos del paso dentro de (min, max), max], min y max
    redondeados a 0.1 mm como en su barra. Un multiplo a menos de 0.1 paso del min se omite
    (etapa 2 d_x: -0.4, 10, 20, ... sin el 0) y uno a menos de 0.05 paso del max se funde con el.
    d_z etapa 1: -2.6, -1.5, 0, 1.5, ..., 13.5, 14.1 ; d_z etapa 3: -1.1, -0.55, 0, ..., 4.4, 4.8."""
    st = geo5_step(vmin, vmax); vmax_r = round(vmax, 1); vmin_r = round(vmin, 1)
    lv = [vmin_r]
    k = math.floor(vmin_r / st) + 1
    while k * st < vmax_r - 1e-9:
        v = round(k * st, 6)
        if v - vmin_r >= 0.1 * st: lv.append(v)
        k += 1
    if vmax_r > lv[-1] + 0.05 * st: lv.append(vmax_r)
    else: lv[-1] = vmax_r
    return lv

# Colores de la barra de GEO5, MUESTREADOS pixel a pixel de sus capturas (GEO5_evidencia_Demo04):
G5_11 = [(0,0,255),(0,0,176),(0,88,88),(0,176,0),(0,255,0),(128,255,0),(255,255,0),(255,128,0),(255,64,0),(255,0,0),(176,0,0)]          # etapa 2 (11 bandas)
G5_13 = [(0,0,255),(0,0,224),(0,0,176),(0,176,0),(0,216,0),(0,255,0),(128,255,0),(255,255,0),(255,192,0),(255,128,0),(255,0,0),(213,0,0),(176,0,0)]  # etapa 1 (13 bandas)
G5_12 = [(0,0,255),(0,0,220),(0,0,176),(0,176,0),(0,216,0),(0,255,0),(255,255,0),(255,192,0),(255,128,0),(255,0,0),(211,0,0),(176,0,0)]  # 12 bandas (capturas d_z etapas 1-2 de la GUI, 2026-09-03): la de 13 sin el lima
def geo5_cmap(nb):
    """Paleta de GEO5 para nb bandas: la muestreada exacta si nb es 11 o 13; si no, interpolacion
    lineal de la de 13 (azul -> azul oscuro -> verde -> amarillo -> naranja -> rojo -> rojo oscuro)."""
    if nb == 11: base = G5_11
    elif nb == 12: base = G5_12
    elif nb == 13: base = G5_13
    else:
        anc = np.array(G5_13, float); xs = np.linspace(0, 1, len(anc)); xq = np.linspace(0, 1, nb)
        base = [tuple(np.interp(xq, xs, anc[:, c])[k] for c in range(3)) for k in range(nb)]
    return ListedColormap([[c[0] / 255.0, c[1] / 255.0, c[2] / 255.0] for c in base])

FIELD = {"dx": ("d_x", lambda u: np.array([-u[2 * i] * 1000.0 for i in range(nn)])),          # signo de GEO5 (x hacia la izquierda +)
         "dz": ("d_z", lambda u: np.array([-u[2 * i + 1] * 1000.0 for i in range(nn)])),      # GEO5: asiento POSITIVO
         "d":  ("d",   lambda u: np.array([math.hypot(u[2 * i], u[2 * i + 1]) * 1000.0 for i in range(nn)]))}   # resultante
def plot_stage(si, u, fs, gfs, name, fn, campo="dx"):
    lab, fld = FIELD[campo]
    dx = fld(u)                                                  # campo en mm
    # EXTREMOS de la barra = nodos ESQUINA solamente (medido 2026-09-04 en las 6 barras de la GUI de GEO5:
    # 17.6/14.1/-2.6/107.3/95.6/-15.1/5.0/4.8/-1.1 son los de esquina; con los nodos medios saldria 17.8/14.6/-3.3)
    esq = np.unique(ELE[:, :3]); vmin = float(np.min(dx[esq])); vmax = float(np.max(dx[esq]))
    lv = np.array(geo5_levels(vmin, vmax)); cmap = geo5_cmap(len(lv) - 1)   # ESCALA DE GEO5
    xg, zg, Z = grid_field(dx)
    XX, ZZ = np.meshgrid(xg, zg)
    fig, ax = plt.subplots(figsize=(9.0, 5.4))
    Zc = np.clip(Z, lv[0], lv[-1])
    cf = ax.contourf(XX, ZZ, Zc, levels=lv, cmap=cmap, norm=BoundaryNorm(lv, cmap.N))
    ax.contour(XX, ZZ, Zc, levels=lv, colors="0.2", linewidths=0.4)
    # geometria encima del campo
    ax.add_collection(LineCollection(MESH, colors="0.45", linewidths=0.3))            # malla T6
    ax.add_collection(LineCollection(INTERF, colors="k", linewidths=1.6, linestyles="--"))  # suelo 1 / suelo 2
    ax.add_collection(LineCollection(BORDE, colors="k", linewidths=1.3))              # contorno del talud
    x1, y1 = centroid(1); x2, y2 = centroid(2)
    ax.text(x1, y1, "SUELO 1\n$\\varphi$=%.1f°  c=%.0f kPa\n$\\gamma$=%.0f kN/m³" % (MAT[0, 2], MAT[0, 3], MAT[0, 4]),
            ha="center", va="center", fontsize=8, bbox=dict(fc="white", ec="k", alpha=0.85))
    ax.text(x2, y2 - 2.5, "SUELO 2\n$\\varphi$=%.0f°  c=%.0f kPa\n$\\gamma$=%.0f kN/m³" % (MAT[1, 2], MAT[1, 3], MAT[1, 4]),
            ha="center", va="center", fontsize=8, bbox=dict(fc="white", ec="k", alpha=0.85))
    # cargas de la etapa
    if si >= 1:      # sobrecarga: flechas hacia abajo en los nodos cargados de la corona
        for i in range(nn):
            if Fs[2 * i + 1] < -1e-9:
                ax.annotate("", xy=(X[i], Y[i]), xytext=(X[i], Y[i] + 1.6),
                            arrowprops=dict(arrowstyle="->", color="k", lw=0.9))
        xs = [X[i] for i in range(nn) if Fs[2 * i + 1] < -1e-9]
        ax.text(float(np.mean(xs)), float(Y[[i for i in range(nn) if Fs[2 * i + 1] < -1e-9][0]]) + 2.0,
                "q = 35 kPa", ha="center", va="bottom", fontsize=8)
    if si >= 2:      # ancla: fuerza puntual en su nodo, dibujada como flecha en la direccion de la fuerza
        ia = int(np.argmax(np.abs(Fa[0::2]) + np.abs(Fa[1::2])))
        fx = float(np.sum(Fa[0::2])); fy = float(np.sum(Fa[1::2])); L = 4.0 / math.hypot(fx, fy)
        ax.annotate("", xy=(X[ia] + fx * L, Y[ia] + fy * L), xytext=(X[ia], Y[ia]),
                    arrowprops=dict(arrowstyle="-|>", color="k", lw=1.6))
        ax.text(X[ia] + fx * L + 0.3, Y[ia] + fy * L - 0.3, "ancla 72 kN", ha="left", va="top", fontsize=8)
    ax.set_aspect("equal"); ax.set_xlabel("x [m]"); ax.set_ylabel("z [m]")
    ax.set_xlim(float(np.min(X)) - 1.0, float(np.max(X)) + 1.0); ax.set_ylim(float(np.min(Y)) - 1.0, float(np.max(Y)) + 3.0)
    ax.set_title("Talud Demo04 %s - %s [mm]  FS=%.4f  (GEO5 %.2f)" % (name, lab, fs, gfs), pad=10)
    cb = fig.colorbar(cf, ax=ax, ticks=lv, boundaries=lv, spacing="uniform"); cb.set_label(lab + " [mm]")
    cb.ax.invert_yaxis()                                   # GEO5: el minimo arriba, el maximo abajo
    st = geo5_step(vmin, vmax); dec = 1   # GEO5 rotula SIEMPRE con 1 decimal (paso 0.55: -0.6, 0.0, 0.6, 1.1, 1.7 ...)
    cb.ax.set_yticklabels([("%%.%df" % dec) % v for v in lv])
    fig.tight_layout(); fig.savefig(fn, dpi=110); print("PNG:", fn)

