# PA-102X-S — інструменти спайку (гілка `spike/PA-102X-S-render`, не мержиться)

Спайк рендеру ескізу в Unity (ADR-0011, варіант B\*). Висновки — у `docs/qa/reports/PA-102X-S-phase1-2026-10-09.md` і `docs/tasks/PA-102X-S.md` (гілка `feature/PA-102-tree-domain`).

## Що тут
| Файл | Призначення |
|---|---|
| `sketch_v11.html` | Ескіз-еталон (WebGL2, відкривати в браузері). `sketch_v11.fragment.html` — той самий код без обгортки сторінки |
| `export.js` | `exportAll(cfgs)` — вивантажує golden-дані (`<key>.json` + `.bin`) і еталонні PNG 1360×960 RGBA з ескізу |
| `save_server.py` | Локальний сервер для `export.js`: `python3 save_server.py <тека з html> <тека виводу>` (порт 8766, лише 127.0.0.1) |
| `unity-run.sh` | Запуск Unity в batchmode на копії проєкту з блокуванням (один Unity за раз): `compile` / `edit [filter]` / `exec <Class.Method>` |

Код спайку — `Assets/_Project/Spikes/PA102XS/`:
- `TreeGen/` — C#-порт генератора ескізу (`SketchGenerator`, контракт `SketchScene`, `GoldenLoader`), без UnityEngine;
- `Tests/` — golden-тести (90 конфігурацій, f32-масиви біт у біт);
- `Render/`, `Shaders/` — меш деревини B\* з проєкцією на SDF (`smin` ескізу), запікання світла per-vertex, листя, горщик (Surface Nets);
- `Editor/` — харнес `PocketArbor.Spike.Editor.SpikeHarness.Run` (PNG, IoU, аркуші, тест глибини URP 2D);
- `Scenes/PA102XS_Spike.unity` — сцена з вибором породи, стилю, насіння, віку.

## Відтворення
1. Golden-дані: зібрати сторінку = `sketch_v11.fragment.html` + `<script src="export.js">`, підняти `save_server.py`, у консолі сторінки `exportAll([...])` (конфігурації: порода × стиль × насіння × t). Golden-тести шукають дані в `PA102XS_GOLDEN` (за замовчуванням — `.agent-workspace/pa102xs/golden`).
2. Тести: `unity-run.sh edit PocketArbor.Spike`.
3. Аркуші й IoU: `unity-run.sh exec PocketArbor.Spike.Editor.SpikeHarness.Run` (з GPU; виводи — `.agent-workspace/pa102xs/unity/`).

Golden-дані (~19 МБ) і еталонні PNG (~25 МБ) у git не входять — їх відтворює крок 1.
