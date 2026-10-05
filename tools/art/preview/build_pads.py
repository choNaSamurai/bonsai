import sys, json, os, colorsys
from pads import *
import pine

l2 = Image.open(SRC + 'Section_0/realism_l2_painterly.jpg').convert('RGB')
nt = Image.open(SRC + 'A_Concept/concept_night.jpg').convert('RGB')

def feather_box_excl(x0, x1, y0, y1):   # remove the (x>x0..x1, y>y0..y1) quadrant softly
    return lambda x, y: 1.0 - smooth(x0, x1, x) * smooth(y0, y1, y)
def feather_box_incl(x0, x1, y0, y1):
    return lambda x, y: smooth(x0, x1, x) * smooth(y0, y1, y)

MOMIJI = [  # (box, cut)
    ((223, 277, 603, 440), cut_below(426, rise_from=500, rise=0.35)),   # apex dome
    ((56, 736, 337, 870), None),     # left low
    ((433, 790, 715, 928), None),    # right low
    ((400, 490, 656, 595), None),    # right upper
    ((442, 634, 689, 741), None),    # right mid
    ((108, 571, 346, 677), None),    # left mid
    ((166, 438, 393, 528), None),    # left upper
    ((418, 392, 606, 471), cut_dome(424, 606, 400, 440)),   # small right
]
KURO = [
    ((227, 269, 560, 424), cut_below(392)),
    ((55, 733, 330, 851), None),
    ((436, 790, 719, 896), None),
    ((405, 459, 659, 590), None),
    ((445, 635, 682, 730), None),
    ((112, 568, 344, 664), None),
    ((170, 439, 373, 527), None),
]

def dayify(im):
    rgb = im.convert('RGB'); a = im.getchannel('A'); p = rgb.load(); W, H = rgb.size
    for y in range(H):
        for x in range(W):
            r, g, b = [v / 255 for v in p[x, y]]
            h, s, v = colorsys.rgb_to_hsv(r, g, b)
            h = (h * 360 * 0.45 + 108 * 0.55) / 360
            s = clamp(s * 1.05 + 0.08, 0, 0.6)
            v = clamp(0.03 + v ** 1.0 * 1.22, 0, 0.9)
            r, g, b = colorsys.hsv_to_rgb(h, s, v)
            p[x, y] = (int(r * 255), int(g * 255), int(b * 255))
    rgb.putalpha(a); return rgb

def to_yamazakura(im):
    rgb = im.convert('RGB'); a = im.getchannel('A'); p = rgb.load(); W, H = rgb.size
    for y in range(H):
        for x in range(W):
            r, g, b = [v / 255 for v in p[x, y]]
            h, s, v = colorsys.rgb_to_hsv(r, g, b)
            h = ((h * 360 + 26) % 360) / 360
            s = clamp(s * 0.92, 0, 1)
            v = clamp(v * 0.86, 0, 1)
            r, g, b = colorsys.hsv_to_rgb(h, s, v)
            p[x, y] = (int(r * 255), int(g * 255), int(b * 255))
    rgb = rgb.filter(ImageFilter.GaussianBlur(0.6))
    a = a.filter(ImageFilter.GaussianBlur(1.0)).point(lambda v: int(clamp((v - 40) / 175.0) * 255))
    rgb.putalpha(a); return rgb

def size_class(w):
    return 'L' if w >= 540 else ('M' if w >= 440 else 'S')

PAD_PX = 12   # PA-102P: transparent margin around every sprite rect (mips/bilinear never reach the rect edge)

def defringe(im, ink=0.86):
    """PA-102P: semi-transparent edge texels take the colour of the nearest opaque interior, darkened toward ink.
    Kills the light/grey fringe left by keying + Lanczos/unsharp (most visible on the top of m0)."""
    rgb = im.convert('RGB'); a = im.getchannel('A')
    known = a.point(lambda v: 255 if v >= 250 else 0).filter(ImageFilter.MinFilter(3))
    fill = push_pull(rgb, known); fp = fill.load(); op = rgb.load(); ap = a.load()
    W, H = rgb.size
    for y in range(H):
        for x in range(W):
            al = ap[x, y]
            if 0 < al < 250:
                fr, fg, fb = fp[x, y]; r, g, b = op[x, y]
                # keep the darker of (original, darkened interior) per channel -> only removes light fringe
                op[x, y] = (min(r, int(fr * ink)), min(g, int(fg * ink)), min(b, int(fb * ink)))
    rgb.putalpha(a); return rgb

def pad_sprite(im, pad=PAD_PX):
    """Grow the PA-103S rect uniformly so the content has >= `pad` px of transparent margin on every side.
    The old rect stays centred inside the new one -> its pivot (0.5 / 0.2) keeps the same texel.
    Returns (sprite, extra, old_w, old_h)."""
    W, H = im.size
    l, t, r, b = im.getchannel('A').getbbox()
    extra = max(0, pad - min(l, t, W - r, H - b))
    out = Image.new('RGBA', (W + 2 * extra, H + 2 * extra), (0, 0, 0, 0)); out.paste(im, (extra, extra))
    return out, extra, W, H

def pack(sprites, AW, AH, gap=2):
    """Skyline bottom-left packer (tallest first). Rects already carry PAD_PX margin, `gap` only separates rects."""
    order = sorted(range(len(sprites)), key=lambda i: (-sprites[i].size[1], -sprites[i].size[0]))
    sky = [(0, AW, 0)]  # segments (x, width, y)
    pos = [None] * len(sprites)
    for i in order:
        w, h = sprites[i].size; w += gap; h += gap
        best = None
        for si in range(len(sky)):
            x = sky[si][0]
            if x + w > AW: break
            # max y over segments covered by [x, x+w)
            y = 0; wl = w; sj = si
            while wl > 0:
                y = max(y, sky[sj][2]); wl -= sky[sj][1]; sj += 1
                if sj >= len(sky) and wl > 0: y = None; break
            if y is None or y + h > AH: continue
            if best is None or (y, x) < (best[1], best[0]): best = (x, y)
        assert best, ('atlas overflow', i, sprites[i].size)
        x, y = best; pos[i] = (x, y)
        # update skyline
        new = []
        for (sx, sw, sy) in sky:
            ex = sx + sw
            if ex <= x or sx >= x + w: new.append((sx, sw, sy)); continue
            if sx < x: new.append((sx, x - sx, sy))
            if ex > x + w: new.append((x + w, ex - (x + w), sy))
        new.append((x, w, y + h)); new.sort()
        merged = []
        for s in new:
            if merged and merged[-1][2] == s[2] and merged[-1][0] + merged[-1][1] == s[0]:
                merged[-1] = (merged[-1][0], merged[-1][1] + s[1], s[2])
            else: merged.append(s)
        sky = merged
    return pos

def build(name, prefix, sprites, AW=2048, AH=1024):
    padded = [pad_sprite(defringe(s)) for s in sprites]
    sprites = [p[0] for p in padded]
    pos = pack(sprites, AW, AH)
    atlas = Image.new('RGBA', (AW, AH), (0, 0, 0, 0))
    for s, (x, y) in zip(sprites, pos):
        atlas.paste(s, (x, y))
    # bleed RGB into transparent texels (mip/bilinear safety); keep straight alpha
    a = atlas.getchannel('A')
    known = a.point(lambda v: 255 if v > 200 else 0)
    small = atlas.convert('RGB').resize((AW // 2, AH // 2), Image.BOX)
    bled = push_pull(small, known.resize((AW // 2, AH // 2), Image.BOX)).resize((AW, AH), Image.BILINEAR)
    rgb = Image.composite(atlas.convert('RGB'), bled, a.point(lambda v: 255 if v > 0 else 0))
    rgb.putalpha(a)
    rgb.save(OUT + name + '.png', optimize=True)
    meta = {"pads": []}
    for i, (s, (x, y), (_, m, lw, lh)) in enumerate(zip(sprites, pos, padded)):
        w, h = s.size   # PA-103S rect lw×lh sits at (m, m) inside; pivot 0.5 / 0.2 of it, from the bottom-left
        meta["pads"].append({"name": "%s%d" % (prefix, i), "x": x, "y": y, "w": w, "h": h,
                             "pivotX": round((m + 0.5 * lw) / w, 5), "pivotY": round((m + 0.2 * lh) / h, 5),
                             "size": size_class(lw), "padPx": m, "innerW": lw, "innerH": lh})
    with open(OUT + name + '.json', 'w') as f:
        json.dump(meta, f, indent=2)
    return rgb, meta

if __name__ == '__main__':
    # usage: python3 build_pads.py [momiji] [kuromatsu] [yamazakura]   (no args = all three)
    only = set(sys.argv[1:]) or {'momiji', 'kuromatsu', 'yamazakura'}
    os.makedirs(OUT, exist_ok=True)
    jobs = []
    if only & {'momiji', 'yamazakura'}:
        mom = []
        for i, (box, cut) in enumerate(MOMIJI):
            mom.append(extract(l2, box, 'day', cut, seed=i, close_k=(17 if i == 0 else 7)))
        if 'momiji' in only: jobs.append(('pads_momiji', 'm', [upscale_rgba(s) for s in mom]))
        if 'yamazakura' in only: jobs.append(('pads_yamazakura', 'y', [upscale_rgba(to_yamazakura(s)) for s in mom]))
    if 'kuromatsu' in only:
        if os.environ.get('KURO_FROM_NIGHT'):   # PA-103S cut-outs of concept_night (thin, notched) — kept for comparison
            kur = [upscale_rgba(dayify(extract(nt, box, 'night', cut, seed=i, close_k=(17 if i == 0 else 11)))) for i, (box, cut) in enumerate(KURO)]
        else:                                   # PA-102Q: painted needle-fan shelves (pine.py): dense, no notches, + S tufts
            kur = pine.kuromatsu_sprites()
        jobs.append(('pads_kuromatsu', 'k', kur))
    for nm, pf, sp in jobs:
        _, meta = build(nm, pf, sp)
        print(nm, [(p['name'], p['x'], p['y'], p['w'], p['h'], p['pivotY'], p['size']) for p in meta['pads']])
