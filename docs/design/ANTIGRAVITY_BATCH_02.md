# Antigravity — пакет генерації №2 (2026-10-05)

Самодостатнє завдання для генератора зображень (Nano Banana в Antigravity). Усе потрібне — в цьому файлі. Промпти англійською.

## Що потрібно від виконавця

1. Згенерувати зображення з розділів 1–4 у вказаному порядку.
2. Кожен промпт — 3–4 варіанти; зберегти **один найкращий** під вказаною назвою.
3. Зберегти файли в репозиторій у вказані теки (`Assets/_Project/Art/Source/...`).
4. Нічого іншого в репозиторії не змінювати.

## Загальні правила (для всіх запитів)

- **Референс стилю:** до кожного запиту додавати зображення `Assets/_Project/Art/Source/A_Concept/concept_day.jpg` і дописувати в кінець промпту: `Match the style of the reference image.`
- **Формат:** тільки **PNG**.
- **Спрайти (розділи 1 і 4):** фон — суцільний чистий **`#FF00FF`** (RGB 255, 0, 255), без тіней, градієнтів, віньєтки й відблисків на фоні. Якщо генератор уміє справжню прозорість (PNG з альфа-каналом) — використовувати її замість magenta.
- **Безшовні текстури (розділ 2):** без фону, краї мають стикуватися в обох напрямках.
- **Ніде:** тексту, підписів, логотипів, водяних знаків, емоджі, людей.
- Стиль: м'яка мальована 2D-ілюстрація (painterly), тепле розсіяне світло, Wabi-Sabi / Japandi, приглушена земляна палітра.

---

## 1. Подушки листя (пріоритет №1)

Тека: `Assets/_Project/Art/Source/D_Sheets/` · Розмір: **2048×2048** · Фон: `#FF00FF`

**Базовий промпт** (`[FOLIAGE]` замінити рядком з таблиці):
```
Sprite sheet PNG on a solid flat pure magenta #FF00FF background: 9 foliage pads for a stylized 2D bonsai, matching the painterly foliage of the reference image exactly. Each pad is a compact cloud-shaped cluster of [FOLIAGE], seen from the side, wider than tall (about 2:1), slightly domed top and flatter bottom with a soft darker underside for volume, light from the upper left. Arrange as a 3×3 grid with generous spacing, no overlaps, no branches or twigs visible, no shadows on the background. Row 1: three small pads; row 2: three medium pads; row 3: three large pads — each pad with a different irregular outline so they can be mixed without repetition. Soft painterly 2D style.
```

| # | Файл | `[FOLIAGE]` | Додатково |
|---|---|---|---|
| 1.1 | `sheet_pads_momiji_v1.png` | `small five-lobed Japanese maple leaves, fresh green #5C8A42 with lighter #8DB86B tips` | — |
| 1.2 | `sheet_pads_kuromatsu_v1.png` | `dense Japanese black pine needle tufts, dark green #2E4F2D with lighter #4E7A4A needle tips, slightly spiky outline` | Другий референс: `A_Concept/concept_night.jpg` (форма хвої). Дописати: `Daylight colors, no fireflies, no night tint.` |
| 1.3 | `sheet_pads_yamazakura_v1.png` | `small oval wild-cherry leaves, olive green #6A994E` | — |

**Сезонні варіанти** (той самий базовий промпт; основний лист породи — як додатковий референс):

| # | Файл | `[FOLIAGE]` |
|---|---|---|
| 1.4 | `sheet_pads_momiji_autumn_v1.png` | `golden ochre #D98A3C and crimson #A33B29 maple leaves` |
| 1.5 | `sheet_pads_yamazakura_bloom_v1.png` | `cherry blossoms in powder pink #FFB7C5 with deeper pink centers #F48FB1 mixed with a few olive leaves` |

---

## 2. Кора за породою

Тека: `Assets/_Project/Art/Source/B_Textures/` · Розмір: **1024×1024** · Безшовні

Для кожної породи: спершу `trunk`, потім `young` — з `trunk` як додатковим референсом і дописом `Same species and palette, younger and smoother.`

**Базовий промпт** (`[SPECIES]` і `[BARK]` з таблиці):
```
Seamless tileable texture, 1024×1024, painted bark of a [SPECIES] bonsai for a stylized 2D game, matching the soft painterly style of the reference image (gentle brush strokes, warm diffuse light, subtle ink-like dark lines in the deepest cracks). [BARK]. Bark grain runs vertically along the image, low-to-medium contrast so it reads well when wrapped around thin procedural branches, even flat lighting with no light direction, no shadows, no highlights, no moss, no leaves, edges tile perfectly in both directions.
```

| # | Файл | `[SPECIES]` | `[BARK]` |
|---|---|---|---|
| 2.1 | `bark_kuromatsu_trunk.png` | `Japanese black pine` | `thick mature bark broken into irregular scaly plates and flakes separated by deep dark fissures, charcoal grey #3A3633 plates with warm grey-brown #5A4E44 edges and a few reddish-brown #6B4A3A undertones in the cracks` |
| 2.2 | `bark_kuromatsu_young.png` | `Japanese black pine` | `young pine bark, smoother with small thin flaking scales and fine fissures, warm grey-brown #6A5A4C with reddish #7A5240 tint` |
| 2.3 | `bark_momiji_trunk.png` | `Japanese maple (Acer palmatum)` | `smooth mature maple bark with very fine shallow vertical striations and soft mottling, grey-brown #6B5E52 with olive-grey #7A7560 patches, almost no cracks` |
| 2.4 | `bark_momiji_young.png` | `Japanese maple (Acer palmatum)` | `young maple shoot bark, very smooth, olive-green #6F7A4A blending into soft red #8A4A3A along the grain, tiny pale lenticel dots` |
| 2.5 | `bark_yamazakura_trunk.png` | `wild Japanese cherry (Yamazakura)` | `smooth glossy cherry bark with distinctive horizontal lenticel bands (short pale stripes across the grain) and faint peeling horizontal strips, rich reddish-brown #5E3A2E with coppery sheen #7A4A36` |
| 2.6 | `bark_yamazakura_young.png` | `wild Japanese cherry (Yamazakura)` | `young cherry bark, smooth satin reddish-brown #7A4636 with small horizontal lenticel marks, slightly lighter and warmer` |

---

## 3. Горщики (платні)

Тека: `Assets/_Project/Art/Source/C_Pots/` · Розмір: **1024×512** · Фон: `#FF00FF`

Додатковий референс форми: `Assets/_Project/Art/Source/Section_0/realism_l2_painterly.jpg` (горщик унизу). Дописати: `Exactly the same pot shape and proportions as the pot in the second reference image, only change the material.`

**Базовий промпт:**
```
Game sprite PNG of a shallow rectangular bonsai pot, perfectly orthographic straight side view, camera exactly at rim height so the top surface and the inside of the pot are NOT visible, no perspective, centered, filling 80% of the width, short feet at the bottom corners, slightly rounded rim lip. [MATERIAL]. Soft even lighting, NO cast shadow and NO contact shadow, solid flat pure magenta #FF00FF background (RGB 255,0,255) with no gradient, nothing else in the frame. Soft painterly 2D style matching the reference image.
```

| # | Файл | `[MATERIAL]` |
|---|---|---|
| 3.1 | `pot_kuro.png` | `Kuro graphite-black stoneware #2C2C2E, silky satin finish, faint warm highlights` |
| 3.2 | `pot_celadon.png` | `Translucent celadon jade glaze #9EBC9E with a delicate crackle network inside the glaze` |

---

## 4. Іконка застосунку (концепт)

Тека: `Assets/_Project/Art/Source/E_Icon/` · Розмір: **1024×1024** · Фон — градієнт (не magenta)

| # | Файл |
|---|---|
| 4.1 | `appicon_concept.png` |

```
iOS app icon concept PNG, 1024×1024, full-bleed square (no rounded corners — the OS applies the mask), no text. A small bonsai in the painterly style of the reference image: tapered S-curved trunk with visible surface roots, two or three cloud-shaped foliage pads and a domed crown, shallow terracotta pot, centered, bold simple silhouette readable at 60×60 px, on a soft warm washi-white to pale lake-blue gradient background. Minimal details, no border, no glossy effects.
```

---

## Чекліст здачі

| # | Файл | Тека |
|---|---|---|
| 1.1–1.5 | `sheet_pads_*.png` (5 шт.) | `D_Sheets/` |
| 2.1–2.6 | `bark_*_{trunk,young}.png` (6 шт.) | `B_Textures/` |
| 3.1–3.2 | `pot_kuro.png`, `pot_celadon.png` | `C_Pots/` |
| 4.1 | `appicon_concept.png` | `E_Icon/` |

Якщо квота закінчиться раніше — пріоритет: **1.1–1.3 → 2.1–2.6 → 3.1–3.2 → 1.4–1.5 → 4.1**.
