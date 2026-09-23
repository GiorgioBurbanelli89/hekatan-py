# -*- coding: utf-8 -*-
# MURO DE CORTE afinado: Abaqus (CDP, DAMAGET) vs Python (numpy, visc baja).
# Muro 3 m x 2 m x 0.2 m, 54x36 Q4 plane stress, bordes confinados, carga lateral
# en el tope al 67 % (ux = 4.04 mm, como Abaqus). Daño a tracción en la dirección
# principal + regularización viscosa. Unidades: N, mm, MPa.
import os, sys
os.environ.setdefault("OPENBLAS_NUM_THREADS", "1")   # un hilo: sin "Memory allocation still failed"
import numpy as np, math, html as _html
try: sys.stdout.reconfigure(encoding="utf-8")   # ν, η en consolas cp1252
except Exception: pass
from scipy.sparse import csr_matrix
from scipy.sparse.linalg import spsolve

# ─── salida estilo Hekatan ───────────────────────────────────────────────────
def _emit(h): print("__CPSPY_HTML__:" + h.replace("\n", " "))
def cp_h(t): _emit(f'<h3 style="color:#1a4f7a;border-bottom:1px solid #d9d9d9;margin:.9em 0 .3em">{_html.escape(t)}</h3>')
def cp_p(t): _emit(f'<p class="line"><span class="eq">{t}</span></p>')
def cp_val(n, v, u="", f="{:.3f}"):
    b, s = (n.split("_", 1) + [""])[:2]
    var = f'<var>{b}</var>' + (f'<sub>{s}</sub>' if s else "")
    _emit(f'<p class="line"><span class="eq">{var} = <b>{f.format(v)}</b> <i class="unit">{u}</i></span></p>')

# ─── 1. Datos ────────────────────────────────────────────────────────────────
W, Hh, t = 3000.0, 2000.0, 200.0
nx, ny = 54, 36
E, nu, ft = 25000.0, 0.2, 2.6
be = 0.15*W                                  # ancho de los elementos de borde
umax, nstep, visc = 4.04, 30, 0.004          # visc BAJA -> la grieta localiza
cp_h("1. Datos del muro")
cp_val("W", W, "mm", "{:.0f}"); cp_val("H", Hh, "mm", "{:.0f}"); cp_val("t", t, "mm", "{:.0f}")
cp_val("E", E, "MPa", "{:.0f}"); cp_val("ν", nu, "", "{:.2f}"); cp_val("f_t", ft, "MPa", "{:.2f}")
cp_val("u_x,max", umax, "mm", "{:.2f}"); cp_val("pasos", nstep, "", "{:.0f}"); cp_val("η_visc", visc, "", "{:.3f}")

# ─── 2. Malla y rigidez ──────────────────────────────────────────────────────
D0 = E/(1-nu*nu)*np.array([[1,nu,0],[nu,1,0],[0,0,(1-nu)/2]])
def NID(i,j): return j*(nx+1)+i
XY = np.array([[W*i/nx, Hh*j/ny] for j in range(ny+1) for i in range(nx+1)])
els = np.array([[NID(i,j),NID(i+1,j),NID(i+1,j+1),NID(i,j+1)] for j in range(ny) for i in range(nx)])
NE, NN, ng = len(els), len(XY), 2*len(XY)
g = 1/math.sqrt(3); GP = [(-g,-g),(g,-g),(g,g),(-g,g)]
def shp(xi,et): return 0.25*np.array([[-(1-et),(1-et),(1+et),-(1+et)],[-(1-xi),-(1+xi),(1+xi),(1-xi)]])
def Bmat(dNx):
    B = np.zeros((3,8))
    for a in range(4):
        B[0,2*a]=dNx[0,a]; B[1,2*a+1]=dNx[1,a]; B[2,2*a]=dNx[1,a]; B[2,2*a+1]=dNx[0,a]
    return B
Bc=[]; Ke0=[]; Dof=[]; Vole=np.zeros(NE); cent=np.zeros((NE,2))
for e_i,e in enumerate(els):
    co=XY[e]; Ke=np.zeros((8,8)); vv=0.0
    for (xi,et) in GP:
        dN=shp(xi,et); J=dN@co; dJ=abs(np.linalg.det(J)); dNx=np.linalg.solve(J,dN)
        Ke += Bmat(dNx).T@D0@Bmat(dNx)*dJ*t; vv+=dJ*t
    dN0=shp(0,0); J0=dN0@co; Bc.append(Bmat(np.linalg.solve(J0,dN0)))
    Ke0.append(Ke); Vole[e_i]=vv; cent[e_i]=co.mean(0)
    Dof.append(np.hstack([[2*a,2*a+1] for a in e]))
I=np.zeros(NE*64,int); J=np.zeros(NE*64,int); V0=np.zeros(NE*64)
for e in range(NE):
    de=Dof[e]; I[e*64:(e+1)*64]=np.repeat(de,8); J[e*64:(e+1)*64]=np.tile(de,8); V0[e*64:(e+1)*64]=Ke0[e].ravel()
borde = (cent[:,0]<be) | (cent[:,0]>W-be)
ftE = np.where(borde, 3.0*ft, ft)            # borde confinado: 3·ft
top=[NID(i,ny) for i in range(nx+1)]
fix=set()
for i in range(nx+1): fix.add(2*NID(i,0)); fix.add(2*NID(i,0)+1)   # base empotrada
for n in top: fix.add(2*n+1); fix.add(2*n)                         # tope: uy=0, ux impuesto
imp=[2*n for n in top]
free=np.array([i for i in range(ng) if i not in fix])
cp_h("2. Malla")
cp_val("elementos", NE, "Q4", "{:.0f}"); cp_val("nudos", NN, "", "{:.0f}"); cp_val("GDL", ng, "", "{:.0f}")

# ─── 3. Solución incremental con daño (la grieta se dibuja EN VIVO) ──────────
#  Cada paso imprime una figura dentro de <div class="hkvivo">; el <script> de ese
#  mismo bloque borra los cuadros anteriores -> se ve UNA figura que se actualiza.
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt, io, base64
from matplotlib.collections import PolyCollection
def cuadro(st, ux):
    fig,ax=plt.subplots(figsize=(8.5,5.2))
    Xd=XY+74*np.column_stack([U[0::2],U[1::2]])                 # deformada x74
    pc=PolyCollection([Xd[e] for e in els],array=dv,cmap="jet",edgecolors=(0.7,0.7,0.7),linewidths=0.1)
    pc.set_clim(0,0.9); ax.add_collection(pc); fig.colorbar(pc,ax=ax,shrink=0.85)
    ax.plot([0,W,W,0,0],[0,0,Hh,Hh,0],color="0.35",lw=1.2)
    ax.set_xlim(-100,W+400); ax.set_ylim(-100,Hh+100); ax.set_aspect("equal")
    ax.set_title("Grieta del muro - paso %d/%d   u$_x$=%.2f mm   d$_{max}$=%.3f   agrietados=%d"
                 %(st,nstep,ux,dv.max(),int((dv>0.5).sum())),fontsize=11)
    b=io.BytesIO(); fig.savefig(b,format="png",dpi=80); plt.close(fig)
    _emit('<div class="hkvivo" style="text-align:center"><img style="max-width:100%" src="data:image/png;base64,'
          + base64.b64encode(b.getvalue()).decode() + '"/></div>'
          '<script>(function(){var f=document.querySelectorAll(".hkvivo");'
          'for(var i=0;i<f.length-1;i++)f[i].remove();})();</script>')
curva_x=[0,0.0006,0.002,0.006]; curva_s=[2.6,0.5,0.1,0.05]     # ablandamiento a tracción
dv=np.zeros(NE); epl=np.zeros((NE,3)); epq=np.zeros(NE); U=np.zeros(ng); dts=1.0/nstep
for st in range(1,nstep+1):
    ux=umax*st/nstep
    for it in range(10):
        K=csr_matrix((V0*np.repeat(1-dv,64),(I,J)),shape=(ng,ng))
        Fpl=np.zeros(ng)
        for e in range(NE): Fpl[Dof[e]]+=(1-dv[e])*Vole[e]*(Bc[e].T@(D0@epl[e]))
        U=np.zeros(ng)
        for dd in imp: U[dd]=ux
        R=Fpl-K@U; U[free]=spsolve(K[free][:,free],R[free])
        dold=dv.copy()
        for e in range(NE):
            sb=D0@(Bc[e]@U[Dof[e]]-epl[e])
            savg=(sb[0]+sb[1])/2; rad=math.sqrt(((sb[0]-sb[1])/2)**2+sb[2]**2)
            s1=savg+rad; th=0.5*math.atan2(2*sb[2], sb[0]-sb[1])
            if s1>ftE[e]:
                c,s2=math.cos(th),math.sin(th); m=np.array([c*c, s2*s2, 2*c*s2])
                dl=(s1-ftE[e])/(m@D0@m)
                if dl>0: epl[e]+=dl*m; epq[e]+=dl
            db=min(0.95, 0.0 if epq[e]<=0 else 1-(np.interp(epq[e],curva_x,curva_s) if epq[e]<0.006 else 0.05)/ft)
            dv[e]=(dv[e]+(dts/visc)*db)/(1+dts/visc)
        if np.max(np.abs(dv-dold))<3e-3: break
    cuadro(st, ux)                                              # cuadro vivo del paso
cp_h("3. Resultado Python")
cp_val("d_max", dv.max(), "", "{:.3f}")
cp_val("elementos con d > 0.5", int((dv>0.5).sum()), "", "{:.0f}")

# ─── 4. Comparación con Abaqus (DAMAGET, mismo centroide) ────────────────────
try: aqui = os.path.dirname(os.path.abspath(__file__))
except NameError: aqui = os.getcwd()
A={}
for l in open(os.path.join(aqui,"abq_wall_dc.csv")).read().splitlines()[1:]:
    x,y,d=l.split(","); A[(round(float(x),1),round(float(y),1))]=float(d)
ab=np.array([A.get((round(c[0],1),round(c[1],1)),np.nan) for c in cent]); m=~np.isnan(ab)
err=np.mean(np.abs(dv[m]-ab[m])); corr=np.corrcoef(dv[m],ab[m])[0,1]
cp_h("4. Abaqus vs Python")
cp_p("error medio = (1/n)·Σ|d<sub>Py</sub> − d<sub>Abq</sub>| ,  corr = correlación de Pearson celda a celda")
cp_val("error_medio", 100*err, "%", "{:.1f}"); cp_val("corr", corr, "", "{:.2f}")

polys=[XY[e] for e in els]
fig,axs=plt.subplots(1,2,figsize=(16,5.6))
for ax,(dat,ttl) in zip(axs,[(ab,"ABAQUS (DAMAGET)"),(dv,"PYTHON afinado (visc baja)")]):
    pc=PolyCollection(polys,array=np.nan_to_num(dat),cmap="jet",edgecolors=(0.7,0.7,0.7),linewidths=0.1)
    pc.set_clim(0,0.9); ax.add_collection(pc); ax.set_xlim(0,W); ax.set_ylim(0,Hh); ax.set_aspect("equal")
    ax.set_title(ttl,fontsize=13); ax.set_xticks([]); ax.set_yticks([]); fig.colorbar(pc,ax=ax,shrink=0.8)
fig.suptitle("MURO afinado: Abaqus vs Python  |  error medio=%.0f%%  corr=%.2f"%(100*err,corr),fontsize=15)
fig.tight_layout()
buf=io.BytesIO(); fig.savefig(buf,format="png",dpi=100); plt.close(fig)
print("__CPSPY_IMG__:"+base64.b64encode(buf.getvalue()).decode())
