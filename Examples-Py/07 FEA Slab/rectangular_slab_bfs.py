# Rectangular Slab FEA -- BFS (Bogner-Fox-Schmit, 16 DOF/elem)
# Port Python del rectangular_slab_bfs.m de Hekatan Lab.
# Placa rectangular simplemente apoyada, carga uniforme q.
# Corre 100% en el motor EMBEBIDO de Hekatan Python (numpy interno + visor orbit3D).
# Dato canonico: deflexion central w(a/2, b/2). Validado vs Navier: 0.03%.
import numpy as np
import math

# ================= Datos de entrada =================
a  = 6.0      # Dimension en x [m]
b  = 4.0      # Dimension en y [m]
t  = 0.1      # Espesor [m]
q  = 10.0     # Carga distribuida [kN/m^2]
E  = 35000.0  # Modulo elastico [MPa]
nu = 0.15     # Coef de Poisson
E_si = E * 1000.0            # [kN/m^2]

n_a = 6       # elementos en a
n_b = 4       # elementos en b
n_e = n_a * n_b
n_j = (n_a + 1) * (n_b + 1)
a_1 = a / n_a
b_1 = b / n_b
n_g = 4 * n_j                # 4 DOF/joint: w, theta_x, theta_y, psi

print("=== Rectangular Slab FEA (BFS 16 DOF) ===")
print("Malla: %d elem (%dx%d), %d joints, %d GDL" % (n_e, n_a, n_b, n_j, n_g))

# ================= Coordenadas de joints =================
x_j = np.zeros(n_j)
y_j = np.zeros(n_j)
xv = 0.0
yv = 0.0
for j in range(n_j):
    x_j[j] = xv
    y_j[j] = yv
    yv = yv + b_1
    if yv > b + 1e-9:
        yv = 0.0
        xv = xv + a_1

# ================= Conectividad (joints 1-based) =================
e_j = np.zeros((n_e, 4), dtype=int)
for i_a in range(1, n_a + 1):
    for i_b in range(1, n_b + 1):
        e = i_b + n_b * (i_a - 1)
        jj = e + i_a - 1
        e_j[e - 1, 0] = jj
        e_j[e - 1, 1] = jj + n_b + 1
        e_j[e - 1, 2] = jj + n_b + 2
        e_j[e - 1, 3] = jj + 1

# ================= Apoyos (bordes) =================
s_j = []
for i in range(1, n_a + 2): s_j.append((n_b + 1) * i - n_b)
for i in range(1, n_a + 2): s_j.append((n_b + 1) * i)
for i in range(2, n_b + 1): s_j.append(i)
for i in range(2, n_b + 1): s_j.append(n_a * (n_b + 1) + i)

# ================= Matriz constitutiva D (flexion de placa) =================
D11 = E_si * t**3 / (12 * (1 - nu**2))
D = D11 * np.array([[1.0, nu, 0.0],
                    [nu, 1.0, 0.0],
                    [0.0, 0.0, (1 - nu) / 2]])

# ================= Hermite cubicas BFS (dominio local 0..1) =================
def phi(k, u, L):
    if k == 1: return 1 - u**2 * (3 - 2*u)
    if k == 2: return u * L * (1 - u * (2 - u))
    if k == 3: return u**2 * (3 - 2*u)
    if k == 4: return u**2 * L * (-1 + u)
    return 0.0

def phi_d(k, u, L):
    if k == 1: return -6*u/L + 6*u**2/L
    if k == 2: return 1 - 4*u + 3*u**2
    if k == 3: return 6*u/L - 6*u**2/L
    if k == 4: return -2*u + 3*u**2
    return 0.0

def phi_dd(k, u, L):
    if k == 1: return -6/L**2 + 12*u/L**2
    if k == 2: return (-4 + 6*u)/L
    if k == 3: return 6/L**2 - 12*u/L**2
    if k == 4: return (-2 + 6*u)/L
    return 0.0

# Mapa DOF elemento (1..16) -> (ix, iy) de las Phi. Nodo 1=(0,0) 2=(1,0) 3=(1,1) 4=(0,1)
def bfs_ix_iy(j):
    node = (j - 1) // 4 + 1
    sub  = (j - 1) % 4 + 1
    if   node == 1: ixw, iyw = 1, 1
    elif node == 2: ixw, iyw = 3, 1
    elif node == 3: ixw, iyw = 3, 3
    else:           ixw, iyw = 1, 3
    if   sub == 1: return ixw,     iyw
    elif sub == 2: return ixw,     iyw + 1
    elif sub == 3: return ixw + 1, iyw
    else:          return ixw + 1, iyw + 1

# ================= Gauss 4x4 en [0,1] =================
gp4 = [-0.861136311594053, -0.339981043584856, 0.339981043584856, 0.861136311594053]
gw4 = [ 0.347854845137454,  0.652145154862546, 0.652145154862546, 0.347854845137454]
gp = [(g + 1) / 2 for g in gp4]
gw = [g / 2 for g in gw4]

# indices precomputados
IDX = []
for j in range(1, 17):
    IDX.append(bfs_ix_iy(j))

# ================= K_e (16x16) y F_e por Gauss =================
K_e = np.zeros((16, 16))
F_e = np.zeros(16)
for ig in range(4):
    for jg in range(4):
        u = gp[ig]; v = gp[jg]; wgt = gw[ig] * gw[jg]
        B = np.zeros((3, 16))
        for j in range(16):
            ix, iy = IDX[j]
            B[0, j] = -phi_dd(ix, u, a_1) * phi(iy, v, b_1)
            B[1, j] = -phi(ix, u, a_1) * phi_dd(iy, v, b_1)
            B[2, j] = -2 * phi_d(ix, u, a_1) * phi_d(iy, v, b_1)
        K_e = K_e + (B.T @ D @ B) * (a_1 * b_1 * wgt)
        for j in range(16):
            ix, iy = IDX[j]
            F_e[j] = F_e[j] + q * phi(ix, u, a_1) * phi(iy, v, b_1) * (a_1 * b_1 * wgt)

# ================= Ensamblaje K global =================
K = np.zeros((n_g, n_g))
F = np.zeros(n_g)
for e in range(n_e):
    for ni in range(4):
        ji = e_j[e, ni]
        for nj in range(4):
            jj = e_j[e, nj]
            for di in range(4):
                gi = 4 * (ji - 1) + di
                for dj in range(4):
                    gj = 4 * (jj - 1) + dj
                    K[gi, gj] = K[gi, gj] + K_e[4*ni + di, 4*nj + dj]
        for di in range(4):
            F[4 * (ji - 1) + di] = F[4 * (ji - 1) + di] + F_e[4*ni + di]

# ================= Condiciones de contorno (simply supported, penalty) =================
ks = 1e20
for js in s_j:
    g = 4 * (js - 1)
    K[g, g] = K[g, g] + ks                       # w = 0
    if abs(y_j[js-1]) < 1e-9 or abs(y_j[js-1] - b) < 1e-9:
        K[g+2, g+2] = K[g+2, g+2] + ks           # borde || x: dw/dx = 0
    if abs(x_j[js-1]) < 1e-9 or abs(x_j[js-1] - a) < 1e-9:
        K[g+1, g+1] = K[g+1, g+1] + ks           # borde || y: dw/dy = 0

# ================= Solucion =================
Z = np.linalg.solve(K, F)

# ================= Deflexion central + validacion analitica =================
center_col = n_a // 2 + 1
center_row = n_b // 2 + 1
center_joint = (center_col - 1) * (n_b + 1) + center_row
w_center = Z[4 * (center_joint - 1)]

# Navier: placa simplemente apoyada, carga uniforme (oraculo analitico independiente)
Dp = E_si * t**3 / (12 * (1 - nu**2))
wn = 0.0
m = 1
while m < 60:
    n = 1
    while n < 60:
        wn = wn + (math.sin(m*math.pi/2) * math.sin(n*math.pi/2)) / \
                  (m * n * ((m/a)**2 + (n/b)**2)**2)
        n = n + 2
    m = m + 2
w_navier = 16 * q / (math.pi**6 * Dp) * wn

err = abs(w_center - w_navier) / w_navier * 100

print("")
print("Joint central: %d en (%.2f, %.2f) m" % (center_joint, x_j[center_joint-1], y_j[center_joint-1]))
print("Deflexion central BFS w(a/2,b/2) = %.4f mm" % (w_center * 1000))
print("Solucion analitica Navier w_max  = %.4f mm" % (w_navier * 1000))
print("Error vs Navier = %.3f %%   ->  %s" % (err, "OK" if err < 3 else "REVISAR"))

# ================= Visor orbit3D: superficie deformada =================
scale = 40.0   # exageracion visual de la flecha (la placa es 6x4 m, w ~ mm)
nodes3 = []
wvals = []
for j in range(n_j):
    wj = Z[4 * j]
    nodes3.append([float(x_j[j]), float(y_j[j]), float(-wj * scale)])
    wvals.append(float(wj * 1000.0))   # mm

faces = []
for e in range(n_e):
    faces.append([int(e_j[e,0]-1), int(e_j[e,1]-1), int(e_j[e,2]-1), int(e_j[e,3]-1)])

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
show_mesh3d(nodes3, faces, {"w [mm]": wvals},
            "Placa deformada BFS (flecha x%d) - w_max %.3f mm" % (int(scale), w_center*1000))
