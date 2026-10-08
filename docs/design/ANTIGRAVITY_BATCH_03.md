# Antigravity — пакет генерації №3: пласкі подушки листя (PA-102U, 2026-10-05)

> Власник: Technical Artist · Статус: **готово до генерації** (генерує власник в Antigravity / Nano Banana) · Промпти англійською.
> ⚑ **2026-10-08: пункти Yamazakura — A3, B2, B3, D3, D5 — замінено [пакетом №4](ANTIGRAVITY_BATCH_04.md) (Y1–Y6). НЕ генерувати їх за цим брифом**: вони повторюють кленоподібне листя `sheet_pads_yamazakura_v1.png`. Momiji і Kuromatsu (A1, A2, B1, D1, D2, D4) — без змін.
> Підстава: [ADR-0004](../adr/0004-procedural-tree-rendering.md) ред. 8 §2 (рівномірний масштаб спрайта; вимоги до пакета №3), [TD-084](../decisions/TECH_DECISIONS.md), контракт атласу TD-073 ([`Art/Runtime/Preview/README.md`](../../Assets/_Project/Art/Runtime/Preview/README.md)). Попередній пакет — [ANTIGRAVITY_BATCH_02.md](ANTIGRAVITY_BATCH_02.md). Задача — [PA-102U](../tasks/PA-102U.md).

## 0. Навіщо

Спрайт подушки тепер масштабується лише рівномірно, а пропорцію подушки задає компонування (TD-084). В атласах пакета №2 є лише «купки»: видима h/w (`innerH/innerW`) 0.45–0.62, у Kuromatsu всі 0.47–0.49. Через це верхня подушка з одного спрайта завжди вища за ціль, а з 2–3 спрайтів виходить «гусениця». Потрібні **пласкі** спрайти з h/w **0.30–0.40**: щонайменше 2 на клас L і M, у тому самому атласі породи, з ідентичними іменами й ректами в сезонних атласах.

Урок пакета №2: промпт «wider than tall (about 2:1)» дав купки 0.46–0.62. Тож у пакеті №3 пропорцію задає не текст, а **напрямний макет** (guide): сірі силуети потрібної пропорції на magenta, які генератор перемальовує листям.

## 1. Еталон: пропорції подушок

Виміряно на масках листя `ref_mask_l2_momiji_foliage.png` і `ref_mask_night_kuromatsu_foliage.png` (компоненти зв'язності, яруси знизу вгору). **bbox** вимірюється так само, як маска харнесу й `innerH/innerW`. **Тіло** — медіана товщини колонок у середній половині ширини, поділена на ширину (тобто без горба й бахроми).

| Ярус | L2 Momiji, bbox h/w | тіло | Night Kuromatsu, bbox h/w | тіло |
|---|---|---|---|---|
| #1 (R, нижній) | 0.46 | 0.38 | 0.47 | 0.40 |
| #2 (L) | 0.47 | 0.41 | 0.48 | 0.41 |
| #3 (R) | 0.43 | 0.36 | 0.46 | 0.37 |
| #4 (L) | 0.44 | 0.36 | 0.45 | 0.35 |
| #5 (R) | 0.40 | 0.33 | 0.41 | 0.32 |
| #6 (L, верхній бічний) | 0.39 | 0.31 | 0.40 | 0.30 |
| купол ∪ #7 | 0.53 | 0.41 | 0.52 | 0.36 |
| *Пакет №2, для порівняння* | *0.45–0.62* | *0.33–0.54* | *0.47–0.49* | *0.39–0.42* |

bbox збігається з таблицею GD (BALANCE §1.4.0) до 0.01.

**Форма** (однакова в обох еталонах):
- Верх — пологий купол. На 10% ширини верх нижчий за вершину на 0.22–0.44 h, на 25% — на 0.10–0.23 h.
- Низ майже рівний.
- Кінці сходять на клин: товщина на 8% і 92% ширини становить 0.31–0.59 від максимуму, тупої «стінки» немає.
- Зовнішній кінець провисає (низ на 0.88–0.99 h), внутрішній, біля стовбура, піднятий (0.59–0.77 h).
- Тіло займає 0.75–0.88 висоти bbox; горб і бахрома додають решту.

**Світло.** Нижні ≈ 40% висоти темніші на 20–35%: в L2 на освітленому боці яскравість падає з 192 до 128, на тіньовому — з 126 до 100; у night — з 60 до 35. Світло йде зліва, тому ліві подушки світліші за праві.

**Висновок.** Верхні яруси еталона мають 0.39–0.41 (тіло 0.30–0.33), нижні — 0.46–0.48. Пласкі спрайти 0.30–0.40 разом із круглими 0.46–0.62 дають атласу неперервну смугу 0.30–0.62. Тож GD може задати смугу будь-якого ярусу, і `Pick` знайде в ній щонайменше 2 кандидати.

Асиметрію «кінець провисає назовні» **не замовляємо**. Дзеркалення спрайта визначає окремий хеш (ADR-0004), тож половина спрайтів провисала б до стовбура.

Робочі скрипти вимірів (поза репо): `.agent-workspace/pa102u-ta/{measure_ref,profile_ref,measure_atlas,solid_ellipse,pack_sim,make_guides}.py`.

## 2. Склад пакета

| # | Файл (`Assets/_Project/Art/Source/D_Sheets/`) | Що | Вхідні зображення (у цьому порядку) | Пріоритет |
|---|---|---|---|---|
| A1 | `sheet_pads_momiji_flat_v1.png` | 10 пласких подушок: 3 S · 3 M · 2 L · 2 L | `D_Sheets/guides/guide_flat_pads_1024.png`, `A_Concept/concept_day.jpg`, `D_Sheets/sheet_pads_momiji_v1.png` | 1 |
| A2 | `sheet_pads_kuromatsu_flat_v1.png` | те саме | guide, `concept_day.jpg`, `sheet_pads_kuromatsu_v1.png`; опційно 4-м — `A_Concept/concept_night.jpg` (форма полиць) | 1 |
| ~~A3~~ | ~~`sheet_pads_yamazakura_flat_v1.png`~~ | **замінено пакетом №4, Y2** (`sheet_pads_yamazakura_flat_v2.png`) | — | — |
| B1 | `sheet_pads_momiji_autumn_flat_v1.png` | осіння версія **A1** (перефарбування, ті самі силуети) | збережений A1, `sheet_pads_momiji_autumn_v1.png` | 2 |
| ~~B2~~ | ~~`sheet_pads_yamazakura_bloom_flat_v1.png`~~ | **замінено пакетом №4, Y4** (`sheet_pads_yamazakura_bloom_flat_v2.png`) | — | — |
| ~~B3~~ | ~~цвітіння круглого аркуша v1~~ | **замінено пакетом №4, Y3**: ім'я `sheet_pads_yamazakura_bloom_v2.png` тепер означає цвітіння **нового** круглого аркуша `sheet_pads_yamazakura_v2.png` | — | — |
| D1–D2 | `sheet_domes_{momiji,kuromatsu}_v1.png`, 1376×768 (16:9) | 2 куполи (`role:dome`) з h/w 0.46 і 0.50 | `D_Sheets/guides/guide_domes_1376x768.png`, `concept_day.jpg`, аркуш породи v1 | 3, **опційно, після рішення GD** |
| ~~D3~~ | ~~`sheet_domes_yamazakura_v1.png`~~ | **замінено пакетом №4, Y5** (`sheet_domes_yamazakura_v2.png`) | — | — |
| D4 | `sheet_domes_momiji_autumn_v1.png` | перефарбування D1 | збережений D1, сезонний аркуш v1 | 3, опційно |
| ~~D5~~ | ~~`sheet_domes_yamazakura_bloom_v1.png`~~ | **замінено пакетом №4, Y6** (`sheet_domes_yamazakura_bloom_v2.png`) | — | — |

**Чому B3** (⚑ тепер Y3 пакета №4, на основі нового листя). Поточний `pads_yamazakura_bloom` має **інші силуети**: 10 подушок (3+3+2+2) проти 12 у базі, плюс темно-рожеву «підкладку» під кожною. Контракт сезону (ті самі імена → ті самі ректи, ADR-0004 ред. 8) він не виконує.

Осінній круглий аркуш `sheet_pads_momiji_autumn_v1.png` перегенеровувати **не треба**: його розкладка та сама, що в Momiji v1, а ректи й альфу TA візьме з бази (§6).

**Чому D опційно.** Купол Momiji має 0.58, Yamazakura — 0.57; обидва вищі за `PadDomeMaxAspect` 0.54 (ADR-0010 ред. 4 §9). Без стиснення по Y вони дадуть `PadAspectOverflow`, поки купол складається з одного спрайта. Пласкіші куполи чи компонування — вирішує GD (BALANCE §1.7 ред. 3).

**Yamazakura.** Листя пакета №2 вийшло кленоподібним: пальчасті лопатеві листки замість цілісного овального листка вишні (README Preview, «Обмеження»). Пласкі спрайти мали б **повторювати** це листя, інакше в одній кроні змішаються два види листя. ⚑ **Рішення власника 2026-10-08: перегенерувати всі аркуші Yamazakura** — [пакет №4](ANTIGRAVITY_BATCH_04.md). Там новий круглий аркуш `sheet_pads_yamazakura_v2.png` (Y1) іде першим, а пласкі (Y2), цвітіння (Y3, Y4) і куполи (Y5, Y6) робляться вже з нього. Тому A3, B2, B3, D3 і D5 цього брифу **не генерувати**.

### 2.1. Розкладка аркуша A (1024×1024; ряд = клас, TD-073)

| Ряд | Клас | К-сть | Ширина силуету, px | Цільова h/w (`innerH/innerW` після ключа) |
|---|---|---|---|---|
| 1 | S | 3 | 220 | 0.34 · 0.37 · 0.40 |
| 2 | M | 3 | 260 | 0.31 · 0.35 · 0.39 |
| 3 | L | 2 | 430 | 0.30 · 0.37 |
| 4 | L | 2 | 430 | 0.34 · 0.40 |

- **Силуети guide на 0.02 пласкіші за ціль**: кінчики листків за контуром додають 0.02–0.03 до bbox альфи. Координати клітинок, класи й цілі — у `D_Sheets/guides/guides_batch03.json`.
- **Проміжки**: щонайменше 57 px по горизонталі (ряд M), ≈ 130 px між рядами, 50 px і більше до краю. Пакет №2 втратив 2 клітинки через злиплі подушки (ряд 2 Momiji).
- **Ширини класів** близькі до пакета №2 (L 450–510 px, M 270–310, S 190–255). Так листок займає ту саму частку подушки, що й у круглих спрайтах того ж класу, і на екрані має той самий розмір. Yamazakura L у пакеті №2 вужчі (317–337): пласкі L будуть ширші за рахунок більшої кількості листків, а не більшого листка.
- **Розмір аркуша.** У пакеті №2 Antigravity видав 1024² замість замовлених 2048² (а для 16:9 — 1376×768), тож план розраховано під 1K. Якщо вийде 2K, TA зменшить аркуш рівномірно ×0.5.
- **Цілі.** На клас 3 рівні з кроком ≈ 0.04, тож у будь-якій смузі GD шириною 0.06 і більше є щонайменше 2 кандидати HRW. L має 4 клітинки, M — 3, тож навіть після відсіву 2 L і 1 M лишиться ≥ 2 пласкі спрайти на клас. У класі ≤ 32 спрайти (валідатор ADR).

### 2.2. Краї для стикування колонок

Подушка з 2–3 пласких спрайтів, що перекриваються на ≈ 30–40%, має читатися як одна. Будь-який спрайт може опинитися крайнім, а дзеркалення випадкове, тому кожен спрайт — **цілісна** подушка, але з такими вимогами:
- **Кінці** сходять на клин (товщина на 8% і 92% ширини — 0.30–0.65 від максимуму, як в еталона) і стають повітрянішими: окремі листки або віяла хвої з дрібними просвітами. **Без** замкненого контуру й без темної «чорнильної» обвідки на кінцях.
- **Низ** майже рівний. Темна смуга низу займає однакову частку висоти в усіх спрайтах породи (≈ нижні 35–40%), щоб низи сусідів зливалися в одну тінь.
- **Горизонтальний градієнт світла** майже відсутній, інакше кожна колонка дасть світлу пляму зліва («зебра»).
- **Повітряна зона** кінця займає не більше 12% ширини, щоб суцільний еліпс покриття кінчиків (`PadSolidRadiusX/Y` 0.30/0.22 квада) лишався в α ≥ 0.5.

«Обрізані» напівспрайти (з одним зрізаним кінцем) **не замовляємо**: контракт не має ролі «сегмент», а `Pick` чи дзеркалення можуть поставити зріз на край подушки. Якщо пласкі спрайти не пройдуть перевірку шва (S1 у §5.2), треба буде окреме рішення з UA про роль сегмента.

### 2.3. Світло, фон

- **Світло — як у подушок пакета №2**, бо спрайти змішуються з ними в одній кроні. М'яке розсіяне світло зліва-згори, світліший верх, запечений темніший низ (`PadUndersideShade` = 0), без бліків, rim-light і сонячних плям. Вимога «без світла зверху» з пакета №2 стосувалась кори (там світло дає шейдер); для подушок вона не діє.
- **Фон** — суцільний `#FF00FF` (RGB 255, 0, 255) без тіней, ореолу, підкладки, градієнта, віньєтки й текстури. Формат — лише PNG. Якщо генератор уміє справжню прозорість — краще прозорість.

## 3. Що робити (виконавцю)

1. **A1 → A2** (A3 — див. [пакет №4](ANTIGRAVITY_BATCH_04.md), Y2). Прикріпити зображення в порядку з таблиці §2, вставити промпт A (§4.1) з підстановками, згенерувати 3–4 варіанти, перевірити за §5.1 і зберегти **один** найкращий під назвою з таблиці.
2. Якщо всі варіанти ігнорують guide (купки, сірі плями, інша кількість подушок) — взяти промпт A′ (§4.2, без guide).
3. **B1** робити на основі **збереженого** A1; промпт B (§4.3). B2 і B3 замінено пакетом №4 (Y4, Y3). Для перевірки перемикати базовий і сезонний аркуші: силуети не мають «стрибати».
4. **D1, D2, D4** — лише якщо оркестратор або GD підтвердить; промпт D (§4.4). D3 і D5 замінено пакетом №4 (Y5, Y6).
5. Якщо не вистачає квоти, порядок такий: **A1–A2 → B1 → D**. Yamazakura — окремо, за пакетом №4.
6. Більше нічого в репозиторії не змінювати.

## 4. Промпти

### 4.1. A — пласкі подушки (з guide)

```
Use the attached images:
Image 1 — LAYOUT GUIDE: ten flat grey silhouettes on a solid magenta background, in four rows.
Image 2 — STYLE reference: a painterly bonsai illustration.
Image 3 — FOLIAGE reference: a sprite sheet of [SPECIES] foliage pads from the same game.

Create a game sprite sheet PNG with exactly the same canvas size and layout as image 1. Replace each grey silhouette with one painted foliage pad of [FOLIAGE]. Every pad must cover its grey silhouette and keep its width, height and position: a low, wide, gently domed layer of foliage, like the flat cloud-shaped pads of the styled bonsai in image 2 — never a tall mound, ball or hemisphere. Only small leaf tips may poke out of the silhouette outline.

Each pad is one continuous cluster seen from the side: gently domed top, almost straight underside, the lower third in a soft darker shade of the same foliage. Both ends taper softly and get slightly airier, with a few loose leaves and no hard outline or dark contour, so that two pads placed side by side with some overlap read as one longer pad. Left and right ends look alike; the pad does not lean or droop to one side.

Match image 3 exactly in leaf shape, leaf size in pixels, colour palette, brushwork and lighting: soft diffuse light from the upper left, lighter leaves on top, darker underside, almost no left-to-right brightness change across a pad. No glossy highlights, no rim light, no glow. Soft painterly 2D style of image 2.

Background: solid flat pure magenta #FF00FF (RGB 255, 0, 255) everywhere outside the pads, with no shadows, no gradient, no vignette, no texture and nothing left of the grey guide shapes. Keep clean magenta gaps between pads: no pad touches or overlaps another. Nothing else in the image: no trunk, no branches, no twigs, no stems, no pot, no ground, no grass, no text, no numbers, no labels, no frame.
```

| # | `[SPECIES]` | `[FOLIAGE]` | Дописати в кінець |
|---|---|---|---|
| A1 | `Japanese maple (momiji)` | `small five-lobed Japanese maple leaves, fresh green #5C8A42 with lighter #8DB86B tips` | — |
| A2 | `Japanese black pine (kuromatsu)` | `dense Japanese black pine needle fans, dark green #2E4F2D with lighter #4E7A4A needle tips, needles radiating outward so the outline is softly spiky` | лише якщо 4-м додано `concept_night.jpg`: `Image 4 shows only the shape of flat pine foliage shelves — use the daylight colours of image 3, no night tint, no fireflies, no glow.` |
| ~~A3~~ | — | **замінено пакетом №4** (§5.3 там). Цей рядок давав `exactly the same olive-green leaves as in image 3`, тобто копіював кленоподібне листя v1 | — |

### 4.2. A′ — запасний, без guide (прикріпити лише image 2 і image 3 з таблиці §2, перенумерувати як image 1 і 2)

```
Game sprite sheet PNG, 1024×1024, on a solid flat pure magenta #FF00FF background (RGB 255, 0, 255): ten separate flat foliage pads of [FOLIAGE] for a stylized 2D bonsai, matching the foliage sprite sheet in image 2 and the painterly style of image 1. Four rows with wide clean magenta gaps: row 1 — three small pads about 220 px wide; row 2 — three medium pads about 260 px wide; rows 3 and 4 — two large pads each, about 430 px wide. Every pad is very flat: its height is only about one third of its width (row 1: 75–88 px tall; row 2: 80–100 px; rows 3–4: 130–170 px) — a low, wide, gently domed layer of foliage, never a mound, ball or hemisphere.

Each pad is one continuous cluster seen from the side: gently domed top, almost straight underside, the lower third in a soft darker shade of the same foliage. Both ends taper softly and get slightly airier, with a few loose leaves and no hard outline or dark contour. Left and right ends look alike. Same leaf shape, leaf size, colours and lighting as image 2: soft diffuse light from the upper left, almost no left-to-right brightness change, no highlights, no rim light, no glow.

No shadows, gradient or texture on the background; no pad touches another. No trunk, no branches, no twigs, no pot, no ground, no text, no numbers, no labels, no frame.
```

### 4.3. B — сезонне перефарбування

```
Use the attached images:
Image 1 — a finished sprite sheet of foliage pads on a solid magenta #FF00FF background.
Image 2 — colour reference for [SEASON] only.

Repaint image 1 as its [SEASON] version. Keep the canvas size and every pad's silhouette, size, position and leaf arrangement exactly the same as in image 1 — the same leaves in the same places — and change only the colours: [SEASON_FOLIAGE], in the palette of image 2. Keep the same lighting: lighter top, soft darker underside in a deeper tone of the same colours, light from the upper left. Do not add, remove, move or resize pads; nothing may extend outside the existing pad outlines. Do not copy the pad shapes of image 2 and do not add any dark backing, halo or shadow behind or under the pads. The background stays solid flat pure magenta #FF00FF with no shadows or gradient. No text.
```

| # | Image 1 | Image 2 | `[SEASON]` | `[SEASON_FOLIAGE]` |
|---|---|---|---|---|
| B1 | збережений A1 | `sheet_pads_momiji_autumn_v1.png` | `autumn` | `golden ochre #D98A3C and crimson #A33B29 maple leaves` |
| ~~B2~~ | — | — | — | **замінено пакетом №4, Y4** (§5.4 там: квіти разом із бронзовими молодими листками) |
| ~~B3~~ | — | — | — | **замінено пакетом №4, Y3** |
| D4 | збережений D1 | `sheet_pads_momiji_autumn_v1.png` | як B1 | як B1 |
| ~~D5~~ | — | — | — | **замінено пакетом №4, Y6** |

### 4.4. D — куполи (опційно)

```
Use the attached images:
Image 1 — LAYOUT GUIDE: two grey silhouettes on a solid magenta background.
Image 2 — STYLE reference: a painterly bonsai illustration.
Image 3 — FOLIAGE reference: a sprite sheet of [SPECIES] foliage pads from the same game.

Create a game sprite sheet PNG with exactly the same canvas size and layout as image 1. Replace each grey silhouette with one painted crown dome of [FOLIAGE] — the topmost foliage cap of a styled bonsai, like the top of the tree in image 2: wider than tall, a soft rounded top with two or three gentle bumps, an almost straight underside with a soft darker shade, slightly airy edges with a few loose leaves and no dark contour. Each dome must cover its grey silhouette and keep its width, height and position; only small leaf tips may poke out.

Match image 3 exactly in leaf shape, leaf size in pixels, colour palette, brushwork and lighting: soft diffuse light from the upper left, lighter top, darker underside, no highlights, no rim light, no glow. Background: solid flat pure magenta #FF00FF everywhere outside the domes, no shadows, no gradient, nothing left of the grey guide shapes; the two domes do not touch. No trunk, no branches, no twigs, no pot, no text, no labels, no frame.
```
Підстановки `[SPECIES]` і `[FOLIAGE]` — ті самі, що в A1–A2. Куполи Yamazakura — пакет №4, Y5.

## 5. Приймання

### 5.1. Виконавець (на око, до збереження)

1. Кількість і розкладка — як у guide: для A це 10 подушок у рядах 3-3-2-2, для D — 2. Кожну оточує чиста magenta, жодні дві не торкаються.
2. Подушки пласкі: приблизно втричі ширші, ніж високі. Не купки й не півкулі; поруч із guide силует помітно не вищий за сіру форму.
3. Немає сірих залишків guide, стовбура, гілок, тексту. Фон рівно-маджентовий, без тіні чи ореолу під подушками.
4. Листя, колір і світло — як в аркуші породи v1 (відкрити поруч). Кінці подушок повітряні, без темної обвідки; низ темніший.
5. Сезон (B1, D4): при перемиканні з базовим аркушем силуети лишаються на місці.

### 5.2. TA (виміри скриптом збірки; провал відсіює клітинку, а не весь аркуш)

| # | Перевірка | Поріг |
|---|---|---|
| P1 | Пропорція `innerH/innerW` (щільний bbox α > 0 після ключа) | 0.28–0.42 (ціль ± 0.03). У класах L і M ≥ 2 прийняті, з них хоча б один ≤ 0.34 і хоча б один ≥ 0.36; у S ≥ 1 |
| P2 | Тіло / висота bbox (медіана товщини середньої половини ширини) | ≥ 0.72 (еталон 0.75–0.88, пакет №2 0.74–0.91): довгі поодинокі листки не роздувають рект |
| P3 | Клин кінців: товщина на 8% / 92% ширини від максимуму | 0.30–0.65 (еталон 0.31–0.59) |
| P4 | Еліпс покриття кінчиків (центр 0.5 / 0.5 квада, радіуси 0.30 / 0.22) | ≥ 98% текселів з α ≥ 0.5 (пакет №2: 98.3–100%) |
| S1 | Шов: пари пласких спрайтів одного класу з перекриттям 35% на спільній лінії низу, з дзеркаленням і без | у зоні перекриття немає темнішого контуру (яскравість 6-px смуги краю ≥ 0.9 × інтер'єру); висота темного низу в парі відрізняється ≤ 20%; зовнішні 15% ширини — в межах ± 10% яскравості центру; колаж для власника |
| K1 | Ключ | `pink_fringe_share` 0.00% (`key.py`), жодного тексела з маджентовим відтінком на краю |
| K2 | Розділення | 1 компонента на клітинку guide (± 40 px) після дилатації 15 px; компоненти поза клітинками < 4000 px (інакше клітинку відсіяно, `EXCLUDE`) |
| K3 | Залишок guide | частка сірих текселів (насиченість < 0.08, value 0.5–0.7) усередині подушки ≤ 1% |
| L1 | Колір і світло | середній Lab пласких проти круглих спрайтів породи: ΔE ≤ 6; нижні 40% висоти темніші за верхні 30% на 15–40% |
| Z1 | Розмір | аркуш 1024² (2048² → рівномірно ×0.5); `innerW` у межах ± 15% ширини силуету guide (лише рівномірний масштаб) |
| Z2 | Сезон | імена й ректи ≡ база (валідатор), альфа = база; залишковий зсув реєстрації ≤ 2 px; фарба сезону покриває ≥ 97% α ≥ 0.5 бази |
| Z3 | Атлас | розмір і вага виміряні (§7); валідатор ADR-0004 ред. 8 без помилок |

## 6. Обробка й контракт (TA, після генерації)

- **Скрипт.** Розширити `tools/art/preview/build_pads_d5.py`: другий аркуш породи йде в той самий атлас. Клітинки беруться з `guides_batch03.json`, а не з кластеризації рядів; клас — за рядом (TD-073).
- **Імена.** Пласкі — `<p>f<ряд><колонка>` (`mf11` … `mf42`, `kf..`, `yf..`), куполи D — `<p>d1`, `<p>d2` з `"role":"dome"`. Імена прив'язані до клітинки guide, тож відсів або перегенерація однієї клітинки не перейменовує інших, і ключ HRW лишається стабільним. Літера `f` — лише для людей: код ім'я не розбирає (ADR-0004 ред. 8: мітки класу пропорції немає).
- **Рект** = щільний bbox α > 0 + `padPx` 12, pivot 0.5 / 0.2 від bbox, `innerW/innerH` = bbox. Без ресемплу у фіксовану клітинку й без нерівномірного масштабу.
- Пласкі й круглі спрайти породи — **в одному атласі** (1 матеріал, 1 draw call).
- **Сезонні атласи** (`pads_momiji_autumn`, `pads_yamazakura_bloom`):
  - імена, ректи, pivot, `innerW/innerH` і альфа — копія базового атласу;
  - RGB — із сезонного аркуша, зареєстрованого на клітинку бази (зсув ≤ 2 px); прогалини фарби всередині альфи заповнюються push-pull;
  - наслідок: імена `a*` / `b*` пакета №2 стають `m*` / `y*`. Код сезонні атласи ще не читає, тож перейменування нічого не ламає.
- **Валідатор** (ADR-0004 ред. 8): `innerW ≤ w − 2·padPx + 1` (так само для `h`), `a ∈ [0.2, 1.2]`, унікальні імена, ≤ 32 спрайти на клас, сезонний атлас ≡ базовому.
- **Звіт:** таблиця `name · size · a · тіло · клин · еліпс`; колажі (аркуш, пари на шві, крона з пласкими верхніми подушками поруч з L2) — у `Temp/art-preview/batch03/`; оновити README Preview і додати рядок TD (атлас і розмір).

## 7. Asset Diet

**Вимір.** Пакування симульовано тим самим skyline-пакувальником, що й у збірці (`build_pads.pack`): поточні ректи з JSON + 10 пласких ректів (силует guide × 1.05 + 2·`padPx`). Пласкі ректи займають 0.52 площі 1024².

| Атлас | Зараз (заповнення) | + пакет №3, правило TD-073 (1024² / 2048×1024) | Мінімальна ширина при висоті 1024 (крок 128) |
|---|---|---|---|
| momiji (і autumn) | 1024² (0.64) | 2048×1024 (0.58) | 1536; з куполами D — 1792 |
| kuromatsu | 2048×1024 (0.39) | 2048×1024 (0.65) | 1792; з куполами — 2048 |
| yamazakura (і bloom v2) | 2048×1024 (0.33) | 2048×1024 (0.59) | 1536; з куполами — 2048 |

**Вага у збірці.** Нижче — сирі дані ASTC з повним ланцюгом mip, у МБ. Download буде ≈ 0.85–0.95 від цього (ASTC погано стискається); підтвердити через `bundletool` / `BuildReport`.

| Варіант | momiji | kuromatsu | yamazakura | autumn | bloom | Σ ASTC 8×8 | Δ до зараз | Σ ASTC 6×6 |
|---|---|---|---|---|---|---|---|---|
| Зараз (PA-102T) | 1024² | 2048×1024 | 2048×1024 | 1024² | 1024² | 2.45 | — | 4.38 |
| A. TD-073 як є | 2048×1024 | 2048×1024 | 2048×1024 | 2048×1024 | 2048×1024 | 3.50 | +1.05 | 6.26 |
| B. Ширина атласу кратна 128 | 1536×1024 | 1792×1024 | 1536×1024 | 1536×1024 | 1536×1024 | 2.71 | +0.26 | 4.84 |
| **C. B + сезонні атласи в ½** | 1536×1024 | 1792×1024 | 1536×1024 | 768×512 | 768×512 | **1.92** | **−0.53** | 3.44 |
| З куполами D (A / B / C) | | | | | | 3.50 / 3.32 / 2.34 | | |

**Бюджет** (ARCHITECTURE §10). Скафолд — 25.06 МБ Android. Прогноз продукту (+2.9…4.4: шрифт, аудіо, арт, код) дає 28.0–29.5 МБ при цілі 28 і межі 30. ADR-0004 (ризик 4) закладав на атласи подушок 1.5–2 МБ.
- Варіант **A** (3.5 МБ) перевищує закладене на 1.5–2 МБ: Android ≈ 29.5–31, тобто реальний ризик пробити межу 30.
- Варіант **C** (1.92 МБ) вкладається в закладене.
- Варіант **B** на ≈ 0.7 МБ понад закладене.

**Рекомендація TA:** варіант **C** з **ASTC 8×8** для всіх атласів подушок. ASTC 6×6 — лише для базових атласів і лише якщо 8×8 дасть видимі блоки на пристрої (+0.4–0.5 МБ на атлас).

Сезонні атласи в ½ роздільності: сезон видно лише частину року, і він змішується через `_SeasonBlend`. Листок на ретині стане м'якшим — це треба перевірити на пристрої; якщо не підійде, повертаємося до B.

Обидва відхилення від TD-073 потребують рішення UA:
1. Ширина атласу — найменша кратна 128 при висоті 1024. ASTC не потребує степеня двійки, mip на NPOT підтримують GLES3, Metal і Vulkan; 1536 і 1792 кратні блоку 8.
2. `_SeasonTex` має ½ роздільності бази з тією ж UV-розкладкою. Валідатор тоді порівнює ректи в UV (рект / розмір атласу), а JSON сезонного атласу або копіює базовий, або не потрібен.

**Репо** (не збірка, LFS): PNG у Preview додадуть ≈ 2–3 МБ, нові джерела — 6–8 аркушів по 1.2–1.4 МБ. Guide-файли: `guide_flat_pads_1024.png` 7.7 КБ, `guide_domes_1376x768.png` 5.8 КБ, `guides_batch03.json` 2.2 КБ.
