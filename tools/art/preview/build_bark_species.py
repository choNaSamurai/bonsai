"""PA-102T batch 02: per-species bark -> Art/Runtime/Preview/bark_<species>_{trunk,young}.png (512x512 RGB, TD-072).

Sources: Art/Source/B_Textures/bark_<species>_{trunk,young}.png (1024^2 RGB, "seamless" generations).
Steps (at 1024, then Lanczos to 512):
 1. light flatten, multiplicative per channel (keeps ink fissures): a) column profile (vertical light bands — the
    glossy highlight of yamazakura_trunk) divided out with strength kc after a wrap blur sigma_c; b) 2-D low frequency
    (wrap blur sigma 96) divided out with strength k2.
 2. seams: the generations wrap in X but show a hard line in Y (3-4x the interior gradient). Min-error boundary cut
    (image quilting): the image is rolled by half so the seam sits mid-tile, a patch of the same tile (best matching
    vertical offset) covers the seam band and is stitched in along two min-cost horizontal paths, feather 3 px.
    Same for X when its seam error is > 1.6x the interior gradient. Interior detail is never cross-faded (no ghosting).
 2b. X: if the wrap error is > 1.6x the interior gradient (yamazakura_trunk), the same column ramp is applied
    transposed (rows made continuous across the X wrap over 96 px).
 3. tone: per-channel gain to the target mean (pulled toward L2 trunk (88,70,50) but keeping the species look and
    the T17 CIELAB bands), contrast around the mean x c.
"""
import sys, os
from lib import *
from PIL import ImageStat

S = ['kuromatsu', 'momiji', 'yamazakura']
# species_kind -> (sigma_c, kc, k2, target mean RGB, contrast)
P = {
    'kuromatsu_trunk':  (48, 0.6, 0.6, (70, 61, 52), 0.88),
    'kuromatsu_young':  (32, 0.7, 0.6, (94, 77, 63), 1.0),
    'momiji_trunk':     (32, 0.8, 0.7, (124, 113, 92), 1.3),
    'momiji_young':     (48, 0.0, 0.5, (112, 100, 64), 1.0),   # green/red grain stripes are the species look: keep columns
    'yamazakura_trunk': (20, 1.0, 0.8, (100, 58, 45), 0.9),
    'yamazakura_young': (32, 0.7, 0.6, (120, 69, 53), 1.0),
}
OUT_SIZE = 512

def wrap_blur(im, sigma):
    """Gaussian blur with wrap-around (3x3 tiling, low-res for big sigmas)."""
    W, H = im.size
    q = max(1, int(sigma // 8))
    sm = im.resize((W // q, H // q), Image.BOX); w, h = sm.size
    t = Image.new(im.mode, (3 * w, 3 * h))
    for i in range(3):
        for j in range(3): t.paste(sm, (i * w, j * h))
    t = t.filter(ImageFilter.GaussianBlur(sigma / q)).crop((w, h, 2 * w, 2 * h))
    return t.resize((W, H), Image.BICUBIC) if q > 1 else t

def divide_out(im, low, k, mean):
    """im * (mean / low)^k per channel."""
    p = im.load(); lp = low.load(); W, H = im.size
    out = im.copy(); op = out.load()
    for y in range(H):
        for x in range(W):
            c = p[x, y]; l = lp[x, y]
            op[x, y] = tuple(int(clamp(c[i] * (mean[i] / max(8.0, l[i])) ** k, 0, 255)) for i in range(3))
    return out

def column_profile(im, sigma):
    W, H = im.size
    col = im.resize((W, 1), Image.BOX)
    col = wrap_blur(col.resize((W, 8), Image.NEAREST), sigma) if sigma else col
    return col.resize((W, 1), Image.BOX).resize((W, H), Image.NEAREST)

def seam_error(im, axis):
    g = im.convert('L'); p = g.load(); W, H = g.size
    if axis == 'y':
        e = sum(abs(p[x, H - 1] - p[x, 0]) for x in range(W)) / W
        i = sum(abs(p[x, y] - p[x, y + 1]) for x in range(W) for y in range(100, 900, 100)) / (8 * W)
    else:
        e = sum(abs(p[W - 1, y] - p[0, y]) for y in range(H)) / H
        i = sum(abs(p[x, y] - p[x + 1, y]) for y in range(H) for x in range(100, 900, 100)) / (8 * H)
    return e, i

def roll(im, dx, dy):
    W, H = im.size
    return ImageChops.offset(im, dx % W, dy % H)

def min_cut_rows(A, B, y0, y1):
    """Min-cost horizontal path through rows [y0, y1) of |A-B|^2; returns path y per column (cyclic in x approx)."""
    W = A.size[0]; ap = A.load(); bp = B.load(); n = y1 - y0
    cost = [[0.0] * n for _ in range(W)]
    for x in range(W):
        for j in range(n):
            a = ap[x, y0 + j]; b = bp[x, y0 + j]
            cost[x][j] = sum((a[i] - b[i]) ** 2 for i in range(3))
    acc = [row[:] for row in cost]; back = [[0] * n for _ in range(W)]
    for x in range(1, W):
        for j in range(n):
            best = acc[x - 1][j]; bj = j
            if j > 0 and acc[x - 1][j - 1] < best: best = acc[x - 1][j - 1]; bj = j - 1
            if j < n - 1 and acc[x - 1][j + 1] < best: best = acc[x - 1][j + 1]; bj = j + 1
            acc[x][j] += best; back[x][j] = bj
    # prefer paths that end where they start (cyclic X): pick end minimising acc + |end - start|*penalty via retrace
    j = min(range(n), key=lambda k: acc[W - 1][k]); path = [0] * W
    for x in range(W - 1, -1, -1):
        path[x] = y0 + j; j = back[x][j]
    return path

def quilt_y(im, band=96, ov=40):
    """Hide the Y wrap seam: roll by H/2, cover rows mid-band..mid+band with a patch from the same tile (best dy),
    stitched along two min-cut paths inside the `ov`-row overlaps, feathered 3 px."""
    W, H = im.size; mid = H // 2
    R = roll(im, 0, mid)
    best = None
    for dy in range(-H // 2 + band + ov, H // 2 - band - ov, 16):   # patch = original rows around (mid+dy)
        P = roll(im, 0, -dy)       # P[y] = im[y + dy]
        if abs(dy) < band + ov + 8: continue
        g1 = ImageChops.difference(R.crop((0, mid - band - ov, W, mid - band)), P.crop((0, mid - band - ov, W, mid - band)))
        g2 = ImageChops.difference(R.crop((0, mid + band, W, mid + band + ov)), P.crop((0, mid + band, W, mid + band + ov)))
        e = sum(ImageStat.Stat(g1).mean) + sum(ImageStat.Stat(g2).mean)
        if best is None or e < best[0]: best = (e, dy, P)
    _, dy, P = best
    top = min_cut_rows(R, P, mid - band - ov, mid - band)
    bot = min_cut_rows(R, P, mid + band, mid + band + ov)
    out = R.copy(); op = out.load(); rp = R.load(); pp = P.load()
    for x in range(W):
        for y in range(mid - band - ov - 4, mid + band + ov + 4):
            t = smooth(-3, 3, y - top[x]) * (1 - smooth(-3, 3, y - bot[x]))
            if t <= 0: continue
            a = rp[x, y]; b = pp[x, y]
            op[x, y] = tuple(int(a[i] * (1 - t) + b[i] * t + 0.5) for i in range(3))
    return roll(out, 0, -mid), dy

def seam_offset_y(im, band=160, sigma=1.5):
    """Vertical-grain textures: make every column continuous across the Y wrap. d(x) = top - bottom row (3-row means,
    lightly blurred in x); half of it is faded in over `band` rows on each side (smoothstep), so streaks run through
    the seam instead of ending on a line. No content is cross-faded -> no ghosting of lenticels."""
    W, H = im.size; p = im.load()
    top = im.crop((0, 0, W, 3)).resize((W, 1), Image.BOX); bot = im.crop((0, H - 3, W, H)).resize((W, 1), Image.BOX)
    if sigma:
        top = wrap_blur(top.resize((W, 8), Image.NEAREST), sigma).resize((W, 1), Image.BOX)
        bot = wrap_blur(bot.resize((W, 8), Image.NEAREST), sigma).resize((W, 1), Image.BOX)
    tp = top.load(); bp = bot.load()
    out = im.copy(); op = out.load()
    for x in range(W):
        d = [tp[x, 0][i] - bp[x, 0][i] for i in range(3)]
        for y in range(band):
            t = 0.5 * (1 - smooth(0, band, y))
            c = op[x, y]; op[x, y] = tuple(int(clamp(c[i] - d[i] * t, 0, 255)) for i in range(3))
            yy = H - 1 - y; c = op[x, yy]; op[x, yy] = tuple(int(clamp(c[i] + d[i] * t, 0, 255)) for i in range(3))
    return out

def tone(im, target, contrast):
    st = ImageStat.Stat(im); m = st.mean
    chans = []
    for i, ch in enumerate(im.split()):
        g = target[i] / max(1.0, m[i])
        chans.append(ch.point(lambda v, g=g, t=target[i]: int(clamp(t + (v * g - t) * contrast, 0, 255))))
    return Image.merge('RGB', chans)

def srgb_to_lab(c):
    def lin(v):
        v /= 255.0; return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = [lin(v) for v in c]
    X = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047; Y = 0.2126 * r + 0.7152 * g + 0.0722 * b; Z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883
    f = lambda t: t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    return (116 * f(Y) - 16, 500 * (f(X) - f(Y)), 200 * (f(Y) - f(Z)))

def mean_lab(im):
    """Mean CIELAB over pixels (sub-sampled), like the T17 measurement (mean of colours, then Lab of the mean)."""
    return srgb_to_lab(ImageStat.Stat(im).mean)

def run(sp, kind):
    key = sp + '_' + kind
    sc, kc, k2, target, contrast = P[key]
    im = Image.open(SRC + 'B_Textures/bark_%s.png' % key).convert('RGB')
    before = [seam_error(im, 'x'), seam_error(im, 'y')]
    mean = ImageStat.Stat(im).mean
    if kc > 0: im = divide_out(im, column_profile(im, sc), kc, mean)
    if k2 > 0: im = divide_out(im, wrap_blur(im, 96), k2, mean)
    # plates (kuromatsu) -> min-cut patch; vertical-grain barks -> per-column offset ramp (a horizontal cut is visible
    # across streaks). X: the generations already wrap; a vertical min-cut across lenticel bands was visible -> not used.
    if sp == 'kuromatsu':
        im, dy = quilt_y(im)
    else:
        im = seam_offset_y(im); dy = 'ramp'
    dx = None
    ex, ix = seam_error(im, 'x')
    if ex > 1.6 * ix:   # yamazakura_trunk: horizontal lenticels/cracks end on the X wrap -> same ramp, transposed
        im = seam_offset_y(im.transpose(Image.TRANSPOSE), band=96).transpose(Image.TRANSPOSE); dx = 'ramp'
    im = tone(im, target, contrast)
    after = [seam_error(im, 'x'), seam_error(im, 'y')]
    out = im.resize((OUT_SIZE, OUT_SIZE), Image.LANCZOS)
    out.save(OUT + 'bark_%s.png' % key, optimize=True)
    # column-profile range (light bands) on the result
    g = out.convert('L'); prof = [v for v in g.resize((OUT_SIZE, 1), Image.BOX).getdata()]
    L, a, b = mean_lab(out)
    print('bark_%s: seam x %.1f/%.1f -> %.1f/%.1f, y %.1f/%.1f -> %.1f/%.1f (patch dy %s dx %s); col-profile %d..%d; '
          'mean %s Lab %.1f %.1f %.1f; std %s; %.0f KB' % (key, before[0][0], before[0][1], after[0][0], after[0][1],
          before[1][0], before[1][1], after[1][0], after[1][1], dy, dx, min(prof), max(prof),
          [round(v) for v in ImageStat.Stat(out).mean], L, a, b, [round(v, 1) for v in ImageStat.Stat(out).stddev],
          os.path.getsize(OUT + 'bark_%s.png' % key) / 1024))

if __name__ == '__main__':
    for sp in (sys.argv[1:] or S):
        for kind in ('trunk', 'young'):
            run(sp, kind)
