from bark import *
import random
from PIL import ImageDraw, ImageStat
segs = [(840, 921, False, 0, 290, 390), (935, 1031, True, 0, 295, 445), (604, 652, False, 0, 330, 415), (935, 1031, False, -1, 295, 445)]
cols = []
for (a, b, flip, roll, xa, xb) in segs:
    im = flatten(unwrap(a, b, xa=xa, xb=xb))
    if flip: im = im.transpose(Image.FLIP_LEFT_RIGHT)
    if roll == -1: im = im.transpose(Image.FLIP_TOP_BOTTOM)
    im = im.resize((150, 512), Image.LANCZOS)
    st = ImageStat.Stat(im)
    cols.append((im, st.mean))
tgt = [sum(c[1][k] for c in cols) / len(cols) for k in range(3)]
norm = []
for im, mn in cols:
    norm.append(Image.merge('RGB', [ch.point(lambda v, k=tgt[i] / mn[i]: int(clamp(v * k, 0, 255))) for i, ch in enumerate(im.split())]))
cols = norm
# assemble with 22px cross-fades -> 4*150 - 4*22 = 512 (wraps horizontally)
W = 512; H = 512; ov = 22
canvas = Image.new('RGB', (W, H)); acc = [[[0.0, 0.0, 0.0, 0.0] for _ in range(W)] for _ in range(H)]
x = 0
for im in cols:
    p = im.load(); w = im.size[0]
    for xx in range(w):
        wt = min(1.0, (xx + 0.5) / ov, (w - xx - 0.5) / ov)
        X = (x + xx) % W
        for y in range(H):
            c = p[xx, y]; a = acc[y][X]
            a[0] += c[0] * wt; a[1] += c[1] * wt; a[2] += c[2] * wt; a[3] += wt
    x += w - ov
cp = canvas.load()
for y in range(H):
    for X in range(W):
        a = acc[y][X]; t = a[3] or 1
        cp[X, y] = (int(a[0] / t), int(a[1] / t), int(a[2] / t))
base = make_seamless_v(canvas)
base.save(SCR + 'bark_base.png')

# ---- detail pass: painterly streaks + thin ink strokes (all tileable by construction)
def detail(base, seed, streak_amp, ink_count, ink_alpha, ink_col, hi_count, hi_alpha, hi_col):
    W, H = base.size
    st = streaks(W, H, seed, n=22, amp=streak_amp)
    out = base.copy(); o = out.load()
    for y in range(H):
        for x in range(W):
            k = 1 + st[y][x]
            r, g, b_ = o[x, y]
            o[x, y] = (int(clamp(r * k, 0, 255)), int(clamp(g * k, 0, 255)), int(clamp(b_ * k, 0, 255)))
    ink = ink_lines(W, H, seed + 1, ink_count, ink_col, (0.8, 1.8), ink_alpha)
    hi = ink_lines(W, H, seed + 2, hi_count, hi_col, (0.8, 1.6), hi_alpha, length=(0.15, 0.4))
    out = out.convert('RGBA'); out.alpha_composite(hi); out.alpha_composite(ink)
    return out.convert('RGB')

def grade(im, mean_t, std_scale):
    st = ImageStat.Stat(im); chans = []
    for i, ch in enumerate(im.split()):
        m = st.mean[i]
        chans.append(ch.point(lambda v, m=m, t=mean_t[i]: int(clamp(t + (v - m) * std_scale, 0, 255))))
    return Image.merge('RGB', chans)

# reference trunk colour (interior of lower trunk)
ref = l2.crop((345, 945, 395, 1025)); rs = ImageStat.Stat(ref)
print('ref trunk mean', [round(v) for v in rs.mean], 'std', [round(v) for v in rs.stddev])
from PIL import ImageEnhance

# ---- trunk (PA-102P): flat painterly base + twisted lit ridges / dark furrows + long ink lines, like the L2 trunk.
# Base from the reference is only low-frequency colour now (σ 5) — the old ×7 upscale read as blur.
tb = grade(wrap_blur(base, 5), rs.mean, 0.75)
ridge, furrow, mod = twisted_field(512, 512, 31, warp_amp=0.5)
trunk = apply_field(tb, ridge, furrow, mod, light=0.5, dark=0.32, hi_col=(172, 136, 94))
trunk = trunk.convert('RGBA')
trunk.alpha_composite(twisted_strokes(512, 512, 41, 12, (192, 158, 112), (4.0, 8.0), 80, length=(0.4, 0.9), wobble=(1, 4), blur=2.0))  # light ridge strokes
trunk.alpha_composite(twisted_strokes(512, 512, 42, 10, (52, 34, 22), (4.0, 7.0), 55, length=(0.3, 0.7), wobble=(1, 4), blur=2.5))   # soft shadow side
trunk.alpha_composite(twisted_strokes(512, 512, 43, 16, (40, 25, 16), (1.6, 3.0), 210, length=(0.4, 0.95), wobble=(1, 4)))          # ink furrows
trunk = trunk.convert('RGB')
trunk = grade(trunk, rs.mean, 1.0)
trunk = ImageEnhance.Color(trunk).enhance(1.12)
trunk.save(OUT + 'bark_trunk.png', optimize=True)

# ---- young: lighter, warmer, smoother; olive #7A6A4F tint, fine horizontal lenticels, a hint of the same twist.
yb = grade(wrap_blur(base, 6), (118, 92, 70), 0.5)
ridge, furrow, mod = twisted_field(512, 512, 57, k=7, warp_amp=0.8)
young = apply_field(yb, ridge, furrow, mod, light=0.22, dark=0.12, hi_col=(170, 146, 108))
young = young.convert('RGBA')
young.alpha_composite(twisted_strokes(512, 512, 61, 18, (178, 154, 112), (1.8, 3.5), 70, length=(0.25, 0.6), blur=1.0))
young.alpha_composite(twisted_strokes(512, 512, 62, 14, (72, 52, 36), (0.8, 1.4), 140, length=(0.2, 0.55)))
# lenticels: short horizontal dashes, tileable via wrap
rnd = random.Random(5)
lay = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0)); d = ImageDraw.Draw(lay)
for _ in range(80):
    x = rnd.uniform(0, 512); y = rnd.uniform(0, 512); l = rnd.uniform(4, 11); a = rnd.randint(80, 140)
    col = rnd.choice([(158, 136, 102), (84, 64, 46)])
    for dx in (-512, 0, 512):
        for dy in (-512, 0, 512):
            d.line([((x + dx) * 2, (y + dy) * 2), ((x + l + dx) * 2, (y + dy) * 2)], fill=col + (a,), width=2)
lay = lay.resize((512, 512), Image.LANCZOS)
young.alpha_composite(lay); young = young.convert('RGB')
young = Image.blend(young, Image.new('RGB', young.size, (122, 106, 79)), 0.18)
young.save(OUT + 'bark_young.png', optimize=True)
