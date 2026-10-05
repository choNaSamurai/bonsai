"""PA-102Q (TA) review collage: Nebari roots vs the SoilFront moss band, at the world scale of the Textured preview
(pot P = 92 units, 10 px/unit). Mirrors the geometry of TexturedPreviewView (pot/soil/SoilFront placement) and
BALANCE 1.4.1a/1.4.2 (gaussian flare, roots from 0.25*th0, last 25% buried) with schematic roots (not the GP mesh).
Panels: reference L2 | PA-103S band (60 px, roots behind) | PA-102Q (band 24 px, roots in front, moss_tuft on tips).
Output: $ART_SCRATCH/pa102q/nebari_moss_before_after.png
"""
import json
from lib import *
from PIL import ImageDraw, ImageEnhance

S = 10.0                       # px per world unit
X0, X1, Y0, Y1 = -52.0, 52.0, -16.0, 30.0
TH0 = 15.3                     # Momiji Styled D80 (BALANCE 1.4.1)
pot = Image.open(OUT + 'pot_tokoname.png'); pm = json.load(open(OUT + 'pot_tokoname.json'))
soil = Image.open(OUT + 'soil_moist.png'); sm = json.load(open(OUT + 'soil.json'))
bark = Image.open(OUT + 'bark_trunk.png').convert('RGB')
moss = Image.open(OUT + 'moss_tuft.png'); mm = json.load(open(OUT + 'moss_tuft.json'))
A = pot.getchannel('A').load()
cols = [x for x in range(pot.size[0]) if any(A[x, y] > 128 for y in range(0, pot.size[1], 2))]
upp = 92.0 / (cols[-1] + 1 - cols[0]); su = max(upp, 92 * float(os.environ.get("SOIL_WIDTH_RATIO", "1.02")) / sm["width"])
baseY = pm['lipY'] + 0.45 * (pm['innerTopY'] - pm['lipY'])
open_w = (pm['innerRight'] - pm['innerLeft']) + 0.45 * ((pm['innerTopRight'] - pm['innerTopLeft']) - (pm['innerRight'] - pm['innerLeft']))
W, H = int((X1 - X0) * S), int((Y1 - Y0) * S)


def P(x, y):
    return ((x - X0) * S, (Y1 - y) * S)


def place(canvas, img, x0, y0, wu, hu):
    """img covering world rect [x0, x0+wu] x [y0, y0+hu]."""
    im = img.resize((max(1, int(wu * S)), max(1, int(hu * S))), Image.LANCZOS)
    px, py = P(x0, y0 + hu); canvas.alpha_composite(im, (int(px), int(py)))


def band(depth_px):
    g = sm['groundFromTop']; bw = open_w * upp * 0.92
    span = bw / (sm['width'] * su); u0 = int((0.5 - 0.5 * span) * sm['width']); u1 = int((0.5 + 0.5 * span) * sm['width'])
    crop = soil.crop((u0, 0, u1, g + depth_px)).copy()
    # soft edges like SetBand: alpha 0 on the outer 12% columns and along the bottom edge
    a = crop.getchannel('A'); w, h = crop.size; m = Image.new('L', (w, h)); mp = m.load()
    for y in range(h):
        fy = clamp((h - 1 - y) / max(1.0, (h - g) * 0.5)) if y > g else 1.0
        for x in range(w):
            fx = clamp(min(x, w - 1 - x) / (0.12 * w))
            mp[x, y] = int(255 * fx * fy)
    crop.putalpha(ImageChops.multiply(a, m))
    top = -0.4 + g * su
    return crop, -0.5 * bw, top - (g + depth_px) * su, bw, (g + depth_px) * su


def textured_poly(canvas, pts, tone=1.0):
    mask = Image.new('L', canvas.size, 0); ImageDraw.Draw(mask).polygon([P(*p) for p in pts], fill=255)
    tex = Image.new('RGB', canvas.size)
    t = bark.resize((int(18 * S), int(18 * S)))
    for yy in range(0, canvas.size[1], t.size[1]):
        for xx in range(0, canvas.size[0], t.size[0]): tex.paste(t, (xx, yy))
    if tone != 1.0: tex = ImageEnhance.Brightness(tex).enhance(tone)
    lay = tex.convert('RGBA'); lay.putalpha(mask)
    canvas.alpha_composite(lay)
    ImageDraw.Draw(canvas).line([P(*p) for p in pts] + [P(*pts[0])], fill=(40, 28, 20, 200), width=2)


def trunk(canvas):
    left, right = [], []
    for i in range(61):
        y = Y1 * (i / 60.0) ** 2            # dense samples near the base (gaussian flare)
        w = (TH0 - (TH0 - 11.5) * min(1.0, y / 35.0)) * (1 + 0.98 * math.exp(-(y / (0.22 * TH0)) ** 2))
        left.append((-w / 2, y)); right.append((w / 2, y))
    textured_poly(canvas, left + right[::-1])


ROOTS = [(-1, 1.00, 0.40, 0.0), (1, 0.95, 0.42, 0.3), (-1, 0.70, 0.34, -1.4), (1, 0.75, 0.36, -1.6),
         (-1, 0.40, 0.30, -3.0), (1, 0.45, 0.32, -3.2), (1, 0.10, 0.30, -4.2)]   # side, length share, thickness*th0, dy of the run


def _bez(p0, p1, p2, t):
    u = 1 - t
    return (u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0], u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1])


def roots(canvas, start_ratio, end_y, tips=None, tone=0.9):
    for k, (sd, ls, th, dz) in enumerate(ROOTS):
        Lr = TH0 * (0.9 + 0.5 * ls)
        y0 = start_ratio * TH0; x0 = sd * TH0 * 0.42
        xk = x0 + sd * y0 * 1.0                       # 45 deg descent to the ground line
        xe = xk + sd * Lr * (0.45 + 0.45 * ls)
        p0, p1, p2 = (x0, y0), (xk, end_y + dz * 0.5), (xe, end_y + dz)
        w0 = TH0 * th; w1 = w0 * 0.30
        up, dn = [], []
        for i in range(25):
            t = i / 24.0
            c = _bez(p0, p1, p2, t); c2 = _bez(p0, p1, p2, min(1, t + 0.01)); c1 = _bez(p0, p1, p2, max(0, t - 0.01))
            tx, ty = c2[0] - c1[0], c2[1] - c1[1]; n = math.hypot(tx, ty) or 1
            nx, ny = -ty / n, tx / n
            w = (w0 + (w1 - w0) * t) * 0.5 * (1.0 if t < 0.9 else 1 - (t - 0.9) * 6)
            up.append((c[0] + nx * w, c[1] + ny * w)); dn.append((c[0] - nx * w, c[1] - ny * w))
        textured_poly(canvas, up + dn[::-1], tone=tone - 0.04 * k)
        if tips is not None:
            tips.append(_bez(p0, p1, p2, 0.86) + (k,))


def panel(kind):
    c = Image.new('RGBA', (W, H), (249, 244, 231, 255))
    left = -0.5 * (pm['innerLeft'] + pm['innerRight']) * upp
    ground = (pm['innerTopY'] - baseY) * upp
    place(c, soil, -0.5 * sm['width'] * su, ground + sm['groundFromTop'] * su - sm['height'] * su, sm['width'] * su, sm['height'] * su)
    place(c, pot, left, -baseY * upp, pot.size[0] * upp, pot.size[1] * upp)
    if kind == 'before':
        roots(c, 0.5, -0.4 - 0.8); trunk(c)
        b, bx, by, bw, bh = band(60); place(c, b, bx, by, bw, bh)
    else:
        trunk(c)
        b, bx, by, bw, bh = band(sm.get('frontBandBelowPx', 24)); place(c, b, bx, by, bw, bh)
        tips = []; roots(c, 0.25, 0.3, tips); trunk(c)
        for tx, ty, k in tips:
            p = mm['pads'][k % len(mm['pads'])]
            spr = moss.crop((p['x'], p['y'], p['x'] + p['w'], p['y'] + p['h']))
            if k % 2: spr = spr.transpose(Image.FLIP_LEFT_RIGHT)
            wu = p['w'] / mm['pxPerUnit']; hu = p['h'] / mm['pxPerUnit']
            place(c, spr, tx - wu / 2, ty - 1.2 - p['pivotY'] * hu, wu, hu)
    ImageDraw.Draw(c).line([P(X0, 0), P(X0 + 4, 0)], fill=(200, 60, 40, 255), width=2)
    return c


def reference():
    ref = Image.open(SRC + 'Section_0/realism_l2_painterly.jpg').convert('RGBA')
    rupp = 92.0 / (667 - 101)
    potTop = -baseY * upp + pot.size[1] * upp
    c = Image.new('RGBA', (W, H), (249, 244, 231, 255))
    place(c, ref, -0.5 * (pm['innerLeft'] + pm['innerRight']) * upp + cols[0] * upp - 101 * rupp,
          potTop + 1068 * rupp - ref.size[1] * rupp, ref.size[0] * rupp, ref.size[1] * rupp)
    return c


if __name__ == '__main__':
    from PIL import ImageEnhance
    globals()['ImageEnhance'] = ImageEnhance
    panels = [('reference L2 (realism_l2_painterly)', reference()), ('PA-103S: band 60 px (5.5 u), roots behind, end -0.8 under', panel('before')),
              ('PA-102Q: band 24 px (2.2 u) + moss crest 2.3 u, roots in front, moss_tuft on tips', panel('after'))]
    out = Image.new('RGB', (W, (H + 22) * len(panels)), (249, 244, 231)); d = ImageDraw.Draw(out)
    for i, (t, p) in enumerate(panels):
        out.paste(p.convert('RGB'), (0, i * (H + 22) + 22)); d.text((8, i * (H + 22) + 5), t + '   (10 px/unit, red tick = y 0)', fill=(60, 50, 40))
    os.makedirs(SCR + 'pa102q', exist_ok=True)
    out.save(SCR + 'pa102q/nebari_moss_before_after.png'); print(SCR + 'pa102q/nebari_moss_before_after.png', out.size)
