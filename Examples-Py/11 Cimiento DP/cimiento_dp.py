#"Cimiento sobre suelo Drucker-Prager — malla no-lineal con mapa de color
#'Junta las dos piezas: la **malla Q4** de la membrana lineal + el **retorno Drucker-Prager**
#'del punto material. Un cimiento carga el suelo; el campo de **tension desviadora q** dibuja el
#'**bulbo de presion** (zona plastica) con mapa de color. Es un mini-GeoFEM.
import numpy as np
import math

#"1. Datos
W = 6.0      #' ancho del dominio [m]
H_s = 4.0    #' alto del dominio [m]
b_f = 1.0    #' semi-ancho del cimiento [m]
q_L = 250.0  #' carga del cimiento [kPa]
E = 3.0e4    #' modulo [kPa]
nu = 0.30    #' Poisson
phi = 25.0   #' friccion [grados]
c = 15.0     #' cohesion [kPa]
#hide
n_x = 16
n_y = 10
s_p = math.sin(phi*math.pi/180)
c_p = math.cos(phi*math.pi/180)
alpha = 2*s_p/(math.sqrt(3)*(3+s_p))
k_dp = 6*c*c_p/(math.sqrt(3)*(3+s_p))
fE = E/((1+nu)*(1-2*nu))
D = fE*np.array([[1-nu,nu,nu,0.0],[nu,1-nu,nu,0.0],[nu,nu,1-nu,0.0],[0.0,0.0,0.0,(1-2*nu)/2]])
nnx = n_x+1
nny = n_y+1
nn = nnx*nny
dx = W/n_x
dy = H_s/n_y
X = np.zeros(nn)
Y = np.zeros(nn)
for j in range(nny):
    for i in range(nnx):
        k = j*nnx+i
        X[k] = -W/2 + i*dx
        Y[k] = -H_s + j*dy
def nid(i,j):
    return j*nnx+i
ne = n_x*n_y
ELE = []
for j in range(n_y):
    for i in range(n_x):
        ELE.append([nid(i,j), nid(i+1,j), nid(i+1,j+1), nid(i,j+1)])
ndof = 2*nn
# BCs: base fija, lados rodillo -> mapa GDL libre
fixed = {}
for k in range(nn):
    if abs(Y[k]+H_s) < 1e-6:
        fixed[2*k] = 1
        fixed[2*k+1] = 1
    if abs(X[k]+W/2) < 1e-6 or abs(X[k]-W/2) < 1e-6:
        fixed[2*k] = 1
red = np.zeros(ndof)
nf = 0
for g in range(ndof):
    if g in fixed:
        red[g] = -1
    else:
        red[g] = nf
        nf = nf + 1
nfree = nf
# Gauss 2x2
g1 = 1.0/math.sqrt(3.0)
gpx = [-g1, g1, g1, -g1]
gpy = [-g1, -g1, g1, g1]
# B, detJ*w por (elem, gauss); Kff (elastica, constante)
Ball = []
dJw = []
edofs = []
Kff = np.zeros((nfree, nfree))
for e in range(ne):
    nd = ELE[e]
    edof = [2*nd[0],2*nd[0]+1,2*nd[1],2*nd[1]+1,2*nd[2],2*nd[2]+1,2*nd[3],2*nd[3]+1]
    edofs.append(edof)
    Bg = []
    Wg = []
    Ke = np.zeros((8,8))
    for gpi in range(4):
        xi = gpx[gpi]; et = gpy[gpi]
        dNr = [-(1-et)/4,(1-et)/4,(1+et)/4,-(1+et)/4]
        dNs = [-(1-xi)/4,-(1+xi)/4,(1+xi)/4,(1-xi)/4]
        J00 = dNr[0]*X[nd[0]]+dNr[1]*X[nd[1]]+dNr[2]*X[nd[2]]+dNr[3]*X[nd[3]]
        J01 = dNr[0]*Y[nd[0]]+dNr[1]*Y[nd[1]]+dNr[2]*Y[nd[2]]+dNr[3]*Y[nd[3]]
        J10 = dNs[0]*X[nd[0]]+dNs[1]*X[nd[1]]+dNs[2]*X[nd[2]]+dNs[3]*X[nd[3]]
        J11 = dNs[0]*Y[nd[0]]+dNs[1]*Y[nd[1]]+dNs[2]*Y[nd[2]]+dNs[3]*Y[nd[3]]
        detJ = J00*J11-J01*J10
        iJ00=J11/detJ; iJ01=-J01/detJ; iJ10=-J10/detJ; iJ11=J00/detJ
        B = np.zeros((4,8))
        for a in range(4):
            dNx = iJ00*dNr[a]+iJ01*dNs[a]
            dNy = iJ10*dNr[a]+iJ11*dNs[a]
            B[0,2*a]=dNx; B[1,2*a+1]=dNy; B[3,2*a]=dNy; B[3,2*a+1]=dNx
        Bg.append(B); Wg.append(detJ)
        Ke = Ke + (B.T @ D @ B)*detJ
    Ball.append(Bg); dJw.append(Wg)
    for a in range(8):
        ra = int(red[edof[a]])
        if ra < 0:
            continue
        for b in range(8):
            rb = int(red[edof[b]])
            if rb >= 0:
                Kff[ra,rb] = Kff[ra,rb] + Ke[a,b]
# carga del cimiento (traccion vertical abajo en borde superior, |x|<=b_f)
F = np.zeros(ndof)
for i in range(n_x):
    xc = -W/2 + (i+0.5)*dx
    if abs(xc) <= b_f + 1e-9:
        na = nid(i,n_y); nb = nid(i+1,n_y)
        F[2*na+1] = F[2*na+1] - q_L*dx/2
        F[2*nb+1] = F[2*nb+1] - q_L*dx/2
Ff = np.zeros(nfree)
for g in range(ndof):
    rg = int(red[g])
    if rg >= 0:
        Ff[rg] = F[g]
def yieldF(s):
    p = (s[0]+s[1]+s[2])/3.0
    d0=s[0]-p; d1=s[1]-p; d2=s[2]-p
    sq = math.sqrt(0.5*(d0*d0+d1*d1+d2*d2)+s[3]*s[3])
    Fy = 3*alpha*p + sq - k_dp
    if sq > 1e-9:
        dQ = np.array([d0/(2*sq),d1/(2*sq),d2/(2*sq),s[3]/sq])
    else:
        dQ = np.array([0.0,0.0,0.0,0.0])
    return Fy, sq, dQ
#show
f"Malla: {n_x} x {n_y} = {ne} elementos, {nn} nodos. Cimiento q = {q_L} kPa, suelo phi={phi} c={c}."

#"2. Solucion no-lineal (viscoplastico Drucker-Prager)
#hide
dtc = 0.5*4*(1+nu)*(1-2*nu)/(E*(1-2*nu+s_p*s_p))
evp = []
for e in range(ne):
    evp.append([np.zeros(4), np.zeros(4), np.zeros(4), np.zeros(4)])
U = np.zeros(ndof)
niter = 0
for it in range(120):
    niter = it
    Fvp = np.zeros(ndof)
    for e in range(ne):
        for gpi in range(4):
            el = D @ evp[e][gpi]
            fe = Ball[e][gpi].T @ el * dJw[e][gpi]
            for a in range(8):
                Fvp[edofs[e][a]] = Fvp[edofs[e][a]] + fe[a]
    rhs = np.zeros(nfree)
    for g in range(ndof):
        rg = int(red[g])
        if rg >= 0:
            rhs[rg] = F[g] + Fvp[g]
    Uf = np.linalg.solve(Kff, rhs)
    U = np.zeros(ndof)
    for g in range(ndof):
        rg = int(red[g])
        if rg >= 0:
            U[g] = Uf[rg]
    Fmax = 0.0
    for e in range(ne):
        ue = np.zeros(8)
        for a in range(8):
            ue[a] = U[edofs[e][a]]
        for gpi in range(4):
            eps = Ball[e][gpi] @ ue
            sig = D @ (np.array([eps[0],eps[1],0.0,eps[3]]) - evp[e][gpi])
            Fy, sq, dQ = yieldF(sig)
            if Fy > 0:
                if Fy > Fmax:
                    Fmax = Fy
                evp[e][gpi] = evp[e][gpi] + dtc*Fy*dQ
    if it > 3 and Fmax < 0.5:
        break
# recuperar q nodal (promedio) para el mapa de color
qn = np.zeros(nn)
wn = np.zeros(nn)
for e in range(ne):
    nd = ELE[e]
    ue = np.zeros(8)
    for a in range(8):
        ue[a] = U[edofs[e][a]]
    for gpi in range(4):
        eps = Ball[e][gpi] @ ue
        sig = D @ (np.array([eps[0],eps[1],0.0,eps[3]]) - evp[e][gpi])
        p = (sig[0]+sig[1]+sig[2])/3.0
        d0=sig[0]-p; d1=sig[1]-p; d2=sig[2]-p
        qv = math.sqrt(3*(0.5*(d0*d0+d1*d1+d2*d2)+sig[3]*sig[3]))
        for a in range(4):
            qn[nd[a]] = qn[nd[a]] + qv
            wn[nd[a]] = wn[nd[a]] + 1
for k in range(nn):
    if wn[k] > 0:
        qn[k] = qn[k]/wn[k]
uy = U[2*nid(n_x//2, n_y)+1]*1000
#show
print("  iteraciones = %d  (convergio Fmax < 0.5)" % (niter+1))
print("  asentamiento bajo el cimiento = %.2f mm" % uy)
print("  tension desviadora q: min = %.1f   max = %.1f kPa" % (qn.min(), qn.max()))

#"3. Mapa de color — bulbo de presion (tension desviadora q)
#hide
nodes3 = []
vals = []
for k in range(nn):
    nodes3.append([float(X[k]), float(Y[k]), 0.0])
    vals.append(float(qn[k]))
faces = []
for j in range(n_y):
    for i in range(n_x):
        faces.append([nid(i,j), nid(i+1,j), nid(i+1,j+1), nid(i,j+1)])
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
show_mesh3d(nodes3, faces, {"q [kPa]": vals}, "Cimiento DP - tension desviadora q (bulbo de presion)")
