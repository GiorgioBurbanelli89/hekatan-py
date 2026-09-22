# %% La zapata no lineal, resuelta con OpenSeesPy dentro de Hekatan Python
#
# El MISMO modelo que corre en Hekatan Struct, ETABS 22, SAFE 20 y el .tcl de
# OpenSees. Aquí se escribe en Python, se resuelve aquí y se ve aquí.
#
# Braja M. Das, «Principles of Foundation Engineering», 9.ª ed., ejemplo 6.10:
# zapata de 1.5 x 1.5 x 0.40 m, P = 606 kN con e_x = 0.15 m y e_y = 0.30 m,
# sobre arena de k_s = 2000 tonf/m^3.
#
# LA NO LINEALIDAD es una sola cosa: **el suelo no tira**. Donde la zapata
# querría levantarse, el muelle no aguanta y esa esquina se despega.

# %% 1 · Los datos
B = L = 1.5          # lado de la zapata [m]
T = 0.40             # espesor [m]
E = 22940519.0       # 15100·√240 kgf/cm^2 -> kN/m^2  (ACI 318)
NU = 0.20
KS = 19613.3         # 2000 tonf/m^3 -> kN/m^3
P = 606.0            # carga [kN]
EX, EY = 0.15, 0.30  # excentricidades [m]
N = 12               # divisiones por lado

dx, dy = B / N, L / N
print(f"Zapata {B} x {L} x {T} m")
print(f"E  = {E:,.0f} kN/m^2      ks = {KS:,.1f} kN/m^3")
print(f"P  = {P} kN  en  e_x = {EX} m ,  e_y = {EY} m")
print(f"malla {N} x {N}  ->  {(N+1)**2} nudos, {N*N} cáscaras")

# %% 2 · El modelo
from openseespy.opensees import (
    wipe, model, node, fix, nDMaterial, section, element, uniaxialMaterial,
    timeSeries, pattern, load, system, numberer, constraints, integrator,
    algorithm, test, analysis, analyze, nodeDisp,
)
import time

t0 = time.time()
wipe()
model("basic", "-ndm", 3, "-ndf", 6)      # 3 dimensiones, 6 grados por nudo

nid = lambda i, j: j * (N + 1) + i + 1
for j in range(N + 1):
    for i in range(N + 1):
        node(nid(i, j), i * dx, j * dy, 0.0)
        # ux, uy y el giro en z SUJETOS (si no, el sistema es singular).
        # uz, rx, ry LIBRES: de eso se encarga el muelle del suelo.
        fix(nid(i, j), 1, 1, 0, 0, 0, 1)

nDMaterial("ElasticIsotropic", 1, E, NU)
section("PlateFiber", 1, 1, T)
e = 0
for j in range(N):
    for i in range(N):
        e += 1
        element("ShellMITC4", e, nid(i, j), nid(i + 1, j),
                nid(i + 1, j + 1), nid(i, j + 1), 1)
print(f"placa: {e} elementos ShellMITC4 (= el Shell-Thick de ETABS)")

# %% 3 · El suelo que no tira
#
# `ENT` = Elastic-No Tension: rigidez k si comprime, CERO si estira.
# Es lo mismo que el «Compression Only» de SAFE y que el
# `areaspring … compresion` del .heks de Hekatan Struct.
def area(i, j):
    return (dx * (0.5 if i in (0, N) else 1.0)) * \
           (dy * (0.5 if j in (0, N) else 1.0))

sig = e
for j in range(N + 1):
    for i in range(N + 1):
        k = KS * area(i, j)
        uniaxialMaterial("ENT", 1000 + nid(i, j), k)
        node(10000 + nid(i, j), i * dx, j * dy, 0.0)   # el gemelo, en el suelo
        fix(10000 + nid(i, j), 1, 1, 1, 1, 1, 1)
        sig += 1
        element("zeroLength", sig, 10000 + nid(i, j), nid(i, j),
                "-mat", 1000 + nid(i, j), "-dir", 3)
print(f"suelo: {(N+1)**2} muelles ENT · k del nudo interior = {KS*dx*dy:,.1f} kN/m")

# %% 4 · La carga, repartida donde apoya la columna
xc, yc = B / 2 + EX, L / 2 + EY
ic, jc = min(N - 1, int(xc / dx)), min(N - 1, int(yc / dy))
xl, yl = (xc - ic * dx) / dx, (yc - jc * dy) / dy
timeSeries("Linear", 1)
pattern("Plain", 1, 1)
for n, w in [(nid(ic, jc), (1 - xl) * (1 - yl)), (nid(ic + 1, jc), xl * (1 - yl)),
             (nid(ic + 1, jc + 1), xl * yl), (nid(ic, jc + 1), (1 - xl) * yl)]:
    load(n, 0.0, 0.0, -P * w, 0.0, 0.0, 0.0)
print(f"carga: P en ({xc:.3f}, {yc:.3f}), repartida a 4 nudos con pesos bilineales")

# %% 5 · Resolver — hace falta Newton porque la rigidez CAMBIA
system("BandGeneral")
numberer("RCM")
constraints("Plain")
test("NormDispIncr", 1e-8, 60, 0)
algorithm("Newton")
integrator("LoadControl", 0.1)     # la carga en 10 pasos
analysis("Static")
ok = analyze(10)
print("análisis:", "OK" if ok == 0 else f"NO convergió ({ok})")

# %% 6 · Los resultados
wmin = pmax = q = 0.0
levantados = 0
mapa = []
for j in range(N + 1):
    fila = []
    for i in range(N + 1):
        w = nodeDisp(nid(i, j), 3)
        wmin = min(wmin, w)
        if w >= 0:
            levantados += 1
            fila.append(0.0)
        else:
            p = -w * KS
            pmax = max(pmax, p)
            q += p * area(i, j)
            fila.append(p)
    mapa.append(fila)

print()
print(f"asiento máximo   w_min = {wmin*1000:10.4f} mm")
print(f"presión máxima   p_max = {pmax:10.3f} kPa")
print(f"nudos levantados       = {levantados} de {(N+1)**2}")
print(f"suma de los muelles    = {q:10.2f} kN   (P = {P} kN)")
print(f"tiempo                 = {time.time()-t0:10.2f} s")
print()
print("LA COMPROBACIÓN QUE IMPORTA: la suma de los muelles tiene que dar la")
print("carga aplicada. Si no da, el modelo está mal, y eso no lo avisa nadie.")

# %% 7 · El mapa de presiones, aquí mismo
import matplotlib.pyplot as plt
import numpy as np

M = np.array(mapa)
fig, ax = plt.subplots(figsize=(6.2, 5.2))
im = ax.imshow(M, origin="lower", extent=[0, B, 0, L], cmap="jet",
               interpolation="bilinear")
ax.contour(np.linspace(0, B, N + 1), np.linspace(0, L, N + 1), M,
           levels=[0.001], colors="white", linewidths=2)
ax.plot(B / 2 + EX, L / 2 + EY, "wo", ms=9, mec="k", mew=1.5)
ax.set_title(f"Presión bajo la zapata [kPa] — máx {pmax:.0f}\n"
             f"la línea blanca separa lo que APOYA de lo que se LEVANTÓ")
ax.set_xlabel("x [m]")
ax.set_ylabel("y [m]")
fig.colorbar(im, ax=ax, label="kPa")
plt.tight_layout()
plt.show()
