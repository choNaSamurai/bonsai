# Image Prompts — Nano Banana (Antigravity)

> Власник: Technical Artist · Підготовлено оркестратором · Статус: **v0.4 (2026-10-05)** — додано **B4: кора за породою** (запит власника); оновлено пріоритети після PA-103S…PA-102R.
> Стиль і кольори — з [DESIGN_SYSTEM_AND_UI_SPEC](../reference/flutter-v3.2/DESIGN_SYSTEM_AND_UI_SPEC.md) §1, §3. Промпти англійською — так генератор працює точніше.

## Стан генерації

| Розділ | Файл | Стан |
|---|---|---|
| 0 | `realism_l1…l4` | ✅ готово, обрано **L2** |
| A1 | `concept_day` | ✅ готово — **головний референс стилю** |
| A2 | `concept_night` | ✅ готово |
| A3 | `concept_dormancy` | 🔁 **перегенерувати** (A3 v2 нижче): виглядає «голим і мертвим» |
| B1 | `washi_paper_tile` | ✅ готово |
| B2 | `bark_tile` | ⏭ замінено **B4** (кора окремо для кожної породи) |
| **B4** | **кора за породою (новий)** | ⏳ не згенеровано — **пріоритет** |
| B3 | `soil_moist` / `soil_dry` | 🔁 **перегенерувати** (B3 v2): різна композиція, а гра плавно змішує їх між собою |
| C1 | `pot_tokoname` | ✅ тимчасово вирізано з референсу L2 (TA); C v2 — бажано для вищої роздільності |
| C2, C3 | `pot_kuro`, `pot_celadon` | ⏳ не згенеровано |
| D1–D4 | листки, квіти, бруньки | ⏳ не згенеровано |
| **D5** | **подушки листя** | ⏳ не згенеровано — **пріоритет №1** (сосна зараз процедурна, сакура — перефарбований клен) |
| E1 | `appicon_concept` | ⏳ не згенеровано |

**Порядок на наступну сесію (за пріоритетом):** D5 (усі три породи) → **B4** (кора трьох порід) → C2–C3 → A3 v2 → B3 v2 → D1–D4 → E1.

## Правила для всіх запитів

1. **Референс:** до кожного запиту додавай `A_Concept/concept_day.jpg` і пиши «match the style of the reference image».
2. **Формат PNG.** Не JPG: JPEG розмиває межі, і на краях спрайтів після вирізання лишається рожева облямівка. JPG прийнятний лише для безшовних текстур без фону (B1, B2).
3. **Фон для спрайтів** — суцільний чистий `#FF00FF` (RGB 255, 0, 255), **без тіней, градієнтів і віньєтки на фоні**. Якщо Antigravity вміє справжню прозорість (`transparent background`, PNG з альфою) — краще так.
4. Кожен промпт генеруй 3–4 варіанти, зберігай найкращий.
5. Куди класти: `Assets/_Project/Art/Source/<розділ>/`. Назви — як у колонці «Файл». Нову версію називай з суфіксом `_v2`, стару не видаляй.
6. Що **не** генеруємо: саме дерево (воно процедурне, малюється кодом), UI-іконки й печатку Hanko (робимо SVG-векторами).

**Спільний стильовий блок** — додавай у кінець кожного промпту розділів B–E:

```
Style: soft painterly 2D illustration matching the reference image, visible gentle brush texture, warm diffuse light, storybook cozy game art. Wabi-Sabi meets Japandi, calm and minimal, matte organic materials, subtle hand-made imperfection, muted earthy palette. No text, no logos, no watermark, no emoji, no people.
```

---

## A. Концепт-арт (у гру не йде)

### A3 v2 — сплячка (перегенерувати)
Файл: `A_Concept/concept_dormancy_v2.png`, 1080×1920.
```
Same composition, tree shape and pot as the reference image (do not change the tree's silhouette or branch structure). The bonsai has been peacefully sleeping for a few weeks: the foliage pads are thinner and smaller but still present (about half of the leaves remain, slightly muted olive-green with a few golden-brown leaves), a few fallen leaves rest on the soil, a soft blanket of sleeping moss #6B7355 creeps up the nebari roots and the lower trunk, the soil is pale and slightly dry #8C8275 with a few fine cracks. Gentle warm morning light, background gradient #E0EAF5 → #FDFBF7, a few tiny dust motes floating in the light. Mood: cozy, patient, quietly resting — like a cat napping in the sun, NOT dead, NOT withered, NOT sad, no bare skeletal branches. Soft painterly 2D illustration, no UI.
```

---

## B. Фон і текстури (йдуть у гру)

| # | Файл | Розмір | Примітка |
|---|---|---|---|
| B1 | `washi_paper_tile.jpg` | 1024×1024 | ✅ готово |
| B2 v2 | `bark_trunk_v2.png`, `bark_young_v2.png` | 512×512 | безшовні; дві версії |
| B3 v2 | `soil_moist_v2.png`, `soil_dry_v2.png` | 2048×512 | **ідентична композиція** |

### B2 v2 — кора (дві текстури)
Згенеруй спершу **trunk**, потім **young** з першою як референсом.
```
Seamless tileable texture, 512×512, painted bark for a stylized 2D bonsai, matching the painterly style of the reference image: [VARIANT]. Soft painterly brush strokes rather than photographic detail, low-to-medium contrast so it reads well when wrapped around thin procedural branches, even flat lighting, no shadows, no highlights from a specific light direction, edges tile perfectly in both directions.
```
- **trunk:** `mature trunk bark, warm dark brown #3E2723 with lighter #6D4C41 ridges, gentle vertical furrows and a few soft knots, medium detail`
- **young:** `young smooth branch bark, lighter warm brown #6D4C41 with subtle greenish-olive tint #7A6A4F, very fine horizontal lenticel marks, almost smooth, low detail`

### B4 — кора за породою (замінює B2 v2)
Файли: `B_Textures/bark_<species>_trunk.png`, `B_Textures/bark_<species>_young.png`, де `<species>` = `kuromatsu` / `momiji` / `yamazakura`; кожен **1024×1024**, безшовний в обох напрямках.
Генеруй спершу `trunk`, потім `young` тієї ж породи з `trunk` як референсом («same species and palette, younger and smoother»).

**Базовий промпт:**
```
Seamless tileable texture, 1024×1024, painted bark of a [SPECIES] bonsai for a stylized 2D game, matching the soft painterly style of the reference image (gentle brush strokes, warm diffuse light, subtle ink-like dark lines in the deepest cracks). [BARK]. Bark grain runs vertically along the image, low-to-medium contrast so it reads well when wrapped around thin procedural branches, even flat lighting with no light direction, no shadows, no highlights, no moss, no leaves, edges tile perfectly in both directions.
```

| Порода | `[SPECIES]` | `[BARK]` trunk | `[BARK]` young |
|---|---|---|---|
| Kuromatsu | `Japanese black pine` | `thick mature bark broken into irregular scaly plates and flakes separated by deep dark fissures, charcoal grey #3A3633 plates with warm grey-brown #5A4E44 edges and a few reddish-brown #6B4A3A undertones in the cracks` | `young pine bark, smoother with small thin flaking scales and fine fissures, warm grey-brown #6A5A4C with reddish #7A5240 tint` |
| Momiji | `Japanese maple (Acer palmatum)` | `smooth mature maple bark with very fine shallow vertical striations and soft mottling, grey-brown #6B5E52 with olive-grey #7A7560 patches, almost no cracks` | `young maple shoot bark, very smooth, olive-green #6F7A4A blending into soft red #8A4A3A along the grain, tiny pale lenticel dots` |
| Yamazakura | `wild Japanese cherry (Yamazakura)` | `smooth glossy cherry bark with distinctive horizontal lenticel bands (short pale stripes across the grain) and faint peeling horizontal strips, rich reddish-brown #5E3A2E with coppery sheen #7A4A36` | `young cherry bark, smooth satin reddish-brown #7A4636 with small horizontal lenticel marks, slightly lighter and warmer` |

> Після генерації TA перевіряє безшовність (3×3), підганяє тон до референсу L2 і зменшує до 512² для гри. Кора стовбура і молодих гілок змішується за товщиною гілки, тож обидві текстури породи мають бути в одній палітрі.

### D5 — нагадування щодо порід
Для **сосни** і **сакури** D5 зараз найважливіший: сосна в прототипі намальована процедурно, сакура — це перефарбований клен. Для сосни додавай до запиту `A_Concept/concept_night.jpg` як другий референс форми хвої, але проси **денне світло**: `daylight colors, no fireflies, no night tint`.

### B3 v2 — ґрунт (два стани з однаковою композицією)
Згенеруй спершу **moist**, потім **dry** з першою як референсом і фразою «exactly the same layout, only change the colors and add cracks».
```
Game sprite, side-view horizontal strip of bonsai soil seen through the pot opening, 2048×512 (4:1). The soil surface is a gently uneven horizontal line at about 40% from the top; everything above the soil line is solid pure magenta #FF00FF; the soil fills the frame from the soil line down to the bottom edge with no magenta below. Fine granular akadama soil with tiny pebbles, two small patches of moss at 20% and 70% of the width. [STATE]. Even flat lighting, no shadows on the magenta background, soft painterly 2D style matching the reference image.
```
- **moist:** `moist state: deep peat brown #4A3525, slightly darker and richer, moss lush emerald #4A7A44`
- **dry:** `dry state: pale grey loam #8C8275, matte, thin hairline cracks across the surface, moss faded olive #6B7355 — same pebbles, same moss positions, same soil line as the reference`

---

## C. Горщики (спрайти, вид суворо збоку) — C v2

| # | Файл | Розмір | Монетизація |
|---|---|---|---|
| C1 | `pot_tokoname_v2.png` | 1024×512 | безкоштовний |
| C2 | `pot_kuro.png` | 1024×512 | IAP $2.99 |
| C3 | `pot_celadon.png` | 1024×512 | IAP $2.99 |

Згенеруй спершу C1, потім C2 і C3 з C1 як референсом форми («exactly the same pot shape and proportions, only change the material»).
```
Game sprite PNG of a shallow rectangular bonsai pot, perfectly orthographic straight side view, camera exactly at rim height so the top surface and the inside of the pot are NOT visible, no perspective, centered, filling 80% of the width, short feet at the bottom corners, slightly rounded rim lip. [MATERIAL]. Soft even lighting, NO cast shadow and NO contact shadow, solid flat pure magenta #FF00FF background (RGB 255,0,255) with no gradient, nothing else in the frame. Soft painterly 2D style matching the reference image.
```
- **C1 [MATERIAL]:** `Unglazed Tokoname terracotta clay #C17753, porous matte texture with tiny kiln speckles`
- **C2 [MATERIAL]:** `Kuro graphite-black stoneware #2C2C2E, silky satin finish, faint warm highlights`
- **C3 [MATERIAL]:** `Translucent celadon jade glaze #9EBC9E with a delicate crackle network inside the glaze`

---

## D. Листя (атласи для рендеру)

> Арт-напрям L2 малює крону **подушками-хмарками** (як у `concept_day`). Тому головний асет — **D5, подушки листя**: рендер розтягує їх на кінцевих групах гілок. Окремі листки D1–D4 потрібні для деталей: падіння, сезонні акценти, зрізані пагони. Точний набір уточнить задача PA-102S.

### D5 — подушки листя (пріоритет)
Файли: `sheet_pads_momiji_v1.png`, `sheet_pads_kuromatsu_v1.png`, `sheet_pads_yamazakura_v1.png`, кожен 2048×2048.
```
Sprite sheet PNG on a solid flat pure magenta #FF00FF background: 9 foliage pads for a stylized 2D bonsai, matching the painterly foliage of the reference image exactly. Each pad is a compact cloud-shaped cluster of [FOLIAGE], seen from the side, wider than tall (about 2:1), slightly domed top and flatter bottom with a soft darker underside for volume, light from the upper left. Arrange as a 3×3 grid with generous spacing, no overlaps, no branches or twigs visible, no shadows on the background. Row 1: three small pads; row 2: three medium pads; row 3: three large pads — each pad with a different irregular outline so they can be mixed without repetition. Soft painterly 2D style.
```
- **momiji:** `small five-lobed Japanese maple leaves, fresh green #5C8A42 with lighter #8DB86B tips`
- **kuromatsu:** `dense Japanese black pine needle tufts, dark green #2E4F2D with lighter #4E7A4A needle tips, slightly spiky outline`
- **yamazakura:** `small oval wild-cherry leaves, olive green #6A994E`

Сезонні варіанти, після основних, з тим самим запитом і основним листом як референсом:
- `sheet_pads_momiji_autumn_v1.png`: `golden ochre #D98A3C and crimson #A33B29 maple leaves`
- `sheet_pads_yamazakura_bloom_v1.png`: `cherry blossoms in powder pink #FFB7C5 with deeper pink centers #F48FB1 mixed with a few olive leaves`

### D1–D4 — окремі листки, квіти, бруньки
| # | Файл | Вміст |
|---|---|---|
| D1 | `sheet_momiji.png` | клен: 4 листки зелені `#5C8A42`, 4 охра `#D98A3C`, 4 багряні `#A33B29` |
| D2 | `sheet_kuromatsu.png` | сосна: 6 пучків хвої `#2E4F2D`, 3 весняні «свічки» `#9CCC65` |
| D3 | `sheet_yamazakura.png` | сакура: 4 листки `#6A994E`, 4 квітки `#FFB7C5` з серцевиною `#F48FB1`, 3 пуп'янки |
| D4 | `sheet_buds.png` | 6 бруньок різного ступеня розкриття, 3 калюси зрізу `#8D6E63` |

```
Sprite sheet PNG on a solid flat pure magenta #FF00FF background: [CONTENT], each element isolated with generous spacing, no overlaps, arranged in a neat grid, each element seen flat from the front, soft painterly shading matching the reference image, clean silhouette readable at 32×32 px, no shadows on the background.
```
- **D1 [CONTENT]:** `12 Japanese maple (Acer palmatum) leaves with 5–7 slender pointed lobes: 4 fresh green #5C8A42, 4 golden ochre #D98A3C, 4 deep crimson #A33B29`
- **D2 [CONTENT]:** `6 Japanese black pine needle clusters (dense fans of dark green needles #2E4F2D) and 3 pale spring candles #9CCC65`
- **D3 [CONTENT]:** `4 wild cherry (Yamazakura) leaves #6A994E, 4 open five-petal cherry blossoms in powder pink #FFB7C5 with deeper pink centers #F48FB1, 3 closed pink flower buds`
- **D4 [CONTENT]:** `6 small tree buds from tightly closed to just opening (green-brown), and 3 round healed pruning-cut callus scars #8D6E63 seen face-on`

---

## E. Іконка застосунку (концепти)

Файл: `E_Icon/appicon_concept.png`, 1024×1024.
```
iOS app icon concept PNG, 1024×1024, full-bleed square (no rounded corners — the OS applies the mask), no text. A small bonsai in the painterly style of the reference image: tapered S-curved trunk, two or three cloud-shaped foliage pads, shallow terracotta pot, centered, bold simple silhouette readable at 60×60 px, on a soft warm washi-white to pale lake-blue gradient background. Minimal details, no border, no glossy effects.
```

---

## Після генерації

Скинь обрані файли в `Assets/_Project/Art/Source/` (або напиши, де вони лежать), потім перемкнись на вікно Unity, щоб він створив `.meta`. Technical Artist:
- вирізає magenta-фон, ріже листи на атласи, перевіряє безшовність текстур;
- налаштовує імпорт (ASTC, mip-maps для листя, спрайт-атласи) і рахує вагу в бюджеті розміру;
- записує джерело і ліцензію (AI-генерація, Nano Banana / Antigravity) у TECH_DECISIONS.

> ⚠ Перед комерційним релізом перевір умови використання згенерованих зображень у твоєму тарифі Google/Antigravity.
