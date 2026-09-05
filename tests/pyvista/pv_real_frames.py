# Corre un guion PyVista con el PyVista REAL fuera de pantalla y guarda un PNG por cada show()/plot().
import sys, os, re, pyvista as pv
src_path, out_prefix = sys.argv[1], sys.argv[2]
pv.OFF_SCREEN = True
n = [0]
_orig_show = pv.Plotter.show
def _show(self, *a, **k):
    n[0] += 1; fn = "%s_%d.png" % (out_prefix, n[0])
    self.window_size = (700, 520)
    try: self.screenshot(fn)
    except Exception as e: print("screenshot fallo:", e)
    print("PNG real:", fn)
    try: self.close()
    except Exception: pass
pv.Plotter.show = _show
src = open(src_path, encoding="utf-8").read()
src = re.sub(r"(?m)^\s*#.*$", "", src)     # comentarios #' #" fuera
exec(compile(src, src_path, "exec"), {"__name__": "__main__"})
