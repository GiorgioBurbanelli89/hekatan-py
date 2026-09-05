# -*- coding: utf-8 -*-
import os, sys
from PIL import Image, ImageDraw
S = os.path.dirname(os.path.abspath(__file__)); suf = sys.argv[1] if len(sys.argv) > 1 else ""
def fit(im, W): return im.resize((W, int(im.size[1] * W / im.size[0])))
def montage(name, reals, emb):
    L = [fit(Image.open(r), 600) for r in reals]; R = fit(Image.open(emb), 600)
    H = max(sum(i.size[1] for i in L) + 20 * len(L), R.size[1] + 20)
    out = Image.new("RGB", (1220, H), "white"); d = ImageDraw.Draw(out); y = 20
    d.text((10, 4), "PyVista REAL (Python 3.12, off-screen)", fill="black"); d.text((630, 4), "Hekatan Python EMBEBIDO (CLI -> html -> render)", fill="black")
    for i in L: out.paste(i, (0, y)); y += i.size[1] + 20
    out.paste(R, (620, 20)); out.save(os.path.join(S, "cmp_%s%s.png" % (name, suf))); print(name, out.size)
montage("pv1", [S + "/pv_test_real_1.png"], S + "/pv_test.png")
montage("pv2", [S + "/pv_test2_real_1.png"], S + "/pv_test2.png")
montage("pv3", [S + "/pv_test3_real_1.png", S + "/pv_test3_real_2.png"], S + "/pv_test3.png")
