import sys, json, os, colorsys
from pads import *

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

def pack(sprites, AW, AH, pad=10):
    order = sorted(range(len(sprites)), key=lambda i: -sprites[i].size[1])
    pos = [None] * len(sprites); x = pad; y = pad; rowh = 0
    for i in order:
        w, h = sprites[i].size
        if x + w + pad > AW:
            x = pad; y += rowh + pad; rowh = 0
        pos[i] = (x, y); x += w + pad; rowh = max(rowh, h)
    assert y + rowh + pad <= AH, ('overflow', y + rowh)
    return pos

def build(name, prefix, sprites, AW=2048, AH=1024):
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
    for i, (s, (x, y)) in enumerate(zip(sprites, pos)):
        w, h = s.size
        meta["pads"].append({"name": "%s%d" % (prefix, i), "x": x, "y": y, "w": w, "h": h,
                             "pivotX": 0.5, "pivotY": 0.2, "size": size_class(w)})
    with open(OUT + name + '.json', 'w') as f:
        json.dump(meta, f, indent=2)
    return rgb, meta

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    mom = []
    for i, (box, cut) in enumerate(MOMIJI):
        mom.append(extract(l2, box, 'day', cut, seed=i, close_k=(17 if i == 0 else 7)))
    kur = []
    for i, (box, cut) in enumerate(KURO):
        kur.append(dayify(extract(nt, box, 'night', cut, seed=i, close_k=(17 if i == 0 else 11))))
    momU = [upscale_rgba(s) for s in mom]
    kurU = [upscale_rgba(s) for s in kur]
    yamU = [upscale_rgba(to_yamazakura(s)) for s in mom]
    for nm, pf, sp in (('pads_momiji', 'm', momU), ('pads_kuromatsu', 'k', kurU), ('pads_yamazakura', 'y', yamU)):
        _, meta = build(nm, pf, sp)
        print(nm, [(p['name'], p['w'], p['h'], p['size']) for p in meta['pads']])
