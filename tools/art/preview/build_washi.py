from lib import *
from PIL import ImageStat
w = Image.open(SRC + 'B_Textures/washi_paper_tile.jpg').convert('RGB')
W, H = w.size
def seam_axis(im, axis):
    off = ImageChops.offset(im, W // 2 if axis == 0 else 0, H // 2 if axis == 1 else 0)
    m = Image.new('L', (W, H)); mp = m.load()
    for y in range(H):
        for x in range(W):
            d = abs((x if axis == 0 else y) - W / 2) / (W / 2)
            mp[x, y] = int(255 * smooth(0.5, 0.97, d))
    return Image.composite(off, im, m)
# check native seam
p = w.load()
dx = sum(sum(abs(a - b) for a, b in zip(p[0, y], p[W - 1, y])) for y in range(H)) / H
dy = sum(sum(abs(a - b) for a, b in zip(p[x, 0], p[x, H - 1])) for x in range(W)) / W
print('native edge diff', dx, dy)
# remove the large soft vertical light bands (low frequency) before tiling: high-pass + flat tone
low = w.filter(ImageFilter.GaussianBlur(60))
st = ImageStat.Stat(w)
hp = Image.new('RGB', (W, H)); hq = hp.load(); lq = low.load()
TGT = (249, 244, 231)    # reference background cream
for y in range(H):
    for x in range(W):
        c = p[x, y]; l = lq[x, y]
        hq[x, y] = tuple(int(clamp(TGT[i] + (c[i] - l[i]) * 0.55, 0, 255)) for i in range(3))
t = seam_axis(seam_axis(hp, 0), 1)
# wrap-aware median denoise (kills JPEG grain, keeps fibres; also ~2.4x smaller PNG)
big = Image.new('RGB', (W * 3, H * 3))
for i in range(3):
    for j in range(3): big.paste(t, (i * W, j * H))
t = big.crop((W - 8, H - 8, 2 * W + 8, 2 * H + 8)).filter(ImageFilter.MedianFilter(5)).crop((8, 8, W + 8, H + 8))
t.save(OUT + 'washi_bg.png', optimize=True)
q = t.load()
dx = sum(sum(abs(a - b) for a, b in zip(q[0, y], q[W - 1, y])) for y in range(H)) / H
dy = sum(sum(abs(a - b) for a, b in zip(q[x, 0], q[x, H - 1])) for x in range(W)) / W
print('result edge diff', dx, dy, ImageStat.Stat(t).mean, ImageStat.Stat(t).stddev)
