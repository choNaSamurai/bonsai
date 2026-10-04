import os
# Pure-PIL helpers (no numpy available).
from PIL import Image, ImageFilter, ImageChops, ImageEnhance
from collections import deque
import math, random

_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
SRC = os.path.join(_ROOT, 'Assets/_Project/Art/Source') + '/'
OUT = os.path.join(_ROOT, 'Assets/_Project/Art/Runtime/Preview') + '/'
SCR = os.environ.get('ART_SCRATCH', os.path.join(_ROOT, 'Temp', 'art-preview')) + '/'  # debug output, not committed
os.makedirs(SCR, exist_ok=True)

def clamp(v, a=0.0, b=1.0):
    return a if v < a else (b if v > b else v)

def smooth(e0, e1, x):
    t = clamp((x - e0) / (e1 - e0))
    return t * t * (3 - 2 * t)

def push_pull(rgb, known):
    """Fill RGB where known (L, 0..255 weight) is low, using a push-pull pyramid.
    Returns new RGB image where unknown pixels get smooth extrapolated colour."""
    W, H = rgb.size
    levels = []
    cw = rgb.convert('RGB'); kw = known.convert('L')
    # premultiply
    def premul(c, k):
        r, g, b = c.split()
        return Image.merge('RGB', [ImageChops.multiply(ch, k) for ch in (r, g, b)])
    pc = premul(cw, kw); pk = kw
    levels.append((pc, pk))
    while pc.size[0] > 2 and pc.size[1] > 2:
        nw, nh = max(1, pc.size[0] // 2), max(1, pc.size[1] // 2)
        pc = pc.resize((nw, nh), Image.BOX); pk = pk.resize((nw, nh), Image.BOX)
        levels.append((pc, pk))
    # unpremultiply each level into colour + weight lists, pull up
    def unprem(pc, pk):
        w, h = pc.size; c = pc.load(); k = pk.load()
        col = [[0, 0, 0] for _ in range(w * h)]; wt = [0.0] * (w * h)
        for y in range(h):
            for x in range(w):
                kk = k[x, y]
                i = y * w + x
                wt[i] = kk / 255.0
                if kk > 0:
                    r, g, b = c[x, y]
                    col[i] = [r * 255.0 / kk, g * 255.0 / kk, b * 255.0 / kk]
        return col, wt
    col, wt = unprem(*levels[-1])
    w, h = levels[-1][0].size
    # coarse: fill zero weight with mean
    tot = sum(wt) or 1e-6
    mean = [sum(col[i][c] * wt[i] for i in range(len(col))) / tot for c in range(3)]
    cur = Image.new('RGB', (w, h))
    cp = cur.load()
    for y in range(h):
        for x in range(w):
            i = y * w + x
            a = wt[i]
            cp[x, y] = tuple(int(col[i][c] * a + mean[c] * (1 - a)) for c in range(3))
    for li in range(len(levels) - 2, -1, -1):
        pc, pk = levels[li]
        w, h = pc.size
        up = cur.resize((w, h), Image.BILINEAR)
        col, wt = unprem(pc, pk)
        out = Image.new('RGB', (w, h)); op = out.load(); upp = up.load()
        for y in range(h):
            for x in range(w):
                i = y * w + x; a = wt[i]; u = upp[x, y]
                op[x, y] = (int(col[i][0] * a + u[0] * (1 - a)),
                            int(col[i][1] * a + u[1] * (1 - a)),
                            int(col[i][2] * a + u[2] * (1 - a)))
        cur = out
    return cur

def components(mask_img, thr=128):
    """Return list of (count, pixelset-as-bytearray index list) for binary L mask."""
    W, H = mask_img.size; m = mask_img.load()
    lab = [0] * (W * H); comps = []
    for y in range(H):
        for x in range(W):
            i = y * W + x
            if m[x, y] >= thr and not lab[i]:
                cid = len(comps) + 1; lab[i] = cid; q = deque([i]); pts = []
                while q:
                    j = q.popleft(); pts.append(j); jx = j % W; jy = j // W
                    for nx, ny in ((jx - 1, jy), (jx + 1, jy), (jx, jy - 1), (jx, jy + 1)):
                        if 0 <= nx < W and 0 <= ny < H:
                            k = ny * W + nx
                            if not lab[k] and m[nx, ny] >= thr:
                                lab[k] = cid; q.append(k)
                comps.append(pts)
    return comps, lab

def keep_largest(mask_img, thr=128):
    W, H = mask_img.size
    comps, lab = components(mask_img, thr)
    if not comps: return mask_img
    big = max(range(len(comps)), key=lambda c: len(comps[c])) + 1
    out = Image.new('L', (W, H), 0); o = out.load()
    for j in comps[big - 1]:
        o[j % W, j // W] = 255
    return out

def fill_enclosed(mask_img, thr=128):
    """Fill holes not connected to the image border."""
    W, H = mask_img.size; m = mask_img.load()
    seen = bytearray(W * H); q = deque()
    for x in range(W):
        for y in (0, H - 1):
            if m[x, y] < thr and not seen[y * W + x]: seen[y * W + x] = 1; q.append(y * W + x)
    for y in range(H):
        for x in (0, W - 1):
            if m[x, y] < thr and not seen[y * W + x]: seen[y * W + x] = 1; q.append(y * W + x)
    while q:
        j = q.popleft(); jx = j % W; jy = j // W
        for nx, ny in ((jx - 1, jy), (jx + 1, jy), (jx, jy - 1), (jx, jy + 1)):
            if 0 <= nx < W and 0 <= ny < H:
                k = ny * W + nx
                if not seen[k] and m[nx, ny] < thr:
                    seen[k] = 1; q.append(k)
    out = Image.new('L', (W, H)); o = out.load()
    for y in range(H):
        for x in range(W):
            o[x, y] = 255 if (m[x, y] >= thr or not seen[y * W + x]) else 0
    return out

def close(mask_img, k):
    return mask_img.filter(ImageFilter.MaxFilter(k)).filter(ImageFilter.MinFilter(k))

def upscale_rgba(rgba, factor=2, sharpen=True):
    W, H = rgba.size
    rgb = rgba.convert('RGB'); a = rgba.getchannel('A')
    rgb2 = rgb.resize((W * factor, H * factor), Image.LANCZOS)
    a2 = a.resize((W * factor, H * factor), Image.LANCZOS)
    if sharpen:
        rgb2 = rgb2.filter(ImageFilter.UnsharpMask(radius=2, percent=70, threshold=2))
        # tighten alpha slightly (Lanczos softens edge)
        a2 = a2.point(lambda v: int(clamp((v - 20) / 215.0) * 255))
    out = rgb2.copy(); out.putalpha(a2)
    return out

def local_std(L, r):
    W, H = L.size; lp = L.load()
    S = [[0.0] * (W + 1) for _ in range(H + 1)]; Q = [[0.0] * (W + 1) for _ in range(H + 1)]
    for y in range(H):
        rs = 0.0; rq = 0.0
        for x in range(W):
            v = lp[x, y]; rs += v; rq += v * v
            S[y + 1][x + 1] = S[y][x + 1] + rs; Q[y + 1][x + 1] = Q[y][x + 1] + rq
    out = [0.0] * (W * H)
    for y in range(H):
        ya = max(0, y - r); yb = min(H, y + r + 1)
        for x in range(W):
            xa = max(0, x - r); xb = min(W, x + r + 1)
            n = (yb - ya) * (xb - xa)
            s = S[yb][xb] - S[ya][xb] - S[yb][xa] + S[ya][xa]
            q = Q[yb][xb] - Q[ya][xb] - Q[yb][xa] + Q[ya][xa]
            out[y * W + x] = max(0.0, q / n - (s / n) ** 2) ** 0.5
    return out
