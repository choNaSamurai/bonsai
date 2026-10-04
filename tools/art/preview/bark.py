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


# ---- PA-102P: twisted ridge/furrow detail (all terms periodic in W and H -> seamless by construction) ----
TWIST_K = 6   # ridges per tile across; one full lateral shift per tile height -> fibre slope W/(K*H) (~9.5 deg at 512²)

def twisted_field(W, H, seed, k=TWIST_K, warp_amp=1.1):
    """Return (ridge, furrow, mod) float maps 0..1: lit ridges, narrow dark furrows, slow amplitude modulation.
    Phases use integer frequencies only, so every map tiles in x and y."""
    rnd = random.Random(seed)
    ph = [rnd.random() * 6.283 for _ in range(8)]
    tau = 6.283185307
    ridge = [[0.0] * W for _ in range(H)]; furrow = [[0.0] * W for _ in range(H)]; mod = [[0.0] * W for _ in range(H)]
    for y in range(H):
        v = y / H
        for x in range(W):
            u = x / W
            warp = warp_amp * (0.55 * math.sin(tau * (2 * u + v) + ph[0]) + 0.3 * math.sin(tau * (3 * v - u) + ph[1])
                               + 0.15 * math.sin(tau * (5 * u + 2 * v) + ph[2]))
            t1 = tau * (k * u - v) + warp  # dx/dy = +1/k, same sense as twisted_strokes
            t2 = tau * ((2 * k + 1) * u - 2 * v) + 0.7 * warp + ph[3]
            s1 = math.sin(t1); s2 = math.sin(t2)
            # lit side sits slightly left of the furrow (light from the upper left)
            hl = 0.8 * smooth(0.45, 0.97, math.sin(t1 + 0.7)) + 0.2 * smooth(0.6, 0.98, math.sin(t2 + 0.7))
            fu = 0.8 * smooth(0.86, 0.995, -s1) + 0.2 * smooth(0.92, 0.998, -s2)
            m = 0.5 + 0.5 * math.sin(tau * (v + 2 * u) + 0.5 * warp + ph[4])  # long, calm modulation along the axis
            m = 0.3 + 0.7 * smooth(0.1, 0.9, m)
            ridge[y][x] = hl; furrow[y][x] = fu; mod[y][x] = m
    return ridge, furrow, mod

def twisted_strokes(W, H, seed, count, color, width, alpha, length=(0.35, 0.9), slope=None, wobble=(1.5, 5.0), blur=0.0):
    """Long thin strokes following the twist direction (dx/dy = slope), drawn with 3×3 wrap copies -> tileable."""
    rnd = random.Random(seed)
    slope = (W / (TWIST_K * H)) if slope is None else slope
    S = 2
    layer = Image.new('RGBA', (W * S, H * S), (0, 0, 0, 0)); d = ImageDraw.Draw(layer)
    for _ in range(count):
        x0 = rnd.uniform(0, W); y0 = rnd.uniform(0, H); ln = rnd.uniform(*length) * H
        sl = slope * rnd.uniform(0.9, 1.1); amp = rnd.uniform(*wobble); per = rnd.choice([1, 2]); ph = rnd.random() * 6.283
        wdt = rnd.uniform(*width); steps = 60
        pts = []
        for i in range(steps + 1):
            t = i / steps; y = y0 + t * ln
            x = x0 + sl * (y - y0) + amp * math.sin(6.283 * per * t * ln / H + ph)
            pts.append((x, y, t))
        for dxw in (-W, 0, W):
            for dyw in (-H, 0, H):
                for (xa, ya, ta), (xb, yb, tb) in zip(pts, pts[1:]):
                    fade = math.sin(math.pi * ta) ** 0.5
                    d.line([((xa + dxw) * S, (ya + dyw) * S), ((xb + dxw) * S, (yb + dyw) * S)],
                           fill=color + (int(alpha * fade),), width=max(1, int(wdt * S * (0.35 + 0.65 * fade))))
    layer = layer.resize((W, H), Image.LANCZOS)
    if blur:
        # wrap-safe blur: blur a 3×3 tiled copy and take the centre
        big = Image.new('RGBA', (W * 3, H * 3))
        for i in range(3):
            for j in range(3): big.paste(layer, (i * W, j * H))
        layer = big.filter(ImageFilter.GaussianBlur(blur)).crop((W, H, 2 * W, 2 * H))
    return layer

def wrap_blur(im, r):
    W, H = im.size
    big = Image.new(im.mode, (W * 3, H * 3))
    for i in range(3):
        for j in range(3): big.paste(im, (i * W, j * H))
    return big.filter(ImageFilter.GaussianBlur(r)).crop((W, H, 2 * W, 2 * H))

def apply_field(base, ridge, furrow, mod, light, dark, hi_col):
    W, H = base.size; out = base.copy(); o = out.load()
    for y in range(H):
        for x in range(W):
            r, g, b = o[x, y]; m = mod[y][x]
            h = ridge[y][x] * light * m; f = furrow[y][x] * dark * (0.5 + 0.5 * m)
            r = r + (hi_col[0] - r) * h; g = g + (hi_col[1] - g) * h; b = b + (hi_col[2] - b) * h
            k = 1 - f
            o[x, y] = (int(clamp(r * k, 0, 255)), int(clamp(g * k, 0, 255)), int(clamp(b * k, 0, 255)))
    return out
