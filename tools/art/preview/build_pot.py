import json
from lib import *
from PIL import ImageDraw
l2 = Image.open(SRC + 'Section_0/realism_l2_painterly.jpg').convert('RGB')
X0, Y0, X1, Y1 = 88, 1056, 684, 1298
crop = l2.crop((X0, Y0, X1, Y1)); W, H = crop.size
BG = (250, 245, 231)

# 1) reconstruct back rim + inner back wall where trunk/roots occlude it (x 248..508, y <= 1090)
rx0, rx1 = 248 - X0, 508 - X0
ry0, ry1 = 1060 - Y0, 1086 - Y0
srcL = (190 - X0, 246 - X0)    # clean rim segment left
srcR = (510 - X0, 580 - X0)    # clean rim segment right
p = crop.load()
orig = crop.copy(); op = orig.load()
segs = [srcL, srcR]
tile = 44; fade = 10
for y in range(ry0, ry1):
    for x in range(rx0, rx1):
        acc = [0.0, 0.0, 0.0]; wt = 0.0
        # two overlapping tile phases for soft blending
        for ph in (0, tile // 2):
            k = (x - rx0 + ph) // tile
            seg = segs[k % 2]
            local = (x - rx0 + ph) % tile
            sx = seg[0] + (local + 7 * k) % (seg[1] - seg[0])
            w = min(local + 1, tile - local) / (tile / 2.0)
            c = op[sx, y]
            acc[0] += c[0] * w; acc[1] += c[1] * w; acc[2] += c[2] * w; wt += w
        p[x, y] = tuple(int(v / wt) for v in acc)
# feather the patch borders horizontally
for y in range(ry0, ry1):
    for x in list(range(rx0 - 6, rx0 + 6)) + list(range(rx1 - 6, rx1 + 6)):
        if 0 <= x < W:
            t = smooth(0, 1, (x - (rx0 - 6)) / 12.0) if x < rx0 + 6 else 1 - smooth(0, 1, (x - (rx1 - 6)) / 12.0)
            a = op[x, y]; b = p[x, y]
            p[x, y] = tuple(int(a[i] * (1 - t) + b[i] * t) for i in range(3))

# 2) alpha: pot vs cream; nothing above the rim top
alpha = Image.new('L', (W, H)); ap = alpha.load()
for y in range(H):
    for x in range(W):
        r, g, b = p[x, y]
        d = abs(r - BG[0]) + abs(g - BG[1]) + abs(b - BG[2])
        a = smooth(30, 90, d)
        if y + Y0 < 1064: a = 0.0
        ap[x, y] = int(a * 255)
alpha = keep_largest(alpha.point(lambda v: 255 if v > 127 else 0)).filter(ImageFilter.MaxFilter(3))
soft = Image.new('L', (W, H)); sp = soft.load(); am = alpha.load()
for y in range(H):
    for x in range(W):
        r, g, b = p[x, y]
        d = abs(r - BG[0]) + abs(g - BG[1]) + abs(b - BG[2])
        sp[x, y] = int(smooth(30, 90, d) * 255) if am[x, y] else 0
        if y + Y0 < 1064: sp[x, y] = 0
alpha = fill_enclosed(soft.point(lambda v: 255 if v > 127 else 0))
# keep soft edge where not enclosed-interior
ap2 = alpha.load()
for y in range(H):
    for x in range(W):
        if not ap2[x, y]: ap2[x, y] = sp[x, y] if sp[x, y] < 128 else 255

# 3) opening (hole) where soil sits: rounded quad, in source coords
quad = [(184, 1083), (598, 1081), (633, 1157), (136, 1157)]
S = 4
hm = Image.new('L', (W * S, H * S), 0); d = ImageDraw.Draw(hm)
d.polygon([((x - X0) * S, (y - Y0) * S) for x, y in quad], fill=255)
hm = hm.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.MinFilter(5))  # round corners
hm = hm.resize((W, H), Image.LANCZOS)
hq = hm.load()
for y in range(H):
    for x in range(W):
        ap2[x, y] = int(ap2[x, y] * (1 - hq[x, y] / 255.0))

# 4) colour decontamination at outer edge: extrapolate colour from solid interior
known = alpha.point(lambda v: 255 if v > 240 else 0).filter(ImageFilter.MinFilter(3))
fill = push_pull(crop, known); fp = fill.load()
out = Image.new('RGB', (W, H)); oq = out.load(); kq = known.load()
for y in range(H):
    for x in range(W):
        oq[x, y] = p[x, y] if kq[x, y] else fp[x, y]
# ink edge ring (like the pads)
dark = out.filter(ImageFilter.MinFilter(3)); dq = dark.load()
amin = alpha.filter(ImageFilter.MinFilter(3)); mq = amin.load()
for y in range(H):
    for x in range(W):
        if ap2[x, y] > 0 and mq[x, y] < 250 and hq[x, y] < 10:
            oq[x, y] = dq[x, y]
            ap2[x, y] = (ap2[x, y] + mq[x, y]) // 2
out.putalpha(alpha)
bb = alpha.getbbox()
out = out.crop((bb[0] - 2, bb[1] - 2, bb[2] + 2, bb[3] + 2))
ox, oy = bb[0] - 2, bb[1] - 2
big = upscale_rgba(out, 2)
big.save(OUT + 'pot_tokoname.png', optimize=True)
Hh = big.size[1]
def ypx(ysrc):  # source y -> pixels from bottom of output
    return int(round(Hh - ((ysrc - Y0) - oy) * 2))
def xpx(xsrc):
    return int(round(((xsrc - X0) - ox) * 2))
meta = {
    "lipY": ypx(1157),
    "innerLeft": xpx(136), "innerRight": xpx(633),
    "innerTopY": ypx(1082), "innerTopLeft": xpx(184), "innerTopRight": xpx(598),
    "rimBackTopY": ypx(1067),
    "width": big.size[0], "height": Hh,
    "note": "y values are pixels from the bottom edge; x from the left edge. Opening (alpha=0 hole) is the quad innerLeft/innerRight at lipY .. innerTopLeft/innerTopRight at innerTopY; soil sprite goes BEHIND the pot."
}
json.dump(meta, open(OUT + 'pot_tokoname.json', 'w'), indent=2)
print(big.size, meta)
