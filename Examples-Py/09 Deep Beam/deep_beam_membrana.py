#"Viga-pared (deep beam) — FEM Q4 membrana validado contra Airy/Fourier
#'Membrana en **tension plana** (DOFs u_x, u_y). Mismo problema que el analitico de Calcpad,
#'resuelto por elementos finitos Q4 y validado contra la **solucion exacta de Airy** (serie de Fourier).
#'Es el precursor lineal del GeoFEM no-lineal (mismo continuo 2D, luego se le anade fluencia).
import numpy as np
import math

#"1. Datos
L = 4.0      #' luz [m]
H = 2.0      #' altura [m]
q = 100.0    #' carga sobre el borde superior [kN/m]
b_L = 0.8    #' longitud de la carga
a_0 = 1.6    #' inicio de la carga
c_0 = a_0 + b_L
d_s = 0.4    #' longitud de apoyo
e_s = L - d_s
r = q * b_L / (2 * d_s)   #' traccion de reaccion en apoyos
E = 30e6     #' modulo [kPa]
nu = 0.2     #' Poisson
N_ser = 21   #' terminos de Fourier del analitico

#"2. Malla Q4 (el andamiaje va oculto con #hide ... #show)
n_x = 32
n_y = 16
#hide
nnx = n_x + 1
nny = n_y + 1
nn = nnx * nny
dx = L / n_x
dy = H / n_y
X = np.zeros(nn)
Y = np.zeros(nn)
for j in range(nny):
    for i in range(nnx):
        k = j * nnx + i
        X[k] = i * dx
        Y[k] = j * dy
f = E / (1 - nu * nu)
D = f * np.array([[1.0, nu, 0.0], [nu, 1.0, 0.0], [0.0, 0.0, (1 - nu) / 2]])
def nid(i, j):
    return j * nnx + i
fixed = {}
fixed[2 * nid(0, 0)] = 1
fixed[2 * nid(0, 0) + 1] = 1
fixed[2 * nid(n_x, 0) + 1] = 1
ndof = 2 * nn
red = np.zeros(ndof)
nf = 0
for g in range(ndof):
    if g in fixed:
        red[g] = -1
    else:
        red[g] = nf
        nf = nf + 1
nfree = nf
g1 = 1.0 / math.sqrt(3.0)
gpx = [-g1, g1, g1, -g1]
gpy = [-g1, -g1, g1, g1]
Kff = np.zeros((nfree, nfree))
Fff = np.zeros(nfree)
Ball = []
edofall = []
def q4shape(xi, et):
    dNr = [-(1 - et) / 4, (1 - et) / 4, (1 + et) / 4, -(1 + et) / 4]
    dNs = [-(1 - xi) / 4, -(1 + xi) / 4, (1 + xi) / 4, (1 - xi) / 4]
    return dNr, dNs
for ej in range(n_y):
    for ei in range(n_x):
        n0 = nid(ei, ej)
        n1 = nid(ei + 1, ej)
        n2 = nid(ei + 1, ej + 1)
        n3 = nid(ei, ej + 1)
        edof = [2*n0, 2*n0+1, 2*n1, 2*n1+1, 2*n2, 2*n2+1, 2*n3, 2*n3+1]
        Ke = np.zeros((8, 8))
        Bg = []
        for gpi in range(4):
            xi = gpx[gpi]
            et = gpy[gpi]
            dNr, dNs = q4shape(xi, et)
            J00 = dNr[0]*X[n0] + dNr[1]*X[n1] + dNr[2]*X[n2] + dNr[3]*X[n3]
            J01 = dNr[0]*Y[n0] + dNr[1]*Y[n1] + dNr[2]*Y[n2] + dNr[3]*Y[n3]
            J10 = dNs[0]*X[n0] + dNs[1]*X[n1] + dNs[2]*X[n2] + dNs[3]*X[n3]
            J11 = dNs[0]*Y[n0] + dNs[1]*Y[n1] + dNs[2]*Y[n2] + dNs[3]*Y[n3]
            detJ = J00*J11 - J01*J10
            iJ00 = J11/detJ
            iJ01 = -J01/detJ
            iJ10 = -J10/detJ
            iJ11 = J00/detJ
            B = np.zeros((3, 8))
            for a in range(4):
                dNx = iJ00*dNr[a] + iJ01*dNs[a]
                dNy = iJ10*dNr[a] + iJ11*dNs[a]
                B[0, 2*a] = dNx
                B[1, 2*a+1] = dNy
                B[2, 2*a] = dNy
                B[2, 2*a+1] = dNx
            Ke = Ke + (B.T @ D @ B) * detJ
            Bg.append(B)
        Ball.append(Bg)
        edofall.append(edof)
        for aa in range(8):
            ga = edof[aa]
            ra = int(red[ga])
            if ra < 0:
                continue
            for bb in range(8):
                gb = edof[bb]
                rb = int(red[gb])
                if rb >= 0:
                    Kff[ra, rb] = Kff[ra, rb] + Ke[aa, bb]
def edge_load(y0, x1, x2, w):
    for i in range(n_x):
        xa = i*dx
        xb = (i+1)*dx
        if xa >= x1 - 1e-9 and xb <= x2 + 1e-9:
            jj = 0 if abs(y0) < 1e-9 else n_y
            na = nid(i, jj)
            nb = nid(i+1, jj)
            for m2 in [na, nb]:
                g = 2*m2 + 1
                rg = int(red[g])
                if rg >= 0:
                    Fff[rg] = Fff[rg] + w*dx/2
edge_load(H, a_0, c_0, -q)
edge_load(0.0, 0.0, d_s, r)
edge_load(0.0, e_s, L, r)
#show
f"Malla: {n_x} x {n_y} = {n_x*n_y} elementos Q4, {nn} nodos, {nfree} GDL libres."

#"3. Solucion FEM y validacion contra Airy
#hide
U_f = np.linalg.solve(Kff, Fff)
U = np.zeros(ndof)
for g in range(ndof):
    rg = int(red[g])
    if rg >= 0:
        U[g] = U_f[rg]
sig_x = np.zeros(nn)
wnod = np.zeros(nn)
ne = n_x*n_y
for e in range(ne):
    edof = edofall[e]
    ue = np.zeros(8)
    for a in range(8):
        ue[a] = U[edof[a]]
    Bg = Ball[e]
    nds = [edof[0]//2, edof[2]//2, edof[4]//2, edof[6]//2]
    for gpi in range(4):
        sig = D @ (Bg[gpi] @ ue)
        m = nds[gpi]
        sig_x[m] = sig_x[m] + sig[0]
        wnod[m] = wnod[m] + 1.0
for k in range(nn):
    if wnod[k] > 0:
        sig_x[k] = sig_x[k] / wnod[k]
def alpha(n):
    return n*math.pi/L
def q_n(n):
    a = alpha(n)
    return 2*q/(a*L)*(math.cos(a*a_0) - math.cos(a*c_0))
def r_n(n):
    a = alpha(n)
    return 2*r/(a*L)*(1 - math.cos(a*L) - (math.cos(a*d_s) - math.cos(a*e_s)))
def sx_an(x, y):
    s = 0.0
    for n in range(1, N_ser+1):
        a = alpha(n)
        ah = a*H
        sh = math.sinh(ah)
        cth = math.cosh(ah)/sh
        beta = ah/sh
        Delta = a*(1-beta)*(1+beta)
        qn = q_n(n)
        rn = r_n(n)
        A = rn/(a*a)
        Bc = qn*(1/sh + beta*cth)/(a*Delta) - rn*(cth + beta/sh)/(a*Delta)
        Cc = -Bc*a
        Dc = (qn*beta - rn)/Delta
        ch = math.cosh(a*y)
        th = math.tanh(a*y)
        Y2 = ch*a*(A*a + Bc*a*th + Cc*(2*th + a*y) + Dc*(2 + a*y*th))
        s = s + Y2*math.sin(a*x)
    return s
k_bot = nid(n_x//2, 0)
k_top = nid(n_x//2, n_y)
sx_bot_fem = sig_x[k_bot]
sx_top_fem = sig_x[k_top]
sx_bot_an = sx_an(L/2, 0.0)
sx_top_an = sx_an(L/2, H)
err_bot = abs(sx_bot_fem - sx_bot_an)/abs(sx_bot_an)*100
err_top = abs(sx_top_fem - sx_top_an)/abs(sx_top_an)*100
#show
#'**σ_x en x = L/2** (kPa) — FEM Q4 vs Airy analitico:
print("  cara inferior (traccion):   FEM = %.1f   analitico = %.1f   err = %.1f%%" % (sx_bot_fem, sx_bot_an, err_bot))
print("  cara superior (compresion): FEM = %.1f   analitico = %.1f   err = %.1f%%" % (sx_top_fem, sx_top_an, err_top))

#"4. Campo de tensiones σ_x (visor 3D orbitable)
#hide
nodes3 = []
vals = []
sabs = 0.0
for k in range(nn):
    if abs(sig_x[k]) > sabs:
        sabs = abs(sig_x[k])
scalez = H / (2*sabs) if sabs > 0 else 1.0
for k in range(nn):
    nodes3.append([float(X[k]), float(Y[k]), float(sig_x[k]*scalez)])
    vals.append(float(sig_x[k]))
faces = []
for ej in range(n_y):
    for ei in range(n_x):
        faces.append([nid(ei,ej), nid(ei+1,ej), nid(ei+1,ej+1), nid(ei,ej+1)])
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
show_mesh3d(nodes3, faces, {"sigma_x [kPa]": vals}, "Deep beam - campo de tension sigma_x (FEM Q4)")
