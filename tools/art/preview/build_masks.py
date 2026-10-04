"""PA-102P: reference silhouette masks for the IoU / proportion harness.

Outputs (768x1376, 8-bit grayscale, 255 = tree, 0 = background / pot / soil):
  ref_mask_l2_momiji.png            <- Section_0/realism_l2_painterly.jpg
  ref_mask_l2_momiji_foliage.png    (foliage only, for pad components / tiers)
  ref_mask_night_kuromatsu.png      <- A_Concept/concept_night.jpg
  ref_mask_night_kuromatsu_foliage.png
Zone: y < POT_TOP (top edge of the pot back rim); everything at y >= POT_TOP is 0.
Also prints the measurements used in docs/tasks/PA-102P.md and writes review collages to $ART_SCRATCH.

Pure PIL (no numpy), like the rest of tools/art/preview. Run: python3 build_masks.py
"""
import json, math, sys
from lib import *
from PIL import ImageDraw

W, H = 768, 1376
REFS = {
    # name: (source, pot top y, pot outer left, pot outer right)  -- pot numbers measured on the sources (TD-028)
    'l2_momiji': ('Section_0/realism_l2_painterly.jpg', 1068, 101, 667),
    'night_kuromatsu': ('A_Concept/concept_night.jpg', 1068, 99, 671),
}
POT_UNITS = 92.0          # BALANCE §1.3.0: pot outer front width in model units
HOLE_MAX = 900            # foliage masks: enclosed gaps smaller than this (px) are filled (gaps between leaves)
HOLE_MAX_L2 = 120         # L2 tree mask: only paper specks between leaves; the pockets between twig forks under pads stay open
                          # the air pockets between trunk / branch / pad are larger and stay open
FOLIAGE_MIN_SHARE = 0.003 # foliage components below this share of the tree mask are dropped (stray leaf specks)
GROUND_BAND = 40          # px: a foliage component reaching this close to the pot top is moss on the flare, not a pad


def flat(img):
    return list(img.get_flattened_data()) if hasattr(img, 'get_flattened_data') else list(img.getdata())


def from_bits(bits):
    m = Image.new('L', (W, H)); m.putdata([255 if b else 0 for b in bits]); return m


def clip_zone(m, pot_top):
    d = ImageDraw.Draw(m); d.rectangle((0, pot_top, W, H), fill=0); return m


HOLES_KEPT = []


def fill_small_holes(m, max_area, log=False, also=None):
    """Fill enclosed background holes smaller than max_area, or (if `also` mask given) mostly covered by it."""
    inv = ImageChops.invert(m)
    comps, _ = components(inv)
    o = m.copy(); op = o.load()
    ap = also.load() if also is not None else None
    for pts in comps:
        if any(j % W in (0, W - 1) or j // W in (0, H - 1) for j in pts): continue
        covered = ap is not None and sum(1 for j in pts if ap[j % W, j // W]) >= 0.5 * len(pts)
        if len(pts) >= max_area and not covered:
            if log: HOLES_KEPT.append(len(pts))
            continue
        for j in pts: op[j % W, j // W] = 255
    return o


def drop_small(m, min_area):
    comps, _ = components(m)
    o = Image.new('L', (W, H), 0); op = o.load()
    for pts in comps:
        if len(pts) >= min_area:
            for j in pts: op[j % W, j // W] = 255
    return o


def smooth_edge(m, radius=1.2):
    return m.filter(ImageFilter.GaussianBlur(radius)).point(lambda v: 255 if v >= 128 else 0)


def local_std_fast(L, r):
    """Box-filter local standard deviation (C-level filters; values in grey levels)."""
    m = flat(L.filter(ImageFilter.BoxBlur(r)))
    q = flat(L.point(lambda v: v * v // 64).filter(ImageFilter.BoxBlur(r)))
    return [math.sqrt(max(0.0, b * 64 - a * a)) for a, b in zip(m, q)]


# ---------------------------------------------------------------- L2 painterly (cream paper)
def masks_l2(img, pot_top):
    px = flat(img)
    # paper colour: median of the top band
    band = sorted(px[:W * 60], key=lambda c: c[0] + c[1] + c[2])
    bg = band[len(band) // 2]
    tree = from_bits([(abs(r - bg[0]) + abs(g - bg[1]) + abs(b - bg[2])) > 48 for (r, g, b) in px])
    tree = clip_zone(tree, pot_top)
    tree = close(tree, 5)
    tree = fill_small_holes(tree, HOLE_MAX_L2)
    tree = keep_largest(tree)
    tree = clip_zone(smooth_edge(tree), pot_top)
    # foliage: yellow-green leaves (g ~ r, b far below), bark is r > g
    leaf = from_bits([(g - r) > -10 and (r - b) > 35 and (r + g) > 120 for (r, g, b) in px])
    return tree, leaf


# ---------------------------------------------------------------- Night concept (gradient sky, bokeh, fireflies)
ROI_DILATE = 41   # px (kernel): the night tree is searched only near the L2 silhouette (same layout) -> no stray bokeh
SKY_DIST = 30.0
HOLE_MAX_NIGHT = 120     # like L2: only specks; holes punched by bokeh/firefly subtraction are filled separately


def masks_night(img, pot_top, prior):
    px = flat(img)
    L = img.convert('L')
    std = local_std_fast(L, 3)
    lum = flat(L)
    roi = prior.filter(ImageFilter.MaxFilter(ROI_DILATE))
    roi_px = flat(roi)
    # 1. sky model: pixels outside the ROI are sky (incl. bokeh); push-pull interpolates the smooth gradient/glow inside
    # (pot and soil are not sky: only rows above the pot top are known)
    known = clip_zone(from_bits([v == 0 for v in roi_px]), pot_top)
    sky = push_pull(img, known.filter(ImageFilter.MinFilter(3)))
    sky = sky.filter(ImageFilter.GaussianBlur(6))
    sp = flat(sky)
    # 2. bokeh discs / firefly glows: smooth, lighter than the local sky, not blue; their soft ring is covered by dilation
    def is_bokeh(c, s, l, k):
        r, g, b = c; sr, sg, sb = k
        slum = 0.299 * sr + 0.587 * sg + 0.114 * sb
        return (s < 5.0 and l > slum + 10 and (b - (r + g) / 2) < 14) or l > 150
    bokeh = from_bits([is_bokeh(c, s, l, k) for c, s, l, k in zip(px, std, lum, sp)])
    bokeh = drop_small(bokeh, 25).filter(ImageFilter.MaxFilter(7))
    # 3. tree = far from the interpolated sky (blue weighted: needles differ from the teal glow mostly in blue)
    def dist(c, k):
        return math.sqrt((c[0] - k[0]) ** 2 + (c[1] - k[1]) ** 2 + (1.6 * (c[2] - k[2])) ** 2)
    tree = from_bits([rv and dist(c, k) > SKY_DIST for c, k, rv in zip(px, sp, roi_px)])
    tree = ImageChops.subtract(tree, bokeh)
    tree = clip_zone(tree, pot_top)
    tree = close(tree, 5)
    tree = fill_small_holes(tree, HOLE_MAX_NIGHT, also=bokeh)
    tree = keep_largest(tree)
    tree = clip_zone(smooth_edge(tree), pot_top)
    # foliage: needles are green-shifted (g - r >= 8 on a 5 px blur) also on the dark underside; bark is grey-brown (g ~ r).
    # The greenish moss/highlight band on the trunk is thinner than a pad and is removed by the opening in foliage_from.
    bp = flat(img.filter(ImageFilter.BoxBlur(5)))
    leaf = from_bits([(g - r) >= 8 and l < 150 for (r, g, b), l in zip(bp, lum)])
    sky.save(SCR + 'night_sky_model.png'); bokeh.save(SCR + 'night_bokeh.png')
    return tree, leaf


def foliage_from(leaf, tree, k_close, k_open, pot_top):
    f = close(leaf, k_close)
    f = fill_small_holes(f, HOLE_MAX * 3)
    f = f.filter(ImageFilter.MinFilter(k_open)).filter(ImageFilter.MaxFilter(k_open))   # opening: drop twigs / bark bands
    f = ImageChops.multiply(smooth_edge(f, 1.5), tree)
    f = f.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MaxFilter(5))   # drop slivers left by the tree clip
    f = fill_small_holes(f, HOLE_MAX * 3)
    # keep pad-sized components that float above the ground (green moss on the root flare is not foliage)
    area = sum(1 for v in flat(tree) if v)
    comps, _ = components(f)
    o = Image.new('L', (W, H), 0); op = o.load()
    for pts in comps:
        if len(pts) < FOLIAGE_MIN_SHARE * area: continue
        if max(j // W for j in pts) > pot_top - GROUND_BAND: continue
        for j in pts: op[j % W, j // W] = 255
    return o


def measure(name, tree, fol, pot_top, pl, pr):
    P = pr - pl; s = POT_UNITS / P
    tp = tree.load()
    ys = [y for y in range(pot_top) if any(tp[x, y] for x in range(0, W, 1))]
    top = ys[0]; Hpx = pot_top - top
    xs = [x for x in range(W) if any(tp[x, y] for y in range(top, pot_top))]
    # W without roots: roots are below the pot top here, but exclude the flare band (last 4% of H) to be safe
    xs2 = [x for x in range(W) if any(tp[x, y] for y in range(top, pot_top - int(0.04 * Hpx)))]
    Wpx = xs2[-1] - xs2[0] + 1
    tree_area = sum(1 for v in flat(tree) if v)
    comps, _ = components(fol)
    pads = []
    for pts in comps:
        cx = [j % W for j in pts]; cy = [j // W for j in pts]
        x0, x1, y0, y1 = min(cx), max(cx), min(cy), max(cy)
        pads.append(dict(area=len(pts), share=round(len(pts) / tree_area, 4), x0=x0, x1=x1, y0=y0, y1=y1,
                         w=x1 - x0 + 1, h=y1 - y0 + 1, cx=sum(cx) / len(pts), cy=sum(cy) / len(pts)))
    pads.sort(key=lambda d: -d['cy'])
    def runs_at(yy):
        rr = []; st = None
        for x in range(W + 1):
            on = x < W and tp[x, yy]
            if on and st is None: st = x
            if not on and st is not None: rr.append((st, x - 1)); st = None
        return rr
    trunk_w = {}
    for fr in (0.06, 0.10, 0.14):   # below the lowest pad (bottom ~0.18 H), above the flare
        rr = runs_at(pot_top - int(fr * Hpx))
        trunk_w[fr] = max((r[1] - r[0] + 1 for r in rr), default=0)
    trunk = (0, trunk_w[0.14] - 1)
    holes = [len(p) for p in components(ImageChops.invert(tree))[0]
             if not any(j % W in (0, W - 1) or j // W in (0, H - 1) for j in p)]
    yb = pot_top - 3
    rowb = [x for x in range(W) if tp[x, yb]]
    res = dict(name=name, P_px=P, units_per_px=round(s, 5), top_y=top, H_px=Hpx, W_px=Wpx, x0=xs2[0], x1=xs2[-1],
               H_over_P=round(Hpx / P, 3), W_over_P=round(Wpx / P, 3), H_units=round(Hpx * s, 1), W_units=round(Wpx * s, 1),
               trunk_w_px=trunk_w, trunk_over_H_at_0_14=round(trunk_w[0.14] / Hpx, 4), enclosed_air_holes_px=sorted(holes),
               base_w_px=(rowb[-1] - rowb[0] + 1) if rowb else 0,
               tree_area=tree_area, foliage_area=sum(p['area'] for p in pads), n_foliage=len(pads),
               n_foliage_ge_0_5pct=sum(1 for p in pads if p['area'] >= 0.005 * tree_area), pads=[])
    for k, p in enumerate(pads):
        side = 'L' if p['cx'] < (pl + pr) / 2 - 0.02 * P else ('R' if p['cx'] > (pl + pr) / 2 + 0.02 * P else 'C')
        res['pads'].append(dict(i=k, side=side, area=p['area'], share=p['share'],
                                cy_H=round((pot_top - p['cy']) / Hpx, 3), bottom_H=round((pot_top - p['y1']) / Hpx, 3),
                                top_H=round((pot_top - p['y0']) / Hpx, 3),
                                w_px=p['w'], h_px=p['h'], w_H=round(p['w'] / Hpx, 3), h_H=round(p['h'] / Hpx, 3),
                                w_u=round(p['w'] * s, 1), h_u=round(p['h'] * s, 1),
                                bbox=[p['x0'], p['y0'], p['x1'], p['y1']]))
    return res


def overlay(src, tree, fol, pot_top, title):
    base = src.convert('RGB')
    t_edge = ImageChops.subtract(tree.filter(ImageFilter.MaxFilter(3)), tree.filter(ImageFilter.MinFilter(3)))
    f_edge = ImageChops.subtract(fol.filter(ImageFilter.MaxFilter(3)), fol.filter(ImageFilter.MinFilter(3)))
    tint = Image.new('RGB', (W, H), (255, 40, 200))
    a = Image.blend(base, tint, 0.0)
    a.paste(Image.blend(base, tint, 0.35), (0, 0), tree.point(lambda v: 255 if v else 0))
    a.paste((255, 0, 160), (0, 0), t_edge)
    a.paste((0, 230, 255), (0, 0), f_edge)
    d = ImageDraw.Draw(a); d.line((0, pot_top, W, pot_top), fill=(255, 200, 0), width=1)
    d.text((8, 8), title, fill=(255, 200, 0))
    return a


def main():
    out = {}
    sheets = []
    prior = None
    for name, (rel, pot_top, pl, pr) in REFS.items():
        src = Image.open(SRC + rel).convert('RGB')
        assert src.size == (W, H), src.size
        if name.startswith('l2'):
            tree, leaf = masks_l2(src, pot_top)
            fol = foliage_from(leaf, tree, 9, 9, pot_top)
            prior = tree
        else:
            tree, leaf = masks_night(src, pot_top, prior)
            fol = foliage_from(leaf, tree, 9, 21, pot_top)
        tree.save(OUT + 'ref_mask_%s.png' % name, optimize=True)
        fol.save(OUT + 'ref_mask_%s_foliage.png' % name, optimize=True)
        m = measure(name, tree, fol, pot_top, pl, pr)
        out[name] = m
        ov = overlay(src, tree, fol, pot_top, '%s  tree=magenta  foliage=cyan  pot top y=%d' % (name, pot_top))
        ov.save(SCR + 'mask_overlay_%s.png' % name)
        # side-by-side: source | overlay | tree mask | foliage mask (x0.5)
        sh = Image.new('RGB', (W * 2, H // 2), (30, 30, 30))
        for k, im in enumerate((src, ov, tree.convert('RGB'), fol.convert('RGB'))):
            sh.paste(im.resize((W // 2, H // 2), Image.LANCZOS), (k * W // 2, 0))
        sheets.append(sh)
    col = Image.new('RGB', (W * 2, H), (30, 30, 30))
    for k, sh in enumerate(sheets): col.paste(sh, (0, k * H // 2))
    col.save(SCR + 'mask_collage.png')
    json.dump(out, open(SCR + 'mask_measurements.json', 'w'), indent=1)
    for name, m in out.items():
        print('==', name)
        print({k: v for k, v in m.items() if k != 'pads'})
        for p in m['pads']: print('  ', p)


if __name__ == '__main__':
    main()
