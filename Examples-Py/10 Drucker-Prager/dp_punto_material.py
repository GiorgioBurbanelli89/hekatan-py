#"Drucker-Prager — punto material (el puente lineal → no lineal)
#'Mismo continuo que la membrana lineal, pero con **material elastoplastico** de suelo.
#'Un punto material bajo carga desviadora: la res_puesta pasa de **elastica** (recta) a **fluencia**
#'(meseta) al llegar a la superficie de Drucker-Prager. Es el material del GeoFEM no-lineal.
import numpy as np
import math

#"1. Material (suelo)
E = 1.303470e5   #' modulo [kPa]
nu = 0.30        #' Poisson
phi = 22.70      #' friccion [grados]
c = 9.0          #' cohesion [kPa]
p_0 = -100.0     #' confinamiento inicial [kPa] (compresion negativa)
#hide
s_p = math.sin(phi*math.pi/180)
c_p = math.cos(phi*math.pi/180)
#show
#'Parametros Drucker-Prager (ajustado a Mohr-Coulomb en extension triaxial, como GEO5):
alpha = 2*s_p/(math.sqrt(3)*(3+s_p))
k_dp = 6*c*c_p/(math.sqrt(3)*(3+s_p))

#"2. Constitutiva y criterio de fluencia
#hide
fE = E/((1+nu)*(1-2*nu))
D = fE*np.array([[1-nu, nu, nu, 0.0],
                 [nu, 1-nu, nu, 0.0],
                 [nu, nu, 1-nu, 0.0],
                 [0.0, 0.0, 0.0, (1-2*nu)/2]])
def yieldF(s):
    p = (s[0]+s[1]+s[2])/3.0
    d0 = s[0]-p
    d1 = s[1]-p
    d2 = s[2]-p
    J2 = 0.5*(d0*d0+d1*d1+d2*d2) + s[3]*s[3]
    sq = math.sqrt(J2)
    F = 3*alpha*p + sq - k_dp
    if sq > 1e-12:
        dQ = np.array([d0/(2*sq), d1/(2*sq), d2/(2*sq), s[3]/sq])
    else:
        dQ = np.array([0.0, 0.0, 0.0, 0.0])
    return F, sq, p, dQ
#show
#c_p f = 3*alpha*p + sqrt(J2) - k

#"3. Ensayo strain-driven (carga desviadora a p constante)
#hide
sig = np.array([p_0, p_0, p_0, 0.0])
de = 1.0e-4
dtc = 0.5*4*(1+nu)*(1-2*nu)/(E*(1-2*nu+s_p*s_p))
nstep = 120
tabla_e = []
tabla_q = []
eps = 0.0
for step in range(nstep):
    eps = eps + de
    sig = sig + D @ np.array([de, -de, 0.0, 0.0])
    F, sq, p, dQ = yieldF(sig)
    if F > 0:
        for it in range(1000):
            F, sq, p, dQ = yieldF(sig)
            if F < 1e-4:
                break
            sig = sig - D @ (dtc*F*dQ)
    tabla_e.append(eps*100)
    tabla_q.append(abs(sig[1]-sig[0]))
#show
#'Curva **q vs deformacion** (kPa) — nota el paso de elastica a meseta:
print("   eps[%]     q[kPa]")
for i in [0, 1, 2, 4, 8, 20, 60, 119]:
    print("   %6.3f   %8.2f" % (tabla_e[i], tabla_q[i]))

#"4. Validacion contra la superficie DP analitica
#hide
Ffin, sqfin, pfin, dQf = yieldF(sig)
sq_an = k_dp - 3*alpha*pfin
err = abs(sqfin - sq_an)/abs(sq_an)*100
#show
q_max = tabla_q[nstep-1]
print("  q maximo (meseta) = %.2f kPa" % q_max)
print("  en fluencia:  sqrt(J2) FEM = %.3f   analitico (k - 3a*p) = %.3f   err = %.3f%%" % (sqfin, sq_an, err))
#'**Resultado:** el retorno Drucker-Prager se queda exacto en la superficie de fluencia.
#'Ensamblando muchos de estos puntos en una malla + reduccion c-φ se obtiene el **GeoFEM** (talud, FS).
