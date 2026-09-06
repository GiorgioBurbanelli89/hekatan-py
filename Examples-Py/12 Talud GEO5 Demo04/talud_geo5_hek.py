# -*- coding: utf-8 -*-
# Talud Demo04 de GEO5 (GeoFEM): PORT FIEL del driver talud_geo5mesh.py (FS etapa 1 = 1.6935 = GEO5)
# escrito con el subconjunto de numpy/scipy que tiene el motor NATIVO de Hekatan Python
# (sin bool-masks, sin arrays 3D por bloques, sin open(), sin np.isfinite/arctan/hypot).
# Corre IGUAL en CPython: `python talud_geo5_hek.py [nstages]`.
#
# Todo EXTRAIDO del solver de GEO5 o MEDIDO en su course of analysis (ver talud_geo5_lab.m):
#   E = 130347 en los dos suelos, sigma0 = 0, cada peldano desde cero, 7 puntos de Gauss,
#   D-P ajuste de extension, return-map radial + tangente algoritmica no simetrica (apice -> De),
#   phi_red = atan(tan phi / SRF), Newton completo + line-search secante (tol 0.8, eta en [0.1,1]),
#   3 normas num/max(den,1), escalera SRM 0.90 con relajacion /2 (max 3, tope 0.99).
import sys, math, time
import numpy as np
from scipy.sparse import lil_matrix, csr_matrix
from scipy.sparse.linalg import splu
import demo04_geo5_fixture as FX

NSTAGES = 3
if len(sys.argv) > 1:
    try: NSTAGES = int(sys.argv[1])
    except Exception: NSTAGES = 3
t_total = time.time()

X = np.array(FX.X); Y = np.array(FX.Y); ELE = np.array(FX.ELE); EMAT = np.array(FX.EMAT)
FIXED = np.array(FX.FIXED); Fg = np.array(FX.Fg); Fs = np.array(FX.Fs); Fa = np.array(FX.Fa)
MAT = np.array(FX.MAT)
nn = len(FX.X); ne = len(FX.ELE); ndof = 2 * nn
XY = np.zeros((nn, 2)); XY[:, 0] = X; XY[:, 1] = Y
free = np.setdiff1d(np.arange(ndof), FIXED); nfree = len(free)
n1 = 0; n2 = 0
for e in range(ne):
    if EMAT[e] == 1: n1 += 1
    else: n2 += 1
print("MALLA GEO5: %d nodos, %d T6 (SOIL_1=%d, SOIL_2=%d), %d gdl fijos" % (nn, ne, n1, n2, len(FIXED)))
for mm in range(2):
    print("MAT SOIL_%d: E=%.0f nu=%.2f phi=%.2f c=%.2f gamma=%.1f psi=%.1f" % (mm + 1, MAT[mm, 0], MAT[mm, 1], MAT[mm, 2], MAT[mm, 3], MAT[mm, 4], MAT[mm, 5]))

# ---- cuadratura de 7 puntos (Hammer grado 5) ----
c7 = 0.1012865073235; C7 = 0.7974269853531; e7 = 0.4701420641051; E7 = 0.0597158717898
gp = [[1 / 3., 1 / 3.], [c7, c7], [C7, c7], [c7, C7], [e7, e7], [E7, e7], [e7, E7]]
gw = [0.225 / 2, 0.1259391805448 / 2, 0.1259391805448 / 2, 0.1259391805448 / 2, 0.1323941527885 / 2, 0.1323941527885 / 2, 0.1323941527885 / 2]
NG = 7

def t6(L1, L2):
    L3 = 1 - L1 - L2
    N = np.array([L1 * (2 * L1 - 1), L2 * (2 * L2 - 1), L3 * (2 * L3 - 1), 4 * L1 * L2, 4 * L2 * L3, 4 * L3 * L1])
    dL1 = np.array([4 * L1 - 1, 0., -(4 * L3 - 1), 4 * L2, -4 * L2, 4 * L3 - 4 * L1])
    dL2 = np.array([0., 4 * L2 - 1, -(4 * L3 - 1), 4 * L1, 4 * L3 - 4 * L2, -4 * L1])
    return N, dL1, dL2

# ---- constitutivo elastico por suelo (4 comp: xx yy zz xy) ----
D4 = {}
for mm in range(2):
    E = MAT[mm, 0]; nu = MAT[mm, 1]; f = E / ((1 + nu) * (1 - 2 * nu))
    D4[mm + 1] = f * np.array([[1 - nu, nu, nu, 0.], [nu, 1 - nu, nu, 0.], [nu, nu, 1 - nu, 0.], [0., 0., 0., (1 - 2 * nu) / 2]])
IX = [0, 1, 3]
ONE = np.array([1., 1., 1., 0.])
PDEV = np.eye(4) - np.outer(ONE, ONE) / 3.0

# ---- B, det(J)*w y gdl por elemento (diccionarios: el motor nativo no bloquea arrays 3D) ----
Bc = {}; dJw = np.zeros((ne, NG)); edof = {}
Fg2 = np.zeros(ndof)
for e in range(ne):
    nd = ELE[e]
    # (xy = XY[nd] con nd = array de filas daba MAL en el motor nativo de Hekatan Python: gather
    #  plano en vez de por filas. Arreglado en el motor el 2026-09-02 (PyNdArray.GatherRows); se
    #  deja el bucle escalar para que el guion corra tambien en las versiones instaladas.)
    xy = np.zeros((6, 2)); dd = []
    for a in range(6):
        xy[a, 0] = X[nd[a]]; xy[a, 1] = Y[nd[a]]
        dd.append(2 * nd[a]); dd.append(2 * nd[a] + 1)
    edof[e] = np.array(dd)
    rho = -MAT[EMAT[e] - 1, 4]
    for q in range(NG):
        N, dL1, dL2 = t6(gp[q][0], gp[q][1])
        j11 = float(dL1 @ xy[:, 0]); j12 = float(dL1 @ xy[:, 1]); j21 = float(dL2 @ xy[:, 0]); j22 = float(dL2 @ xy[:, 1])
        dJ = j11 * j22 - j12 * j21
        dNx = (j22 * dL1 - j12 * dL2) / dJ; dNy = (-j21 * dL1 + j11 * dL2) / dJ
        Bb = np.zeros((3, 12))
        for a in range(6):
            Bb[0, 2 * a] = dNx[a]; Bb[1, 2 * a + 1] = dNy[a]; Bb[2, 2 * a] = dNy[a]; Bb[2, 2 * a + 1] = dNx[a]
        Bc[e * NG + q] = Bb; dJw[e, q] = dJ * gw[q]
        for a in range(6): Fg2[2 * nd[a] + 1] += N[a] * rho * dJ * gw[q]
sFg = 0.; sFg2 = 0.; sFs = 0.; sFax = 0.; sFay = 0.; dmax = 0.
for i in range(nn):
    sFg += Fg[2 * i + 1]; sFg2 += Fg2[2 * i + 1]; sFs += Fs[2 * i + 1]; sFax += Fa[2 * i]; sFay += Fa[2 * i + 1]
    dmax = max(dmax, abs(Fg2[2 * i] - Fg[2 * i]), abs(Fg2[2 * i + 1] - Fg[2 * i + 1]))
print("cargas: gravedad Fy=%.3f (recalculada %.3f, dif max %.2e) | sobrecarga Fy=%.3f | ancla Fx=%.3f Fy=%.3f kN" % (sFg, sFg2, dmax, sFs, sFax, sFay))

# ---- K elastica (respaldo si la tangente sale singular) ----
Kel = lil_matrix((ndof, ndof))
for e in range(ne):
    De = D4[EMAT[e]]; Dm = De[np.ix_(IX, IX)]; d = edof[e]; Ke = np.zeros((12, 12))
    for q in range(NG):
        B = Bc[e * NG + q]; Ke += B.T @ Dm @ B * dJw[e, q]
    Kel[np.ix_(d, d)] += Ke
LU_EL = splu(csr_matrix(Kel)[free][:, free].tocsc())

def finite(v):
    n = float(np.linalg.norm(v))
    return math.isfinite(n)

def dp_ab(phi, c):
    s = math.sin(phi); co = math.cos(phi); r3 = math.sqrt(3.0)
    return 2 * s / (r3 * (3 + s)), 6 * c * co / (r3 * (3 + s))

def dp_return_g5(deps, al, k, De, wantK, sig_n=None):
    """Return-map D-P de GEO5 desde el estado sig_n (None = 0) + tangente algoritmica. Devuelve (sig, Dep)."""
    G = De[3, 3]; K = (De[0, 0] + 2.0 * De[0, 1]) / 3.0
    sig = De @ deps
    if sig_n is not None: sig = sig + sig_n
    I1 = sig[0] + sig[1] + sig[2]; sm = I1 / 3.0
    s = sig - sm * ONE
    J = math.sqrt(max(0.5 * (s[0] * s[0] + s[1] * s[1] + s[2] * s[2]) + s[3] * s[3], 0.0))
    f = J + al * I1 - k
    if f <= 1e-12: return sig, De
    if al > 1e-12: apex_p = k / (3.0 * al)
    else: apex_p = 1e300
    if sm < apex_p and J > 1e-12:
        lam = f / G; Jn = J - G * lam
        if Jn >= 1e-12:
            beta = Jn / J; sig = sm * ONE + beta * s
            if not wantK: return sig, De
            p = (sig[0] + sig[1] + sig[2]) / 3.0
            dv = np.array([sig[0] - p, sig[1] - p, sig[2] - p, sig[3]])
            sj = math.sqrt(max(0.5 * (dv[0] * dv[0] + dv[1] * dv[1] + dv[2] * dv[2]) + dv[3] * dv[3], 0.0))
            if sj < 1e-12: return sig, 1e-3 * De
            w = np.array([dv[0], dv[1], dv[2], 2.0 * dv[3]]) / (2.0 * sj)
            n = w + al * ONE; m = w
            Xi = K * np.outer(ONE, ONE) + beta * (2.0 * G * PDEV)
            Xi[3, 3] = beta * G
            Xm = Xi @ m; den = float(n @ Xm)
            if abs(den) > 1e-30: Dep = Xi - np.outer(Xm, n @ Xi) / den
            else: Dep = Xi
            return sig, Dep
    return np.array([apex_p, apex_p, apex_p, 0.0]), De

# ================================================================================================
# ALGEBRA DE CADA ITERACION DE GEO5 (medida en el problema de UN elemento, g5_1e, iteracion a
# iteracion a 4 cifras, 2026-09-03):
#   1) du = Kt^-1 R, con Kt armada con la tangente ALGORITMICA del ULTIMO retorno de cada punto de
#      Gauss (De donde el ultimo incremento fue elastico);
#   2) line-search de un tiro (eta) evaluando R(u + du) con un retorno de PRUEBA desde el estado;
#   3) u += eta du, y la tension de cada punto de Gauss se ACUMULA desde la iteracion anterior:
#      sigma_i = retorno(sigma_{i-1} + De B eta du)   <- NO se recalcula desde la deformacion total;
#   4) residuo con sigma_i y las tres normas.
# Cada reduction step arranca de cero (sigma = 0, u = 0). Con COMMIT_ITER = False se recupera lo
# de antes (retorno unico desde la deformacion total), que da el MISMO FS pero otro camino.
COMMIT_ITER = True
import os as _os
try: _ENV = dict(_os.environ)          # el motor NATIVO de Hekatan Python no tiene os.environ: sin variables
except Exception: _ENV = {}
HK_DUMP = (("all" if _ENV["HK_DUMP"] == "all" else tuple(int(x) for x in _ENV["HK_DUMP"].split(","))) if "HK_DUMP" in _ENV else None)
DUMP_ALL = {}   # HK_DUMP=all -> {(etapa,rs,it): SIG} de TODAS las iteraciones (tamiz a 17 cifras contra GEO5)
SRF_ROUND = _ENV.get("GEO5_SRFROUND", "0") == "1"   # 0 = SRF exacto como GEO5 (defecto desde 2026-09-04) | 1 = round(SRF,4) historico (A/B)   # HK_DUMP=etapa,rs,it -> guarda SIG de esa iteracion
DUMP_SIG = []; DUMP_DEPS = []
ITER_U = []; ITER_DU = []; ITER_ETA = []; STEP_TRACE = {}   # traza de la ultima nrstep y de todos los peldanos (diagnostico)
SIG = {}; DEPG = {}      # estado por punto de Gauss (clave e*NG+q): tension acumulada y tangente del ultimo retorno

def reset_state():
    for e in range(ne):
        for q in range(NG): SIG[e * NG + q] = np.zeros(4); DEPG[e * NG + q] = None

def assemble_inc(du, ab, commit):
    """Fuerzas internas con sigma = retorno(SIG + De B du) por punto de Gauss. Si commit: guarda
    sigma y la tangente algoritmica de ese retorno en SIG/DEPG (para la Kt de la iteracion siguiente)."""
    Fi = np.zeros(ndof)
    for e in range(ne):
        mm = EMAT[e]; De = D4[mm]; d = edof[e]; ue = du[d]; al = ab[mm][0]; k = ab[mm][1]
        for q in range(NG):
            kk = e * NG + q; B = Bc[kk]; ep = B @ ue
            sg, Dep = dp_return_g5(np.array([ep[0], ep[1], 0.0, ep[2]]), al, k, De, commit, SIG[kk])
            Fi[d] += B.T @ np.array([sg[0], sg[1], sg[3]]) * dJw[e, q]
            if commit: SIG[kk] = sg; DEPG[kk] = Dep
    return Fi

def tangent_from_state(ab):
    """Kt con la tangente del ULTIMO retorno guardado (DEPG); De si aun no hubo retorno."""
    Kt = lil_matrix((ndof, ndof))
    for e in range(ne):
        mm = EMAT[e]; De = D4[mm]; d = edof[e]; Ke = np.zeros((12, 12))
        for q in range(NG):
            kk = e * NG + q; B = Bc[kk]; Dep = DEPG[kk]
            if Dep is None: Dep = De
            Ke += B.T @ Dep[np.ix_(IX, IX)] @ B * dJw[e, q]
        Kt[np.ix_(d, d)] += Ke
    return Kt

def assemble(u, ab, wantK):
    """(modo antiguo) sigma = retorno unico desde la deformacion TOTAL, sin estado."""
    Fi = np.zeros(ndof)
    if wantK: Kt = lil_matrix((ndof, ndof))
    for e in range(ne):
        mm = EMAT[e]; De = D4[mm]; d = edof[e]; ue = u[d]; al = ab[mm][0]; k = ab[mm][1]
        if wantK: Ke = np.zeros((12, 12))
        for q in range(NG):
            B = Bc[e * NG + q]; ep = B @ ue
            sg, Dep = dp_return_g5(np.array([ep[0], ep[1], 0.0, ep[2]]), al, k, De, wantK)
            Fi[d] += B.T @ np.array([sg[0], sg[1], sg[3]]) * dJw[e, q]
            if wantK: Ke += B.T @ Dep[np.ix_(IX, IX)] @ B * dJw[e, q]
        if wantK: Kt[np.ix_(d, d)] += Ke
    if wantK: return Fi, Kt
    return Fi, None

def nrstep(SRF, Fext, u0, rstep):
    maxit = 100
    ab = {}
    for mm in range(2):
        phi = math.atan(math.tan(MAT[mm, 2] * math.pi / 180.0) / SRF); c = MAT[mm, 3] / SRF
        ab[mm + 1] = dp_ab(phi, c)
    u = u0.copy(); conv = False; it = 0
    zero = np.zeros(ndof)
    ITER_U.clear(); ITER_DU.clear(); ITER_ETA.clear()
    if COMMIT_ITER:
        reset_state(); Fi = assemble_inc(zero, ab, False); Rf = Fext[free] - Fi[free]
    else:
        Fi, _ = assemble(u, ab, False); Rf = Fext[free] - Fi[free]
    ADDisp = np.zeros(nfree); DForce = Rf.copy(); CLoad = Rf.copy(); r_prev = 1e300; ndiv = 0
    for it in range(1, maxit + 1):
        if not finite(Rf): break
        if COMMIT_ITER: Kt = tangent_from_state(ab)
        else: _, Kt = assemble(u, ab, True)
        try:
            LU = splu(csr_matrix(Kt)[free][:, free].tocsc()); duf = LU.solve(Rf)
        except Exception:
            duf = LU_EL.solve(Rf)
        if not finite(duf): duf = LU_EL.solve(Rf)
        du = np.zeros(ndof); du[free] = duf
        s0 = float(duf @ Rf); n0 = float(np.linalg.norm(Rf)); al = 1.0
        if COMMIT_ITER: F1 = assemble_inc(du, ab, False)                  # retorno de PRUEBA desde el estado
        else: F1, _ = assemble(u + al * du, ab, False)
        R1 = Fext[free] - F1[free]
        s1 = float(duf @ R1); n1 = float(np.linalg.norm(R1)); den = s0 - s1
        if n0 > 1e-10 and n1 > 1e-10 and abs(den) > 1e-10 and n1 / n0 >= 0.8: al = al * s0 / den
        if al < 0.1: al = 0.1
        elif al >= 1.0: al = 1.0
        drf = al * duf; u = u + al * du
        ITER_U.append(u.copy()); ITER_DU.append(du.copy()); ITER_ETA.append(al)   # traza por iteracion (diagnostico)
        if COMMIT_ITER: Fi = assemble_inc(al * du, ab, True)              # COMMIT: sigma_i y tangente
        else: Fi, _ = assemble(u, ab, False)
        Rf = Fext[free] - Fi[free]
        if HK_DUMP == "all": DUMP_ALL[(len(FS) + 1, rstep, it)] = dict(SIG)
        elif HK_DUMP and HK_DUMP == (len(FS) + 1, rstep, it): DUMP_SIG.append(dict(SIG)); DUMP_DEPS.append(al * du)   # volcado del estado (comparar con el breakpoint de GEO5)
        ADDisp = ADDisp + drf
        nDD = float(np.linalg.norm(drf)); dA = float(np.linalg.norm(ADDisp))
        nDL = float(np.linalg.norm(Rf)); dF = float(np.linalg.norm(DForce))
        # MEDIDO (2026-09-03, columna |ui*gi|/|u*g| del Log): la norma de energia usa el residuo DESPUES de
        # actualizar (Rf nuevo), no el de antes: E = sqrt(|du.Rf|) / sqrt(|ADDisp.F|)
        nEN = math.sqrt(abs(float(drf @ Rf))); dE = math.sqrt(abs(float(ADDisp @ DForce)))
        if dA > 1.0: eu = nDD / dA
        else: eu = nDD
        if dF > 1.0: ef = nDL / dF
        else: ef = nDL
        if dE > 1.0: ee = nEN / dE
        else: ee = nEN
        CLoad = Rf.copy()
        print("  RS=%d SRF=%.4f it=%2d eta=%.4f DNorm=%.5e OBFNorm=%.5e ENorm=%.5e |gi|=%.4e dx=%.1f" % (rstep, SRF, it, al, eu, ef, ee, nDL, float(np.max(np.abs(u[0::2]))) * 1e3))
        if ef < 1e-2 and ee < 1e-2 and eu < 1e-2:
            conv = True; break
        if ef > 250.0: break
        if nDL <= r_prev or ef <= 1e-2: ndiv = 0
        else: ndiv += 1
        r_prev = nDL
        if ndiv >= 2: break   # MEDIDO en los Log de GEO5 (etapas 1-3): declara divergencia tras DOS subidas seguidas de |gi|
    return conv, u, it

def srm(Ftot):
    RED0 = 0.90; RELAX = 2.0; MINSTEP = 0.99; MAXRELAX = 3
    Racc = 1.0; fs = 1.0; nrelax = 0; rs = 0; prog = ""
    ustart = np.zeros(ndof); ulo = ustart
    # 'Reduction step 0' de GEO5 = SRF 1.0 (estado de trabajo): GEO5 tabula/dibuja u(SRF) - u(1.0)
    c0, u0w, n0 = nrstep(1.0, Ftot, ustart, 0)
    STEP_TRACE[(len(FS), 0)] = (1.0, c0, list(ITER_U), list(ITER_DU), list(ITER_ETA))
    # MEDIDO (2026-09-03, traza por iteracion vs Output de GEO5): GEO5 tabula u(FS) - u_ELASTICA, donde
    # u_elastica = K_el^-1 F (= la primera iteracion de cualquier peldano), NO la u(1.0) plastica convergida.
    # Con la convergida el error nodal era 0.95 mm; con la elastica 0.023 mm (|u| = 58 mm).
    uel = np.zeros(ndof); uel[free] = LU_EL.solve(Ftot[free])
    global U_WORK_LAST; U_WORK_LAST = uel
    print("    SRM rs00 paso=1.0000 SRF=1.0000 %s it=%d" % ("CONVERGE" if c0 else "DIVERGE", n0))
    while True:
        s = 1.0 - (1.0 - RED0) / (RELAX ** nrelax)
        if s > MINSTEP: break
        trial = round(1.0 / (Racc * s), 4) if SRF_ROUND else 1.0 / (Racc * s)
        if trial > 3.0: break
        rs += 1
        conv, u, nit = nrstep(trial, Ftot, ustart, rs)
        STEP_TRACE[(len(FS), rs)] = (trial, conv, list(ITER_U), list(ITER_DU), list(ITER_ETA))
        if conv and finite(u):
            Racc *= s; fs = trial; ulo = u            # GEO5 NO reinicia la relajacion tras converger (Log: 'Relaxation reduction step' persiste)
            U_STEPS.append(trial); U_STEPS.append(u)
            prog += " %.3f:%.0f" % (trial, float(np.max(np.abs(u[0::2]))) * 1e3)
            print("    SRM rs%02d paso=%.4f SRF=%.4f CONVERGE it=%d" % (rs, s, trial, nit))
        else:
            nrelax += 1
            print("    SRM rs%02d paso=%.4f SRF=%.4f DIVERGE relax=%d" % (rs, s, trial, nrelax))
            if nrelax > MAXRELAX: break
    return fs, prog, ulo

names = ["etapa1 (peso propio)", "etapa2 (+sobrecarga)", "etapa3 (+ancla)"]
Fst = [Fg, Fg + Fs, Fg + Fs + Fa]; gfs = [1.69, 1.48, 1.69]
U_STEPS = []   # [SRF, u, SRF, u, ...] de los peldanos convergidos (todas las etapas seguidas)
FS = []; TT = []; U_STAGES = []; U_WORK = []; U_WORK_LAST = None   # U_STAGES[si] = u del ULTIMO peldano convergido; U_WORK[si] = u ELASTICA (la referencia que resta GEO5)
for si in range(NSTAGES):
    t0 = time.time()
    print("\n### %s ###" % names[si])
    fs, prog, u_last = srm(Fst[si])
    FS.append(fs); TT.append(time.time() - t0); U_STAGES.append(u_last); U_WORK.append(U_WORK_LAST)
    print("    progresion dx(mm) por SRF:%s" % prog)
    print("  %-22s >>> FS=%.4f  (GEO5=%.2f)  [%.1f s]" % (names[si], fs, gfs[si], TT[si]))
print("\n================ RESUMEN (Hekatan Python, malla exacta GEO5) ================")
for si in range(NSTAGES):
    print("  %-22s FS=%.4f  (GEO5 %.2f)  dif=%+.1f%%  t=%.1f s" % (names[si], FS[si], gfs[si], 100 * (FS[si] / gfs[si] - 1), TT[si]))
print("TOTAL %.1f s" % (time.time() - t_total))

# volcado por peldano (solo CPython; el motor nativo no tiene savez)
try:
    import numpy as _np
    _srf = [U_STEPS[i] for i in range(0, len(U_STEPS), 2)]; _U = [U_STEPS[i] for i in range(1, len(U_STEPS), 2)]
    _np.savez("srm_steps_hek_e%d.npz" % NSTAGES, srf=_np.array(_srf), U=_np.array(_U), U1=_np.array(U_WORK), FS=_np.array(FS))
    import pickle; pickle.dump(STEP_TRACE, open("step_trace_e%d.pkl" % NSTAGES, "wb"))
    if DUMP_SIG: pickle.dump((HK_DUMP, DUMP_SIG, DUMP_DEPS), open("sig_dump_s%d_rs%d_it%d.pkl" % HK_DUMP, "wb"))
    if DUMP_ALL: pickle.dump(DUMP_ALL, open("sig_dump_all_e%d%s.pkl" % (NSTAGES, "" if SRF_ROUND else "_exact"), "wb"))
except Exception:
    pass
