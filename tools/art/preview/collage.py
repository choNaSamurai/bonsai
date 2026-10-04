import json
from lib import *
from PIL import ImageDraw
CREAM = (249, 244, 231); DARK = (40, 44, 54)
def pads(nm):
    a = Image.open(OUT + nm + '.png'); m = json.load(open(OUT + nm + '.json'))['pads']
    return [a.crop((p['x'], p['y'], p['x'] + p['w'], p['y'] + p['h'])) for p in m], m
rows = []
CW = 2400
def row_of(sprites, bg, scale=0.5, label=''):
    sp = [s.resize((int(s.size[0] * scale), int(s.size[1] * scale)), Image.LANCZOS) for s in sprites]
    h = max(s.size[1] for s in sp) + 40
    r = Image.new('RGB', (CW, h), bg); d = ImageDraw.Draw(r)
    x = 20
    for s in sp:
        r.paste(s, (x, 30), s); x += s.size[0] + 14
    d.text((8, 6), label, fill=(120, 110, 100) if bg == CREAM else (200, 200, 210))
    return r
for nm in ('pads_momiji', 'pads_kuromatsu', 'pads_yamazakura'):
    sp, m = pads(nm)
    rows.append(row_of(sp, CREAM, 0.5, nm + ' (x0.5) on cream: ' + ' '.join(p['name'] + p['size'] for p in m)))
    rows.append(row_of(sp, DARK, 0.5, nm + ' on dark'))
# bark 3x3 + washi + pot/soil composite
blk = Image.new('RGB', (CW, 820), CREAM); d = ImageDraw.Draw(blk)
for k, nm in enumerate(('bark_trunk', 'bark_young')):
    b = Image.open(OUT + nm + '.png'); t = Image.new('RGB', (1536, 1536))
    for i in range(3):
        for j in range(3): t.paste(b, (i * 512, j * 512))
    blk.paste(t.resize((384, 384), Image.LANCZOS), (20 + k * 400, 30)); d.text((20 + k * 400, 10), nm + ' 3x3 (x0.25)', fill=(90, 80, 70))
    blk.paste(b.crop((0, 0, 256, 256)).resize((384, 384), Image.NEAREST) if False else t.crop((384, 384, 768, 768)), (20 + k * 400, 430)); d.text((20 + k * 400, 418), nm + ' 1:1 across seam', fill=(90, 80, 70))
w = Image.open(OUT + 'washi_bg.png'); wt = Image.new('RGB', (2048, 2048))
for i in range(2):
    for j in range(2): wt.paste(w, (i * 1024, j * 1024))
blk.paste(wt.crop((512, 512, 1536, 1536)).resize((400, 400)), (820, 30)); d.text((820, 10), 'washi_bg 2x2 seam crossing (x0.4)', fill=(90, 80, 70))
# pot + soil composite on washi
pot = Image.open(OUT + 'pot_tokoname.png'); pm = json.load(open(OUT + 'pot_tokoname.json'))
for k, sn in enumerate(('soil_moist', 'soil_dry')):
    soil = Image.open(OUT + sn + '.png'); sj = json.load(open(OUT + 'soil.json'))
    comp = Image.new('RGBA', (pot.size[0] + 40, pot.size[1] + 40), CREAM + (255,))
    ph = pot.size[1]
    top_y = 20 + (ph - pm['innerTopY'])          # pot opening top in canvas coords
    sx = 20 + (pm['innerLeft'] + pm['innerRight']) // 2 - soil.size[0] // 2
    comp.alpha_composite(soil, (sx, top_y - sj['groundFromTop']))
    comp.alpha_composite(pot, (20, 20))
    c2 = comp.convert('RGB').resize((comp.size[0] // 2, comp.size[1] // 2), Image.LANCZOS)
    blk.paste(c2, (1250 + 0, 30 + k * 260)); d.text((1250, 18 + k * 260), 'pot + ' + sn + ' (x0.5)', fill=(90, 80, 70))
# pot on dark
pdk = Image.new('RGB', pot.size, DARK); pdk.paste(pot, (0, 0), pot)
blk.paste(pdk.resize((pot.size[0] // 2, pot.size[1] // 2), Image.LANCZOS), (1250, 560)); d.text((1250, 548), 'pot on dark (halo check)', fill=(90, 80, 70))
rows.append(blk)
H = sum(r.size[1] for r in rows)
out = Image.new('RGB', (CW, H)); y = 0
for r in rows:
    out.paste(r, (0, y)); y += r.size[1]
out.save(SCR + 'assets_contact.png')
print(out.size)
