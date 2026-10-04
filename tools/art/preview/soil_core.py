from lib import *
from PIL import ImageStat
sm = Image.open(SRC + 'B_Textures/soil_moist.jpg').convert('RGB')
band = sm.crop((0, 160, 1376, 420))
W, H = band.size; p = band.load()
alpha = Image.new('L', (W, H)); ap = alpha.load()
for y in range(H):
    for x in range(W):
        r, g, b = p[x, y]
        mag = min(r, b) - g   # magenta-ness
        ap[x, y] = int(255 * (1 - smooth(60, 140, mag)))
# despill: where partially magenta, clamp r,b to g-ish
for y in range(H):
    for x in range(W):
        r, g, b = p[x, y]
        if min(r, b) - g > 20:
            m = g + 20
            p[x, y] = (min(r, m + 30), g, min(b, m))
alpha = alpha.filter(ImageFilter.MinFilter(3))
# stylise: reduce detail, painterly
st = band.resize((W // 2, H // 2), Image.LANCZOS).filter(ImageFilter.ModeFilter(3)).filter(ImageFilter.MedianFilter(3))
st = st.resize((W, H), Image.LANCZOS).filter(ImageFilter.UnsharpMask(2, 60, 2))
# colour-match to reference moss soil
l2 = Image.open(SRC + 'Section_0/realism_l2_painterly.jpg').convert('RGB')
ref = l2.crop((150, 1088, 230, 1152)); rs = ImageStat.Stat(ref)
ss = ImageStat.Stat(st, alpha)
chs = []
for i, ch in enumerate(st.split()):
    m = ss.mean[i]; sd = ss.stddev[i] or 1; tm = rs.mean[i]; td = rs.stddev[i]
    chs.append(ch.point(lambda v, m=m, sd=sd, tm=tm, td=td: int(clamp(tm + (v - m) * td / sd * 0.9, 0, 255))))
st = Image.merge('RGB', chs)
from PIL import ImageEnhance
import colorsys
st = ImageEnhance.Color(st).enhance(0.85)
q = st.load()
for y in range(H):
    for x in range(W):
        r, g_, b = [v / 255 for v in q[x, y]]
        h, s, v = colorsys.rgb_to_hsv(r, g_, b)
        if 0.17 < h < 0.42:   # moss: nudge toward the reference's yellow-olive moss
            h = h * 0.6 + 0.20 * 0.4
        r, g_, b = colorsys.hsv_to_rgb(h, s, v)
        q[x, y] = (int(r * 255), int(g_ * 255), int(b * 255))
st.putalpha(alpha)
FW = 1056; FH = int(round(H * FW / W))
moist = st.resize((FW, FH), Image.LANCZOS)
# bleed colour under transparent texels
a = moist.getchannel('A')
bled = push_pull(moist.convert('RGB'), a.point(lambda v: 255 if v > 200 else 0))
bled.putalpha(a)
moist = bled

