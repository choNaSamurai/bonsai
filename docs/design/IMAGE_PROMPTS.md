# Image Prompts — Nano Banana (Antigravity)

> Власник: Technical Artist · Підготовлено оркестратором 2026-10-04 · Статус: Draft
> Стиль і кольори — з [DESIGN_SYSTEM_AND_UI_SPEC](../reference/flutter-v3.2/DESIGN_SYSTEM_AND_UI_SPEC.md) §1, §3. Промпти англійською — так генератор працює точніше.

## Як користуватися

1. Спочатку згенеруй **розділ A (концепт-арт)** — він задає візуальну ціль. Обери 1–2 найкращі.
2. Для розділів B–D додавай обраний концепт як **референсне зображення** («match the style of the reference image») — так асети будуть в одному стилі.
3. Кожен промпт генеруй 3–4 варіанти, зберігай найкращий.
4. Генератори зазвичай **не дають справжньої прозорості**. Тому для спрайтів просимо **однотонний фон `#FF00FF` (magenta)** — Technical Artist вирізає його при імпорті. Якщо Antigravity вміє «transparent background» — краще так.
5. Куди класти: `Assets/_Project/Art/Source/<розділ>/` (сирі джерела, Git LFS). Назви — як у колонці «Файл».
6. Що **не** генеруємо картинками: саме дерево (воно процедурне, малюється кодом), UI-іконки (робимо SVG-векторами — генератор дає растр, а іконкам потрібна чіткість на всіх екранах), печатку Hanko для QR (теж SVG).

**Спільний стильовий блок** (додавай у кінець кожного промпту розділів B–D):

```
Style: Wabi-Sabi meets Japandi, calm and minimal, soft natural daylight, matte organic materials, subtle hand-made imperfection, muted earthy palette, no text, no logos, no watermark, no emoji, no people.
```

---

## A. Концепт-арт (візуальна ціль, у гру не йде)

| # | Файл | Розмір |
|---|---|---|
| A1 | `concept_day.png` | 1080×1920 (портрет) |
| A2 | `concept_night.png` | 1080×1920 |
| A3 | `concept_dormancy.png` | 1080×1920 |

**A1 — день**
```
Mobile game screen concept art, portrait 9:16. A single small bonsai tree (Japanese maple, Acer palmatum) in a shallow unglazed terracotta Tokoname pot (#C17753), seen straight from the side, centered in the lower two thirds of the frame. Generous negative space around the tree (Japanese "Ma" concept). Background: soft vertical gradient from pale lake blue #C4DAE8 at the top to warm washi paper white #F5F7F2 at the bottom, with a barely visible handmade washi paper fiber texture. Tree: gracefully curved trunk with visible nebari roots, asymmetric layered crown, fresh green leaves #5C8A42, dark bark #3E2723, a small pale callus scar on one cut branch. Dark moist soil with a patch of emerald moss #4A7A44. At the bottom, a small floating frosted-glass toolbar pill with four thin line-art icons (pruning shears, copper wire coil, watering can, mist sprayer). Calm, meditative, cozy, flat-shaded 2D illustration with soft painterly texture, not 3D, not photorealistic.
```

**A2 — ніч**
```
Same composition as the reference image, but at civil night: background gradient from deep midnight indigo #1A1F2B at the top to dark sapphire #2C3545 at the bottom, warm dim ambient light, no cold blue glare. 12–16 small soft glowing fireflies drifting around the crown, warm yellow-green bokeh. Japanese black pine (Kuromatsu) bonsai with dense dark needle clusters #2E4F2D in a matte graphite-black Kuro pot #2C2C2E. Peaceful, sleepy, cozy mood. Flat-shaded 2D illustration, no UI.
```

**A3 — сплячка (Dormancy)**
```
Same composition as the reference image. A dormant bonsai that has been resting for weeks: a few leaves dropped onto the soil, the base of the trunk and the soil covered in soft sleeping moss #6B7355, slightly pale dry soil #8C8275 with fine cracks, muted desaturated colors, gentle morning light #E0EAF5 → #FDFBF7. The tree looks peacefully asleep, not dead or sad — calm and patient, waiting. Flat-shaded 2D illustration, no UI.
```

---

## B. Фон і текстури (йдуть у гру)

| # | Файл | Розмір | Примітка |
|---|---|---|---|
| B1 | `washi_paper_tile.png` | 1024×1024 | безшовна (tileable) |
| B2 | `bark_tile.png` | 512×512 | безшовна, для мешу гілок |
| B3 | `soil_moist.png` / `soil_dry.png` | 1024×256 | смуга ґрунту, вид збоку |

**B1 — папір васі**
```
Seamless tileable texture, top-down flat scan of handmade Japanese washi paper, warm off-white #F5F7F2, very subtle long plant fibers and slight uneven thickness, extremely low contrast so it can sit behind a game scene, even lighting, no shadows, no vignette, no folds.
```

**B2 — кора**
```
Seamless tileable texture of mature bonsai bark, dark oak brown #3E2723 with slightly lighter #5D4037 ridges, fine vertical furrows, soft and stylized rather than photographic, even flat lighting, no shadows, suitable for wrapping along thin procedural branches.
```

**B3 — ґрунт (два варіанти з однаковою композицією)**
```
Side-view horizontal strip of bonsai pot soil surface, 4:1 aspect ratio, fine granular akadama soil with tiny pebbles, small patches of emerald moss #4A7A44 on the left third. Variant 1 "moist": deep peat brown #4A3525, slightly glossy. Variant 2 "dry": pale grey loam #8C8275 with thin hairline cracks, matte. Flat even lighting, solid magenta #FF00FF background above the soil line.
```

---

## C. Горщики (спрайти, вид суворо збоку)

| # | Файл | Розмір | Монетизація |
|---|---|---|---|
| C1 | `pot_tokoname.png` | 1024×512 | безкоштовний |
| C2 | `pot_kuro.png` | 1024×512 | IAP $2.99 |
| C3 | `pot_celadon.png` | 1024×512 | IAP $2.99 |

**Базовий промпт** (міняється лише рядок про матеріал):
```
Game sprite of a shallow rectangular bonsai pot, perfectly orthographic straight side view, no perspective, no top surface visible, centered, filling 80% of the width, short feet at the bottom corners, slightly rounded rim lip. [MATERIAL]. Soft even studio lighting, very subtle contact shadow only under the feet, solid flat magenta #FF00FF background, nothing else in the frame.
```

- **C1 [MATERIAL]:** `Unglazed Tokoname terracotta clay #C17753, porous matte texture with tiny kiln speckles`
- **C2 [MATERIAL]:** `Kuro graphite-black stoneware #2C2C2E, silky satin finish, faint warm highlights`
- **C3 [MATERIAL]:** `Translucent celadon jade glaze #9EBC9E with a delicate crackle network inside the glaze`

---

## D. Листя, квіти, бруньки (атлас для інстансингу)

Генеруємо **листом** — кілька однакових за стилем елементів на одному полотні, потім TA ріже на атлас. Кожен елемент окремо, з відступом, без перекриття.

| # | Файл | Вміст |
|---|---|---|
| D1 | `sheet_momiji.png` | клен: 4 листки зелені `#5C8A42`, 4 охра `#D98A3C`, 4 багряні `#A33B29` |
| D2 | `sheet_kuromatsu.png` | сосна: 6 пучків хвої `#2E4F2D`, 3 весняні «свічки» `#9CCC65` |
| D3 | `sheet_yamazakura.png` | сакура: 4 листки `#6A994E`, 4 квітки `#FFB7C5` з серцевиною `#F48FB1`, 3 пуп'янки |
| D4 | `sheet_buds.png` | 6 бруньок різного ступеня розкриття, 3 калюси зрізу `#8D6E63` |

**Базовий промпт:**
```
Sprite sheet on a solid flat magenta #FF00FF background: [CONTENT], each element isolated with generous spacing, no overlaps, arranged in a neat grid, each element seen flat from the front, soft simple shading, clean silhouette suitable for small in-game sprites (readable at 32×32 px), consistent stylized 2D look matching the reference image, no shadows on the background.
```

- **D1 [CONTENT]:** `12 Japanese maple (Acer palmatum) leaves with 5–7 slender pointed lobes: 4 fresh green #5C8A42, 4 golden ochre #D98A3C, 4 deep crimson #A33B29`
- **D2 [CONTENT]:** `6 Japanese black pine needle clusters (dense fans of dark green needles #2E4F2D) and 3 pale spring candles #9CCC65`
- **D3 [CONTENT]:** `4 wild cherry (Yamazakura) leaves #6A994E, 4 open five-petal cherry blossoms in powder pink #FFB7C5 with deeper pink centers #F48FB1, 3 closed pink flower buds`
- **D4 [CONTENT]:** `6 small tree buds from tightly closed to just opening (green-brown), and 3 round healed pruning-cut callus scars #8D6E63 seen face-on`

---

## E. Іконка застосунку (концепти)

| # | Файл | Розмір |
|---|---|---|
| E1 | `appicon_concept.png` | 1024×1024 |

```
iOS app icon concept, 1024×1024, full-bleed square (no rounded corners — the OS applies the mask), no text. A tiny stylized bonsai tree in a shallow terracotta pot, centered, simple bold silhouette readable at 60×60 px, on a soft warm washi-white to pale lake-blue gradient background. Calm Wabi-Sabi Japandi style, flat 2D with gentle soft shading, minimal details, no border, no glossy effects.
```

---

## Після генерації

Скинь обрані файли в `Assets/_Project/Art/Source/` (або просто напиши, де вони лежать). Technical Artist:
- вирізає magenta-фон, ріже листи на атлас, перевіряє безшовність текстур;
- налаштовує імпорт (ASTC, mip-maps для листя, спрайт-атласи) і рахує вагу в бюджеті розміру;
- записує джерело і ліцензію (AI-генерація, Nano Banana / Antigravity) у TECH_DECISIONS.

> ⚠ Перед комерційним релізом перевір умови використання згенерованих зображень у твоєму тарифі Google/Antigravity.
