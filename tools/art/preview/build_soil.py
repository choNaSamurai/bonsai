import json, random, math, colorsys
from lib import *
from PIL import ImageDraw
l2 = Image.open(SRC + 'Section_0/realism_l2_painterly.jpg').convert('RGB')
exec(open(SCR + 'soil_core.py').read())
# ground line: median of top-most opaque row per column
am = moist.getchannel('A').load(); tops = []
for X in range(moist.size[0]):
    for y in range(moist.size[1]):
        if am[X, y] > 128: tops.append(y); break
tops.sort(); GROUND_FROM_TOP = tops[len(tops) // 2]
# fit the strip to the pot opening: trapezoid mask (sides tuck 12px under the inner rim walls),
# so nothing shows outside the pot when the strip is drawn BEHIND pot_tokoname.png
pm = json.load(open(OUT + 'pot_tokoname.json'))
from PIL import ImageDraw as _D
SWD = pm['innerRight'] - pm['innerLeft'] + 24
moist = moist.crop(((moist.size[0] - SWD) // 2, 0, (moist.size[0] - SWD) // 2 + SWD, moist.size[1]))
gy = GROUND_FROM_TOP; topw = pm['innerTopRight'] - pm['innerTopLeft'] + 24
botY = gy + (pm['innerTopY'] - pm['lipY'])          # ground line -> front lip in pot pixels
inset_top = (SWD - topw) / 2.0
mk = Image.new('L', (SWD * 4, moist.size[1] * 4), 0); dd = _D.Draw(mk)
def ix(yy):  # inset at row yy (linear along the opening side walls), extended above/below
    t = (yy - gy) / float(max(1, botY - gy))
    return inset_top * (1 - t)
H4 = moist.size[1]
poly = [(ix(0) * 4, 0), ((SWD - ix(0)) * 4, 0), ((SWD - ix(H4)) * 4, H4 * 4), (ix(H4) * 4, H4 * 4)]
dd.polygon(poly, fill=255)
mk = mk.resize(moist.size, Image.LANCZOS)
moist.putalpha(ImageChops.multiply(moist.getchannel('A'), mk))
moist.save(OUT + 'soil_moist.png', optimize=True)

# ---- dry: same layout, recoloured + cracks
def dryify(im):
    rgb = im.convert('RGB'); a = im.getchannel('A'); p = rgb.load(); W, H = rgb.size
    moss = Image.new('L', (W, H)); mq = moss.load()
    for y in range(H):
        for x in range(W):
            r, g, b = [v / 255 for v in p[x, y]]
            h, s, v = colorsys.rgb_to_hsv(r, g, b)
            hd = h * 360
            if 45 <= hd <= 140 and s > 0.2:      # moss -> sleepy dusty moss #6B7355
                h = (h * 360 * 0.2 + 75 * 0.8) / 360; s = 0.12 + s * 0.2; v = 0.2 + v * 0.5
                mq[x, y] = 255
            else:                                # soil/stones -> pale dry soil #8C8275
                h = (h * 360 * 0.4 + 32 * 0.6) / 360; s = s * 0.5; v = 0.30 + v * 0.55
            r, g, b = colorsys.hsv_to_rgb(h, s, v)
            p[x, y] = (int(r * 255), int(g * 255), int(b * 255))
    # cracks: random-walk dark hairlines, mostly horizontal-ish (perspective of a flat surface)
    rnd = random.Random(31)
    lay = Image.new('RGBA', (W * 2, H * 2), (0, 0, 0, 0)); d = ImageDraw.Draw(lay)
    for _ in range(34):
        x = rnd.uniform(0, W); y = rnd.uniform(H * 0.25, H * 0.9); ang = rnd.uniform(-0.5, 0.5) + (0 if rnd.random() < 0.7 else math.pi / 2)
        pts = [(x, y)]
        for k in range(rnd.randint(5, 12)):
            ang += rnd.uniform(-0.6, 0.6); st = rnd.uniform(3, 8)
            x += math.cos(ang) * st; y += math.sin(ang) * st * 0.6
            pts.append((x, y))
            if rnd.random() < 0.18:
                bx, by = x, y; ba = ang + rnd.choice([-1, 1]) * rnd.uniform(0.6, 1.2)
                d.line([(bx * 2, by * 2), ((bx + math.cos(ba) * 7) * 2, (by + math.sin(ba) * 4) * 2)], fill=(58, 48, 40, 150), width=2)
        d.line([(px * 2, py * 2) for px, py in pts], fill=(58, 48, 40, 175), width=2)
    lay = lay.resize((W, H), Image.LANCZOS)
    moss = moss.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(2))
    la = lay.getchannel('A'); la = ImageChops.multiply(la, ImageChops.invert(moss)); lay.putalpha(la)
    out = rgb.convert('RGBA'); out.alpha_composite(lay)
    out.putalpha(a)
    return out
dry = dryify(moist)
dry.save(OUT + 'soil_dry.png', optimize=True)
json.dump({"groundFromTop": GROUND_FROM_TOP, "fitsPot": "pot_tokoname", "width": moist.size[0], "height": moist.size[1],
           "note": "Both soil_moist.png and soil_dry.png share this exact layout; groundFromTop = px from the top edge to the ground line (top of the opaque band). Strip sits BEHIND pot_tokoname.png, ground line aligned to pot innerTopY, strip centred on the pot opening (innerLeft..innerRight + 12px each side). Side edges are trapezoid-masked to the opening of pot_tokoname; another pot needs its own mask."},
          open(OUT + 'soil.json', 'w'), indent=2)
print(moist.size, GROUND_FROM_TOP)

# PA-102Q: SoilFront band numbers (moss crest / recommended depth) -> soil.json
import soil_front
print(soil_front.measure())
