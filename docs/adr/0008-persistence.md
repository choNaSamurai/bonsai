# ADR-0008: AtomicFileStore з SHA-256 envelope, `.bak` і Poison-Pill захистом

- **Статус**: Proposed
- **Дата**: 2026-10-03 · **Ревізія 2**: 2026-10-05 (схема сейву дерева після PA-102/PA-102S) · **Доповнення 2a**: 2026-10-05 (PA-102P: `lastCutDay`, `plantedRulesVersion`, семантика `rulesVersion`; статус — Proposed)
- **Автор**: Unity Architect
- **Погоджує**: власник проєкту
- **Пов'язані задачі**: PA-205, PA-102, PA-102S, PA-102P

## Контекст
ТЗ §5.3 / Архітектура §2.3: атомарний запис, захист від ENOSPC, bit-rot, урізаних файлів, «отруєного» перезапису бекапу.

## Рішення
- `AtomicFileStore` (Infrastructure, реалізує порт `ISaveStore`):
  1. `TreeSnapshot` + `SoilState` + метадані → JSON (Newtonsoft, `schemaVersion`) → UTF-8 → SHA-256 → `[64 hex][json]`.
  2. Запис у `tree_state.json.tmp` (`FileStream`, `Flush(true)`).
  3. `File.Replace(tmp, primary, bak)`; якщо primary ще не існує — `File.Move`. Fallback на `IOException` — `Copy + Delete`.
  4. Читання: `length <= 64` → corrupted; хеш не збігся → `.bak`; успіх з `.bak` → **негайний перезапис primary**; битий файл → `quarantine/<timestamp>.json`.
- Налаштування — `PlayerPrefs` (лише примітиви).
- Сейв викликається: після кожної дії (debounce 2 с) та синхронно в `OnApplicationPause(true)`.

### Схема сейву дерева — `schemaVersion: 1`
DTO живуть в Infrastructure (`TreeSaveDto`, `NodeSaveDto`) і мапляться з/у `TreeSnapshot`; доменні типи не мають атрибутів серіалізації (Domain без пакетів). Усі поля **обов'язкові** (`Required.Always` або явна валідація DTO): відсутнє поле = пошкоджений файл → `.bak`/карантин, а не тихе значення за замовчуванням.

```jsonc
{
  "schemaVersion": 1,
  "rulesVersion": 2,            // GrowthRules.Version збірки, що записала файл (ADR-0003 §8): 1 — PA-102S, 2 — PA-102P
  "savedAtUtcMs": 0,
  "tree": {
    "genome":  { "seed": 0, "species": "Momiji", "vigor": 1.0, "bendBias": 0.0 },   // TreeGenome, канонічний (TD-017)
    "growth":  { "hourCounter": 0, "dayIndex": 0, "shootCredit": 0.0,
                 "sumGrowth": 0.0, "sumMoisture": 0.0, "frozen": true },             // GrowthState (TD-015)
    "plantedRulesVersion": 2,   // (2a) GrowthRules.Version на момент посадки; незмінне
    "nextId":  3,
    "version": 0,
    "nodes": [ {
      "id": 0, "parent": -1, "depth": 0,
      "length": 20.0, "targetLength": 48.0, "thickness": 4.0,
      "baseAngle": 0.0, "currentAngle": 0.0, "curvature": 0.0,
      "attachT": 1.0, "branchSide": 0,
      "ageDays": 0, "status": "Mature", "leafScale": 0.0, "flowerStage": 0.0,
      "wiredAtUtcMs": 0, "wireProgressDays": 0,
      "lastCutDay": -1             // (2a) GrowthDayIndex останнього зрізу дитини; -1 = не було
    } ]
  },
  "soil": { /* SoilState, PA-202 */ },
  "meta": { /* онбординг, Dormancy, якір часу — PA-204/PA-106 */ }
}
```

- **Нові поля відносно редакції 1**: вузол — `targetLength`, `branchSide`, `attachT`, **(2a)** `lastCutDay`; дерево — `genome`, `growth`, `nextId`, **(2a)** `plantedRulesVersion`.
- **Не зберігаються**: похідні `TreeShapeAnalysis` (`ord`, ланцюг, `TipDistance`, `IsLeafy`, `PadId`), `HasLeaves`, `TierPhase`, світові позиції, стан RNG (ADR-0003 §2–§3).
- **Порядок вузлів** у масиві = топологічний порядок `TreeGraph`; `parent` — індекс. Після читання — `TreeSnapshot.Validate` (інваріанти ADR-0003 §2); провал → як битий хеш.
- **`double` — round-trip побітово**: серіалізатор пише `double` форматом `"R"`/`G17` (кастомний `JsonConverter`, не покладатися на дефолт рантайму) і читає з `CultureInfo.InvariantCulture`; NaN/∞ у сейві заборонені. Тест: 100 випадкових дерев → JSON → снапшот `BitwiseEquals` — в EditMode **і** на пристрої (IL2CPP), бо форматування `double` у Mono BCL історично мало розбіжності з .NET Core.
- `status`, `species` — рядками (стійкість до перенумерації enum).
- Розмір: ≈ 300 Б JSON на вузол → ≈ 60 КБ на 200 вузлів — нормально для сейву, але **не** для Хроніки (< 5 КБ/день, ADR-0003): Хроніка зберігає компактний бінарний знімок (квантизовані до 16 біт лише рендер-поля: parent, attachT, length, thickness, currentAngle, curvature, branchSide, status, leafScale ≈ 22 Б/вузол ≈ 4.4 КБ) — окреме рішення задачі Хроніки.

### Міграції
- **Сейвів у гравців ще немає** → PA-102S не потребує мігратора: стартуємо з `schemaVersion: 1`, що вже містить усі поля вище. Фікстури снапшотів PA-102 перегенеровуються (новий потік RNG, BALANCE §1.2.9).
- Майбутні зміни схеми — `ISaveMigrator` `vN → vN+1` по ланцюжку; правило для нових полів вузла — явне значення за замовчуванням у мігратора (напр., якби `attachT` з'явився пізніше: `1.0` — «кріплення на кінчику», поведінка до PA-102S), а не дефолт серіалізатора (`0.0`).
- **`rulesVersion`**: сейв зберігає стан, а не історію, тому зміна правил росту після релізу не ламає наявні дерева — вони продовжують рости за новими правилами від поточного стану. Поле потрібне, щоб (а) логувати/аналізувати, (б) за потреби вмикати правило лише для нових дерев. Змінювати RNG-бюджет пагона після релізу — лише з інкрементом `rulesVersion` (ADR-0003 §6).

### Доповнення 2a (PA-102P): `lastCutDay` і `rulesVersion`
> **Узгодити з rev.3 при злитті з `develop`.** На `develop` ADR-0008 має rev.3 (PA-101b: `JsonUtility`, `double` → `long`-біти з суфіксом `Bits`, обов'язковість через `IsValid`, без Newtonsoft/`Required.Always`/`JsonConverter`). Розділ «Схема сейву дерева» вище написаний у термінах rev.2; при злитті rev.3 має пріоритет щодо **формату чисел і механізму обов'язковості**, а поля нижче переносяться без змін — обидва нові поля цілочисельні, тож правило `double → long`-біти їх не зачіпає.

| Поле | Де | Тип DTO | Валідація (`IsValid`, після `FromJson`) | Мапінг |
|---|---|---|---|---|
| `lastCutDay` | `NodeSaveDto` | `int` (як є, без `Bits`) | `−1 ≤ lastCutDay ≤ growth.dayIndex`; інакше — пошкоджений файл (`.bak`/карантин) | 1:1 з `NodeData.LastCutDay` (ADR-0003 rev.3 §2) |
| `rulesVersion` | корінь | `int` | `≥ 1` (0 = поле відсутнє → пошкоджений файл) | `GrowthRules.Version` збірки, що пише файл |
| `plantedRulesVersion` | `tree` | `int` | `1 ≤ plantedRulesVersion`; `≤ rulesVersion` не вимагаємо (відкат збірки) | `TreeSnapshot.PlantedRulesVersion` (ADR-0003 rev.3 §3) |

- **Обов'язковість.** Усі три поля обов'язкові в `schemaVersion: 1` — сейвів у гравців ще немає, тож вони входять у v1 без мігратора (як `attachT`). Для `rulesVersion`/`plantedRulesVersion` присутність перевіряється значенням (0 невалідний). Для `lastCutDay` 0 — **валідний** день (онбординговий зріз на Д0), а JsonUtility мовчки дає 0 для відсутнього поля, тому присутність `lastCutDay` гарантує не значення, а хеш envelope + `schemaVersion` (rev.3, правило 4: файл пишемо лише ми). Якщо колись знадобиться читати файли, що могли бути записані без поля, — це новий `schemaVersion` і мігратор, який явно ставить `−1`, а не дефолт серіалізатора.
- **Тест-гейт** (доповнює rev.3 п.6): round-trip 100 випадкових дерев **з випадковими `lastCutDay` ∈ {−1, 0, 1…dayIndex}** → JSON → `BitwiseEquals`; файл з `lastCutDay = −2` або `> dayIndex` → пошкоджений; файл без `rulesVersion` → пошкоджений.
- **Семантика `rulesVersion` (замінює абзац «`rulesVersion`» у «Міграціях»)** — ADR-0003 rev.3 §8: сейв = стан, дерево ніколи не переграється від seed, тому нова версія правил не змінює вже вирослу геометрію, лише ріст далі. `rulesVersion < поточної` — читаємо, ріст за новими правилами, подія `RulesUpgraded`; `rulesVersion > поточної` при тому самому `schemaVersion` — читаємо з попередженням; структурну несумісність ловить лише `schemaVersion`.

## Розглянуті альтернативи
| Варіант | Плюси | Мінуси |
|---|---|---|
| `PlayerPrefs` для всього | Просто | Ліміти розміру, не атомарно, немає бекапу |
| SQLite | Транзакції | +розмір, нативні бібліотеки, надмірно |
| Easy Save (asset) | Готово | Платний, менше контролю |

## Наслідки
- + Повний паритет з вимогами ТЗ; логіка тестується EditMode-тестами з тимчасовою текою (обрізані файли, битий хеш, відсутній `.bak`).
- − `File.Replace` на Android/iOS поводиться як rename у межах одного тому — підтверджується тестом на пристрої (PA-205).
- − Побітовий round-trip `double` через JSON треба підтвердити на IL2CPP-пристрої (PA-205).
