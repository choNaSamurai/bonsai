# Preview art pipeline (PA-103S)

Python 3 + Pillow scripts that derive the dev textures in `Assets/_Project/Art/Runtime/Preview/`
from the generated sources in `Assets/_Project/Art/Source/` (L2 reference crops, B/C generations).

| Script | Output |
|---|---|
| `build_pads.py` | `pads_{momiji,kuromatsu,yamazakura}.png/.json` |
| `build_bark.py` | `bark_trunk.png`, `bark_young.png` |
| `build_pot.py` | `pot_tokoname.png/.json` |
| `build_soil.py` | `soil_moist.png`, `soil_dry.png`, `soil.json` |
| `build_washi.py` | `washi_bg.png` |
| `collage.py` | contact sheet for review (written to `$ART_SCRATCH`, default `Temp/art-preview/`) |

Run from this folder: `python3 build_pads.py` etc. Re-run when D5 / B2 v2 / B3 v2 / C v2 arrive from
Antigravity (see `docs/design/IMAGE_PROMPTS.md`) — crop coordinates inside the scripts are tuned to the
current 768×1376 references and must be revisited for new sources.
