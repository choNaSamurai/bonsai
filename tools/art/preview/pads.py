import os, sys, json, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lib import *

CLONE_OFFS = [(0, -34), (21, -27), (-21, -27), (0, -55), (34, -40), (-34, -40), (0, -80), (40, 0), (-40, 0), (55, 10), (-55, 10), (0, 40)]

def extract(src, box, mode, cut=None, seed=1, close_k=7, vgap=0):
    """box=(x0,y0,x1,y1) in src px. cut(x,y)->0..1 extra mask in absolute src coords."""
    rnd = random.Random(seed)
    m = max(10, close_k + 6)
    x0, y0, x1, y1 = box[0] - m, box[1] - m, box[2] + m, box[3] + m
    crop = src.crop((x0, y0, x1, y1)); W, H = crop.size; p = crop.load()
    keyimg = crop.filter(ImageFilter.GaussianBlur(2.2)) if mode != 'day' else crop
    if mode != 'day':
        Lc = crop.convert('L'); Lb = Lc.filter(ImageFilter.GaussianBlur(2.5)); lcp = Lc.load(); lbp = Lb.load()
    kq = keyimg.load()
    # background plate
    if mode == 'day':
        bgp = Image.new('RGB', (W, H), (250, 245, 231))
    else:
        rough = Image.new('L', (W, H)); rq = rough.load()
        for y in range(H):
            for x in range(W):
                r, g, b = p[x, y]
                rq[x, y] = 255 if (g - max(r, b) > 5 or (r + g + b) / 3 > 90) else 0
        zone = close(rough, 5).filter(ImageFilter.MaxFilter(9))
        bgk = ImageChops.invert(zone)
        bgp = push_pull(crop, bgk).filter(ImageFilter.GaussianBlur(4))
    bq = bgp.load()
    if mode != 'day':
        stdmap = local_std(crop.convert('L'), 3)
    soft = Image.new('L', (W, H)); sp = soft.load()
    hard = Image.new('L', (W, H)); hp = hard.load()
    hole = Image.new('L', (W, H)); hl = hole.load()   # pixels to inpaint inside pad
    cgap = Image.new('L', (W, H)); cgl = cgap.load()  # background seen through foliage
    for y in range(H):
        for x in range(W):
            r, g, b = p[x, y]
            br, bg_, bb = bq[x, y]
            c = cut(x + x0, y + y0) if cut else 1.0
            d = abs(r - br) + abs(g - bg_) + abs(b - bb)
            if mode == 'day':
                a = smooth(45, 115, d) * smooth(58, 88, bb - b)
                mx = max(r, g, b); mn = min(r, g, b); ch = mx - mn
                hue = (60 * (g - b) / ch) % 360 if (ch and mx == r) else 90
                branch = (hue < 42) and ch > 12 and d > 60
                fire = False
                if bb - b < 70 and (r + g + b) / 3 > 185:
                    cgl[x, y] = 255; a = 0.0
            else:
                lum = (r + g + b) / 3
                kr, kg, kb = kq[x, y]
                mx = max(kr, kg, kb); mn = min(kr, kg, kb); ch = mx - mn
                r0, g0, b0 = r, g, b; r, g, b = kr, kg, kb
                if ch == 0: hue = 0
                elif mx == r: hue = (60 * (g - b) / ch) % 360
                elif mx == g: hue = 60 * (b - r) / ch + 120
                else: hue = 60 * (r - g) / ch + 240
                a = smooth(38, 58, hue) * (1 - smooth(118, 148, hue)) * smooth(5, 13, ch)
                a *= smooth(1.5, 5.0, stdmap[y * W + x])
                hp_ = lcp[x, y] - lbp[x, y]
                a = clamp(a * 1.7 - 0.35 + hp_ * 0.05)
                branch = False
                fire = (r0 > g0 + 4 and r0 > 120) or (r0 + g0 + b0) / 3 > 160
                r, g, b = r0, g0, b0
            if branch or fire:
                hl[x, y] = 255; a = 0.0 if branch else a
            a *= c
            sp[x, y] = int(a * 255)
            hp[x, y] = 255 if (a > 0.5 or (fire and c > 0.5)) else 0
    if mode != 'day':
        hole = hole.filter(ImageFilter.GaussianBlur(7)).point(lambda v: 255 if v > 2 else 0)
        soft = soft.filter(ImageFilter.MedianFilter(3)); sp = soft.load()
        hard = soft.point(lambda v: 255 if v > 127 else 0); hp = hard.load()
    base = keep_largest(close(hard, 3))
    encl = fill_enclosed(base)
    clo = fill_enclosed(close(base, close_k))
    vfill = Image.new('L', (W, H)); vf = vfill.load()
    if vgap:
        cq = clo.load()
        for x in range(W):
            runs = []; inside = False
            for y in range(H):
                v = cq[x, y] >= 128
                if v and not inside: runs.append([y, y]); inside = True
                elif v: runs[-1][1] = y
                else: inside = False
            for (a0, a1), (b0, b1) in zip(runs, runs[1:]):
                if b0 - a1 <= vgap:
                    for y in range(a1, b0): vf[x, y] = 255
    bq_ = base.load(); eq = encl.load(); cq = clo.load(); hl = hole.load(); hp = hard.load(); cgp = cgap.load()
    forced = Image.new('L', (W, H)); fq = forced.load()
    deep = clo.filter(ImageFilter.MinFilter(9)); dq = deep.load()
    for y in range(H):
        for x in range(W):
            if (eq[x, y] and not bq_[x, y]) or (cq[x, y] and hl[x, y]) or vf[x, y] or dq[x, y]:
                fq[x, y] = 255
    alpha = Image.new('L', (W, H)); ap = alpha.load()
    known = Image.new('L', (W, H)); kp = known.load()
    ip = fq; sol = bq_
    region = base.filter(ImageFilter.MaxFilter(5)); rp = region.load()
    for y in range(H):
        for x in range(W):
            s = sp[x, y]
            if fq[x, y]:
                ap[x, y] = 255
            elif not rp[x, y] or hl[x, y] or cgp[x, y]:
                ap[x, y] = 0
            elif bq_[x, y] and hp[x, y]:
                ap[x, y] = max(s, 200)
            else:
                ap[x, y] = int(255 * (s / 255.0) ** 1.6)
            if s > 235 and bq_[x, y] and not hl[x, y] and not cgp[x, y]:
                kp[x, y] = 255
    fill = push_pull(crop, known)
    rgb = Image.new('RGB', (W, H)); op = rgb.load(); fp = fill.load()
    for y in range(H):
        for x in range(W):
            if kp[x, y]:
                op[x, y] = p[x, y]; continue
            fr, fg, fb = fp[x, y]
            a = sp[x, y] / 255.0
            if mode != 'day' and ap[x, y] > 0 and not hl[x, y]:
                op[x, y] = p[x, y]
            elif ap[x, y] > 0 and not hl[x, y] and a > 0.2 and not ip[x, y]:
                # un-blend from background plate, mix with extrapolated colour
                r, g, b = p[x, y]; br, bg_, bb = bq[x, y]
                ur = clamp((r - (1 - a) * br) / a, 0, 255)
                ug = clamp((g - (1 - a) * bg_) / a, 0, 255)
                ub = clamp((b - (1 - a) * bb) / a, 0, 255)
                k = 0.5 if sol[x, y] else 0.9
                op[x, y] = (int(k * ur + (1 - k) * fr), int(k * ug + (1 - k) * fg), int(k * ub + (1 - k) * fb))
            elif ap[x, y] > 0 and ip[x, y]:
                src_c = None
                for dx, dy in CLONE_OFFS:
                    sx, sy = x + dx, y + dy
                    if 0 <= sx < W and 0 <= sy < H and kp[sx, sy]:
                        src_c = p[sx, sy]; break
                if src_c is None:
                    src_c = (fr, fg, fb)
                n = 0.78 if mode == "day" else 0.92
                op[x, y] = (int(src_c[0] * n), int(src_c[1] * n), int(src_c[2] * n))
            else:
                op[x, y] = (fr, fg, fb)
    # edge ring: darken toward ink, erode half a pixel (kills cream/grey halo)
    amin = alpha.filter(ImageFilter.MinFilter(3)); mp = amin.load()
    dark = rgb.filter(ImageFilter.MinFilter(3)); dp = dark.load()
    for y in range(H):
        for x in range(W):
            if ap[x, y] > 0 and mp[x, y] < 250:
                op[x, y] = dp[x, y]
                ap[x, y] = (ap[x, y] + mp[x, y]) // 2
    rgb.putalpha(alpha)
    bb = alpha.getbbox()
    rgba = rgb.crop((bb[0] - 2, bb[1] - 2, bb[2] + 2, bb[3] + 2))
    return rgba

def soft_cut_y_below(limit, amp=5, period=23, feather=8):
    # keep pixels above an irregular line y < limit + wave
    def f(x, y):
        yy = limit + amp * math.sin(x / period * 6.283) + 0.5 * amp * math.sin(x / (period * 0.37) * 6.283 + 1.3)
        return 1.0 - smooth(yy - feather, yy, y)
    return f

def cut_box(x0, y0, x1=10**9, y1=10**9):
    def f(x, y):
        return 1.0 if (x0 <= x <= x1 and y0 <= y <= y1) else 0.0
    return f

def scallop(x, period=15.0, amp=5.0):
    return amp * abs(math.sin(math.pi * x / period))

def cut_below(base, feather=2.5, rise_from=10**9, rise=0.0):
    # keep pixels above a scalloped bottom line (leaf clumps hanging down)
    def f(x, y):
        ph = x / 15.0 + 0.35 * math.sin(x / 23.0) + 0.2 * math.sin(x / 9.0)
        yy = base + 6 * abs(math.sin(math.pi * ph)) ** 0.5 + 3 * math.sin(x / 37.0) - max(0, x - rise_from) * rise
        return 1.0 - smooth(yy - feather, yy + feather, y)
    return f

def cut_dome(x0, x1, ytop, ybase, feather=2.5):
    # keep pixels below a scalloped dome between x0..x1
    def f(x, y):
        t = clamp((x - x0) / float(x1 - x0))
        yy = ybase - (ybase - ytop) * math.sin(math.pi * t) ** 0.7 - scallop(x, 14, 4)
        return smooth(yy - feather, yy + feather, y) * (1.0 if x0 <= x <= x1 else 0.0)
    return f

def cut_left(x0, feather=3.0):
    def f(x, y):
        xx = x0 + 5 * abs(math.sin(math.pi * y / 11.0)) ** 0.5
        return smooth(xx - feather, xx + feather, x)
    return f
