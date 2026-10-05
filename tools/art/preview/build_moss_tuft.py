"""PA-102Q (TA): moss_tuft.png/.json — small moss cushions that hide the buried 25% tip of a Nebari root (BALANCE 1.4.2).

Painted (pure PIL, supersampled x3, seeded): a low cushion = union of round bumps along a domed top, each bump shaded
dark at its base -> lighter toward the top-left (light from the top-left like the bark/pads), fine highlight stipple,
1 px darker rim (no light halo on washi). Palette = moss percentiles of soil_moist.png, so a tuft melts into the
SoilFront band it sits on. Bottom edge is opaque and slightly wavy: the tuft sits ON the ground line and covers the
root tip (alpha >= 0.5 over the whole lower half).

Texel density 24 px/unit (pads ~14.5, soil ~10.8) -> crisp at 768x1376 and on retina; JSON contract = pads_*.json
({"pads":[{name,x,y,w,h,pivotX,pivotY,size,padPx,innerW,innerH}]}) + per-tuft "widthUnits"/"heightUnits" (visible size
at 24 px/unit) and atlas-level "pxPerUnit". pivot = (0.5, bottom of the opaque body) -> place the pivot on the root tip
at the ground line.
"""
import json
from lib import *
from PIL import ImageDraw
import colorsys

SS = 3
PX_PER_UNIT = 24.0
PAD = 12   # transparent margin (mip safety, as pads)
DARK = (52, 58, 26); BASE = (82, 90, 38); MID = (106, 122, 46); LIGHT = (137, 155, 55); HIGH = (167, 171, 90); SPARK = (186, 210, 96)
#            name   width u  height u  bumps seed
VARIANTS = [('t0', 7.0, 2.3, 6, 11), ('t1', 5.6, 1.9, 5, 12), ('t2', 8.4, 2.6, 7, 13)]


def _mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def tuft(wu, hu, bumps, seed):
    rnd = random.Random(seed)
    W = int(wu * PX_PER_UNIT) * SS; H = int(hu * PX_PER_UNIT) * SS
    m = int(H * 0.6)
    img = Image.new('RGBA', (W + 2 * m, H + 2 * m), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    ground = m + H; cx = m + W / 2.0

    def top(x):   # low dome, slightly asymmetric
        t = clamp((x - m) / float(W))
        return ground - H * (0.14 + 0.86 * math.sin(math.pi * t) ** 0.7)

    # 1. silhouette: base slab + a chain of small lumps along the dome (lumpy, not balls)
    lumps = []
    n = bumps * 3
    for i in range(n):
        t = 0.07 + 0.86 * (i + 0.5) / n + rnd.uniform(-0.012, 0.012)
        x = m + W * t; r = H * rnd.uniform(0.16, 0.26)
        lumps.append((x, min(top(x) + r * 0.55, ground - r * 0.6), r))
    d.polygon([(m, ground)] + [(m + W * i / 40.0, top(m + W * i / 40.0) + H * 0.12) for i in range(41)] + [(m + W, ground)],
              fill=BASE + (255,))
    for x, y, r in lumps:
        d.ellipse((x - r, y - r, x + r, y + r), fill=_mix(BASE, MID, rnd.uniform(0.0, 0.5)) + (255,))
    # 2. lumps' lit caps (top-left), back to front by height
    for x, y, r in sorted(lumps, key=lambda l: l[1]):
        for c, f, off in ((MID, 0.75, 0.22), (LIGHT, 0.48, 0.38)):
            rr = r * f; ox = x - r * off * 0.5; oy = y - r * off
            d.ellipse((ox - rr, oy - rr, ox + rr, oy + rr), fill=_mix(c, BASE, 0.25 * rnd.random()) + (255,))
    # 3. stipple over the whole cushion: tiny fronds, lighter near the top-left, darker toward the ground
    a = img.getchannel('A'); ap = a.load()
    for _ in range(int(W * H / (SS * SS) * 0.45)):
        x = rnd.uniform(m, m + W); y = rnd.uniform(ground - H * 1.05, ground)
        if ap[int(x), int(y)] < 128: continue
        hgt = clamp((ground - y) / float(H))
        lit = clamp(0.25 + 0.85 * hgt - 0.25 * (x - cx) / W + rnd.uniform(-0.25, 0.25))
        c = _mix(DARK, BASE, lit * 2) if lit < 0.5 else _mix(MID, HIGH if rnd.random() < 0.8 else SPARK, (lit - 0.5) * 2)
        s = SS * rnd.uniform(0.6, 1.7)
        d.ellipse((x - s, y - s, x + s, y + s), fill=c + (255,))
    # 4. ground contact shade: bottom 30% darker (sits in the moss band)
    shade = Image.new('L', img.size, 0); sd = ImageDraw.Draw(shade)
    for i in range(int(H * 0.30)):
        sd.line((0, ground - i, img.size[0], ground - i), fill=int(120 * (1 - i / (H * 0.30))))
    dark = Image.new('RGBA', img.size, DARK + (255,))
    a = img.getchannel('A')
    img = Image.composite(dark, img, ImageChops.multiply(shade, a))
    clip = Image.new('L', img.size, 0); ImageDraw.Draw(clip).rectangle((0, 0, img.size[0], ground), fill=255)
    img.putalpha(ImageChops.multiply(a, clip))   # nothing below the ground line
    out = img.resize((img.size[0] // SS, img.size[1] // SS), Image.LANCZOS)
    # darker 1 px rim (ink), as the pads' defringe: semi-transparent edge texels take a darkened interior colour
    al = out.getchannel('A'); inner = al.filter(ImageFilter.MinFilter(3)); rgb = out.convert('RGB')
    dk = rgb.filter(ImageFilter.MinFilter(3)); rp = rgb.load(); dp = dk.load(); ap = al.load(); ip = inner.load()
    for yy in range(out.size[1]):
        for xx in range(out.size[0]):
            if ap[xx, yy] > 0 and ip[xx, yy] < 250:
                r0, g0, b0 = rp[xx, yy]; r1, g1, b1 = dp[xx, yy]
                rp[xx, yy] = (int(min(r0, r1) * 0.85), int(min(g0, g1) * 0.85), int(min(b0, b1) * 0.85))
    rgb.putalpha(al)
    bb = al.getbbox()
    rgb = rgb.crop((bb[0] - PAD, bb[1] - PAD, bb[2] + PAD, bb[3] + PAD))
    return rgb, bb


if __name__ == '__main__':
    AW, AH = 512, 256
    atlas = Image.new('RGBA', (AW, AH), (0, 0, 0, 0)); meta = {"pxPerUnit": PX_PER_UNIT, "pads": []}
    x = 0; y = 0; rowh = 0
    for name, wu, hu, bumps, seed in VARIANTS:
        sp, bb = tuft(wu, hu, bumps, seed)
        w, h = sp.size
        if x + w > AW: x = 0; y += rowh + 2; rowh = 0      # shelf packing
        assert x + w <= AW and y + h <= AH, (name, sp.size)
        atlas.paste(sp, (x, y)); rowh = max(rowh, h)
        iw, ih = bb[2] - bb[0], bb[3] - bb[1]
        meta["pads"].append({"name": name, "x": x, "y": y, "w": w, "h": h,
                             "pivotX": 0.5, "pivotY": round(PAD / float(h), 5), "size": "S", "padPx": PAD,
                             "innerW": iw, "innerH": ih,
                             "widthUnits": round(iw / PX_PER_UNIT, 2), "heightUnits": round(ih / PX_PER_UNIT, 2)})
        x += w + 2
    a = atlas.getchannel('A')
    bled = push_pull(atlas.convert('RGB'), a.point(lambda v: 255 if v > 200 else 0))
    rgb = Image.composite(atlas.convert('RGB'), bled, a.point(lambda v: 255 if v > 0 else 0)); rgb.putalpha(a)
    rgb.save(OUT + 'moss_tuft.png', optimize=True)
    with open(OUT + 'moss_tuft.json', 'w') as f:
        json.dump(meta, f, indent=2)
    print(json.dumps(meta))
