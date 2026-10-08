# Antigravity — пакет генерації №4: перегенерація листя Yamazakura (2026-10-08)

> Власник: Technical Artist · Статус: **готово до генерації** (генерує власник в Antigravity / Nano Banana) · Промпти англійською.
> Підстава: **рішення власника 2026-10-08** — у листя Yamazakura ботанічна помилка, тож перегенеровуються **всі** аркуші Yamazakura. Контракт атласів: TD-073 і TD-085 ([`Art/Runtime/Preview/README.md`](../../Assets/_Project/Art/Runtime/Preview/README.md)), [ADR-0004](../adr/0004-procedural-tree-rendering.md) ред. 8 §2 (рівномірний масштаб, сезонні атласи з тими самими іменами й ректами).
> Попередні пакети: [№2](ANTIGRAVITY_BATCH_02.md) (п. 1.3, 1.5) і [№3](ANTIGRAVITY_BATCH_03.md). Пакет №4 **замінює** пункти A3, B2, B3, D3 і D5 пакета №3. ID задачі в BACKLOG призначає оркестратор. Суміжні задачі: [PA-102U](../tasks/PA-102U.md) (на паузі) і [PA-102X](../tasks/PA-102X.md).

## 0. Що не так і чому

В аркуші `sheet_pads_yamazakura_v1.png` (пакет №2) листки **пальчасті й лопатеві, як у клена**. У гірської вишні Yamazakura (*Cerasus jamasakura*, syn. *Prunus jamasakura*) листок **цілісний**: еліптично-яйцеподібний, з довгим загостреним кінчиком і дрібнопилчастим краєм (§1). Тож у грі крона читається як «ще один клен».

Звідки помилка:
1. Промпт 1.3 пакета №2 мав лише `small oval wild-cherry leaves, olive green #6A994E`, без заборони лопатей.
2. **Стильовий референс `A_Concept/concept_day.jpg` — це японський клен** із пальчастим листям. Промпт вимагав `matching the painterly foliage of the reference image exactly`, тож генератор узяв звідти й форму листка.
3. Пакет №3 вимагав повторити листя v1 (A3, B2, B3, D3, D5), тобто поширив би помилку далі.

Що змінено в пакеті №4:
- **Форму листка задає окремий напрямний референс** `D_Sheets/guides/guide_leaf_yamazakura.png`. Це пласка ботанічна схема правильного листка, яку генератор використовує **лише для форми**.
- `concept_day.jpg` прикріплюється тільки до Y1 і тільки як референс **манери письма**. У промпті прямо сказано, що на ньому клен і форму листка з нього брати не можна.
- **`sheet_pads_yamazakura_v1.png` до запитів не прикріплюється.** Розкладку v1 передає сірий макет `guide_round_pads_yamazakura_1024.png`: ті самі клітинки й пропорції, але без листя.
- Кожен промпт містить явні заборони: `NOT lobed, NOT palmate, NOT maple-like, no five-pointed leaves`.

## 1. Ботаніка Yamazakura (для виконавця і для приймання)

| Ознака | Має бути | Не має бути |
|---|---|---|
| Пластинка | **Цілісна**, еліптично-яйцеподібна. Довжина ≈ 2–2.5 ширини, найширше місце трохи нижче середини, основа заокруглена | Лопаті, пальчастий поділ, «зірочка», «долонька», 3 або 5 кінчиків |
| Кінчик | **Один**, довгий і витягнутий (acuminate) | Тупий або круглий кінчик, кілька вершин |
| Край | Дрібнопилчастий, гострі зубці спрямовані до кінчика. На спрайті це лише легка зубчастість контуру | Глибокі вирізи, гладкий «пластиковий» контур |
| Жилки | Одна середня жилка і 7–10 пар бічних, що дугою йдуть до кінчика (перисте жилкування) | Жилки, що розходяться віялом з однієї точки (пальчасте жилкування) |
| Розміщення | **Чергове** на коротких пагонах. Частина листків **звисає на черешках**, тож крона пухкіша за кленову, а листок відносно більший | Пласка щільна мозаїка дрібних зірочок |
| Колір | Дорослі листки оливково- або свіжо-зелені `#6A994E` (основний колір, BALANCE §3), світліші `#8DB46A` на освітлених верхах, глибший `#4A6E38` знизу, ледь атласні. **Молоді листки бронзово-червонуваті** `#8C4A34`…`#B86A42` — характерна риса Yamazakura | Жовто-лаймовий тон «олив-лайм» v1, глянцеві відблиски |
| Цвітіння | Блідо-рожеві `#FFB7C5` квітки з серцевиною `#F48FB1`, **5 округлих пелюсток з невеликою вирізкою на кінчику кожної**, у пучках по 2–5 на тонких квітконіжках. Квітки розкриваються **одночасно з молодими бронзовими листками** | Махрові квіти, квіти без листя на голих гілках (так цвіте Somei-Yoshino, а не Yamazakura), темно-рожева «підкладка» під подушкою |

Справжня Yamazakura цвіте майже білим із рожевим відтінком. Пудрово-рожевий `#FFB7C5` — художнє рішення палітри (BALANCE §3), тож його лишаємо.

## 2. Склад пакета

Усі файли — у `Assets/_Project/Art/Source/D_Sheets/`. Суфікс `_v2` означає «правильний листок вишні». Розкладки й імена атласів — за контрактом TD-073 / TD-085 (§7).

| # | Файл | Що | Вхідні зображення (саме в цьому порядку) | Пріоритет | Замінює |
|---|---|---|---|---|---|
| **Y1** | `sheet_pads_yamazakura_v2.png` | 12 круглих подушок у рядах 3-3-3-3 (ряд 1 — S, ряд 2 — M, ряди 3–4 — L), 1024² | `guides/guide_round_pads_yamazakura_1024.png`, `A_Concept/concept_day.jpg`, `guides/guide_leaf_yamazakura.png` | **1, гейт** (§3) | `sheet_pads_yamazakura_v1.png` (пакет №2, п. 1.3) |
| **Y2** | `sheet_pads_yamazakura_flat_v2.png` | 10 пласких подушок у рядах 3-3-2-2, 1024² | `guides/guide_flat_pads_1024.png`, **збережений Y1**, `guides/guide_leaf_yamazakura.png` | 1 | A3 пакета №3 (`sheet_pads_yamazakura_flat_v1.png`) |
| **Y3** | `sheet_pads_yamazakura_bloom_v2.png` | цвітіння **Y1**: перефарбування з тими самими силуетами | **збережений Y1** | 2 | `sheet_pads_yamazakura_bloom_v1.png` (пакет №2, п. 1.5) і B3 пакета №3 |
| **Y4** | `sheet_pads_yamazakura_bloom_flat_v2.png` | цвітіння **Y2** | **збережений Y2**, **збережений Y3** | 2 | B2 пакета №3 |
| Y5 | `sheet_domes_yamazakura_v2.png`, 1376×768 | 2 куполи (`role:dome`) з h/w 0.46 і 0.50 | `guides/guide_domes_1376x768.png`, **збережений Y1**, `guides/guide_leaf_yamazakura.png` | 3, **опційно**, лише після рішення GD або власника щодо куполів (пакет №3 §2, TD-085 (г)) | D3 пакета №3 |
| Y6 | `sheet_domes_yamazakura_bloom_v2.png`, 1376×768 | цвітіння **Y5** | **збережений Y5**, **збережений Y3** | 3, опційно | D5 пакета №3 |

**Чому Y1 малюється за макетом, а не з v1.** Макет `guide_round_pads_yamazakura_1024.png` повторює клітинки v1: ті самі ряди й колонки, та сама лінія низу, ширина v1 і пропорція v1 мінус 0.02. Таблиця клітинок — `guides/guides_batch04.json`. Ряди 2–4 рівномірно стиснуто ×0.86–0.89, щоб між подушками лишалося ≥ 48 px чистої magenta: у v1 в рядах 2–4 проміжки лише 13–27 px, а до краю аркуша 13–24 px. Злиплі подушки відсіюються (урок пакета №2). Силуети згладжено: купол із 2–3 низькими горбиками, майже рівний низ, клин на кінцях (товщина на 8% і 92% ширини — 0.35–0.57 від максимуму, як в еталоні пакета №3 §1).

**Чому Y3 — перефарбування Y1.** За TD-085 (б) сезонний атлас бере імена, ректи й альфу з базового. Тож силуети цвітіння мають **точно** збігатися з Y1. Цвітіння v1 цій вимозі не відповідає: там 10 подушок проти 12 і темна «підкладка».

## 3. Що робити (виконавцю)

1. **Y1 — гейт.** Прикріпити три зображення з таблиці §2 у вказаному порядку, вставити промпт §5.1, згенерувати 3–4 варіанти і перевірити кожен за §6.1. Зберегти **один** найкращий. **Перш ніж робити щось далі, показати Y1 оркестратору або TA**, бо всі наступні аркуші похідні від Y1.
   - Якщо у всіх варіантах лишаються лопатеві чи «кленові» листки, взяти запасний промпт Y1′ (§5.2, без `concept_day.jpg`).
   - Якщо генератор ігнорує макет (інша кількість подушок, сірі плями), так само спробувати Y1′. Якщо й це не допомогло — повідомити TA.
2. **Y2** (§5.3) — на основі **збереженого** Y1.
3. **Y3** (§5.4) — на основі **збереженого** Y1, потім **Y4** — на основі збереженого Y2, з Y3 як референсом кольору. Для перевірки перемикати базовий і сезонний аркуші: силуети не мають «стрибати».
4. **Y5 → Y6** — лише якщо оркестратор або GD підтвердить куполи (промпти §5.5 і §5.4).
5. Якщо не вистачає квоти, порядок такий: **Y1 → Y2 → Y3 → Y4 → Y5 → Y6**.
6. **Не прикріплювати** `sheet_pads_yamazakura_v1.png` і нічого, що згенеровано на його основі. Аркуші v1 не видаляти й не перейменовувати: вони лишаються в репозиторії, доки v2 не прийде й не буде обробленим.
7. Більше нічого в репозиторії не змінювати.

## 4. Загальні правила

- **Формат** — лише PNG. Канва — як у макета: 1024² для Y1–Y4, 1376×768 для Y5–Y6. Якщо вийде 2048², TA зменшить рівномірно ×0.5.
- **Фон** — суцільний `#FF00FF` (RGB 255, 0, 255) без тіней, ореолу, підкладки, градієнта, віньєтки й текстури. Якщо генератор уміє справжню прозорість — краще прозорість.
- **Світло** — як у пакетах №2/№3: м'яке розсіяне зліва-згори, світліший верх, запечений темніший низ (`PadUndersideShade` = 0). Без бліків, rim-light і сонячних плям; лише ледь атласний полиск листка.
- **Ніде** немає тексту, цифр, підписів, рамок, стовбура, гілок, горщика, ґрунту. Квіти — лише в Y3, Y4 і Y6.
- **Розмір листка** однаковий у всіх подушках аркуша: пластинка ≈ 40–50 px завдовжки і 18–24 px завширшки на канві 1024. Це трохи більше за кленовий листок інших порід.

## 5. Промпти

### 5.1. Y1 — круглі подушки (макет + стиль + форма листка)

```
Use the attached images:
Image 1 — LAYOUT GUIDE: twelve grey mound silhouettes on a solid magenta background, in four rows of three.
Image 2 — STYLE reference: a painterly bonsai illustration. Its tree is a Japanese MAPLE: take ONLY the painting style from it (soft brushwork, warm diffuse light, soft darker undersides, muted colour mood) — NEVER its leaf shape.
Image 3 — LEAF SHAPE reference: a flat botanical diagram of the correct mountain-cherry leaf, mature green and young bronze, front, three-quarter and hanging views. Leaf SHAPE only, style from image 2: ignore its flat colours, thin outlines, white background and layout.

Create a game sprite sheet PNG with exactly the same canvas size and layout as image 1. Replace each grey silhouette with one painted foliage pad of wild Japanese mountain cherry (yamazakura, Cerasus jamasakura). Every pad must cover its grey silhouette and keep its width, height and position: a soft rounded mound of foliage seen from the side, gently domed top, almost straight underside, the lower third in a soft darker shade of the same foliage, both ends tapering. Only small leaf tips may poke out of the silhouette outline.

Leaves: every leaf is one single, undivided, elliptic-ovate blade exactly like image 3 — about twice as long as wide, widest just below the middle, rounded base, ONE long drawn-out pointed tip, finely toothed edge, one central vein. NOT lobed, NOT palmate, NOT maple-like, no five-pointed leaves, no star-shaped or hand-shaped leaves, no needles. Leaves grow alternately on short hidden shoots at varied angles; many hang slightly downward on short leaf stalks, so the pad looks soft and a little loose at its edges, while its middle stays dense with no holes. Each leaf blade is about 40–50 px long and 18–24 px wide, the same size in every pad — larger and fewer leaves than on a maple.

Colours: mature leaves olive green #6A994E, lighter fresh green #8DB46A on the lit tops, deeper #4A6E38 in the underside; at the outer tips and on top of each pad a few young leaves (about one leaf in eight) in reddish bronze #8C4A34 to copper #B86A42. A faint satin sheen is fine; no glossy highlights, no rim light, no glow. Light from the upper left, almost no left-to-right brightness change across a pad.

Background: solid flat pure magenta #FF00FF (RGB 255, 0, 255) everywhere outside the pads, with no shadows, no gradient, no vignette, no texture and nothing left of the grey guide shapes. Keep clean magenta gaps between pads: no pad touches or overlaps another. Nothing else in the image: no flowers, no trunk, no branches, no visible twigs, no pot, no ground, no text, no numbers, no labels, no frame.
```

### 5.2. Y1′ — запасний, без стильового референсу

Прикріпити лише `guide_round_pads_yamazakura_1024.png` (image 1) і `guide_leaf_yamazakura.png` (image 2). Узяти промпт §5.1 з такими змінами:
- рядок `Image 2 — STYLE reference…` видалити;
- `Image 3 — LEAF SHAPE reference…` перейменувати на `Image 2 — …` і замінити в ньому `style from image 2` на `style as described below`;
- `exactly like image 3` замінити на `exactly like image 2`;
- в кінець абзацу «Create a game sprite sheet…» дописати:

```
Style: soft painterly 2D illustration for a calm Japanese bonsai game — gentle visible brush strokes, warm diffuse light, muted earthy natural greens, soft ink-like darker accents only in the deepest shadows, Wabi-Sabi / Japandi mood, no outlines around leaves, not vector, not photographic.
```

### 5.3. Y2 — пласкі подушки (на основі Y1)

```
Use the attached images:
Image 1 — LAYOUT GUIDE: ten flat grey silhouettes on a solid magenta background, in four rows.
Image 2 — FOLIAGE and STYLE reference: a finished sprite sheet of wild mountain-cherry (yamazakura) foliage pads from the same game.
Image 3 — LEAF SHAPE reference: a flat botanical diagram of the mountain-cherry leaf. Leaf SHAPE only, style from image 2.

Create a game sprite sheet PNG with exactly the same canvas size and layout as image 1. Replace each grey silhouette with one painted foliage pad of exactly the same mountain-cherry leaves as in image 2. Every pad must cover its grey silhouette and keep its width, height and position: a low, wide, gently domed layer of foliage — never a tall mound, ball or hemisphere. Only small leaf tips may poke out of the silhouette outline.

Each pad is one continuous cluster seen from the side: gently domed top, almost straight underside, the lower third in a soft darker shade of the same foliage. Both ends taper softly and get slightly airier, with a few loose hanging leaves and no hard outline or dark contour, so that two pads placed side by side with some overlap read as one longer pad. Left and right ends look alike; the pad does not lean or droop to one side.

Match image 2 exactly in leaf shape, leaf size in pixels, colour palette (olive-green mature leaves with a few reddish-bronze young leaves at the tips), brushwork and lighting: soft diffuse light from the upper left, lighter leaves on top, darker underside, almost no left-to-right brightness change across a pad. Every leaf is a single elliptic blade with one long pointed tip, like image 3: NOT lobed, NOT palmate, NOT maple-like, no five-pointed leaves. No glossy highlights, no rim light, no glow.

Background: solid flat pure magenta #FF00FF (RGB 255, 0, 255) everywhere outside the pads, with no shadows, no gradient, no vignette, no texture and nothing left of the grey guide shapes. Keep clean magenta gaps between pads: no pad touches or overlaps another. Nothing else in the image: no flowers, no trunk, no branches, no twigs, no pot, no ground, no grass, no text, no numbers, no labels, no frame.
```

Якщо генератор ігнорує макет, взяти промпт A′ пакета №3 (§4.2). Прикріпити до нього збережений Y1 як image 2 і замінити `[FOLIAGE]` на `mountain-cherry leaves exactly as in image 2 — single elliptic blades with one long pointed tip, NOT lobed, NOT palmate, NOT maple-like`.

### 5.4. Y3, Y4, Y6 — цвітіння (перефарбування)

```
Use the attached images:
Image 1 — a finished sprite sheet of wild mountain-cherry (yamazakura) foliage pads on a solid magenta #FF00FF background.
[IMAGE 2 LINE]

Repaint image 1 as its spring blossom version. Keep the canvas size and every pad's silhouette, size and position exactly the same as in image 1; nothing may extend outside the existing pad outlines; do not add, remove, move or resize pads.

Yamazakura flowers open together with its young leaves: cover about two thirds of every pad with clusters of small cherry blossoms, two to five flowers per cluster on thin stalks. Each flower has five rounded petals in pale powder pink #FFB7C5 with a small notch at the tip of every petal, a deeper pink centre #F48FB1 and tiny stamens. Single flowers only, not double. Between the clusters keep the leaves of image 1 in the same shapes and places, repainted as young leaves in reddish bronze #8C4A34 to copper #B86A42: single elliptic blades with one long pointed tip — NOT lobed, NOT palmate, NOT maple-like, no five-pointed leaves.

Keep the same lighting: lighter top, the lower third in a soft deeper tone of the same pinks and bronzes, light from the upper left. Do not add any dark backing, halo or shadow behind or under the pads. The background stays solid flat pure magenta #FF00FF with no shadows or gradient. No text.
```

| # | Image 1 | `[IMAGE 2 LINE]` |
|---|---|---|
| Y3 | збережений Y1 | рядок видалити (одне зображення) |
| Y4 | збережений Y2 | `Image 2 — colour and flower reference: the finished blossom version of other pads from the same tree. Match its flowers, flower size in pixels, colours and coverage exactly; do not copy its pad shapes.` (image 2 = збережений Y3) |
| Y6 | збережений Y5 | те саме, що Y4 (image 2 = збережений Y3) |

Якщо квіти в Y3 вийшли не в стилі гри, повторити Y3 і як image 2 додати `sheet_pads_yamazakura_bloom_v1.png`, а `[IMAGE 2 LINE]` замінити на: `Image 2 — brushwork reference for the blossoms only. Do not copy its pad shapes, its leaves or the dark pink backing under its pads.`

### 5.5. Y5 — куполи (опційно, на основі Y1)

```
Use the attached images:
Image 1 — LAYOUT GUIDE: two grey silhouettes on a solid magenta background.
Image 2 — FOLIAGE and STYLE reference: a finished sprite sheet of wild mountain-cherry (yamazakura) foliage pads from the same game.
Image 3 — LEAF SHAPE reference: a flat botanical diagram of the mountain-cherry leaf. Leaf SHAPE only, style from image 2.

Create a game sprite sheet PNG with exactly the same canvas size and layout as image 1. Replace each grey silhouette with one painted crown dome of exactly the same mountain-cherry leaves as in image 2 — the topmost foliage cap of a styled bonsai: wider than tall, a soft rounded top with two or three gentle bumps, an almost straight underside with a soft darker shade, slightly airy edges with a few loose hanging leaves and no dark contour. Each dome must cover its grey silhouette and keep its width, height and position; only small leaf tips may poke out.

Match image 2 exactly in leaf shape, leaf size in pixels, colour palette (olive-green mature leaves, a few reddish-bronze young leaves on top), brushwork and lighting: soft diffuse light from the upper left, lighter top, darker underside, no highlights, no rim light, no glow. Every leaf is a single elliptic blade with one long pointed tip: NOT lobed, NOT palmate, NOT maple-like, no five-pointed leaves. Background: solid flat pure magenta #FF00FF everywhere outside the domes, no shadows, no gradient, nothing left of the grey guide shapes; the two domes do not touch. No flowers, no trunk, no branches, no twigs, no pot, no text, no labels, no frame.
```

## 6. Приймання

### 6.1. Виконавець (на око, до збереження)

1. **Ботаніка (гейт).** Переглянути аркуш при 100–200%. **Кожен** видимий листок — одна цілісна овальна пластинка з **одним** гострим витягнутим кінчиком. Якщо на аркуші більше 2–3 листків з лопатями, «зірочкою» чи «долонькою», варіант відкинути.
2. Листки помітно більші й рідші за кленові з інших аркушів, частина звисає вниз. Зверху й на кінцях є кілька бронзово-червоних молодих листків, решта — оливково-зелені, без лайму.
3. Кількість і розкладка — як у макеті: Y1 — 12 подушок у рядах 3-3-3-3, Y2 — 10 у рядах 3-3-2-2, Y5 — 2 куполи. Кожну подушку оточує чиста magenta, жодні дві не торкаються. Сірих залишків макета немає. Квітів у Y1, Y2 і Y5 немає.
4. Y1 — пухкі купки за формою макета. Y2 — пласкі подушки, приблизно втричі ширші, ніж високі. Кінці повітряні, без темної обвідки, низ темніший.
5. Цвітіння (Y3, Y4, Y6): у квіток по 5 пелюсток з вирізкою, квіти не махрові. Між квітами видно бронзові молоді листки. Немає темно-рожевої «підкладки». При перемиканні з базовим аркушем силуети лишаються на місці.
6. Y2 і Y5 за листком, кольором і світлом такі самі, як Y1 (відкрити поруч).

### 6.2. TA (виміри скриптом збірки; провал відсіює клітинку, а не весь аркуш)

Перевірки P1–P4, S1, K1–K3, L1 і Z1–Z3 — як у пакеті №3 (§5.2), з такими уточненнями:

| # | Перевірка | Поріг |
|---|---|---|
| P1 (Y1) | `innerH/innerW` клітинки | `targetAspect` з `guides_batch04.json` ± 0.04 (смуга 0.40–0.60). Для Y2 і Y5 — як у пакеті №3 |
| K2 (Y1) | Розділення | 1 компонента на клітинку `guides_batch04.json` (± 40 px) після дилатації 15 px |
| B1 | Форма листка | Колаж 100% з 12 випадкових ділянок: 0 лопатевих чи пальчастих листків на ділянку. У сумнівних випадках вирішує власник |
| B2 | Розмір листка | Медіана довжини 10 виміряних пластинок — 40–50 px. Y2 і Y5 — у межах ± 15% від Y1 |
| L2 | Палітра | Медіана зелених текселів (без бронзових) — ΔE ≤ 10 від `#6A994E`. Бронзові тексели (тон 10–35°, s > 0.35) займають 4–20% α ≥ 0.5 |
| Z2 | Сезон | Як у пакеті №3, плюс TD-085 (б): сезонний атлас — ½ бази, без власного JSON |

## 7. Обробка й контракт (TA, після генерації)

- **Скрипт** `tools/art/preview/build_pads_d5.py`:
  - `SHEETS['yamazakura']` → `sheet_pads_yamazakura_v2.png`, клітинки — з `guides_batch04.json`;
  - другий аркуш породи — `sheet_pads_yamazakura_flat_v2.png`, клітинки — з `guides_batch03.json` (як у пакеті №3 §6);
  - `yamazakura_bloom` → `sheet_pads_yamazakura_bloom_v2.png` + `_bloom_flat_v2.png`, RGB реєструється на клітинки бази.
- **Імена.** Круглі — за чинним правилом скрипта: `y0` купол (TD-073) + `y1…`. Пласкі — `yf<ряд><колонка>`, куполи — `yd1`, `yd2` (пакет №3 §6). Контракт JSON не змінюється.
- **Атласи** `pads_yamazakura` і `pads_yamazakura_bloom` перезбираються **з тими самими іменами файлів**. Код і `PreviewArtLibrary` не змінюються.
- **Джерела v1** (`sheet_pads_yamazakura_v1.png`, `sheet_pads_yamazakura_bloom_v1.png`) лишаються, доки Y1–Y4 не прийняті. Прибирати їх — окремим кроком після погодження власника.
- **Звіт TA:** таблиця `name · size · a · тіло · клин · еліпс`, колажі (аркуш, крона Yamazakura до/після) у `Temp/art-preview/batch04/`, оновлений README Preview, рядок TD з розміром і вагою атласів.

## 8. Asset Diet

- **Збірка.** Розкладка й кількість спрайтів ті самі, що у v1, а ректи рядів 2–4 на 11–14% вужчі. Тож оцінка атласу Yamazakura з пласкими (1536×1024, бриф пакета №3 §7, TD-085 (а)) не зростає, сезонний — ½ бази. Ліміт Σ атласів подушок ≤ 2.0 МБ ASTC (TD-085 (г)) пакет №4 не зачіпає. Виміряти після обробки.
- **Репозиторій (LFS):** `guide_leaf_yamazakura.png` — **68 КБ** (1200×800, палітра 64 кольори), `guide_round_pads_yamazakura_1024.png` — **8.5 КБ**, `guides_batch04.json` — **2.7 КБ**. Нові джерела — 4–6 аркушів по ≈ 1.3 МБ.

## 9. Відомі обмеження й відкриті питання

- **Ширина L-класу.** Розкладку v1 (3-3-3-3) збережено на вимогу оркестратора. Через це круглі L-подушки мають ≈ 275 px, а пласкі L — 430 px. Після рівномірного масштабу (TD-084) листок на круглих L-спрайтах здаватиметься ≈ 1.5× більшим, ніж на пласких L того самого класу. У Momiji і Kuromatsu L ≈ 450–510 px при розкладці 3-3-2-2. Альтернатива — Y1 у розкладці 3-3-2-2: сталий розмір листка, але 4 варіанти L замість 6, плюс потрібен перерахунок пакування під ліміт TD-085 (г). Це рішення оркестратора й GD **до** генерації Y1; макет переробляється скриптом за кілька хвилин.
- **Підхід до рендеру (PA-102X).** Ескіз PA-102X малює листя інстансами окремих листків, а ADR-0004 — спрайтами-подушками. Якщо PA-102X змінить підхід, атласи подушок (Y1–Y6) можуть стати непотрібними. Тому **перед генерацією Y2–Y6 уточнити в оркестратора**, що спрайти-подушки лишаються. Y1 і `guide_leaf_yamazakura.png` корисні за будь-якого рішення: це еталон форми й кольору листка для процедурного листя.
- **Дрібниці на спрайті.** Зубці краю, жилки й залозки на черешку на спрайті майже не видно. Ознаки, які мають читатися: цілісна овальна пластинка, витягнутий кінчик, звисання, бронзові кінчики.

## 10. Референси й ліцензії

| Референс | Що це | Ліцензія / походження | Як використовувати |
|---|---|---|---|
| `D_Sheets/guides/guide_leaf_yamazakura.png` | Схема листка: A — дорослий листок анфас (з черешком і залозками); B — той самий у ¾, складений по жилці, плюс молодий бронзовий; C — пагін збоку з черговими листками, нижні звисають, на кінці бронзові молоді | Власна робота TA (скрипт PIL), без сторонніх джерел | Обов'язковий вхід Y1, Y2, Y5 — **leaf SHAPE only** |
| `D_Sheets/guides/guide_round_pads_yamazakura_1024.png` + `guides_batch04.json` | Макет розкладки Y1 | Власна робота TA. Виміри клітинок узято з аркуша v1 (AI-генерація, TD-029) | Обов'язковий вхід Y1 |
| Ishizaki Yūshi, «Prunus jamasakura», 1817–1823, Naturalis Biodiversity Center, Cock Blomhoff Collection ([Commons](https://commons.wikimedia.org/wiki/File:Naturalis_Biodiversity_Center_-_RMNH.ART.851_-_Prunus_jamasakura_-_Y%C5%ABshi_Ishizaki_-_Cock_Blomhoff_Collection_-_pencil_drawing_-_water_colour.jpg)) | Ботанічна акварель: квіти з вирізкою на пелюстках разом із молодими бронзовими листками | **Суспільне надбання** (Public Domain Mark 1.0). Подяка: «Naturalis Biodiversity Center/Wikimedia Commons» | **Опційно.** Обрізка без написів — `.agent-workspace/batch04-ta/ref_naturalis_jamasakura_crop.jpg` (поза репо). Молоді листки тут ще напівскладені й віялом виходять з однієї точки, тож здалеку можуть нагадати «долоньку». Для Y1 і Y2 **не прикріплювати**. Можна спробувати як image 2 у запасному Y3, з приміткою `botanical reference for flower shape and young-leaf colour only` |
| Pekachu, «Branches of Cerasus jamasakura at Mount Obukuma Saga», 2016 ([Commons](https://commons.wikimedia.org/wiki/File:Branches_of_Cerasus_jamasakura_at_Mount_Obukuma_Saga.JPG)) | Фото: квіти разом із мідно-бронзовими молодими листками | CC BY-SA 4.0 | **Лише щоб подивитися**, до генератора **не прикріплювати**: генерація на його основі може вважатися похідною роботою з вимогами BY-SA |
| [Категорія Commons «Prunus jamasakura»](https://commons.wikimedia.org/wiki/Category:Prunus_jamasakura) | Інші фото й ілюстрації | Ліцензію перевіряти для кожного файлу | Для довідки |

Скрипти, що створили напрямні файли (поза репо): `.agent-workspace/batch04-ta/make_leaf_guide.py`, `.agent-workspace/batch04-ta/make_round_guide.py`.
