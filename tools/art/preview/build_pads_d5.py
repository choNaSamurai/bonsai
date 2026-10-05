"""PA-102T batch 02: pad atlases from the D5 generations (D_Sheets/sheet_pads_*_v1.png, RGB on magenta).

usage: python3 build_pads_d5.py [momiji] [kuromatsu] [yamazakura] [momiji_autumn] [yamazakura_bloom]   (no args = all)

Pipeline: magenta key (key.py: solved alpha + interior-colour despill) -> components of alpha > 0.5 after a 15 px
dilation (stray leaves join their pad) -> drop specks and the hand-listed fused/overlapping pads (EXCLUDE) ->
tight bbox of alpha > 0 + PAD_PX transparent margin -> defringe (build_pads.defringe, ink 0.86) -> skyline pack
into the smallest of 1024x1024 / 2048x1024 -> RGB bleed into transparent texels -> JSON (contract of README).
Size class by the sheet row (prompt: row 1 small, row 2 medium, rows 3+ large) — px thresholds do not transfer
between sheets (Yamazakura rows 2-4 are one size, Kuromatsu small pads are 255 px wide). Leaves keep one pixel size
inside a sheet, so the class still orders by width inside an atlas.
Dome: among L sprites at least 0.9x as wide as the widest L, the most dome-like one (opaque h/w); named <p>0, "role":"dome".
"""
import sys, json, os
from lib import *
from key import key_magenta, pink_fringe_share
from build_pads import pack, defringe

SHEETS = {   # name -> (source, prefix)
    'momiji': ('sheet_pads_momiji_v1.png', 'm'),
    'kuromatsu': ('sheet_pads_kuromatsu_v1.png', 'k'),
    'yamazakura': ('sheet_pads_yamazakura_v1.png', 'y'),
    'momiji_autumn': ('sheet_pads_momiji_autumn_v1.png', 'a'),
    'yamazakura_bloom': ('sheet_pads_yamazakura_bloom_v1.png', 'b'),
}
# (row, col) cells of the sheet (rows/cols counted from the top-left, by component centre) that are two pads fused
# into one overlapping cluster -> discarded (a split would leave a bitten-out silhouette). Filled after review.
EXCLUDE = {
    'momiji': {(1, 1), (1, 2)},
    'momiji_autumn': {(1, 1), (1, 2)},
}
PAD_PX = 12          # transparent margin, px (>= 12: mips 1/4 reach 0 at the rect edge, PA-102P)
SIZE_M = 230         # innerW thresholds, source px — only used for the log (classes come from the sheet row)
SIZE_L = 360
MIN_AREA = 4000      # opaque px; smaller components are specks

def size_class(w):
    return 'L' if w >= SIZE_L else ('M' if w >= SIZE_M else 'S')

def row_class(row):
    """D5 prompt: row 1 small, row 2 medium, rows 3+ large (sheets came as 3+3+2+2 or 3+3+3+3)."""
    return 'S' if row == 0 else ('M' if row == 1 else 'L')

def cut_components(rgba):
    W, H = rgba.size
    a = rgba.getchannel('A')
    hard = a.point(lambda v: 255 if v > 127 else 0)
    grown = hard.filter(ImageFilter.MaxFilter(15))
    comps, lab = components(grown)
    hp = hard.load(); res = []
    for ci, pts in enumerate(comps):
        xs = [j % W for j in pts]; ys = [j // W for j in pts]
        area = sum(1 for j in pts if hp[j % W, j // W])
        if area < MIN_AREA: continue
        res.append({'id': ci + 1, 'bbox': (min(xs), min(ys), max(xs) + 1, max(ys) + 1), 'area': area})
    # grid cells: rows by centre-y clustering (gap > 60 px), cols by centre-x order inside the row
    res.sort(key=lambda c: (c['bbox'][1] + c['bbox'][3]) / 2)
    row = 0; prev = None
    for c in res:
        cy = (c['bbox'][1] + c['bbox'][3]) / 2
        if prev is not None and cy - prev > 60: row += 1
        c['row'] = row; prev = cy
    for r in set(c['row'] for c in res):
        rc = sorted([c for c in res if c['row'] == r], key=lambda c: c['bbox'][0])
        for k, c in enumerate(rc): c['col'] = k
    return res, lab

def sprite_of(rgba, comp, lab):
    W, H = rgba.size
    x0, y0, x1, y1 = comp['bbox']
    crop = rgba.crop((x0, y0, x1, y1)); cp = crop.load(); cid = comp['id']
    for y in range(y1 - y0):
        for x in range(x1 - x0):
            if lab[(y + y0) * W + (x + x0)] != cid:
                r, g, b, _ = cp[x, y]; cp[x, y] = (r, g, b, 0)
    l, t, r, b = crop.getchannel('A').getbbox()
    crop = crop.crop((l, t, r, b))
    iw, ih = crop.size
    out = Image.new('RGBA', (iw + 2 * PAD_PX, ih + 2 * PAD_PX), (0, 0, 0, 0)); out.paste(crop, (PAD_PX, PAD_PX))
    return out, iw, ih

def opaque_ratio(sprite):
    """opaque (alpha >= 128) height / width of the content — the 'dome-ness' used for the dome pick."""
    l, t, r, b = sprite.getchannel('A').point(lambda v: 255 if v >= 128 else 0).getbbox()
    return (b - t) / float(r - l)

def build_atlas(name, pre, sprites_info):
    """sprites_info: list of (sprite, innerW, innerH, srcCell). Dome first (index 0)."""
    sprites = [defringe(s[0]) for s in sprites_info]
    for AW, AH in ((1024, 1024), (2048, 1024)):
        try:
            pos = pack(sprites, AW, AH); break
        except AssertionError:
            pos = None
    assert pos, 'atlas overflow ' + name
    atlas = Image.new('RGBA', (AW, AH), (0, 0, 0, 0))
    for s, (x, y) in zip(sprites, pos): atlas.paste(s, (x, y))
    a = atlas.getchannel('A')
    known = a.point(lambda v: 255 if v > 200 else 0)
    small = atlas.convert('RGB').resize((AW // 2, AH // 2), Image.BOX)
    bled = push_pull(small, known.resize((AW // 2, AH // 2), Image.BOX)).resize((AW, AH), Image.BILINEAR)
    rgb = Image.composite(atlas.convert('RGB'), bled, a.point(lambda v: 255 if v > 0 else 0))
    rgb.putalpha(a)
    rgb.save(OUT + 'pads_' + name + '.png', optimize=True)
    meta = {"pads": []}
    for i, (s, (x, y), info) in enumerate(zip(sprites, pos, sprites_info)):
        w, h = s.size; iw, ih = info[1], info[2]
        d = {"name": "%s%d" % (pre, i), "x": x, "y": y, "w": w, "h": h,
             "pivotX": round((PAD_PX + 0.5 * iw) / w, 5), "pivotY": round((PAD_PX + 0.2 * ih) / h, 5),
             "size": row_class(info[3][0]), "padPx": PAD_PX, "innerW": iw, "innerH": ih}
        if i == 0: d["size"] = "L"; d["role"] = "dome"
        meta["pads"].append(d)
    with open(OUT + 'pads_' + name + '.json', 'w') as f:
        json.dump(meta, f, indent=2)
    return rgb, meta

def run(name):
    src, pre = SHEETS[name]
    sheet = Image.open(SRC + 'D_Sheets/' + src).convert('RGB')
    keyed = key_magenta(sheet)
    keyed.save(SCR + 'batch02/keyed_' + name + '.png')
    comps, lab = cut_components(keyed)
    ex = EXCLUDE.get(name, set())
    info = []
    for c in comps:
        cell = (c['row'], c['col'])
        tag = 'EXCLUDED' if cell in ex else ''
        print('  %s cell %s bbox %s area %d %s' % (name, cell, c['bbox'], c['area'], tag))
        if tag: continue
        sp, iw, ih = sprite_of(keyed, c, lab)
        info.append((sp, iw, ih, cell))
    # dome: most dome-like among the L sprites (opaque h/w), then the widest
    Ls = [k for k, s in enumerate(info) if row_class(s[3][0]) == 'L']
    wmax = max(info[k][1] for k in Ls)
    Ls = [k for k in Ls if info[k][1] >= 0.9 * wmax]
    dome = max(Ls, key=lambda k: (round(opaque_ratio(info[k][0]), 2), info[k][1]))
    rank = {'L': 0, 'M': 1, 'S': 2}
    order = [dome] + sorted([k for k in range(len(info)) if k != dome], key=lambda k: (rank[row_class(info[k][3][0])], -info[k][1], info[k][3]))
    info = [info[k] for k in order]
    rgb, meta = build_atlas(name, pre, info)
    fr, n = pink_fringe_share(rgb)
    print('pads_%s %s: %d sprites %s; edge texels %d, magenta-ish %.2f%%' % (name, rgb.size, len(meta['pads']),
          ' '.join('%s%s(%dx%d,cell%s)' % (p['name'], p['size'], p['innerW'], p['innerH'], info[i][3]) for i, p in enumerate(meta['pads'])), n, 100 * fr))

if __name__ == '__main__':
    os.makedirs(SCR + 'batch02', exist_ok=True)
    for nm in (sys.argv[1:] or list(SHEETS)):
        run(nm)
