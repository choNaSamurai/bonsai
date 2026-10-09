# PA-102X-S phase 2a K11: device screenshots (Vulkan / GLES3) vs editor (Metal) renders and vs each other.
# usage: python3 -I compare_k11.py <pa102xs workspace>
import sys, os
from PIL import Image
ws = sys.argv[1]
def load(p):
    return Image.open(p).convert('RGBA') if os.path.exists(p) else None
def cmp(a, b):
    A = a.load(); B = b.load(); w, h = a.size
    inter = uni = 0; n = 0; s = 0; big = 0
    for y in range(h):
        for x in range(w):
            pa = A[x, y]; pb = B[x, y]
            ia = pa[3] >= 128; ib = pb[3] >= 128
            if ia and ib: inter += 1
            if ia or ib: uni += 1
            if ia and ib:
                d = max(abs(pa[0]-pb[0]), abs(pa[1]-pb[1]), abs(pa[2]-pb[2]))
                s += d; n += 1
                if d > 16: big += 1
    return inter / max(uni, 1), s / max(n, 1), 100.0 * big / max(n, 1)
keys = ['momiji_moyogi_s3_t100', 'kuromatsu_moyogi_s3_t100', 'yamazakura_moyogi_s3_t100']
print('| Знімок | Порівняння | IoU альфи | сер. \\|ΔRGB\\| | пікселів з \\|Δ\\| > 16 |')
print('|---|---|---|---|---|')
for k in keys + [k + '_budget' for k in keys[:2]]:
    base = k.replace('_budget', '')
    ed = load(os.path.join(ws, 'unity', 'budget' if k.endswith('_budget') else 'p2a_regress', base + '.png'))
    vk = load(os.path.join(ws, 'device', 'vulkan', 'pa102xs', 'shot_' + k + '_Vulkan.png'))
    gl = load(os.path.join(ws, 'device', 'gles3', 'pa102xs', 'shot_' + k + '_OpenGLES3.png'))
    for name, a, b in (('Vulkan vs редактор (Metal)', vk, ed), ('GLES3 vs редактор (Metal)', gl, ed), ('Vulkan vs GLES3', vk, gl)):
        if a is None or b is None: print(f'| {k} | {name} | — | — | — |'); continue
        iou, mad, big = cmp(a, b)
        print(f'| {k} | {name} | {iou:.4f} | {mad:.2f} | {big:.2f}% |')
