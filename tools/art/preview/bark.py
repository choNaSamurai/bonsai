import math, random
from lib import *
from PIL import ImageDraw, ImageStat

l2 = Image.open(SRC + 'Section_0/realism_l2_painterly.jpg').convert('RGB'); P = l2.load()

def isbark(c):
    r, g, b = c; mx = max(c); mn = min(c); ch = mx - mn
    if ch == 0: return False
    hue = (60 * (g - b) / ch) % 360 if mx == r else 90
    return hue < 45 and ch > 15 and (r + g + b) / 3 < 170

def row_span(y, xa=280, xb=480):
    runs = []; inrun = False
    for x in range(xa, xb):
        v = isbark(P[x, y])
        if v and not inrun: runs.append([x, x]); inrun = True
        elif v: runs[-1][1] = x
        else: inrun = False
    runs = [r for r in runs if r[1] - r[0] > 30]
    return max(runs, key=lambda r: r[1] - r[0]) if runs else None

def unwrap(y0, y1, N=64, inner=0.72, xa=280, xb=480):
    rows = []
    spans = [(y, row_span(y, xa, xb)) for y in range(y0, y1)]
    spans = [(y, s) for y, s in spans if s]
    sm = []
    for i, (y, s) in enumerate(spans):
        a = max(0, i - 4); b = min(len(spans), i + 5)
        sm.append((y, (sum(spans[j][1][0] for j in range(a, b)) / (b - a), sum(spans[j][1][1] for j in range(a, b)) / (b - a))))
    for y, sp in sm:
        L, R = sp; w = R - L; c = (L + R) / 2.0
        a = c - w * inner / 2; b = c + w * inner / 2
        row = []
        for i in range(N):
            # arcsine unwrap: equal arc length across visible half-cylinder
            t = -1 + 2 * (i + 0.5) / N
            xs = c + (w / 2.0) * inner * math.sin(t * math.pi / 2)
            x0 = int(xs); f = xs - x0
            c0 = P[x0, y]; c1 = P[x0 + 1, y]
            row.append(tuple(c0[k] * (1 - f) + c1[k] * f for k in range(3)))
        rows.append(row)
    im = Image.new('RGB', (N, len(rows)))
    for y, row in enumerate(rows):
        for x, c in enumerate(row):
            im.putpixel((x, y), tuple(int(v) for v in c))
    return im

def flatten(im):
    # remove cylinder shading: divide by column-mean luminance and row-mean (low freq)
    W, H = im.size; p = im.load()
    L = [[sum(p[x, y]) / 3.0 for x in range(W)] for y in range(H)]
    colm = [sum(L[y][x] for y in range(H)) / H for x in range(W)]
    rowm = [sum(L[y]) / W for y in range(H)]
    # smooth rows
    rs = []
    for y in range(H):
        a = max(0, y - 12); b = min(H, y + 13)
        rs.append(sum(rowm[a:b]) / (b - a))
    gm = sum(colm) / W
    out = Image.new('RGB', (W, H)); o = out.load()
    for y in range(H):
        for x in range(W):
            k = (gm / max(colm[x], 1)) * (gm / max(rs[y], 1))
            k = 1 + (k - 1) * 0.9
            o[x, y] = tuple(int(clamp(v * k, 0, 255)) for v in p[x, y])
    return out

def make_seamless_v(im):
    W, H = im.size
    off = ImageChops.offset(im, 0, H // 2)
    mask = Image.new('L', (W, H)); m = mask.load()
    for y in range(H):
        dy = abs(y - H / 2) / (H / 2)
        v = int(255 * smooth(0.45, 0.98, dy))
        for x in range(W):
            m[x, y] = v
    return Image.composite(off, im, mask)

def make_seamless(im):
    """Offset by half and cross-fade with the original using a centre-weighted mask."""
    W, H = im.size
    off = ImageChops.offset(im, W // 2, H // 2)
    mask = Image.new('L', (W, H)); m = mask.load()
    for y in range(H):
        for x in range(W):
            dx = abs(x - W / 2) / (W / 2); dy = abs(y - H / 2) / (H / 2)
            # 1 near edges of the original (seams of the original are at the edges) -> use offset copy there
            e = max(dx, dy)
            m[x, y] = int(255 * smooth(0.55, 0.95, e))
    # original has seams at its borders; offset copy has seams in the centre cross.
    # use original in centre, offset near borders
    return Image.composite(off, im, mask)

def streaks(W, H, seed, n=26, amp=1.0, kmin=6, kmax=48):
    rnd = random.Random(seed)
    comps = [(rnd.randint(kmin, kmax), rnd.random() * 6.283, rnd.choice([1, 2, 3]), rnd.random() * 6.283, rnd.uniform(0.3, 1.0)) for _ in range(n)]
    out = [[0.0] * W for _ in range(H)]
    for y in range(H):
        for x in range(W):
            v = 0.0
            for k, ph, m, ps, a in comps:
                v += a * math.sin(6.283 * k * x / W + ph + 0.9 * math.sin(6.283 * m * y / H + ps))
            out[y][x] = v / n * amp
    return out

def ink_lines(W, H, seed, count, color, width, alpha, length=(0.25, 0.6)):
    rnd = random.Random(seed)
    S = 2
    layer = Image.new('RGBA', (W * S, H * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for _ in range(count):
        x0 = rnd.uniform(0, W); y0 = rnd.uniform(0, H); ln = rnd.uniform(*length) * H
        amp = rnd.uniform(2, 9); per = rnd.choice([1, 2]); ph = rnd.random() * 6.283
        wdt = rnd.uniform(*width)
        pts = []
        steps = 40
        for i in range(steps + 1):
            t = i / steps
            y = y0 + t * ln
            x = x0 + amp * math.sin(6.283 * per * y / H + ph) + 0.15 * (y - y0) * rnd.uniform(-0.02, 0.02)
            pts.append((x, y, t))
        for dxw in (-W, 0, W):
            for dyw in (-H, 0, H):
                for (xa, ya, ta), (xb, yb, tb) in zip(pts, pts[1:]):
                    fade = math.sin(math.pi * ta) ** 0.6
                    a = int(alpha * fade)
                    d.line([((xa + dxw) * S, (ya + dyw) * S), ((xb + dxw) * S, (yb + dyw) * S)], fill=color + (a,), width=max(1, int(wdt * S * (0.4 + 0.6 * fade))))
    return layer.resize((W, H), Image.LANCZOS)
