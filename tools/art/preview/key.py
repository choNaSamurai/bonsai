"""PA-102T batch 02: chroma keying of RGB generations on a magenta (#FF00FF-ish) background (pure PIL, no numpy).

Alpha is solved per pixel from "magenta-ness" m = min(R, B) - G: background ≈ 247, foliage/pots ≤ ~40 (pink
blossoms included). The foreground value of m is not constant (pink petals vs green leaves), so it is estimated
locally by push-pull from confidently-opaque pixels; alpha = (m_bg - m) / (m_bg - m_fg). Edge colour is then
replaced by the nearest interior colour (despill + defringe), so no pink rim survives on RGB sources.
"""
from lib import *

def magentaness(rgb):
    """L image: (min(R,B) - G + 256) / 2 — linear in the mix, magenta bg ≈ 252, green/orange paint ≈ 80…95, pink petals ≈ 135…150."""
    W, H = rgb.size; p = rgb.load()
    out = Image.new('L', (W, H)); o = out.load()
    for y in range(H):
        for x in range(W):
            r, g, b = p[x, y]
            v = (min(r, b) - g + 256) >> 1
            o[x, y] = 0 if v < 0 else (255 if v > 255 else v)
    return out

def border_colour(rgb, n=6):
    W, H = rgb.size; p = rgb.load(); acc = [0, 0, 0]; k = 0
    for x in range(0, W, 7):
        for y in list(range(n)) + list(range(H - n, H)):
            c = p[x, y]; acc[0] += c[0]; acc[1] += c[1]; acc[2] += c[2]; k += 1
    return tuple(v / k for v in acc)

def key_magenta(rgb, fg_thr=165, bg_m=None, bg_field=None, min_span=40):
    """rgb: RGB on magenta. Returns RGBA (straight alpha), edge colours taken from the interior.
    fg_thr is on the magenta-ness scale of `magentaness` (bg ≈ 252; paint ≤ 150); bg_m defaults to the border median.
    bg_field: optional L image with the local background magenta-ness (for non-uniform backgrounds)."""
    W, H = rgb.size
    m = magentaness(rgb); mp = m.load()
    # confident foreground: low magenta-ness, eroded 2 px (no edge pixels in the estimate)
    fg = m.point(lambda v: 255 if v < fg_thr else 0).filter(ImageFilter.MinFilter(5))
    # local foreground magenta-ness (push-pull at 1/4 res is plenty: it varies with the paint, not per pixel)
    q = 4
    small_m = Image.merge('RGB', [m, m, m]).resize((W // q, H // q), Image.BOX)
    small_k = fg.resize((W // q, H // q), Image.BOX)
    mf = push_pull(small_m, small_k).getchannel('R').resize((W, H), Image.BILINEAR); mfp = mf.load()
    bgp = bg_field.load() if bg_field is not None else None
    if bg_m is None:
        bc = border_colour(rgb); bg_m = (min(bc[0], bc[2]) - bc[1] + 256) / 2.0
    mb_const = bg_m
    alpha = Image.new('L', (W, H)); ap = alpha.load()
    for y in range(H):
        for x in range(W):
            v = mp[x, y]
            mb = bgp[x, y] if bgp else mb_const
            mfv = min(mfp[x, y], mb - min_span)
            a = (mb - v) / float(mb - mfv)
            a = (a - 0.08) / 0.84          # mild edge tightening (generator edges are ~4 px soft)
            a = 0.0 if a < 0.03 else (1.0 if a > 0.97 else a)
            ap[x, y] = int(a * 255 + 0.5)
    # interior colour field for the edge (despill): only pixels with alpha 255, eroded 1 px
    known = alpha.point(lambda v: 255 if v >= 255 else 0).filter(ImageFilter.MinFilter(3))
    q = 2
    inner = push_pull(rgb.resize((W // q, H // q), Image.BOX), known.resize((W // q, H // q), Image.BOX)).resize((W, H), Image.BILINEAR)
    ip = inner.load(); p = rgb.load()
    out = rgb.copy(); op = out.load()
    for y in range(H):
        for x in range(W):
            a = ap[x, y]
            if a == 0:
                op[x, y] = ip[x, y]
            elif a < 255:
                # unmix where the pixel is mostly paint; interior colour toward the transparent side
                r, g, b = p[x, y]; t = a / 255.0
                ir, ig, ib = ip[x, y]
                w = smooth(0.55, 0.95, t)
                # despilled source: magenta-ness capped to the interior's
                cap = min(ir, ib) - ig
                if min(r, b) - g > cap:
                    d = (min(r, b) - g) - cap
                    r = r - d * 0.5; b = b - d * 0.5; g = g + d * 0.5
                op[x, y] = (int(clamp(r * w + ir * (1 - w), 0, 255)), int(clamp(g * w + ig * (1 - w), 0, 255)),
                            int(clamp(b * w + ib * (1 - w), 0, 255)))
            else:
                r, g, b = p[x, y]; ir, ig, ib = ip[x, y]
                cap = min(ir, ib) - ig
                if min(r, b) - g > cap + 12:      # pink spill inside the opaque body (rare): pull toward interior
                    op[x, y] = ((r + ir) // 2, (g + ig) // 2, (b + ib) // 2)
    out.putalpha(alpha)
    return out

def pink_fringe_share(rgba, a_lo=1, a_hi=254):
    """QA: share of semi-transparent edge texels whose colour is magenta-ish (min(R,B) - G > 24)."""
    p = rgba.load(); W, H = rgba.size; n = k = 0
    for y in range(H):
        for x in range(W):
            r, g, b, a = p[x, y]
            if a_lo <= a <= a_hi:
                n += 1
                if min(r, b) - g > 24: k += 1
    return k / max(1, n), n
