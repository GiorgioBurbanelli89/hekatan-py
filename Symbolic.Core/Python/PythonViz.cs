// AUTO-GENERADO (gen_pyviz.py) — plantilla JS del visor 3D de la mesa para Calcpad-Py.
// El builtin nativo mesa_viewer(...) inyecta datos aqui; el .py queda limpio.
using System.Globalization;
using System.Text;

namespace Calcpad.Core.Python
{
    internal static class PythonViz
    {
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        private const string MesaTemplate = @"<div id=""__ID__"" style=""font:13px Segoe UI""> <b>Resultado:</b> <select id=""__ID__s"" style=""font:13px Segoe UI;padding:2px 6px;margin:4px 0""> <option value=""w"">Deflexión w [mm]</option> <option value=""Mxy"">Momento torsor Mxy [kNm/m]</option> <option value=""Mx"">Momento Mx [kNm/m]</option> <option value=""My"">Momento My [kNm/m]</option> </select> <div style=""display:flex;flex-wrap:wrap;gap:10px""><div id=""__ID__2""></div><div id=""__ID__3""></div></div></div> <script>(function(){ var na=__NA__,nb=__NB__,A=__A__,Bb=__B__,Hc=__H__; var D={w:[],Mxy:[],Mx:[],My:[]},U={w:""mm"",Mxy:""kNm/m"",Mx:""kNm/m"",My:""kNm/m""}; __DATA__ var nx=na+1,ny=nb+1; function jt(t){t=Math.max(0,Math.min(1,t));return[Math.max(0,Math.min(1,Math.min(4*t-1.5,-4*t+4.5)))*255|0,Math.max(0,Math.min(1,Math.min(4*t-0.5,-4*t+3.5)))*255|0,Math.max(0,Math.min(1,Math.min(4*t+0.5,-4*t+2.5)))*255|0];} function jc(t){var c=jt(t);return new THREE.Color(c[0]/255,c[1]/255,c[2]/255);} var P2=document.getElementById(""__ID__2""),P3=document.getElementById(""__ID__3""),SEL=document.getElementById(""__ID__s""); var tip=document.createElement(""div"");tip.style.cssText=""position:fixed;pointer-events:none;background:rgba(20,20,28,.9);color:#fff;font:12px Consolas;padding:3px 7px;border-radius:4px;display:none;z-index:99999"";document.body.appendChild(tip); function draw2D(g,uni){var W=420,H=380,ml=38,mr=64,mt=16,pw=W-ml-mr,ph=H-mt-26;P2.innerHTML=`` ;var hd=document.createElement(""div"");var wr=document.createElement(""div"");wr.style.cssText=""position:relative;width:""+W+""px;height:""+H+""px;flex:0 0 auto"";var bs=document.createElement(""canvas"");bs.width=W;bs.height=H;bs.style.cssText=""position:absolute;border:1px solid #ddd"";var ov=document.createElement(""canvas"");ov.width=W;ov.height=H;ov.style.cssText=""position:absolute;pointer-events:none"";wr.appendChild(bs);wr.appendChild(ov);P2.appendChild(hd);P2.appendChild(wr);var cx=bs.getContext(""2d""),ox=ov.getContext(""2d""); var xs=[];for(var i=0;i<nx;i++)xs.push(i*A/na);var ys=[];for(var j=0;j<ny;j++)ys.push(j*Bb/nb);function gv(i,j){return g[i*ny+j];} function SX(x){return ml+x/A*pw;}function SY(y){return mt+(Bb-y)/Bb*ph;}function wX(p){return(p-ml)/pw*A;}function wY(p){return Bb-(p-mt)/ph*Bb;} function bl(x,y){if(x<0||x>A||y<0||y>Bb)return null;var i=0;while(i<nx-2&&xs[i+1]<x)i++;var j=0;while(j<ny-2&&ys[j+1]<y)j++;var u=(x-xs[i])/(xs[i+1]-xs[i]),v=(y-ys[j])/(ys[j+1]-ys[j]);return gv(i,j)*(1-u)*(1-v)+gv(i+1,j)*u*(1-v)+gv(i,j+1)*(1-u)*v+gv(i+1,j+1)*u*v;} var vn=1e30,vx=-1e30;for(var k=0;k<g.length;k++){if(g[k]<vn)vn=g[k];if(g[k]>vx)vx=g[k];}if(vx-vn<1e-9)vx=vn+1; var im=cx.createImageData(pw,ph),dd=im.data;for(var py=0;py<ph;py++)for(var px=0;px<pw;px++){var v=bl(wX(ml+px),wY(mt+py)),qq=(py*pw+px)*4;if(v==null){dd[qq+3]=0;}else{var c=jt((v-vn)/(vx-vn));dd[qq]=c[0];dd[qq+1]=c[1];dd[qq+2]=c[2];dd[qq+3]=255;}}cx.putImageData(im,ml,mt); cx.strokeStyle=""rgba(40,40,40,.25)"";for(var i=0;i<nx;i++){cx.beginPath();cx.moveTo(SX(xs[i]),mt);cx.lineTo(SX(xs[i]),mt+ph);cx.stroke();}for(var j=0;j<ny;j++){cx.beginPath();cx.moveTo(ml,SY(ys[j]));cx.lineTo(ml+pw,SY(ys[j]));cx.stroke();}cx.strokeStyle=""#888"";cx.strokeRect(ml,mt,pw,ph); var cbx=W-mr+20;cx.font=""10px Consolas"";for(var k=0;k<ph;k++){var c=jt(1-k/ph);cx.fillStyle=""rgb(""+c[0]+"",""+c[1]+"",""+c[2]+"")"";cx.fillRect(cbx,mt+k,13,1);}cx.fillStyle=""#333"";cx.fillText(vx.toFixed(2),cbx-2,mt-3);cx.fillText(vn.toFixed(2),cbx-2,mt+ph+10); hd.innerHTML=""<b>2D (planta)</b> max=""+vx.toFixed(2)+uni+"" min=""+vn.toFixed(2)+uni; bs.onmousemove=function(ev){var rc=bs.getBoundingClientRect();var px=ev.clientX-rc.left,py=ev.clientY-rc.top,x=wX(px),y=wY(py);var v=(px>=ml&&px<=ml+pw&&py>=mt&&py<=mt+ph)?bl(x,y):null;ox.clearRect(0,0,W,H);if(v==null)return;ox.strokeStyle=""#000"";ox.beginPath();ox.moveTo(px,mt);ox.lineTo(px,mt+ph);ox.moveTo(ml,py);ox.lineTo(ml+pw,py);ox.stroke();ox.fillStyle=""rgba(20,20,28,.9)"";ox.fillRect(px+8,py-15,140,15);ox.fillStyle=""#fff"";ox.font=""11px Consolas"";ox.fillText(v.toFixed(2)+uni+"" @(""+x.toFixed(1)+"",""+y.toFixed(1)+"")"",px+11,py-4);};bs.onmouseleave=function(){ox.clearRect(0,0,W,H);};} var scn,cam,ren,ctrl,grp,mesh,geo,vv,rdy=false; function init3D(){var W=440,H=400;scn=new THREE.Scene();scn.background=new THREE.Color(0xeef0f4);cam=new THREE.PerspectiveCamera(45,W/H,.001,9000);ren=new THREE.WebGLRenderer({antialias:true,preserveDrawingBuffer:true});ren.setSize(W,H);var hd=document.createElement(""div"");hd.id=""__ID__3h"";P3.appendChild(hd);P3.appendChild(ren.domElement);cam.up.set(0,0,1);var dg0=Math.hypot(A,Bb,Hc)||1;cam.position.set(A/2+dg0,Bb/2-dg0*1.4,Hc/2+dg0);cam.lookAt(A/2,Bb/2,Hc/2);ctrl=new THREE.OrbitControls(cam,ren.domElement);ctrl.target.set(A/2,Bb/2,Hc/2);ctrl.update();scn.add(new THREE.AmbientLight(0xffffff,.9));var dl=new THREE.DirectionalLight(0xffffff,.5);dl.position.set(8,-12,18);scn.add(dl);var ray=new THREE.Raycaster(),mo=new THREE.Vector2();ren.domElement.addEventListener(""mousemove"",function(ev){if(!mesh)return;var r=ren.domElement.getBoundingClientRect();mo.x=((ev.clientX-r.left)/r.width)*2-1;mo.y=-((ev.clientY-r.top)/r.height)*2+1;ray.setFromCamera(mo,cam);var h=ray.intersectObject(mesh,false);if(h.length){var f=h[0].face,ap=geo.attributes.position,p0=new THREE.Vector3().fromBufferAttribute(ap,f.a),p1=new THREE.Vector3().fromBufferAttribute(ap,f.b),p2=new THREE.Vector3().fromBufferAttribute(ap,f.c),bc=new THREE.Vector3();new THREE.Triangle(p0,p1,p2).getBarycoord(h[0].point,bc);var val=bc.x*vv[f.a]+bc.y*vv[f.b]+bc.z*vv[f.c];tip.style.display=""block"";tip.style.left=(ev.clientX+13)+""px"";tip.style.top=(ev.clientY+8)+""px"";tip.innerHTML=val.toFixed(2);}else tip.style.display=""none"";});ren.domElement.addEventListener(""mouseleave"",function(){tip.style.display=""none"";});function anim(){requestAnimationFrame(anim);ctrl.update();ren.render(scn,cam);}anim();rdy=true;} function build3D(colorG,uni){if(!rdy)return;if(grp)scn.remove(grp);grp=new THREE.Group(); var wG=D.w;function wv(i,j){return wG[i*ny+j];}function cg(i,j){return colorG[i*ny+j];} var wn=Math.min.apply(null,wG),wx=Math.max.apply(null,wG);var wa=Math.max(Math.abs(wn),Math.abs(wx),1e-9);var ampw=(.40*Hc)/wa; function Pt(i,j){return new THREE.Vector3(i*A/na,j*Bb/nb,Hc+wv(i,j)*ampw);} var cn=Math.min.apply(null,colorG),cx2=Math.max.apply(null,colorG);if(cx2-cn<1e-9)cx2=cn+1; var pos=[],col=[];vv=[];function pv(p,t){pos.push(p.x,p.y,p.z);var c=jc(t);col.push(c.r,c.g,c.b);} for(var i=0;i<nx-1;i++)for(var j=0;j<ny-1;j++){var pa=Pt(i,j),pb=Pt(i+1,j),pc=Pt(i+1,j+1),pd=Pt(i,j+1),ta=(cg(i,j)-cn)/(cx2-cn),tb=(cg(i+1,j)-cn)/(cx2-cn),tc=(cg(i+1,j+1)-cn)/(cx2-cn),td=(cg(i,j+1)-cn)/(cx2-cn);pv(pa,ta);pv(pb,tb);pv(pc,tc);vv.push(cg(i,j),cg(i+1,j),cg(i+1,j+1));pv(pa,ta);pv(pc,tc);pv(pd,td);vv.push(cg(i,j),cg(i+1,j+1),cg(i,j+1));} geo=new THREE.BufferGeometry();geo.setAttribute(""position"",new THREE.Float32BufferAttribute(pos,3));geo.setAttribute(""color"",new THREE.Float32BufferAttribute(col,3));geo.computeVertexNormals();mesh=new THREE.Mesh(geo,new THREE.MeshBasicMaterial({vertexColors:true,side:THREE.DoubleSide}));grp.add(mesh); var wp=[];for(var i=0;i<nx;i++)for(var j=0;j<ny-1;j++){var aa=Pt(i,j),bb=Pt(i,j+1);wp.push(aa.x,aa.y,aa.z,bb.x,bb.y,bb.z);}for(var j=0;j<ny;j++)for(var i=0;i<nx-1;i++){var aa=Pt(i,j),bb=Pt(i+1,j);wp.push(aa.x,aa.y,aa.z,bb.x,bb.y,bb.z);}var wg=new THREE.BufferGeometry();wg.setAttribute(""position"",new THREE.Float32BufferAttribute(wp,3));grp.add(new THREE.LineSegments(wg,new THREE.LineBasicMaterial({color:0x556677}))); var corn=[[0,0],[nx-1,0],[nx-1,ny-1],[0,ny-1]]; var cp=[];for(var k=0;k<4;k++){var ci=corn[k][0],cj=corn[k][1],ptop=Pt(ci,cj);cp.push(ci*A/na,cj*Bb/nb,0,ptop.x,ptop.y,ptop.z);}var cgeo=new THREE.BufferGeometry();cgeo.setAttribute(""position"",new THREE.Float32BufferAttribute(cp,3));grp.add(new THREE.LineSegments(cgeo,new THREE.LineBasicMaterial({color:0x222222}))); var bp=[];function edge(ii,jj,di,dj,n){for(var s=0;s<n;s++){var a1=Pt(ii+di*s,jj+dj*s),a2=Pt(ii+di*(s+1),jj+dj*(s+1));bp.push(a1.x,a1.y,a1.z,a2.x,a2.y,a2.z);}} edge(0,0,1,0,nx-1);edge(0,ny-1,1,0,nx-1);edge(0,0,0,1,ny-1);edge(nx-1,0,0,1,ny-1);var bgeo=new THREE.BufferGeometry();bgeo.setAttribute(""position"",new THREE.Float32BufferAttribute(bp,3));grp.add(new THREE.LineSegments(bgeo,new THREE.LineBasicMaterial({color:0x8d6e63,linewidth:2}))); for(var k=0;k<4;k++){var ci=corn[k][0]*A/na,cj=corn[k][1]*Bb/nb,cm=new THREE.Mesh(new THREE.ConeGeometry(.05*Math.max(A,Bb),.10*Math.max(A,Bb),4),new THREE.MeshBasicMaterial({color:0x2244aa}));cm.position.set(ci,cj,-.05*Math.max(A,Bb));cm.rotation.x=Math.PI/2;grp.add(cm);} scn.add(grp); var cxx=A/2,cyy=Bb/2,czz=Hc/2,diag=Math.hypot(A,Bb,Hc)||1;cam.up.set(0,0,1);cam.position.set(cxx+diag*1.0,cyy-diag*1.4,czz+diag*.9);cam.lookAt(cxx,cyy,czz);ctrl.target.set(cxx,cyy,czz);ctrl.update(); document.getElementById(""__ID__3h"").innerHTML=""<b>Mesa 3D — ""+SEL.options[SEL.selectedIndex].text+""</b> max=""+cx2.toFixed(2)+uni+"" min=""+cn.toFixed(2)+uni+"" (arrastra/zoom/hover)"";} function render(){var k=SEL.value,g=D[k],uni=U[k];draw2D(g,uni);build3D(g,uni);} SEL.onchange=render; var s1=document.createElement(""script"");s1.src=""https://cdn.jsdelivr.net/npm/three@0.145.0/build/three.min.js"";s1.onload=function(){var s2=document.createElement(""script"");s2.src=""https://cdn.jsdelivr.net/npm/three@0.145.0/examples/js/controls/OrbitControls.js"";s2.onload=function(){init3D();render();};document.head.appendChild(s2);};document.head.appendChild(s1); draw2D(D.w,U.w); })();</script>";

        private static string Arr(double[] v)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(','); sb.Append(v[i].ToString(CI)); }
            return sb.Append(']').ToString();
        }

        public static string MesaViewer(double[] dw, double[] dmxy, double[] dmx, double[] dmy,
                                        int na, int nb, double a, double b, double H, string id)
        {
            string data = "D.w=" + Arr(dw) + ";D.Mxy=" + Arr(dmxy) + ";D.Mx=" + Arr(dmx) + ";D.My=" + Arr(dmy) + ";";
            return MesaTemplate
                .Replace("__ID__", id)
                .Replace("__NA__", na.ToString(CI)).Replace("__NB__", nb.ToString(CI))
                .Replace("__A__", a.ToString(CI)).Replace("__B__", b.ToString(CI)).Replace("__H__", H.ToString(CI))
                .Replace("__DATA__", data);
        }

        // ── Visor FEM GENÉRICO interactivo (malla cualquiera): heatmap jet + HOVER
        //    (valor en el cursor) + SELECTOR de campo. Builtin nativo → WebView2. ──
        private const string MeshTemplate = @"<div id=""__ID__"" style=""font:13px Segoe UI;color:#222""><b>__TITLE__</b> &nbsp; Campo: <select id=""__ID__s"" style=""font:13px Segoe UI;padding:2px 6px"">__OPTS__</select><div style=""position:relative;margin-top:6px""><canvas id=""__ID__c""></canvas><canvas id=""__ID__o"" style=""position:absolute;left:0;top:0;pointer-events:none""></canvas></div><div id=""__ID__h"" style=""font:12px Consolas;color:#555;margin-top:4px""></div></div><script>(function(){var D=__DATA__;var SEL=document.getElementById(""__ID__s"");var cv=document.getElementById(""__ID__c""),ov=document.getElementById(""__ID__o""),HD=document.getElementById(""__ID__h"");var ctx=cv.getContext(""2d""),octx=ov.getContext(""2d"");var PAL=[[127,0,0],[175,0,0],[223,0,0],[255,15,0],[255,63,0],[255,127,0],[255,175,0],[255,223,0],[239,255,15],[191,255,63],[127,255,127],[79,255,175],[31,255,223],[0,239,255],[0,191,255],[0,127,255],[0,79,255],[0,31,255],[0,0,239],[0,0,191],[0,0,143]];function jt(t){t=Math.max(0,Math.min(1,t));var b=Math.floor(t*20);if(b<0)b=0;if(b>20)b=20;return PAL[b];}var xs=D.nd.map(function(p){return p[0];}),ys=D.nd.map(function(p){return p[1];});var x0=Math.min.apply(null,xs),x1=Math.max.apply(null,xs),y0=Math.min.apply(null,ys),y1=Math.max.apply(null,ys);var sp=0.06*Math.max(x1-x0,y1-y0||1);x0-=sp;x1+=sp;y0-=sp;y1+=sp;var W=660,H=Math.max(110,Math.round(W*(y1-y0)/(x1-x0)));cv.width=W;cv.height=H;ov.width=W;ov.height=H;function SX(x){return (x-x0)/(x1-x0)*W;}function SY(y){return H-(y-y0)/(y1-y0)*H;}function WX(p){return x0+p/W*(x1-x0);}function WY(p){return y0+(H-p)/H*(y1-y0);}function tval(wx,wy,V){for(var t=0;t<D.tris.length;t++){var tr=D.tris[t],a=D.nd[tr[0]],b=D.nd[tr[1]],c=D.nd[tr[2]];var det=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1]);if(Math.abs(det)<1e-12)continue;var l1=((b[1]-c[1])*(wx-c[0])+(c[0]-b[0])*(wy-c[1]))/det,l2=((c[1]-a[1])*(wx-c[0])+(a[0]-c[0])*(wy-c[1]))/det,l3=1-l1-l2;if(l1>=-1e-6&&l2>=-1e-6&&l3>=-1e-6)return l1*V[tr[0]]+l2*V[tr[1]]+l3*V[tr[2]];}return null;}var cur=null;function render(){var k=SEL.value,V=D.fs[k];var vn=Math.min.apply(null,V),vx=Math.max.apply(null,V);if(vx-vn<1e-9)vx=vn+1;var im=ctx.createImageData(W,H),dd=im.data;for(var py=0;py<H;py++)for(var px=0;px<W;px++){var v=tval(WX(px+0.5),WY(py+0.5),V),q=(py*W+px)*4;if(v==null){dd[q+3]=0;}else{var c=jt((v-vn)/(vx-vn));dd[q]=c[0];dd[q+1]=c[1];dd[q+2]=c[2];dd[q+3]=255;}}ctx.putImageData(im,0,0);ctx.strokeStyle=""rgba(20,20,28,.35)"";ctx.lineWidth=1;for(var t=0;t<D.tris.length;t++){var tr=D.tris[t];ctx.beginPath();ctx.moveTo(SX(D.nd[tr[0]][0]),SY(D.nd[tr[0]][1]));ctx.lineTo(SX(D.nd[tr[1]][0]),SY(D.nd[tr[1]][1]));ctx.lineTo(SX(D.nd[tr[2]][0]),SY(D.nd[tr[2]][1]));ctx.closePath();ctx.stroke();}cur={V:V};HD.innerHTML=""max=""+vx.toFixed(4)+""  min=""+vn.toFixed(4)+""  (pasa el cursor para ver el valor)"";}cv.onmousemove=function(ev){var r=cv.getBoundingClientRect();var px=ev.clientX-r.left,py=ev.clientY-r.top;var v=tval(WX(px),WY(py),cur.V);octx.clearRect(0,0,W,H);if(v==null)return;octx.strokeStyle=""#000"";octx.beginPath();octx.moveTo(px,0);octx.lineTo(px,H);octx.moveTo(0,py);octx.lineTo(W,py);octx.stroke();var s=v.toExponential(3)+"" @(""+WX(px).toFixed(2)+"",""+WY(py).toFixed(2)+"")"";octx.font=""12px Consolas"";var w=octx.measureText(s).width+10;var bx=px+12,by=py-22;if(bx+w>W)bx=px-12-w;if(by<0)by=py+8;octx.fillStyle=""rgba(20,20,28,.92)"";octx.fillRect(bx,by,w,18);octx.fillStyle=""#fff"";octx.fillText(s,bx+5,by+13);};cv.onmouseleave=function(){octx.clearRect(0,0,W,H);};SEL.onchange=render;render();})();</script>";

        // ── Visor 3D ORBIT genérico para SÓLIDOS (hexaedros/tetraedros): superficie
        //    triangulada 3D + campo nodal, THREE.js OrbitControls (arrastrar/zoom/hover),
        //    colormap jet_r. Builtin nativo mesh3d_viewer(...) → WebView2, sin python real. ──
        private const string Solid3DTemplate = @"<div id=""__ID__"" style=""font:13px Segoe UI;color:#222""><b>__TITLE__</b> &nbsp; Campo: <select id=""__ID__s"" style=""font:13px Segoe UI;padding:2px 6px"">__OPTS__</select><div id=""__ID__3"" style=""margin-top:6px""></div><div id=""__ID__h"" style=""font:12px Consolas;color:#555;margin-top:4px""></div></div><script>(function(){var D=__DATA__;var SEL=document.getElementById(""__ID__s""),P3=document.getElementById(""__ID__3""),HD=document.getElementById(""__ID__h"");var ND=D.nd,TR=D.tris;function jt(t){t=Math.max(0,Math.min(1,t));return[Math.max(0,Math.min(1,Math.min(4*t-1.5,-4*t+4.5))),Math.max(0,Math.min(1,Math.min(4*t-0.5,-4*t+3.5))),Math.max(0,Math.min(1,Math.min(4*t+0.5,-4*t+2.5)))];}var xs=ND.map(function(p){return p[0];}),ys=ND.map(function(p){return p[1];}),zs=ND.map(function(p){return p[2];});var x0=Math.min.apply(null,xs),x1=Math.max.apply(null,xs),y0=Math.min.apply(null,ys),y1=Math.max.apply(null,ys),z0=Math.min.apply(null,zs),z1=Math.max.apply(null,zs);var cx=(x0+x1)/2,cy=(y0+y1)/2,cz=(z0+z1)/2,diag=Math.hypot(x1-x0,y1-y0,z1-z0)||1;var scn,cam,ren,ctrl,grp,mesh,geo,vv,tip,rdy=false;function init3D(){var W=560,H=480;scn=new THREE.Scene();scn.background=new THREE.Color(0xeef0f4);cam=new THREE.PerspectiveCamera(45,W/H,diag*0.001,diag*50);ren=new THREE.WebGLRenderer({antialias:true,preserveDrawingBuffer:true});ren.setSize(W,H);P3.appendChild(ren.domElement);cam.up.set(0,0,1);cam.position.set(cx+diag*1.1,cy-diag*1.5,cz+diag*1.0);cam.lookAt(cx,cy,cz);ctrl=new THREE.OrbitControls(cam,ren.domElement);ctrl.target.set(cx,cy,cz);ctrl.update();scn.add(new THREE.AmbientLight(0xffffff,.95));var dl=new THREE.DirectionalLight(0xffffff,.45);dl.position.set(diag,-diag*1.2,diag*1.5);scn.add(dl);tip=document.createElement(""div"");tip.style.cssText=""position:fixed;pointer-events:none;background:rgba(20,20,28,.9);color:#fff;font:12px Consolas;padding:3px 7px;border-radius:4px;display:none;z-index:99999"";document.body.appendChild(tip);var ray=new THREE.Raycaster(),mo=new THREE.Vector2();ren.domElement.addEventListener(""mousemove"",function(ev){if(!mesh)return;var r=ren.domElement.getBoundingClientRect();mo.x=((ev.clientX-r.left)/r.width)*2-1;mo.y=-((ev.clientY-r.top)/r.height)*2+1;ray.setFromCamera(mo,cam);var h=ray.intersectObject(mesh,false);if(h.length){var f=h[0].face,ap=geo.attributes.position,p0=new THREE.Vector3().fromBufferAttribute(ap,f.a),p1=new THREE.Vector3().fromBufferAttribute(ap,f.b),p2=new THREE.Vector3().fromBufferAttribute(ap,f.c),bc=new THREE.Vector3();new THREE.Triangle(p0,p1,p2).getBarycoord(h[0].point,bc);var val=bc.x*vv[f.a]+bc.y*vv[f.b]+bc.z*vv[f.c];tip.style.display=""block"";tip.style.left=(ev.clientX+13)+""px"";tip.style.top=(ev.clientY+8)+""px"";tip.innerHTML=val.toFixed(4);}else tip.style.display=""none"";});ren.domElement.addEventListener(""mouseleave"",function(){tip.style.display=""none"";});function anim(){requestAnimationFrame(anim);ctrl.update();ren.render(scn,cam);}anim();rdy=true;}function build(name){if(!rdy)return;if(grp)scn.remove(grp);grp=new THREE.Group();var V=D.fs[name];var vn=Math.min.apply(null,V),vx=Math.max.apply(null,V);if(vx-vn<1e-9)vx=vn+1;var pos=[],col=[];vv=[];for(var t=0;t<TR.length;t++){var tr=TR[t];for(var k=0;k<3;k++){var ni=tr[k],p=ND[ni];pos.push(p[0],p[1],p[2]);var c=jt(1-(V[ni]-vn)/(vx-vn));col.push(c[0],c[1],c[2]);vv.push(V[ni]);}}geo=new THREE.BufferGeometry();geo.setAttribute(""position"",new THREE.Float32BufferAttribute(pos,3));geo.setAttribute(""color"",new THREE.Float32BufferAttribute(col,3));geo.computeVertexNormals();mesh=new THREE.Mesh(geo,new THREE.MeshBasicMaterial({vertexColors:true,side:THREE.DoubleSide}));grp.add(mesh);var wp=[];for(var t=0;t<TR.length;t++){var tr=TR[t];for(var k=0;k<3;k++){var a=ND[tr[k]],b=ND[tr[(k+1)%3]];wp.push(a[0],a[1],a[2],b[0],b[1],b[2]);}}var wg=new THREE.BufferGeometry();wg.setAttribute(""position"",new THREE.Float32BufferAttribute(wp,3));grp.add(new THREE.LineSegments(wg,new THREE.LineBasicMaterial({color:0x556677,transparent:true,opacity:.25})));scn.add(grp);HD.innerHTML=""max=""+vx.toFixed(4)+""  min=""+vn.toFixed(4)+""  (arrastra=orbita · rueda=zoom · hover=valor)"";}function render(){build(SEL.value);}SEL.onchange=render;function go(){init3D();render();}if(window.THREE&&THREE.OrbitControls){go();}else{var s1=document.createElement(""script"");s1.src=""https://cdn.jsdelivr.net/npm/three@0.145.0/build/three.min.js"";s1.onload=function(){var s2=document.createElement(""script"");s2.src=""https://cdn.jsdelivr.net/npm/three@0.145.0/examples/js/controls/OrbitControls.js"";s2.onload=go;document.head.appendChild(s2);};document.head.appendChild(s1);}})();</script>";

        public static string Solid3DViewer(double[][] nodes3, int[][] tris,
                                           string[] names, double[][] vals, string title, string id)
        {
            var nd = new StringBuilder("[");
            for (int i = 0; i < nodes3.Length; i++)
            {
                if (i > 0) nd.Append(',');
                nd.Append('[').Append(nodes3[i][0].ToString(CI)).Append(',')
                  .Append(nodes3[i][1].ToString(CI)).Append(',')
                  .Append(nodes3[i][2].ToString(CI)).Append(']');
            }
            nd.Append(']');
            var tr = new StringBuilder("[");
            for (int i = 0; i < tris.Length; i++)
            {
                if (i > 0) tr.Append(',');
                tr.Append('[').Append(tris[i][0]).Append(',').Append(tris[i][1]).Append(',').Append(tris[i][2]).Append(']');
            }
            tr.Append(']');
            var fs = new StringBuilder("{");
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0) fs.Append(',');
                fs.Append('"').Append(names[i]).Append("\":").Append(Arr(vals[i]));
            }
            fs.Append('}');
            string data = "{nd:" + nd + ",tris:" + tr + ",fs:" + fs + "}";
            var opts = new StringBuilder();
            foreach (var n in names) opts.Append("<option value=\"").Append(n).Append("\">").Append(n).Append("</option>");
            return Solid3DTemplate.Replace("__ID__", id).Replace("__TITLE__", title)
                                  .Replace("__OPTS__", opts.ToString()).Replace("__DATA__", data);
        }


        // ── Visor 3D de SÓLIDOS con CORTE INTERACTIVO: recibe elementos de VOLUMEN (tetraedros
        //    4 idx / hexaedros 8 idx), extrae la piel en JS y un SLIDER corta el sólido en vivo;
        //    la seccion cortada queda RELLENA (sin huecos). THREE.js OrbitControls + hover, jet_r. ──
        private const string SolidClipTemplate = @"<div id=""__ID__"" style=""font:13px Segoe UI;color:#222""><b>__TITLE__</b> &nbsp; Campo: <select id=""__ID__s"" style=""font:13px Segoe UI;padding:2px 6px"">__OPTS__</select> &nbsp;|&nbsp; Corte: <select id=""__ID__ax"" style=""font:13px Segoe UI;padding:2px 6px""><option value=""-1"">(sin corte)</option><option value=""0"">X</option><option value=""1"">Y</option><option value=""2"">Z</option></select> <input id=""__ID__sl"" type=""range"" min=""0"" max=""1000"" value=""1000"" style=""width:170px;vertical-align:middle""> <label style=""font-size:12px""><input id=""__ID__op"" type=""checkbox""> lado opuesto</label><div id=""__ID__3"" style=""margin-top:6px""></div><div id=""__ID__h"" style=""font:12px Consolas;color:#555;margin-top:4px""></div></div><script>(function(){
var D=__DATA__;var ND=D.nd,EL=D.el,FS=D.fs;
var SEL=document.getElementById(""__ID__s""),AX=document.getElementById(""__ID__ax""),SL=document.getElementById(""__ID__sl""),OP=document.getElementById(""__ID__op""),P3=document.getElementById(""__ID__3""),HD=document.getElementById(""__ID__h"");
var TetF=[[0,1,2],[0,1,3],[0,2,3],[1,2,3]],HexF=[[0,1,2,3],[4,5,6,7],[0,1,5,4],[1,2,6,5],[2,3,7,6],[3,0,4,7]];
function boundary(els){var cnt={},rep={};for(var e=0;e<els.length;e++){var el=els[e],F=el.length===4?TetF:(el.length===8?HexF:null);if(!F)continue;for(var f=0;f<F.length;f++){var fd=F[f],nds=[];for(var k=0;k<fd.length;k++)nds.push(el[fd[k]]);var key=nds.slice().sort(function(x,y){return x-y;}).join(""_"");if(cnt[key]){cnt[key]++;}else{cnt[key]=1;rep[key]=nds;}}}var tris=[];for(var key in cnt)if(cnt[key]===1){var nd=rep[key];tris.push([nd[0],nd[1],nd[2]]);if(nd.length===4)tris.push([nd[0],nd[2],nd[3]]);}return tris;}
var mn=[1e30,1e30,1e30],mx=[-1e30,-1e30,-1e30];for(var i=0;i<ND.length;i++)for(var d=0;d<3;d++){if(ND[i][d]<mn[d])mn[d]=ND[i][d];if(ND[i][d]>mx[d])mx[d]=ND[i][d];}
var cx=(mn[0]+mx[0])/2,cy=(mn[1]+mx[1])/2,cz=(mn[2]+mx[2])/2,diag=Math.hypot(mx[0]-mn[0],mx[1]-mn[1],mx[2]-mn[2])||1;
function kept(){var ax=+AX.value;if(ax<0)return EL;var t=+SL.value/1000,pos=mn[ax]+t*(mx[ax]-mn[ax]),side=OP.checked?-1:1,out=[];for(var e=0;e<EL.length;e++){var el=EL[e],c=0;for(var k=0;k<el.length;k++)c+=ND[el[k]][ax];c/=el.length;if(side*(c-pos)<=0)out.push(el);}return out;}
function jt(t){t=Math.max(0,Math.min(1,t));return[Math.max(0,Math.min(1,Math.min(4*t-1.5,-4*t+4.5))),Math.max(0,Math.min(1,Math.min(4*t-0.5,-4*t+3.5))),Math.max(0,Math.min(1,Math.min(4*t+0.5,-4*t+2.5)))];}
var scn,cam,ren,ctrl,grp,mesh,geo,vv,tip,rdy=false;
function init3D(){var W=580,H=500;scn=new THREE.Scene();scn.background=new THREE.Color(0xeef0f4);cam=new THREE.PerspectiveCamera(45,W/H,diag*0.001,diag*50);ren=new THREE.WebGLRenderer({antialias:true,preserveDrawingBuffer:true});ren.setSize(W,H);P3.appendChild(ren.domElement);cam.up.set(0,0,1);cam.position.set(cx+diag*1.1,cy-diag*1.5,cz+diag*1.0);cam.lookAt(cx,cy,cz);ctrl=new THREE.OrbitControls(cam,ren.domElement);ctrl.target.set(cx,cy,cz);ctrl.update();scn.add(new THREE.AmbientLight(0xffffff,.95));var dl=new THREE.DirectionalLight(0xffffff,.45);dl.position.set(diag,-diag*1.2,diag*1.5);scn.add(dl);tip=document.createElement(""div"");tip.style.cssText=""position:fixed;pointer-events:none;background:rgba(20,20,28,.9);color:#fff;font:12px Consolas;padding:3px 7px;border-radius:4px;display:none;z-index:99999"";document.body.appendChild(tip);var ray=new THREE.Raycaster(),mo=new THREE.Vector2();ren.domElement.addEventListener(""mousemove"",function(ev){if(!mesh)return;var r=ren.domElement.getBoundingClientRect();mo.x=((ev.clientX-r.left)/r.width)*2-1;mo.y=-((ev.clientY-r.top)/r.height)*2+1;ray.setFromCamera(mo,cam);var h=ray.intersectObject(mesh,false);if(h.length){var f=h[0].face,ap=geo.attributes.position,p0=new THREE.Vector3().fromBufferAttribute(ap,f.a),p1=new THREE.Vector3().fromBufferAttribute(ap,f.b),p2=new THREE.Vector3().fromBufferAttribute(ap,f.c),bc=new THREE.Vector3();new THREE.Triangle(p0,p1,p2).getBarycoord(h[0].point,bc);var val=bc.x*vv[f.a]+bc.y*vv[f.b]+bc.z*vv[f.c];tip.style.display=""block"";tip.style.left=(ev.clientX+13)+""px"";tip.style.top=(ev.clientY+8)+""px"";tip.innerHTML=val.toFixed(4);}else tip.style.display=""none"";});ren.domElement.addEventListener(""mouseleave"",function(){tip.style.display=""none"";});function anim(){requestAnimationFrame(anim);ctrl.update();ren.render(scn,cam);}anim();rdy=true;}
function build(){if(!rdy)return;if(grp)scn.remove(grp);grp=new THREE.Group();var tris=boundary(kept());var name=SEL.value,V=FS[name];var vn=1e30,vx=-1e30;for(var q=0;q<V.length;q++){if(V[q]<vn)vn=V[q];if(V[q]>vx)vx=V[q];}if(vx-vn<1e-9)vx=vn+1;var pos=[],col=[];vv=[];for(var t=0;t<tris.length;t++){var tr=tris[t];for(var k=0;k<3;k++){var ni=tr[k],p=ND[ni];pos.push(p[0],p[1],p[2]);var c=jt(1-(V[ni]-vn)/(vx-vn));col.push(c[0],c[1],c[2]);vv.push(V[ni]);}}geo=new THREE.BufferGeometry();geo.setAttribute(""position"",new THREE.Float32BufferAttribute(pos,3));geo.setAttribute(""color"",new THREE.Float32BufferAttribute(col,3));geo.computeVertexNormals();mesh=new THREE.Mesh(geo,new THREE.MeshBasicMaterial({vertexColors:true,side:THREE.DoubleSide}));grp.add(mesh);scn.add(grp);var axn=[""X"",""Y"",""Z""],a=+AX.value;HD.innerHTML=""max=""+vx.toFixed(4)+""  min=""+vn.toFixed(4)+""  ·  ""+tris.length+"" triangulos""+(a<0?""  (sin corte)"":""  ·  corte ""+axn[a]+"" @ ""+(mn[a]+(+SL.value/1000)*(mx[a]-mn[a])).toFixed(3))+""  (arrastra=orbita · corte=slider)"";}
SEL.onchange=build;AX.onchange=build;SL.oninput=build;OP.onchange=build;
function go(){init3D();build();}
if(window.THREE&&THREE.OrbitControls){go();}else{var s1=document.createElement(""script"");s1.src=""https://cdn.jsdelivr.net/npm/three@0.145.0/build/three.min.js"";s1.onload=function(){var s2=document.createElement(""script"");s2.src=""https://cdn.jsdelivr.net/npm/three@0.145.0/examples/js/controls/OrbitControls.js"";s2.onload=go;document.head.appendChild(s2);};document.head.appendChild(s1);}
})();</script>";

        public static string SolidClipViewer(double[][] nodes3, int[][] elems,
                                             string[] names, double[][] vals, string title, string id)
        {
            var nd = new StringBuilder("[");
            for (int i = 0; i < nodes3.Length; i++)
            {
                if (i > 0) nd.Append(',');
                nd.Append('[').Append(nodes3[i][0].ToString(CI)).Append(',')
                  .Append(nodes3[i][1].ToString(CI)).Append(',')
                  .Append(nodes3[i][2].ToString(CI)).Append(']');
            }
            nd.Append(']');
            var el = new StringBuilder("[");
            for (int i = 0; i < elems.Length; i++)
            {
                if (i > 0) el.Append(',');
                el.Append('[');
                for (int k = 0; k < elems[i].Length; k++) { if (k > 0) el.Append(','); el.Append(elems[i][k]); }
                el.Append(']');
            }
            el.Append(']');
            var fs = new StringBuilder("{");
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0) fs.Append(',');
                fs.Append('"').Append(names[i]).Append("\":").Append(Arr(vals[i]));
            }
            fs.Append('}');
            string data = "{nd:" + nd + ",el:" + el + ",fs:" + fs + "}";
            var opts = new StringBuilder();
            foreach (var n in names) opts.Append("<option value=\"").Append(n).Append("\">").Append(n).Append("</option>");
            return SolidClipTemplate.Replace("__ID__", id).Replace("__TITLE__", title)
                                    .Replace("__OPTS__", opts.ToString()).Replace("__DATA__", data);
        }

        public static string MeshViewer(double[][] nodes, int[][] tris,
                                        string[] names, double[][] vals, string title, string id)
        {
            var nd = new StringBuilder("[");
            for (int i = 0; i < nodes.Length; i++)
            {
                if (i > 0) nd.Append(',');
                nd.Append('[').Append(nodes[i][0].ToString(CI)).Append(',').Append(nodes[i][1].ToString(CI)).Append(']');
            }
            nd.Append(']');
            var tr = new StringBuilder("[");
            for (int i = 0; i < tris.Length; i++)
            {
                if (i > 0) tr.Append(',');
                tr.Append('[').Append(tris[i][0]).Append(',').Append(tris[i][1]).Append(',').Append(tris[i][2]).Append(']');
            }
            tr.Append(']');
            var fs = new StringBuilder("{");
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0) fs.Append(',');
                fs.Append('"').Append(names[i]).Append("\":").Append(Arr(vals[i]));
            }
            fs.Append('}');
            string data = "{nd:" + nd + ",tris:" + tr + ",fs:" + fs + "}";
            var opts = new StringBuilder();
            foreach (var n in names) opts.Append("<option value=\"").Append(n).Append("\">").Append(n).Append("</option>");
            return MeshTemplate.Replace("__ID__", id).Replace("__TITLE__", title)
                               .Replace("__OPTS__", opts.ToString()).Replace("__DATA__", data);
        }
    }
}
