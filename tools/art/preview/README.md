# Preview art pipeline (PA-103S)

Python 3 + Pillow scripts that derive the dev textures in `Assets/_Project/Art/Runtime/Preview/`
from the generated sources in `Assets/_Project/Art/Source/` (L2 reference crops, B/C generations).

| Script | Output |
|---|---|
| `build_pads.py [momiji] [kuromatsu] [yamazakura]` | `pads_{momiji,kuromatsu,yamazakura}.png/.json` (no args = all; Kuromatsu is painted by `pine.py` since PA-102Q, `KURO_FROM_NIGHT=1` = old night cut-outs) |
| `pine.py` | PA-102Q: procedural Kuromatsu needle-fan shelves (dome / L / M / S), heights grid-fitted to the opaque-ratio targets |
| `build_moss_tuft.py` | PA-102Q: `moss_tuft.png/.json` (3 moss cushions for root tips) |
| `soil_front.py` | PA-102Q: measures the SoilFront moss crest, patches `soil.json` (`front*` fields); also run at the end of `build_soil.py` |
| `preview_nebari.py` | PA-102Q review: roots vs SoilFront band at preview scale (`$ART_SCRATCH/pa102q/nebari_moss_before_after.png`; `SOIL_WIDTH_RATIO=0.88` env to try the pot-scale strip) |
| `build_bark.py` | `bark_trunk.png`, `bark_young.png` |
| `build_pot.py` | `pot_tokoname.png/.json` |
| `build_soil.py` | `soil_moist.png`, `soil_dry.png`, `soil.json` |
| `build_washi.py` | `washi_bg.png` |
| `build_masks.py` | `ref_mask_{l2_momiji,night_kuromatsu}[_foliage].png` (PA-102P IoU fixtures) + виміри (stdout, `$ART_SCRATCH/mask_measurements.json`) і оверлеї `mask_overlay_*.png`, `mask_collage.png` |
| `collage.py` | contact sheet for review (written to `$ART_SCRATCH`, default `Temp/art-preview/`) |

Run from this folder: `python3 build_pads.py` etc. Re-run when D5 / B2 v2 / B3 v2 / C v2 arrive from
Antigravity (see `docs/design/IMAGE_PROMPTS.md`) — crop coordinates inside the scripts are tuned to the
current 768×1376 references and must be revisited for new sources.
