"""PA-102T batch 02: pot skins from C_Pots generations (1376x768 RGB, side view at rim height, no opening).

usage: python3 build_pots_c2.py [kuro] [celadon] [tokoname_v2]   (no args = all)
-> Art/Runtime/Preview/pot_<name>.png/.json, same JSON fields as pot_tokoname.json (y from the bottom, x from the left).
These pots have NO opening (the top is not visible): innerTopY = lipY -> PotArt.HasOpening == false, BaseY = lipY.
  lipY        — top of the rim at the centre (median over the middle 60% of columns of the topmost alpha >= 0.5 row)
  rimOuterLeft/Right — opaque x-extent of the rim (2 px below the rim top)
  innerLeft/Right    — soil extent: rim outer edges inset by `wallPx` (0.035 x rim width, painted wall thickness)
  innerTopY/innerTopLeft/innerTopRight/rimBackTopY = lipY / innerLeft / innerRight / lipY (no back edge)
  soilMaxBelowLipPx  — depth of the rim band: the soil strip must be clipped this far below lipY (body tapers inward)
Keying: kuro/celadon — key.key_magenta on the uniform magenta; tokoname_v2 — background is a darker pink
(211,74,196) with a contact shadow, so the local background magenta-ness is push-pulled from the background pixels
(shadow included -> it keys out with the background).
"""
import sys, json, os
from lib import *
from key import key_magenta, magentaness, pink_fringe_share

POTS = {'kuro': 'pot_kuro.png', 'celadon': 'pot_celadon.png', 'tokoname_v2': 'pot_tokoname.png'}
PAD = 12

def bg_field_of(rgb, seed_thr):
    W, H = rgb.size
    m = magentaness(rgb)
    seeds = m.point(lambda v: 255 if v > seed_thr else 0).filter(ImageFilter.MinFilter(7))
    q = 4
    f = push_pull(Image.merge('RGB', [m, m, m]).resize((W // q, H // q), Image.BOX), seeds.resize((W // q, H // q), Image.BOX))
    return f.getchannel('R').resize((W, H), Image.BILINEAR)

def measure(rgba):
    W, H = rgba.size; a = rgba.getchannel('A').load()
    tops = []
    for x in range(int(W * 0.2), int(W * 0.8)):
        for y in range(H):
            if a[x, y] >= 128: tops.append(y); break
    tops.sort(); top = tops[len(tops) // 2]
    yr = top + 2
    xs = [x for x in range(W) if a[x, yr] >= 128]
    ol, orr = min(xs), max(xs) + 1
    wall = int(round(0.035 * (orr - ol)))
    lipY = H - top
    # rim band depth: first row below the rim top where the opaque body is narrower than the soil extent
    depth = 0
    for y in range(yr, H):
        xs2 = [x for x in range(0, W, 2) if a[x, y] >= 128]
        if not xs2 or min(xs2) > ol + wall or max(xs2) < orr - wall: depth = y - top; break
    return {"lipY": lipY, "innerLeft": ol + wall, "innerRight": orr - wall, "innerTopY": lipY,
            "innerTopLeft": ol + wall, "innerTopRight": orr - wall, "rimBackTopY": lipY,
            "rimOuterLeft": ol, "rimOuterRight": orr, "wallPx": wall, "soilMaxBelowLipPx": max(0, depth - 12), "width": W, "height": H, "hasOpening": False,
            "note": ("PA-102T batch 02 skin: front wall only, NO opening (generation is a side view at rim height). "
                     "y from the bottom edge, x from the left. Ground line / trunk base = lipY (PotArt.BaseY when "
                     "innerTopY <= lipY). Soil strip goes BEHIND the pot, scaled to innerRight-innerLeft, its ground line "
                     "(soil.json groundFromTop) at lipY, clipped to at most soilMaxBelowLipPx below lipY (the body tapers "
                     "under the rim: deeper soil would show beside the walls). Only the moss crest above the rim stays visible.")}

def run(name):
    rgb = Image.open(SRC + 'C_Pots/' + POTS[name]).convert('RGB')
    if name == 'tokoname_v2':
        rgba = key_magenta(rgb, fg_thr=150, bg_field=bg_field_of(rgb, 160), min_span=30)
        # the contact shadow keys to alpha ~0.2-0.25 (darker pink than the field) -> cut it, keep a 1-2 px AA edge
        rgba.putalpha(rgba.getchannel('A').point(lambda v: int(clamp((v / 255.0 - 0.32) / 0.6) * 255)))
    else:
        rgba = key_magenta(rgb)
    # keep the largest component (stray specks), crop + margin
    a = rgba.getchannel('A')
    big = keep_largest(a.point(lambda v: 255 if v >= 128 else 0)).filter(ImageFilter.MaxFilter(9))
    rgba.putalpha(ImageChops.multiply(a, big))
    l, t, r, b = rgba.getchannel('A').getbbox()
    crop = rgba.crop((l, t, r, b))
    out = Image.new('RGBA', (crop.size[0] + 2 * PAD, crop.size[1] + 2 * PAD), (0, 0, 0, 0)); out.paste(crop, (PAD, PAD))
    # bleed colour into transparent texels (straight alpha, bilinear/mip safety)
    al = out.getchannel('A')
    filled = push_pull(out.convert('RGB'), al.point(lambda v: 255 if v > 200 else 0))
    rgbo = Image.composite(out.convert('RGB'), filled, al.point(lambda v: 255 if v > 0 else 0)); rgbo.putalpha(al)
    rgbo.save(OUT + 'pot_' + name + '.png', optimize=True)
    meta = measure(rgbo)
    with open(OUT + 'pot_' + name + '.json', 'w') as f: json.dump(meta, f, indent=2)
    fr, n = pink_fringe_share(rgbo)
    print('pot_%s %s lipY %d inner %d..%d rim %d..%d; edge texels %d magenta-ish %.2f%%; %.0f KB' % (
        name, rgbo.size, meta['lipY'], meta['innerLeft'], meta['innerRight'], meta['rimOuterLeft'], meta['rimOuterRight'],
        n, 100 * fr, os.path.getsize(OUT + 'pot_' + name + '.png') / 1024))

if __name__ == '__main__':
    for nm in (sys.argv[1:] or list(POTS)): run(nm)
