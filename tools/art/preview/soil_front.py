"""PA-102Q (TA): SoilFront band numbers for soil.json (BALANCE 1.4.2: opaque moss above the ground line <= 2.5 units
in the root zone |x| <= 35 units, pot P = 92 units). Measures soil_moist/soil_dry (alpha >= 128) and patches soil.json.
Run after build_soil.py (it calls this) or alone: python3 soil_front.py
"""
import json
from lib import *

POT_UNITS = 92.0          # TreeRenderConfig.PotWidthUnits (BALANCE 1.3.0)
SOIL_WIDTH_RATIO = 1.02   # TexturedStyleConfig.SoilWidthRatio
ZONE_UNITS = 35.0         # root zone half-width
BELOW_UNITS = 2.2         # recommended opaque band below the ground line (hides the trunk mesh cut + buried root tips)


def measure():
    pot = Image.open(OUT + 'pot_tokoname.png').getchannel('A'); A = pot.load(); W, H = pot.size
    cols = [x for x in range(W) if any(A[x, y] > 128 for y in range(0, H, 2))]
    upp = POT_UNITS / (cols[-1] + 1 - cols[0])
    meta = json.load(open(OUT + 'soil.json'))
    su = max(upp, POT_UNITS * SOIL_WIDTH_RATIO / meta['width'])       # TexturedPreviewView: soilUpp
    g = meta['groundFromTop']; half = int(round(ZONE_UNITS / su))
    crest = 0
    for f in ('soil_moist.png', 'soil_dry.png'):
        al = Image.open(OUT + f).getchannel('A'); P = al.load(); w, h = al.size; c = w // 2
        for x in range(c - half, c + half):
            for y in range(h):
                if P[x, y] >= 128:
                    crest = max(crest, g - y); break
    below = int(round(BELOW_UNITS / su))
    meta.update({
        "unitsPerPx": round(su, 5),
        "frontZoneHalfWidthPx": half,
        "frontMossAbovePx": crest,
        "frontMossAboveUnits": round(crest * su, 2),
        "frontBandBelowPx": below,
        "frontBandBelowUnits": round(below * su, 2),
        "frontBandDepthShare": round(below / float(meta['height']), 3),
        "unitsPerPxAtPotScale": round(upp, 5),
        "frontMossAboveUnitsAtPotScale": round(crest * upp, 2),
        "frontBandBelowUnitsAtPotScale": round(below * upp, 2),
        "frontNote": "PA-102Q SoilFront: in |x| <= frontZoneHalfWidthPx (35 units at pot 92) the opaque moss (alpha >= 0.5) rises at most frontMossAbovePx above the ground line (frontMossAboveUnits, BALANCE 1.4.2 limit 2.5). Recommended band below the ground line: frontBandBelowPx (= TexturedStyleConfig.SoilFrontDepthShare frontBandDepthShare; PA-103S default 0.3 = 60 px = 5.5 units). unitsPerPx assumes PotWidthUnits 92 and SoilWidthRatio 1.02 (strip drawn 1.15x wider than the pot pixels it was cut for); *AtPotScale = SoilWidthRatio <= 0.886 (strip at pot pixel scale, fits the opening as designed).",
    })
    with open(OUT + 'soil.json', 'w') as fh:
        json.dump(meta, fh, indent=2)
    return meta


if __name__ == '__main__':
    print(json.dumps(measure(), indent=2))
