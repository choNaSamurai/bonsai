"""PA-102T batch 02 review sheets -> $ART_SCRATCH/batch02/ (default Temp/art-preview/batch02/).
usage: python3 collage_batch02.py [pads] [bark] [pots]   (no args = all)"""
import sys, json, os
from lib import *
from PIL import ImageDraw
CREAM = (249, 244, 231); DARK = (40, 44, 54)
D = SCR + 'batch02/'

def on(bg, im):
    c = Image.new('RGBA', im.size, bg + (255,)); c.alpha_composite(im); return c.convert('RGB')

def pads_sheet(names):
    rows = []
    for nm in names:
        if not os.path.exists(OUT + 'pads_' + nm + '.png'): continue
        a = Image.open(OUT + 'pads_' + nm + '.png'); m = json.load(open(OUT + 'pads_' + nm + '.json'))['pads']
        sp = [(a.crop((p['x'], p['y'], p['x'] + p['w'], p['y'] + p['h'])), p) for p in m]
        h = max(s.size[1] for s, _ in sp) + 44; W = sum(s.size[0] + 10 for s, _ in sp) + 20
        for bg in (CREAM, DARK):
            r = Image.new('RGB', (max(W, 900), h), bg); d = ImageDraw.Draw(r); x = 10
            for s, p in sp:
                r.paste(on(bg, s), (x, 34))
                # rect outline + pivot
                d.rectangle((x, 34, x + s.size[0] - 1, 34 + s.size[1] - 1), outline=(150, 150, 150))
                px = x + p['pivotX'] * p['w']; py = 34 + (1 - p['pivotY']) * p['h']
                d.ellipse((px - 4, py - 4, px + 4, py + 4), outline=(255, 60, 60))
                d.text((x + 2, 20), '%s %s%s %dx%d' % (p['name'], p['size'], ' dome' if p.get('role') == 'dome' else '', p['innerW'], p['innerH']),
                       fill=(90, 80, 70) if bg == CREAM else (210, 210, 220))
                x += s.size[0] + 10
            d.text((8, 4), 'pads_%s  %dx%d  %.2f MB' % (nm, a.size[0], a.size[1], os.path.getsize(OUT + 'pads_' + nm + '.png') / 1e6),
                   fill=(90, 80, 70) if bg == CREAM else (210, 210, 220))
            rows.append(r)
    W = max(r.size[0] for r in rows); H = sum(r.size[1] for r in rows)
    out = Image.new('RGB', (W, H), (255, 255, 255)); y = 0
    for r in rows: out.paste(r, (0, y)); y += r.size[1]
    out.save(D + 'pads_contact.png'); print('pads_contact', out.size)
    # edge zoom: 3x nearest crops of the top edge of each dome on dark (halo check)
    zs = []
    for nm in names:
        if not os.path.exists(OUT + 'pads_' + nm + '.json'): continue
        a = Image.open(OUT + 'pads_' + nm + '.png'); p = json.load(open(OUT + 'pads_' + nm + '.json'))['pads'][0]
        s = a.crop((p['x'], p['y'], p['x'] + p['w'], p['y'] + p['h']))
        c = s.crop((s.size[0] // 2 - 60, 0, s.size[0] // 2 + 60, 80))
        zs.append(on(DARK, c).resize((360, 240), Image.NEAREST)); zs.append(on((255, 255, 255), c).resize((360, 240), Image.NEAREST))
    z = Image.new('RGB', (360 * len(zs), 240))
    for i, c in enumerate(zs): z.paste(c, (i * 360, 0))
    z.save(D + 'pads_edge_zoom.png')

def bark_sheet():
    sp = ['kuromatsu', 'momiji', 'yamazakura']
    T = 300
    blk = Image.new('RGB', (3 * (3 * T + 20) + 20, 2 * (3 * T + 40) + 40), CREAM); d = ImageDraw.Draw(blk)
    for i, s in enumerate(sp):
        for j, k in enumerate(('trunk', 'young')):
            f = OUT + 'bark_%s_%s.png' % (s, k)
            if not os.path.exists(f): continue
            b = Image.open(f).convert('RGB').resize((T, T), Image.LANCZOS)
            x0 = 20 + i * (3 * T + 20); y0 = 30 + j * (3 * T + 40)
            for u in range(3):
                for v in range(3): blk.paste(b, (x0 + u * T, y0 + v * T))
            d.text((x0, y0 - 18), 'bark_%s_%s  3x3  %d px  %.2f MB' % (s, k, Image.open(f).size[0], os.path.getsize(f) / 1e6), fill=(90, 80, 70))
    blk.save(D + 'bark_3x3.png'); print('bark_3x3', blk.size)
    # before/after: source (1024 -> 300) vs runtime, single tile
    ba = Image.new('RGB', (6 * (T + 10) + 10, 2 * (T + 30) + 10), CREAM); d = ImageDraw.Draw(ba)
    for i, s in enumerate(sp):
        for j, k in enumerate(('trunk', 'young')):
            src = Image.open(SRC + 'B_Textures/bark_%s_%s.png' % (s, k)).convert('RGB').resize((T, T), Image.LANCZOS)
            x = 10 + (i * 2 + j) * (T + 10)
            ba.paste(src, (x, 25)); d.text((x, 8), 'src %s_%s' % (s, k), fill=(90, 80, 70))
            f = OUT + 'bark_%s_%s.png' % (s, k)
            if os.path.exists(f):
                ba.paste(Image.open(f).convert('RGB').resize((T, T), Image.LANCZOS), (x, T + 55)); d.text((x, T + 38), 'runtime', fill=(90, 80, 70))
    ba.save(D + 'bark_before_after.png')

def pots_sheet():
    rows = []
    for nm in ('pot_tokoname', 'pot_tokoname_v2', 'pot_kuro', 'pot_celadon'):
        f = OUT + nm + '.png'
        if not os.path.exists(f): continue
        pot = Image.open(f); pm = json.load(open(OUT + nm + '.json'))
        soil = Image.open(OUT + 'soil_moist.png'); sj = json.load(open(OUT + 'soil.json'))
        W, H = pot.size
        c = Image.new('RGBA', (W + 40, H + 140), CREAM + (255,))
        if pm['innerTopY'] > pm['lipY']:
            gy = pm['innerTopY']; sw = pm['innerRight'] - pm['innerLeft'] + 24
        else:
            gy = pm.get('groundY', pm['lipY']); sw = pm['innerRight'] - pm['innerLeft']
        s2 = soil.resize((int(sw), int(soil.size[1] * sw / soil.size[0])), Image.LANCZOS)
        g2 = sj['groundFromTop'] * sw / soil.size[0]
        cx = 20 + (pm['innerLeft'] + pm['innerRight']) / 2
        top = 120 + (H - gy)
        if 'soilMaxBelowLipPx' in pm:
            s2 = s2.crop((0, 0, s2.size[0], int(g2 + pm['soilMaxBelowLipPx'])))
        c.alpha_composite(s2, (int(cx - s2.size[0] / 2), int(top - g2)))
        c.alpha_composite(pot, (20, 120))
        d = ImageDraw.Draw(c)
        ly = 120 + H - pm['lipY']
        d.line((20 + pm['innerLeft'], ly, 20 + pm['innerRight'], ly), fill=(255, 40, 40), width=2)
        if 'groundY' in pm:
            d.line((20 + pm['innerLeft'], 120 + H - pm['groundY'], 20 + pm['innerRight'], 120 + H - pm['groundY']), fill=(40, 120, 255), width=2)
        d.text((24, 8), '%s %dx%d %.2f MB  red = lipY / innerLeft..innerRight, blue = groundY' % (nm, W, H, os.path.getsize(f) / 1e6), fill=(90, 80, 70))
        dk = on(DARK, pot)
        r = Image.new('RGB', (c.size[0] + dk.size[0] + 20, c.size[1]), CREAM)
        r.paste(c.convert('RGB'), (0, 0)); r.paste(dk, (c.size[0] + 10, 120))
        rows.append(r.resize((r.size[0] // 2, r.size[1] // 2), Image.LANCZOS))
    W = max(r.size[0] for r in rows); H = sum(r.size[1] for r in rows)
    out = Image.new('RGB', (W, H), CREAM); y = 0
    for r in rows: out.paste(r, (0, y)); y += r.size[1]
    out.save(D + 'pots_contact.png'); print('pots_contact', out.size)

if __name__ == '__main__':
    os.makedirs(D, exist_ok=True)
    what = set(sys.argv[1:]) or {'pads', 'bark', 'pots'}
    if 'pads' in what: pads_sheet(['momiji', 'kuromatsu', 'yamazakura', 'momiji_autumn', 'yamazakura_bloom'])
    if 'bark' in what: bark_sheet()
    if 'pots' in what: pots_sheet()
