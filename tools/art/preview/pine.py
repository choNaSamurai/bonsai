"""PA-102Q (TA): procedural Kuromatsu pads painted as needle fans, after A_Concept/concept_night.jpg (day light, no fireflies).

Why procedural: the night-concept cut-outs (PA-103S) were thin, had notches at the bottom where branches entered and
grainy patches where fireflies were cloned out. Here every pad is a dense shelf built from the concept's motif —
fans of tapered needles radiating upward from a base, rows overlapping back (top, lighter) to front (bottom, darker):
  * silhouette: domed top (needle tips), near-flat bottom fringed by drooping side needles -> no notches;
  * body: a dark core under every fan -> opaque inside, the dark underside is baked (PadUndersideShade = 0);
  * palette: percentiles of the PA-103S dayified pine sprites (hue ~108 deg, s <= 0.6) -> same pine green as before;
  * light from the top-left (needles pointing up-left are slightly lighter), muted, no gloss (Wabi-Sabi x Japandi).
Pure PIL, supersampled x3, deterministic (seeded).
"""
from lib import *
from PIL import ImageDraw
import colorsys

SS = 3
# day palette (PA-103S kuromatsu sprites, luminance percentiles 5/20/50/80/95/99)
DARK = (21, 41, 14)
SHADOW = (33, 56, 28)
MID = (49, 78, 48)
LIGHT = (72, 98, 58)
HIGH = (97, 128, 80)
TIP = (119, 152, 97)


def _mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def _shade(c, k):
    h, s, v = colorsys.rgb_to_hsv(*[x / 255 for x in c])
    r, g, b = colorsys.hsv_to_rgb(h, s, clamp(v * k))
    return (int(r * 255), int(g * 255), int(b * 255))


def _needle(d, x, y, ang, L, w0, cols, curve):
    """Tapered needle from (x, y) along ang (rad, screen space), three colour bands base->tip; slight bend."""
    pts_l, pts_r = [], []
    n = 6
    nx, ny = -math.sin(ang), math.cos(ang)
    for i in range(n + 1):
        t = i / n
        a = ang + curve * t
        # position along a gently curved path
        px = x + math.cos(ang) * L * t + nx * curve * L * 0.25 * t * t
        py = y + math.sin(ang) * L * t + ny * curve * L * 0.25 * t * t
        w = w0 * (1 - t) ** 0.8 + 0.35 * SS * (1 - t)
        pts_l.append((px + nx * w * 0.5, py + ny * w * 0.5)); pts_r.append((px - nx * w * 0.5, py - ny * w * 0.5))
    # three bands
    bands = ((0, 2, cols[0]), (2, 4, cols[1]), (4, 6, cols[2]))
    for i0, i1, c in bands:
        poly = pts_l[i0:i1 + 1] + pts_r[i0:i1 + 1][::-1]
        d.polygon(poly, fill=c + (255,))


def _core(d, x, y, R, tone):
    """Dark core under a fan (drawn for all fans before any needle -> opaque body, never visible as a disc)."""
    cr = R * 0.34
    y = y + R * 0.10
    d.ellipse((x - cr * 1.3, y - cr * 0.8, x + cr * 1.3, y + cr * 0.15), fill=_shade(DARK, tone) + (255,))


def _fan(d, rnd, x, y, R, tone, droop=0.0, spread=(-165, -15), density=1.0, light_dir=-0.6, tilt=0.0):
    """A fan of needles from (x, y): angles in `spread` (deg, -90 = up); `droop` extends both ends below horizontal."""
    a0, a1 = spread[0] - droop + tilt, spread[1] + droop + tilt
    n = max(9, int((a1 - a0) / 7.0 * density))
    order = list(range(n)); rnd.shuffle(order)
    for k in order:
        t = (k + rnd.uniform(-0.35, 0.35)) / (n - 1)
        deg = a0 + (a1 - a0) * t
        ang = math.radians(deg)
        up = -math.sin(ang)                   # 1 = straight up
        L = R * (0.62 + 0.26 * max(0.0, up) ** 0.7) * rnd.uniform(0.90, 1.02)
        lit = 0.5 + 0.5 * (-math.cos(ang) * -light_dir * 0.6 + up * 0.5)   # up-left brighter
        lit = clamp(lit + rnd.uniform(-0.12, 0.12))
        base = _shade(_mix(SHADOW, MID, 0.3 + 0.4 * lit), tone)
        mid = _shade(_mix(MID, LIGHT, 0.2 + 0.7 * lit), tone)
        tip = _shade(_mix(LIGHT, TIP if lit > 0.6 else HIGH, 0.3 + 0.7 * lit), tone)
        r0 = R * rnd.uniform(0.10, 0.22)   # needles start off the hub -> no dark converging disc
        _needle(d, x + math.cos(ang) * r0, y + math.sin(ang) * r0, ang, L * 0.92, R * 0.115 * rnd.uniform(0.85, 1.15), (base, mid, tip), (0.22 * math.cos(ang)) + rnd.uniform(-0.08, 0.08))


def pad(W, H, seed, dome=False, fan_r=None, rows_hint=None, flat=0.25):
    """Paint one pine pad, final size about W x H px (opaque bbox). Returns tight RGBA."""
    rnd = random.Random(seed)
    R = (fan_r or max(34.0, 0.125 * W)) * SS
    fans = []
    w, h = W * SS, H * SS
    m = int(R * 1.3)
    img = Image.new('RGBA', (w + 2 * m, h + 2 * m), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    cx = m + w / 2.0; bottom = m + h; top = m

    def top_y(x):     # domed top profile (y of the needle tips)
        u = clamp(abs(x - cx) / (w / 2.0))
        p = 1.25 if dome else 1.45      # mound / lens, not a box
        return top + (h * (1 - flat)) * (u ** p) + rnd.uniform(-0.02, 0.02) * h

    def bot_y(x):     # near-flat bottom, ends curl up a little
        u = clamp(abs(x - cx) / (w / 2.0))
        return bottom - h * 0.30 * u ** 2.2

    # rows: back (top) first; fan bases sit ~0.80 R below the top profile, then every ~0.42 R toward the bottom
    step = R * 0.50
    y = top + R * 0.80
    rows = []
    while y < bottom - R * 0.18:
        rows.append(y); y += step
    rows.append(bottom - R * 0.18)
    for ri, ry in enumerate(rows):
        last = ri == len(rows) - 1
        frac = ri / max(1, len(rows) - 1)
        tone = 1.10 - 0.42 * frac            # top rows lit, bottom rows in shade
        # horizontal extent at this row: where the base is under the top profile
        xs = []
        x = cx - w / 2.0 + R * 0.55 + rnd.uniform(-0.2, 0.2) * R
        while x <= cx + w / 2.0 - R * 0.55:
            if top_y(x) + R * 0.55 <= ry <= bot_y(x) - R * 0.10 or last:
                xs.append(x)
            x += R * rnd.uniform(0.58, 0.74)
        if last:   # bottom row stays inside the bottom profile
            xs = [x for x in xs if abs(x - cx) < w / 2.0 - R * 0.35]
        rnd.shuffle(xs)
        for x in xs:
            yy = ry + rnd.uniform(-0.12, 0.12) * R
            if not last:
                yy = max(yy, top_y(x) + R * 0.72)
            edge = abs(x - cx) / (w / 2.0)
            droop = 14 if last else ((22 + 14 * edge) if edge > 0.72 else 0)
            fans.append((x, yy, R * rnd.uniform(0.9, 1.1) * (1.0 if not last else 0.9), tone * rnd.uniform(0.94, 1.05), droop, (-155, -25), 1.0,
                         48 * ((x - cx) / (w / 2.0)) + rnd.uniform(-8, 8)))
    # side fans at both ends: small, mid-height of the lens, needles fanning outwards (rounded shelf ends, no "ears")
    for sgn in (-1, 1):
        x = cx + sgn * (w / 2.0 - R * 0.75)
        yy = 0.5 * (top_y(x) + bot_y(x)) + R * 0.15
        spread = (-120, 20) if sgn > 0 else (-200, -60)
        fans.insert(len(fans) // 2, (x, yy, R * 0.62, 0.85, 0.0, spread, 0.8, 0.0))
    # continuous dark body inside the silhouette (no see-through holes between fans)
    body = []
    for i in range(41):
        x = cx - w / 2.0 + R * 0.6 + (w - R * 1.2) * i / 40.0
        body.append((x, top_y(x) + R * 0.75))
    for i in range(40, -1, -1):
        x = cx - w / 2.0 + R * 0.6 + (w - R * 1.2) * i / 40.0
        body.append((x, max(bot_y(x) - R * 0.40, top_y(x) + R * 0.75)))
    d.polygon(body, fill=_shade(DARK, 0.85) + (255,))
    # hanging fringe along the bottom: single needles pointing down/outward (soft continuous underside, no zigzag)
    x = cx - w / 2.0 + R * 0.45
    while x < cx + w / 2.0 - R * 0.45:
        side = (x - cx) / (w / 2.0)
        ang = math.radians(90 - 62 * side + rnd.uniform(-22, 22))
        y0 = bot_y(x) - R * rnd.uniform(0.30, 0.50)
        L = R * rnd.uniform(0.28, 0.44)
        c0 = _shade(DARK, 0.9); c1 = _shade(SHADOW, rnd.uniform(0.8, 1.0)); c2 = _shade(MID, rnd.uniform(0.7, 0.9))
        _needle(d, x, y0, ang, L, R * 0.08, (c0, c1, c2), rnd.uniform(-0.1, 0.1))
        x += R * rnd.uniform(0.05, 0.10)
    for (x, yy, r, tone, droop, spread, dens, tilt) in fans:
        if top_y(x) + R * 1.05 < yy < bottom - R * 0.4:
            _core(d, x, yy, r, tone)
    for (x, yy, r, tone, droop, spread, dens, tilt) in fans:
        _fan(d, rnd, x, yy, r, tone, droop=droop, spread=spread, density=dens, tilt=tilt)
    out = img.resize((img.size[0] // SS, img.size[1] // SS), Image.LANCZOS)
    out = Image.merge('RGBA', [c.filter(ImageFilter.GaussianBlur(0.7)) for c in out.convert('RGB').split()] + [out.getchannel('A')])
    a = out.getchannel('A').point(lambda v: int(clamp((v - 10) / 235.0) * 255)); out.putalpha(a)
    bb = a.getbbox()
    return out.crop((bb[0] - 2, bb[1] - 2, bb[2] + 2, bb[3] + 2))


# Atlas plan (names keep the PA-103S contract: k0 = dome (L), L side, M, S tips):
#   k0 dome ~720 px | k1, k2 L ~610 | k3..k6 M ~480..510 (< 540) | k7..k9 S ~320..350 (< 440)
PLAN = [   # (kind, painted width W, opaque h/w of the painted core, seed, domed top); needles add ~+50..70 px of width
    ('dome', 660, 0.40, 101, True),
    ('L', 565, 0.355, 102, False), ('L', 545, 0.355, 103, False),
    ('M', 460, 0.355, 104, False), ('M', 450, 0.355, 105, False), ('M', 440, 0.355, 106, False), ('M', 430, 0.355, 107, False),
    ('S', 300, 0.44, 108, True), ('S', 285, 0.44, 109, True), ('S', 270, 0.46, 110, True),
]


# Target opaque height / atlas-rect width (rect = bbox + ~30 px margin): at TexturedStyleConfig defaults a side pad of
# puff-union U is one sprite 1.16*U wide, so U 42..46 -> visible 46..50 x 19..21 units needs 0.395 (BALANCE 1.4.5).
TARGET = {'dome': 0.44, 'L': 0.395, 'M': 0.40, 'S': 0.50}


def _ratio(sp):
    bb = sp.getchannel('A').point(lambda v: 255 if v >= 128 else 0).getbbox()
    return (bb[3] - bb[1]) / (bb[2] - bb[0] + 30.0)   # rect = bbox + ~2*(5 trim + 10 padPx)


def _cls(sp):
    w = sp.size[0] + 6       # innerW ~ trimmed width + 2*3 (build() keeps the PA-103S rect; size by innerW)
    return 'L' if w >= 540 else ('M' if w >= 440 else 'S')


def kuromatsu_sprites():
    """Same seed per sprite; the painted height is chosen from a grid so the opaque ratio is closest to TARGET and the
    size class (by width) stays the planned one (the painting is not monotonic in height: rows are re-laid)."""
    out = []
    for kind, W, hw, seed, dome in PLAN:
        fr = 0.17 * W if kind == 'S' else None
        want = 'L' if kind == 'dome' else kind
        best = None
        for k in range(-12, 13):
            h = hw * (1 + 0.025 * k)
            sp = pad(W, int(W * h), seed, dome=dome, fan_r=fr, flat=0.35 if kind == 'S' else 0.25)
            err = abs(_ratio(sp) - TARGET[kind]) + (1.0 if _cls(sp) != want else 0.0)
            if best is None or err < best[0]: best = (err, sp)
        out.append(best[1])
    return out
