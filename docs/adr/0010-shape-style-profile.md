# ADR-0010: `ShapeStyleProfile` — стиль як накладка на параметри форми породи; стиль не зберігається в сейві (MVP)

- **Статус**: Proposed
- **Дата**: 2026-10-05 · **Ревізія 2**: 2026-10-05 (PA-102R: розміщення нових полів BALANCE §1.5 — §6; статус ревізії — Proposed) · **Ревізія 3**: 2026-10-05 (PA-102T: `SpeciesRenderProfile` — де живе, склад, мапінг, співіснування зі `ShapeStyleProfile`, Asset Diet кори — §7; розміщення полів BALANCE §1.6 — §8; уточнення правила 5 §6 для рендерних ручок; статус ревізії — Proposed) · **Ревізія 4**: 2026-10-05 (PA-102U: розміщення полів BALANCE §1.7 — §9: верх (шийка, ребра, стилізація S8/S11) — `SpeciesConfig`/`GrowthConfig`/`ShapeStyleProfile`/`StyleConfig`; основа — `PotConfig` (група «Основа»), `TreeRenderConfig`, `SoilVisualConfig`; видалення плеча k4, після прекчеку PA-102U (R1) — і `CapRibSlotT1`; доповнено за BALANCE §1.7 **ред. 2**: U-i «шийка не додає висоти» — `k_cap` лише в `TargetLength` c2 (`ShootBuilder`, без поля конфігу), `PadDomeMaxAspect` [RENDER], `NebariRootDescendShareMax` [RENDER], нові дефолти, `Validate` слотів осі крони — §9; статус ревізії — Proposed: числа й рішення — BALANCE §1.7, Accepted власником 2026-10-05; **доповнення 2026-10-05 (рішення власника після PNG, ADR-0004 ред. 8, TD-084)**: `PadSideMaxAspect*`/`PadDomeMaxAspect` — цілі компонування на візуальному bbox, а не стиснення по Y; розміщення полів компонування BALANCE §1.7 ред. 3 (GD) — §9.3; **доповнення 2 (2026-10-05, синхронізація з BALANCE §1.7.10 ред. 3)**: цілі на видимому bbox 0.42/0.024 і 0.52, нові поля `PadAspectMaxColumns`, `PadAspectBandBelowRatio`, `PadDomeAspectBandBelowRatio` (`FoliageConfig`), `PadTipShiftMaxRatio`, `PadTipSpriteMinScale` (`TexturedStyleConfig`) → production `TreeRenderConfig`, `Validate`; мінімальний крок і перекриття — похідні, не поля)
- **Автор**: Unity Architect
- **Погоджує**: власник проєкту
- **Пов'язані задачі**: PA-102Q, PA-102R, PA-102T, PA-102U, PA-007, PA-103a, PA-103b, PA-105B, PA-205; майбутня задача «дріт на стовбурі» (v1.1)

## Контекст
BALANCE §1.4.7 (Proposed, PA-102Q) і GDD §10.9 зводять усі параметри форми дерева в «ручки» — `ShapeStyleProfile`: лінія стовбура (σ, λ, джитер), висота й темп, товщина, яруси, купол, подушки, Небарі, еталон стилізації. Порода = профіль за замовчуванням; стиль (Moyogi / Chokkan / Shakan) = пресет. Зараз ці числа розкидані: частина — глобальні поля `GrowthConfig` (Domain), частина — `SpeciesConfig` (Domain, по породі), рендерні — `TreeRenderConfig`/`FoliageConfig` (DevTools-прототип), стилізація — `StyleConfig` (DevTools). ScriptableObject-обгорток ще немає (PA-007).

Обмеження:
- `Domain` — `noEngineReferences`, без атрибутів серіалізації (TD-019), математика в `double`; рендерні параметри в Domain не потрапляють (ADR-0002, ADR-0004).
- AC19 PA-102Q: профіль Moyogi (σ 1, λ 0) мусить давати **побітово** той самий `TreeSnapshot`, що й поточні прямі поля конфігу (30 сидів × 3 породи × Д0…Д150).
- AC16 PA-102Q / BALANCE §1.4.6: нових збережуваних полів немає, RNG-бюджет 8/18 без змін.
- **Рішення власника 2026-10-05**: гравцевий вибір стилю дротом на стовбурі — **v1.1**; у MVP усі дерева Moyogi; Chokkan/Shakan — лише пресети DevTools; нахил Shakan — **максимум 30°**.

## Рішення

> Рішення узгоджено з реалізацією GP, що вже йде в PA-102Q (`Domain/Growth/ShapeStyleProfile.cs`): стиль — **накладка** поверх наявних полів конфігу, а не перенесення всіх полів форми в новий тип. Обмеження вище вона виконує, а дифф і ризик для AC19 менші.

### 1. Типи і шари
| Тип | Шар / збірка | Що містить | Хто читає |
|---|---|---|---|
| **База форми** — наявні поля `GrowthConfig` (глобальні: `TrunkSegmentDecay`, `TrunkChainTaperRatio`, `PrimaryLengthFactor`, `DomeWeightMultiplier`, `TrunkLeanOffsetMaxDeg` = **30**, …) і `SpeciesConfig` (по породі: `TrunkSwayDeg`, `PrimaryAngleMeanDeg`, `CrownLateralAngleMeanDeg`, `BranchDroop`, `PadDepth`, …) | Domain | [MODEL]-числа BALANCE §1.4 — **єдине джерело** базових значень | `ShapeParams.Resolve` |
| **`ShapeStyleProfile`** | Domain, `sealed class`, публічні поля, без атрибутів серіалізації | Стиль-накладка: σ `TrunkSwayScale`, λ `TrunkLeanOffsetDeg`, `TrunkSwayJitterMinScale` (власні параметри) + **перевизначення** груп §1.4.7 (лінія, висота, товщина, яруси, купол); `NaN` = успадкувати базу. Пресети — фабрики (`Moyogi()` = σ 1, λ 0, без перевизначень) | `ShapeParams.Resolve` |
| **`ShapeParams`** | Domain, `readonly struct` | Розв'язані значення «база ⊕ стиль»; λ клампиться до ±`TrunkLeanOffsetMaxDeg` (30°, рішення власника) | `ShootBuilder`, `GrowthSimulator`, `GrowthFormulas` |
| **Рендер-профіль породи** (`NebariFlare*`, `NebariRoot*`, `SoilFrontMossHeightUnits`, `PuffRadius`, `PadFlatten`, `PadMaxRows*`) | DevTools (`TreeRenderConfig`/`FoliageConfig`) → Presentation `SpeciesRenderProfile` (PA-103a/b) | [RENDER]-ручки §1.4.7 | `TreeRenderer`, `NebariBuilder`, `FoliageMeshBuilder`, `SoilFrontView` |

`StyleConfig` (S1–S7, `PruneToStyle`) — **[STYLE]**, лишається в DevTools; переїде в Application, лише коли з'явиться ігровий споживач. Рендерні ручки в Domain **не** потрапляють (ADR-0002, ADR-0004).

### 2. Порода × стиль
- `SpeciesConfig.Style : ShapeStyleProfile` — стиль породи; **`null` = Moyogi на прямих полях конфігу** (у MVP так у всіх порід — рішення власника). Порода тюниться полями `SpeciesConfig`, стиль — накладкою.
- Пресети Chokkan / Shakan живуть **лише в DevTools**: харнес клонує `SpeciesConfig` і ставить `Style = пресет` (алокації в DevTools допустимі). У гру стиль не потрапляє.
- Перевизначення в пресеті **абсолютні**: Chokkan `CrownLateralAngleMeanDeg` 45° заміняє і Momiji 60°, і Kuromatsu 70°. Для DevTools-доказу досяжності (AC20 — Momiji) цього досить. Якщо ігровому стилю знадобиться «свій Chokkan для кожної породи», треба буде або відносні модифікатори, або пресет на породу. Це окреме рішення v1.1.
- `ShapeParams.Resolve` — чиста функція без алокацій (`readonly struct`, ≈ 20 полів). Зараз вона викликається на кожен пагін / `DailyStep`. Це допустимо, якщо AC17 (`DailyStep`@200 ≤ 30 µs, 0 B GC) проходить. Інакше треба резолвити один раз на `DailyStep` і передавати `in`.
- **Незмінність у рантаймі**: після Bootstrap профілі й конфіги не мутуються (тест: поля до/після `DailyStep`). Динамічні σ/λ майбутнього правила «продовження лінії» (v1.1, TD-046) — **локальні значення** в `ShootBuilder`, а не запис у профіль. Так два дерева Алеї й catch-up не ділять мутабельний стан.

### 3. Ассети й мапінг (PA-007)
- `NaN`-сентинел — лише в Domain-типі. У SO (клас — у збірці конфігів PA-007; до рішення — `PocketArbor.Presentation`, `Scripts/Presentation/Config/`) кожне перевизначення — пара `bool override` + значення, щоб дизайнер ніколи не вводив `NaN`. Мапінг `пара → NaN/значення` — у Bootstrap (ARCHITECTURE §9).
- EditMode-тест відповідності: кожне публічне поле Domain-профілю має поле в DTO SO і мапиться (рефлексія лише в тесті). Інакше перейменування в Domain тихо скинуло б значення ассета до дефолту.
- Ассети: `Config/Species/species_<id>.asset` (база + опціональне посилання на стиль) і `Config/Styles/style_<id>.asset` — лише коли з'явиться ігровий споживач (класифікатор §9.2, подарункова насінина, дріт на стовбурі — v1.1). Для дизайнера «одна панель ручок» = інспектор породи (Model) + рендер-профіль породи (Render) поруч. Editor-обгортка без зміни типів.

### 4. Збереження: стиль **не** зберігається (MVP)
- У MVP кожне дерево — Moyogi (`Style = null`), вибору стилю немає (рішення власника). Тому **нових полів сейву немає**, ADR-0008 і `schemaVersion: 1` без змін (AC16).
- **Числа профілю ніколи не пишуться в сейв.** Профіль — це правила росту, а не стан дерева. Зміна чисел пресету/бази = зміна правил → `GrowthRules.Version++` (ADR-0003 §8), і дерево далі росте за новими числами від свого стану. Запис чисел у сейв заморозив би баланс по деревах і створив би N гілок правил (відхилено в ADR-0003 rev.3 §8).
- **Якщо у v1.1 стиль стане вибором гравця:**
  - **дріт на стовбурі** (рекомендація GD, GDD §10.9): σ/λ виводяться з кутів сегментів у графі (`CurrentAngle`, вже в сейві), новий стан **не потрібен** (TD-046);
  - **подарункова насінина зі стилем**: `ShapeStyleId` (byte: Moyogi 0, Chokkan 1, Shakan 2) у резервному байті геному (`TreeGenome`, BALANCE §8). Він незмінний, їде в `#dna=`, канонізується як TD-017; формат геному — PA-205;
  - **вибір при посадці / меню** (GD не рекомендує): `TreeGraph.StyleId` (byte) з новим `schemaVersion` і мігратором, що **явно** ставить `Moyogi` (ADR-0003 §8, правило 4).
  У всіх варіантах у сейві лише **ідентифікатор** стилю, не числа. `ShapeStyleId` додається в код разом із першим споживачем, не в PA-102Q.

### 5. Детермінізм і версія правил
- PA-102Q змінює числа BALANCE §1.4 → `GrowthRules.Version = 3` (уже в коді GP), фікстури перегенеровуються.
- AC19 = пресет `Moyogi()` проти `Style = null` **на одній збірці**, побітово (30 сидів × 3 породи × Д0…Д150). Рефактор і нові числа йдуть одним кроком, тому помилку джерела поля під час рефактору (напр. `TrunkSwayDecay` не з того конфігу) AC19 не ловить. Її ловлять формульні тести AC14 і таблиця темпу AC17 (TD-045).
- `Resolve` — `double`, без `Math.Exp/Pow`, без RNG; бюджет `u()` 8/18 не змінюється.

### 6. Нові поля BALANCE §1.5 (ред. 2, PA-102R) — де живе кожне
BALANCE §1.5 (Proposed) додає ≈ 20 ручок: частина — [MODEL] (лінія стовбура, яруси, купол), частина — [RENDER] (корені, подушки, кора, горщик), дві — [STYLE] (S8). Розміщення — за правилами нижче; вони діють і для наступних ручок форми.

**Правила розміщення:**
1. **Одне місце бази.** [MODEL]-число живе або в `GrowthConfig` (однакове для всіх порід), або в `SpeciesConfig` (відрізняється між породами). Коли глобальне число стає породним, воно **переїжджає** в `SpeciesConfig` з однаковими дефолтами для порід, де воно не змінюється. Дублювати його як «глобальне + породне перевизначення» не можна: дві копії бази розходяться під час тюнінгу.
2. **Перевизначення в `ShapeStyleProfile` — лише для ручок, які стиль може змінити**: лінія стовбура, яруси, купол. Ручки темпу, RNG і структури (`BaseGrowthProbability`, слоти `*SlotT0/T1`, `MaxNodes`, `ShootsPerDay*`) перевизначень не мають. Їх додають разом із першим пресетом, якому вони справді потрібні (YAGNI: кожне поле профілю — це ще поле DTO, мапінг і тест).
3. **σ — у формулі, не в значенні.** Профіль і база зберігають величину «при σ = 1», а множення на σ робить формула в `ShootBuilder` або рендері. Тоді Chokkan (σ 0) не потребує окремого перевизначення для кожної нової σ-ручки, а v1.1 (σ і λ, виведені з дроту, TD-046) працює без змін профілю.
4. **Порода не користується `Style`.** `SpeciesConfig.Style` — лише стиль (у MVP `null`). Породні числа (Kuromatsu `PrimaryLengthFactor` 0.78) живуть у полях `SpeciesConfig`: пресети DevTools заміняють `Style` і інакше стерли б породне значення.
5. **[RENDER] — лише Presentation.** До PA-103a/b ці ручки живуть у DevTools-конфігах, потім — у `SpeciesRenderProfile` (порода) або `PotConfig` (горщик/сцена). Рендер, якому потрібна σ (скручування кори), бере її з `ShapeParams.Resolve(config, species).SwayScale` під час перебудови мешу, раз на `Version`. Окремої рендерної копії σ немає.

**[MODEL] — Domain:**

| Поле §1.5 | База | Перевизначення в `ShapeStyleProfile` (`NaN` = база) | Як діє σ | Група §1.4.7 |
|---|---|---|---|---|
| `TrunkSwayProfile[k1…k4]` | `SpeciesConfig.TrunkSwayProfile` — `readonly struct TrunkSwayProfile4 { K1, K2, K3, K4 }` (`double`), **не масив** | поле того ж типу, `NaN` **поелементно** | `A_k·σ` (як і раніше) | Лінія стовбура |
| `TrunkSwayIrregularity` | `GrowthConfig` | так | усередині `A_k` → ×σ | Лінія стовбура |
| `TrunkBowCurvature` | `GrowthConfig` | так | ×σ у формулі `Curvature_k` (§1.5.4 а) | Лінія стовбура |
| `TrunkSwayDeg` (Momiji 30), `TrunkBaseSwayRatio` (0.40) | є (`SpeciesConfig` / `GrowthConfig`) | є | `A_k·σ` | Лінія стовбура |
| `TrunkSwayDecay` | більше не читається для k ≤ 4 (§1.5.4) | — | — | **Видалити** з `GrowthConfig`, `ShapeStyleProfile` і `ShapeParams`, якщо після PA-102R читачів немає (GP перевіряє пошуком). Якщо читач лишився (k > 4, див. нижче) — коментар у полі, де саме воно читається. Перевизначення в профілі тоді не потрібне. Мертва ручка — пастка для тюнінгу |
| `PadTierDecayCompensation` `c` | `SpeciesConfig` (0.8 / 0.6 / 0.6) | так | — | Яруси |
| `PadLengthFactor` | `GrowthConfig` | так | — | Яруси |
| `PrimaryReachCompensation` `g` | `GrowthConfig` | так | неявно: при σ = 0 `o ≈ 0`; λ у `o` не входить | Яруси |
| `PrimaryLengthFactor` (Kuromatsu 0.78) | **переїжджає** `GrowthConfig` → `SpeciesConfig` (Momiji / Yamazakura — поточне 0.85, Kuromatsu 0.78; правило 1) | є (абсолютне, як і решта) | — | Яруси |
| `TrunkBaseSlotT0` 0.70, `CrownLateralSlotT0` 0.65 (⚑ ред. 4: **0.92**, лише c2+, слот за стороною — §9) | `GrowthConfig` (є) | **ні** (структура слотів, правило 2) | — | — |
| `DomeLateralWeight` | `GrowthConfig` | так | — | Купол |
| `DomeWeightMultiplier` 2.0, `CrownAxisLengthFactor` ~~0.60~~ **0.70** (⚑ ред. 4: одне значення, BALANCE §1.7.0) | є | є | — | Купол / Висота |

- Хеш `h_k` (`TrunkSwayIrregularity`) — не поле конфігу, а функція геному: `StableHash` у Domain (ADR-0003 §6, доповнення PA-102R; TD-055). Нового стану й `u()` немає.
- `TrunkSwayProfile4` — значення без посилань, тому `ShapeParams` лишається `readonly struct` без алокацій і без аліасингу мутабельного масиву. Індекс `k` → компонент — `switch` у методі `At(int k)`; для k > 4 (лише якщо GD колись збільшить `TrunkChainSegments`, BALANCE §1.5.11) — або `Validate` вимагає `TrunkChainSegments ≤ 5`, або `K4 · TrunkSwayDecay^(k − 4)`. У другому випадку `TrunkSwayDecay` лишається з коментарем «лише за межами профілю». Для дефолтного ланцюга з `A_k` зникає `Math.Pow(TrunkSwayDecay, k − 1)` (ADR-0003 §6: менше libm у кутах). У SO — 4 `float` + `bool override` на весь профіль або на кожен елемент (вирішує GP у PA-007); тест відповідності DTO ↔ Domain (§3) охоплює й поля вкладеної структури.
- `ShapeParams` росте з ≈ 20 до ≈ 28 `double`. Резолв на кожен пагін допустимий, якщо AC17 PA-102R (`DailyStep`@200 ≤ 30 µs, 0 B GC) проходить. Інакше — один `Resolve` на `DailyStep` і передача `in` (§2).

**[RENDER] — Presentation (до PA-103a/b — DevTools):**

| Поле §1.5 | Прототип (DevTools) | Production | Примітка |
|---|---|---|---|
| `NebariRootStartHeightRatio` 0.60, **`NebariRootStartAngleDeg`** 72 (новий), `NebariRootThicknessMin/Max` 0.42/0.55, `NebariRootTipRatio` 0.50, `NebariRootLengthMin/Max` 1.0/1.5, `NebariFlareHeightRatio` 0.40 | `TreeRenderConfig` | `SpeciesRenderProfile`, група «Небарі» | Геометрія стику — ADR-0004 rev.5 §1. Сюди ж v1.1 `NebariTensionRootScale` (Shakan) |
| **`PadRowSlopeMaxSide`** 0.12, **`PadSideMinSpanUnits`** 30 | `FoliageConfig` | `SpeciesRenderProfile`, група «Подушки» (поруч із `PadMaxRowsSide/Dome`) | Купол — `PadRowSlopeMaxDome` 0.35 (колишній спільний ліміт). ADR-0004 rev.5 §2 |
| **`BarkTwistShear`** 0.30 | `TreeRenderConfig` → властивість матеріалу кори прототипу | значення — `SpeciesRenderProfile`, група «Кора»; у шейдері — `_BarkTwistShear` (CBUFFER), ставить `TreeRenderer` один раз на екземпляр матеріалу | σ і знак вигину — у вершинному атрибуті фази скручування (ADR-0004 rev.5 §1). Тюнінг числа не потребує перебудови мешу |
| `TrunkBendBulge` 0.06 (опційно, TA) | `TreeRenderConfig` | `SpeciesRenderProfile`, «Кора» | Лише mesh-builder, ×σ; `TreeGeometry` не змінюється |
| **`TreeBaseBelowRimUnits`** 5.0 | `TexturedStyleConfig.TreeBaseBelowRimUnits` (замінює `TreeBaseInOpening`) | **`PotConfig`** — властивість горщика/сцени, не породи; у MVP 5.0 для всіх горщиків (Editor-валідатор, як `FrontWidthUnits`) | Лінія відліку `y_rim` — ADR-0004 rev.5 §4a |

**[STYLE] — DevTools:** `StylePadGapUnits` 14, `StylePadGapCutsPerPass` 2 (S8) — `StyleConfig`, як S1–S7. У гру S8 не переходить (BALANCE §1.5.3 в: у грі це звичайний зріз гравця).

**Детермінізм і версія:** нові [MODEL]-правила змінюють ріст для того самого `(Genome, історія)` → `GrowthRules.Version = 4` (ADR-0003 §8). AC18 PA-102R (`Moyogi()` ≡ `Style = null` побітово) лишається тестом рефактору: нові поля профілю за замовчуванням `NaN`, тож Moyogi бере базу.

### 7. (ред. 3) `SpeciesRenderProfile` — рендер-профіль породи (PA-102T, BALANCE §1.6.6)
Запит власника 2026-10-05: «кожна порода — своя кора». До цього рендер-профіль породи існував лише як назва в §1/§6 (поля жили в DevTools-конфігах). PA-102T дає йому перший реальний вміст — кору — і тому тут фіксуються місце, склад, мапінг і межі.

**7.1. Де живе**
| Що | Рішення |
|---|---|
| Клас | `PocketArbor.Presentation.Config.SpeciesRenderProfile : ScriptableObject`, `Assets/_Project/Scripts/Presentation/Config/SpeciesRenderProfile.cs` (збірка `PocketArbor.Presentation`; переїде в збірку конфігів, якщо PA-007 її створить — §3) |
| Ассети | `Assets/_Project/Config/Species/species_<id>_render.asset` — поруч із базою моделі `species_<id>.asset` (§3): «одна панель ручок» породи = два ассети в одній теці |
| Мапінг порода → профіль | `SpeciesRenderCatalog` (SO, `Config/Species/species_render_catalog.asset`): серіалізований масив `{ SpeciesId, SpeciesRenderProfile }`. Bootstrap (`RootLifetimeScope`) валідує (кожна порода рівно один раз, без `null`) і реєструє в VContainer; при першому зверненні будується масив `SpeciesRenderProfile[]`, індексований `(int)SpeciesId` — O(1), 0 B GC, без `Dictionary` і без `FindObjectOfType`/`Resources.Load` |
| Хто читає | `TreeRenderer` (матеріал кори — один раз при прив'язці дерева, порода незмінна), `NebariBuilder` і `FoliageMeshBuilder` (при перебудові на `Version`), `SoilFrontView`. Ніколи — щокадру |
| Domain | Не бачить профілю (ADR-0002/0004). Дзеркального Domain-типу й DTO-мапінгу **немає**: усі поля — [RENDER], а Presentation може тримати `UnityEngine`-типи (`Texture2D`, `Color`) напряму. Тест відповідності DTO ↔ Domain (§3) для нього не потрібен |
| Прототип (до PA-103a/b) | DevTools: простий клас (напр. `PreviewSpeciesRenderProfile`) з **тими самими назвами полів**; текстури — через `PreviewArtLibrary`; харнес `-cfg` — ключі `SpeciesRender.<Species>.<Field>`. Форму прототипу обирає GP; вимога — назви полів збігаються з production, щоб перенесення в PA-103a було 1:1 |

**7.2. Склад (групи інспектора)**
| Група | Поля | Примітка |
|---|---|---|
| **Кора** | `BarkTrunkTexture`, `BarkYoungTexture` (`Texture2D`, можуть бути `null`); `BarkTint`, `BarkYoungTint`, `BarkYoungTipTint` (`Color`, sRGB у інспекторі, у матеріал — лінійні); `BarkContrast`, `BarkSheen`, `BarkTwistShear`, `BarkTileV` (`float`); `BarkTrunkMeanLuma`, `BarkYoungMeanLuma` (`float`, **не редагуються руками** — пише Editor-валідатор з текстури; опорна точка контрасту, ADR-0004 rev.6 §1); `TrunkBendBulge` (ред. 2) | Числа — BALANCE §1.6.6 (Kuromatsu / Momiji / Yamazakura). Порода без градієнта молодого пагона — `BarkYoungTipTint = BarkYoungTint` |
| **Подушки** | атлас подушок породи і сезонний атлас (+ JSON-метадані TA), `PuffRadius`, `PadFlatten` (⚑ ADR-0004 ред. 8: сплющення еліпса пуфа моделі, **не** масштаб спрайта), `PadMaxRowsSide`, `PadMaxRowsDome` | Числа, що **відрізняються** між породами (Kuromatsu 1 ряд) |
| **Небарі** | — (поки порожня) | Див. правило 6 нижче |

**Правило 6 (рендерний аналог правила 1 §6) — одне місце бази рендерного числа.** Рендерне число живе або в **`TreeRenderConfig`** (Presentation SO, спільне для всіх порід; прототип — `TreeRenderConfig`/`FoliageConfig`/`TexturedStyleConfig` DevTools), або в **`SpeciesRenderProfile`** (якщо відрізняється між породами). Коли спільне стає породним — **переїжджає** в профіль, дублювати «спільне + породне перевизначення» не можна. *Уточнення до §6 (ред. 2)*: `Nebari*`, `SoilFrontMossHeightUnits`, `PadRowSlopeMax*`, `PadSideMinSpanUnits` поки однакові для всіх порід, тож вони в `TreeRenderConfig`, а не в групі «Небарі»/«Подушки» профілю — переїдуть у профіль із першою породною відмінністю. Так само спільними лишаються нові поля §1.6: `BarkYoungMaxThickness` 2.5, `BarkMatureThickness` 7, `BarkTileVRef`/`BarkTileWorldRef`, дефолтна пара текстур кори (fallback), `PadSideMaxAspect*`, `PadFullNodesTopTiers`, `PadMergeOverlapRatio`, `PuffMinSpacingRatio`, нові `NebariRoot*` §1.6.4.

**Правило 7 — профіль кори не змінює меш гілок.** Поля групи «Кора» доходять до GPU лише як властивості матеріалу (текстури, CBUFFER), а меш, UV і кількість draw calls для того самого `TreeGraph` побітово однакові з будь-яким профілем (T18 PA-102T; ADR-0004 rev.6 §1 — тайлінг і тон у шейдері). Нове поле кори, яке вимагало б зміни мешу (напр. породні пороги молодої кори), спершу йде в шейдер; якщо це неможливо — окремим рішенням із виміром T18.

**Відсутня текстура** (до асетів Antigravity): профіль із `BarkTrunkTexture = null` легальний; код зборки матеріалу ставить дефолтну пару з `TreeRenderConfig` і тон породи. Editor-валідатор профілю — попередження (не помилка), рантайм — без логу (T17). Видалення дефолтної пари зі збірки — коли всі три породи мають текстури (ADR-0004 rev.6 §1, Asset Diet).

**7.3. Співіснування з `ShapeStyleProfile`**
| | `ShapeStyleProfile` | `SpeciesRenderProfile` |
|---|---|---|
| Вісь | стиль (Moyogi / Chokkan / Shakan) | порода (Kuromatsu / Momiji / Yamazakura) |
| Шар | Domain, plain C#, [MODEL] | Presentation, ScriptableObject, [RENDER] |
| Що змінює | правила росту (через `ShapeParams.Resolve`) → `TreeGraph` | лише зображення (меш листя/коренів, матеріал кори) |
| У сейві | ні (MVP); у v1.1 — лише `ShapeStyleId` (§4) | ні: похідне від `Genome.Species` |
| Зміна чисел | `GrowthRules.Version++` | ні — правила росту не змінюються |

- Профілі **ортогональні** і не посилаються один на одного. Порода не має «стилю в рендері», стиль не перевизначає рендер-профіль породи.
- **Точка з'єднання — шейдер і формула mesh-builder**, а не дані: скручування кори = `BarkTwistShear` (порода, властивість матеріалу) × φ (σ стилю з `ShapeParams.Resolve`, атрибут вершини, §6 правило 5); Chokkan дає прямі волокна будь-якої породи без перевизначень.
- Якщо стилю v1.1 знадобиться рендерна ручка (напр. `NebariTensionRootScale` Shakan, §6) — окремий Presentation-тип `StyleRenderOverlay` за `ShapeStyleId`, а не поле в профілі породи і не перевизначення в `ShapeStyleProfile` (Domain не знає рендеру). Зараз його немає (YAGNI).

**7.4. Asset Diet кори** (бюджет BALANCE §1.6.6: 6 текстур ≤ 1.2 МБ у збірці; деталі — ADR-0004 rev.6 §1, TD-069)
| Текстура | Розмір | Формат | Розмір у збірці (з mips) |
|---|---|---|---|
| `bark_<species>_trunk` × 3 | 512×1024 | ASTC 6×6, sRGB, без альфи | ≈ 0.31 МБ × 3 = 0.93 МБ |
| `bark_<species>_young` × 3 | 256×512 | ASTC 8×8, sRGB, без альфи | ≈ 0.04 МБ × 3 = 0.13 МБ |
| **Разом** | | | **≈ 1.06 МБ** (download — менше після стиснення пакета); чиста дельта до поточної пари ≈ +0.75 МБ |

- Один формат ASTC для iOS і Android (без ETC2-дубля). Wrap Repeat, Max Size 1024, mips увімкнено. Імпорт-налаштування ставить Editor-постпроцесор для теки `Assets/_Project/Art/Runtime/Bark/` (за аналогією з `PreviewArtPostprocessor`, TD-028) — TA.
- Вимоги до джерел (для промптів Antigravity): вертикально тайлові (і горизонтально — `u` теж Repeat), без світла згори (освітлення дає шейдер), без тексту; джерело ≥ 2× фінальної роздільності — лише в `Art/Source/` (не в збірці).
- Пам'ять: профілі тримають жорсткі посилання → текстури трьох порід завжди завантажені (≈ 1.1 МБ GPU). Прийнятно; Addressables не беремо (TD-026).
- **Бінарні асети не комітити, поки не працює `git lfs` у репо** (CLAUDE.md, правило 3) — перевіряє оркестратор перед комітом.

### 8. (ред. 3) Нові поля BALANCE §1.6 — де живе кожне (правила §6 + правило 6)
**[MODEL] — Domain:**
| Поле §1.6 | База | Перевизначення в `ShapeStyleProfile` | Як діє σ | Група §1.4.7 |
|---|---|---|---|---|
| `PadNodeTarget[t0…t5]` (13/13/11/10/9/8) | `SpeciesConfig.PadNodeTarget` — `readonly struct PadNodeTargets { T0…T5 }` (`int`), **не масив** (той самий аргумент, що й `TrunkSwayProfile4`: поверхневий клон `SpeciesConfig` у DevTools ділив би масив) | **так**, поелементно, сентинел `−1` = база (`PadNodeTargets.Unset`, `Over(in species)`; реалізація GP). Відступ від правила 2 прийнято: BALANCE §1.4.7/§1.6.1 відносить цілі до групи «Яруси» стилю, а розмір ярусів — стильова ознака (Chokkan/Shakan v1.1) | — | Яруси |
| `DomeNodeTarget` (16; Kuromatsu 14) | `SpeciesConfig` (`int`) | **так** — Chokkan **12** (BALANCE §1.6.7); у профілі — `double` з `NaN` = база (єдина конвенція сентинела профілю), `Resolve` перетворює в `int`; `Validate` профілю має відкидати нецілі значення (інакше `Math.Round` — банківське округлення — тихо змінить x.5); у SO — пара `bool` + `int` (§3) | — | Купол |
| `CrownAxisLeanDeg` (8°) | `SpeciesConfig` | так | **×σ у формулі** (`−s_4 · CrownAxisLeanDeg · σ`, правило 3) — Chokkan 0, Shakan 4° без перевизначень | Купол |
| `CrownAxisLengthFactor` **0.70** (⚑ ред. 4: одне значення; Proposed 0.60 BALANCE §1.6.2 г не діє — BALANCE §1.7.0) | є (`GrowthConfig`) | є | — | Купол / Висота |
| `CrownApicalNeedsLaterals` (2), ~~`CrownShoulderSlotT` (0.95)~~ (⚑ ред. 4: видаляється, §9) | `GrowthConfig` | **ні** — структура росту й слотів (правило 2) | — | — |
| `TierHungryShare`, `TierDeficitGain`, `TierFedFactor`, `TierCapRatio`, `TierCapFactor`, `PinchLateralOnly` | `GrowthConfig` | **ні** — розподіл росту, а не форма стилю (правило 2) | — | — |

- `ShapeParams` отримує `DomeNodeTarget` і `CrownAxisLeanDeg` (≈ 30 полів); `PadNodeTargets` — значення без посилань, `readonly struct` лишається без алокацій. Чи передавати цілі в `ShapeParams`, чи читати з `SpeciesConfig` напряму в `GrowthSimulator` — на розсуд GP; умова одна: Chokkan-пресет змінює `DomeNodeTarget` лише через `Resolve`.
- `T_g` для групи `Dome` = `ShapeParams.DomeNodeTarget`; для `T0…T5` — `PadNodeTarget`; для `Other` баланс не діє (ADR-0003 rev.4 §6).

**[STYLE] — DevTools:** `StyleTierCapRatio` (1.10, S10), ~~`StyleKeepCrownShoulder` (true)~~ (⚑ ред. 4: видаляється, §9) — `StyleConfig`, як S1–S8. S10 читає `TierGroup`/`N_g` з аналізу і `T_g` — з того самого джерела, що й ріст (`PadNodeTarget`, `ShapeParams.DomeNodeTarget`), а не власну копію цілей.

**[RENDER] — Presentation (до PA-103a/b — DevTools):** кора — `SpeciesRenderProfile` «Кора» (§7.2); `BarkYoungMaxThickness`, `BarkMatureThickness`, `PadSideMaxAspectBase/PerTier`, `PadFullNodesTopTiers`, `PadMergeOverlapRatio`, `PuffMinSpacingRatio`, `NebariThicknessPerRoot`, `NebariRootCountMax`, `NebariRootThicknessMain/Minor`, `NebariRootMainCount`, `NebariRootMinorStartRatio`, `NebariRootFork*`, `NebariRootLength*` — `TreeRenderConfig` (спільні, правило 6); прототип — `TreeRenderConfig`/`FoliageConfig`.

**Детермінізм і версія:** [MODEL]-правила §1.6 → `GrowthRules.Version = 5` (ADR-0003 rev.4 §8). AC18 PA-102R (`Moyogi()` ≡ `Style = null`) лишається тестом: нові перевизначення за замовчуванням — сентинел, Moyogi бере базу. Рендер-профіль кори на ріст і версію правил не впливає.

### 9. (ред. 4) Нові поля BALANCE §1.7 — де живе кожне (правила §6 + правило 6)
Верх (крона як поверх, захист шийки, стилізація) — [MODEL]/[STYLE]; основа (площина ґрунту, нога, корені) — [RENDER]. Числа — BALANCE §1.7.1–§1.7.4 (Accepted). Прекчек PA-102U (R1 — одне ребро на шийці, R3 — народження шийки важиться в `Dome`; ADR-0003 ред. 5 §4b п. 8–9, TD-082) нових полів конфігу не вводить, а одне прибирає: `CapRibSlotT1`. Перемикачів R1/R3 у конфігу немає — це правила. BALANCE §1.7 **ред. 2** (U-i «шийка не додає висоти») нового [MODEL]-поля не вводить: компенсація висоти `k_cap` — похідна від наявних полів (§9.1); нові поля — лише [RENDER]: `PadDomeMaxAspect` (крона) і `NebariRootDescendShareMax` (основа). ⚑ Доповнення (рішення власника 2026-10-05, ADR-0004 ред. 8): спрайти подушок масштабуються лише рівномірно, тож стелі пропорції подушок стають **цілями компонування** (рядки нижче), а нові поля компонування BALANCE §1.7 ред. 3 розміщуються за §9.3.

**[MODEL] — Domain:**
| Поле §1.7 | База | Перевизначення в `ShapeStyleProfile` | Як діє σ | Група §1.4.7 |
|---|---|---|---|---|
| `CapNeckLengthUnits` (ред. 2: Momiji **16** · Yamazakura **15** · Kuromatsu **15**; важіль §1.7.7, 11–18) | `SpeciesConfig` (`double`) — відрізняється між породами (правило 1) | **так** — `double`, `NaN` = база; Chokkan **12**; у SO — пара `bool` + значення (§3); `ShapeStyleProfile.Validate`: `NaN` або скінченне > 0 | — (довжина, не ×σ) | Купол |
| `CapRibSlotT0` / `CapRibSlotJitter` (ред. 2: **0.78** / 0.02; важіль §1.7.7, 0.72–0.90) — єдиний слот ребра шийки (R1, плече нижче підлоги — U-i п. 3) | `GrowthConfig` | **ні** — структура слотів (правило 2). `Validate`: `CapRibSlotJitter ≥ 0`, `AttachTMin ≤ CapRibSlotT0 ± CapRibSlotJitter ≤ AttachTMax` | — | — |
| ~~`CapRibSlotT1`~~ | **не вводиться** (R1: у шийки один бічний слот; ребро над #6 — slot0 c2) | — | — | — |
| `CrownLateralSlotT0` 0.70 → **0.92** (ред. 2, U-i п. 2; не важіль §1.7.7) | `GrowthConfig` (є) | ні (правило 2) | — | — — **семантика**: лише для c2+ (slot0 c2+ на `+s_4` — перше ребро над #6 на верхівці хребта, R1; ребро шийки — `CapRibSlotT0`). **Більший за `LateralSlotT1` (0.85) — легально**: слот бічної осі крони визначає сторона, не `AttachT` (§9.2) |
| `CrownAxisLengthFactor` **0.70** — одне значення (ред. 2) | `GrowthConfig` (є) | є | — | Купол / Висота — **семантика**: c2 — формула × `k_cap` (§9.1), c3+ — формула; шийка — `CapNeckLengthUnits` |
| Компенсація висоти U-i `k_cap` | **поля немає** — похідна від `GrowthConfig` (`ShootBaseLength`, `ShootLengthDecay`, `ApicalLengthMultiplier`, `TrunkChainSegments`) і `ShapeParams` (`CrownAxisLengthFactor`, `CapNeckLengthUnits`) | — (стиль діє через входи: Chokkan 12 → `k_cap` = 1.0) | — | Купол / Висота |
| `CrownAxisLeanDeg` 12° | `SpeciesConfig` (є) | є | ×σ у формулі (Chokkan 0, Shakan 6°) | Купол — діє на шийку й c2+ |
| `CrownLateralAngleMeanDeg` (важіль §1.7.7) | `SpeciesConfig` (є) | є (пресети стилів — без змін) | — | Купол |
| ~~`CrownShoulderSlotT`~~ | **видалити** з `GrowthConfig` разом із `Validate`, `ShapeAnalysisParams.CrownShoulder`, `ShootBuilder.IsShoulderSlot`/`Role.CrownShoulder`, `TreeShapeAnalysis.IsCrownShoulder` | — | — | — |

- Нових полів `ShapeParams` немає, крім `CapNeckLengthUnits` (≈ 31 поле); `Resolve` — як і раніше, без алокацій.
- `IsCapNeck`, `IsProtected`, `y_cap` — похідні (ADR-0003 ред. 5 §4b), не поля конфігу. `TrunkChainSegments` (`GrowthConfig`) визначає і ланцюг, і шийку — одне джерело.

**9.1. (ред. 4, BALANCE §1.7 ред. 2) U-i «шийка не додає висоти» — де живе `k_cap`.** Лише в `ShootBuilder.TargetLength` для `Role.CrownAxis` (умови ADR-0003 ред. 5 §4b п. 10):
- `segs = TrunkChainSegments`; номінал осі крони без джитера `c°(d) = ShootBaseLength·ShootLengthDecay^(d−1)·ApicalLengthMultiplier·CrownAxisLengthFactor` (той самий вираз, що й формула з `u5`, без множника джитера);
- `Depth == segs` (шийка) — U-a: `CapNeckLengthUnits·(1 + ShootLengthJitter·(2u5 − 1))`;
- `Depth == segs + 1` (c2) — `L_f(c2; u5)·k_cap`, `k_cap = Clamp((c°(segs) + c°(segs+1) − CapNeckLengthUnits) / c°(segs+1), CrownBudgetMinScale, 1.0)`; `CapNeckLengthUnits` і `CrownAxisLengthFactor` — з `ShapeParams` (з перевизначенням стилю);
- `Depth > segs + 1` (c3+) — формула без змін.

Одна статична функція (напр. `CrownBudgetScale(GrowthConfig, in ShapeParams)`), викликається лише для c2 (≈ раз на дерево, 2 `Math.Pow`). Без стану, без поля сейву, без `u()` (`u5` той самий, 8/18), геометрії не читає; слоти, ролі й групи довжин не читають → рівень A, рівень B не погіршується. `CrownBudgetMinScale` = 0.25 — `const` у `ShootBuilder` із посиланням на BALANCE §1.7.1 U-i, **не** поле конфігу: формула `k_cap` не є важелем §1.7.7, а в діапазоні важеля шийки 11–18 `k_cap` ≥ 0.566, тож нижня межа — запобіжник, що за дефолтів не діє (правило 2, YAGNI). Значення: Momiji 0.745, Kuromatsu/Yamazakura 0.834, Chokkan 1.0 (U1, 1e-12). Перегенерація фікстур rules v6 — після U-i.

**9.2. (ред. 4) `Validate` і класифікація слотів осі крони (`CrownLateralSlotT0` 0.92 > `LateralSlotT1` 0.85).**
- `GrowthConfig.Validate` зараз перевіряє `CrownLateralSlotT0` лише на `[0, 1]`, тож 0.92 він **не** відкидає. Змінити: замість `[0, 1]` — `AttachTMin ≤ CrownLateralSlotT0 ± AttachTJitter ≤ AttachTMax` (0.88…0.96 ⊂ [0.05, 0.97]); обмеження порядку `CrownLateralSlotT0 < LateralSlotT1` **не додавати** (коментар: слот осі крони — за стороною). `CapRibSlotT0 ± CapRibSlotJitter` — як у рядку вище.
- Справжня перешкода — **класифікація слота в `ShootBuilder.Build`**: для батька осі крони зараз `slotBoundary = 0.5·(CrownLateralSlotT0 + LateralSlotT1)` = 0.885 → бічна slot0 (`AttachT` 0.88…0.96) класифікується як slot1, а діапазони з джитером перетинаються (0.88…0.89). Для батька c2+ — **за стороною** (ADR-0003 ред. 5 §4b п. 8): `BranchSide == +s_4` → slot0, інакше slot1; шийка — один слот (R1). Середина `AttachT` лишається лише для бічних осей ord ≥ 1 (`LateralSlotT0/T1`); для #0 — як зараз, для ланцюга — за стороною (PA-102T).
- Тести з неявним «slot0 < slot1» осі крони (свідоме оновлення): `IdealShapeTests` і `IterationThreeShapeTests` (діапазон `CrownLateralSlotT0` 0.45–0.70 → 0.92), `IterationThreeShapeTests.Slots_TrunkBaseSlot0AndCrownLateralSlot0_ConfiguredValueWithinJitter` (класифікація серединою → стороною), `BonsaiShapeTests` (зазор сусідів `LateralSlotT1 − s0 − 2·AttachTJitter` для осі крони від'ємний, перевірка вироджується → «по одній бічній на кожну сторону»).
- Рендерні валідатори: `NebariRootDescendShareMax ∈ (0, 1)` (частка < 1 — гарантія відрізка на моху), `PadDomeMaxAspect ≥ 0` (0 = вимкнено); поля компонування — §9.3.

**[STYLE] — DevTools (`StyleConfig`, як S1–S10):**
| Поле | Значення | Примітка |
|---|---|---|
| `StylePadGapByEnvelope` (новий, `bool`) | `true` (`false` = PA-102T) | S8 за обвідною листя |
| `StyleEnvelopeRadiusFactor` (новий) | 1.25 | обвідна = кінчик ± `factor·PuffRadius` |
| `StylePadGapUnits` | 18 → **3** (ред. 2; важіль §1.7.7, 3–8) | семантика змінюється разом із `StylePadGapByEnvelope` |
| `StyleCapFloor` (новий, `bool`), `StyleCapFloorToleranceUnits` (новий) | `true`, 2 | S11 за голими кінчиками; `y_cap` — з тієї самої геометрії, що й харнес (одна функція) |
| `StyleCrownAxisMax` | 2 (без змін) | **семантика**: рахує шийку (шийка + c2); `Validate` ≥ 1 |
| ~~`StyleKeepCrownShoulder`~~ | **видалити** | S3 знову ріже slot0 k4 |

- Жодне S-правило не має власної копії «захищеного»: кандидати фільтруються `analysis.IsProtected(i)` (шийка і #0), а `Prune` однаково відмовить (ADR-0003 ред. 5 §4b п. 4). Вузли ланцюга k1…k4 не захищені; якщо стилізація їх не обирає (BALANCE §1.7.2 в), це стильовий фільтр `IsTrunkChain`, а не захист.

**[RENDER] — крона (Presentation; до PA-103b — DevTools):**
| Поле | Прототип (DevTools) | Production | Примітка |
|---|---|---|---|
| `PadDomeMaxAspect` 0.54 → **0.52** (новий, ред. 2, U-i п. 4; ред. 3 — видимий bbox, та сама геометрія, BALANCE §1.7.10 б; не важіль §1.7.7) | **`FoliageConfig`** — поруч із `PadSideMaxAspectBase/PerTier` (той самий читач — розкладка подушок `TreePreviewMeshBuilder.Textured`, ті самі ключі `-cfg` харнесу); BALANCE §1.7.1 називає `TexturedStyleConfig` — число й семантика ті самі, місце — за аналогом | `TreeRenderConfig` (спільне, правило 6: Kuromatsu h/w ≈ 0.49 стелю майже не зачіпає — породної відмінності немає) | ⚑ ред. 8 ADR-0004: **ціль компонування** — пропорція візуального bbox кластера групи `Dome` (купол з ребрами; щільні ректи спрайтів без `padPx`) ≤ `PadDomeMaxAspect`; досягається рядами, колонками, кроком і вибором спрайта за правилом BALANCE §1.7 ред. 3, **без масштабу по Y** (було: «стиснення позицій, далі `PadFlatten` ×0.85» — скасовано); недосяжна → `LastPadAspectOverflow`; `0` = цілі немає. `TreeGraph` не змінює; перебудова листя лише на `Version`, 0 B GC |
| `PadSideMaxAspectBase` / `PadSideMaxAspectPerTier` 0.44/0.025 → **0.42/0.024** (є, BALANCE §1.6.3 б; семантика змінюється — ред. 8 ADR-0004; числа — видимий bbox, BALANCE §1.7.10 б) | `FoliageConfig` (є) | `TreeRenderConfig` (спільне, правило 6) | **ціль компонування** бічної подушки й злитого кластера Wild на візуальному bbox (було: стиснення спрайтів по Y — `CapSideAspect`, видаляється); числа в новій базі (візуальний bbox замість квадів) — GD, BALANCE §1.7 ред. 3 |

**[RENDER] — основа (Presentation; до PA-103a — DevTools):**
| Поле / дані | Прототип (DevTools) | Production | Примітка |
|---|---|---|---|
| `PlantDepthV` 0.58, `PlantU` 0.5 | `TexturedStyleConfig` | **`PotConfig`**, група «Основа»; MVP — одне значення для всіх скінів (Editor-валідатор, як `FrontWidthUnits`) | геометрія горщика/сцени, не породи; діапазон B7 0.50–0.65 |
| `TreeBaseBelowRimUnits` 4.5 | `TexturedStyleConfig` (є) | `PotConfig` (є) | лишається **ручкою** (прив'язка 0 моделі до `y_rim`, ADR-0004 ред. 7 §4a) |
| Геометрія отвору скіна: `lipY`, `innerLeft/Right`, `innerTopY`, `innerTopLeft/Right`, `frontEdge`, `planDepthRatio`, `hasOpening`, `rimOuterLeft/Right`, `wallPx`, `soilMaxBelowLipPx`, `soilCrestPx` | `pot_<skin>.json` → `PotArt` (парсинг **усіх** полів TD-073 + нових; `OuterLeft/Right = rimOuter*`) | `PotConfig`, група «Основа» | TA на кожен скін (4 скіни); Editor-валідатор (ADR-0004 ред. 7 §4a) |
| `SoilPlane` | `PotArt.BuildSoilPlane(upp, plantDepthV, …)` | `PotView` — похідна (`readonly struct`), **не** ассет | рахується раз на `PotRevision` |
| Текстури поверхні ґрунту `soil_plane_{moist,dry}`, атлас моху (кущики пірнання, 1–2 варіанти коміра) | `PreviewArtLibrary` (до арту — `soil_moist`, натягнута білінійно) | **`SoilVisualConfig`** (Presentation SO; стани вологості — PA-203), один на гру в MVP | не в `PotConfig`: площина натягується білінійно на отвір будь-якого горщика; заміна арту — заміна текстури |
| `BaseMossHeightUnits` 2.0, `BaseCollarHeightRatio` 0.20, `BaseCollarHeightMaxUnits` 4.0, `BaseCollarTuftsMax` 6, `TrunkSinkRatio` 0.15, `FootSpread` 0.10, `RimMarginUnits` 4.0 | `TreeRenderConfig` | `TreeRenderConfig` (спільне, правило 6) | замінюють `SoilFrontGroundY` (→ похідна площини) і `SoilFrontMossHeightUnits` (→ `BaseMossHeightUnits`) |
| `NebariRootStartAngleDeg` 72 → **50**, `NebariRootDescendRunRatio` 1.0, **`NebariRootDescendShareMax` 0.70** (новий, ред. 2; `Validate` ∈ (0, 1); профіль: кінець спуску `t_1 = min(NebariRootDescendRunRatio·th0, NebariRootDescendShareMax·r_dive)` від краю розширення, `r_dive` — відстань точки пірнання від нього; відрізок на моху ≥ 0.30·`r_dive` — ADR-0004 ред. 7 §1 п. 4), `NebariRootHumpRatio` 0.6, `NebariRootExposedShare` 0.70, `NebariRootEndDepthUnits` 1.0, `NebariRootReachShare` 0.85, `NebariLongRootLengthRatio` 2.5, `NebariLongRootCount` 1 (`Validate` ∈ {0, 1}), `NebariBackFanJitterDeg` 5, `NebariFanSlots[8/9]` = ±1.40 (TD-079), `NebariTipTuftWidthRatio` 1.2 (тепер для всіх коренів) | `TreeRenderConfig` | `TreeRenderConfig` (спільне); група «Небарі» `SpeciesRenderProfile` лишається порожньою, поки GD не попросить породних відмінностей | діапазони — BALANCE §1.7.4 в (не важелі критерію зупинки) |
| **Видаляються** | `NebariFrontRootScale`, `NebariRootTipBuryRatio`, `NebariRootMossClearance`, `NebariRootDropMin/MaxDeg`, `NebariRootEndDropDeg`, `NebariRootBuryDropDeg`, `NebariRootEndDepth` (→ `NebariRootEndDepthUnits`), `SoilFrontGroundY`, `SoilFrontMossHeightUnits`, `TexturedStyleConfig.SoilFront*` | — | GP перевіряє пошуком, що читачів не лишилось (мертва ручка — пастка тюнінгу) |

**9.3. (ред. 4, доповнення 2026-10-05; ADR-0004 ред. 8, TD-084) Поля компонування подушок BALANCE §1.7 ред. 3.** Спрайт подушки масштабується лише рівномірно, тож пропорцію подушки задає розкладка. Назви й числа полів — BALANCE §1.7.10 е (GD, ред. 3; для PA-102U **не** важелі §1.7.7); тут — куди кожне йде (правило 6 і аналог правила 2: одна база, один читач). Спершу правила розміщення, потім — конкретні поля.

| Роль поля в правилі GD | Прототип (DevTools) | Production | Примітка |
|---|---|---|---|
| Цілі й смуги пропорції: межі пропорції подушки (верхня — наявні `PadSideMaxAspect*`/`PadDomeMaxAspect`; нижня, якщо GD введе), смуга пропорції для вибору спрайта атласу | `FoliageConfig` — поруч із `PadSideMaxAspect*`/`PadDomeMaxAspect` (один читач — розкладка подушок `TreePreviewMeshBuilder.Textured`, ті самі ключі `-cfg`) | `TreeRenderConfig`; якщо GD задасть різні значення для порід — переїжджає в `SpeciesRenderProfile` «Подушки» (правило 6, без «спільне + перевизначення») | на **візуальному** bbox (щільні ректи без `padPx`); `Validate`: `0 ≤ min ≤ max`; `0` = вимкнено лише там, де так визначає BALANCE |
| Важелі розкладки, що уточнюють наявні ручки (колонки й ширина спрайта — `PadSpriteMaxWidthRatio`; крок/перекриття — `PadClusterOverlap`, `PadSingleCover`; ряди — `PadRowShrink`, `PadRowLift`, `PadMaxRows*`; ширина спрайта покриття — `PadTipSpriteRadii`) | у **тому самому** конфігу, що й ручка, яку поле уточнює (зараз `TexturedStyleConfig`; ряди за породою — `FoliageConfig`) — один важіль не розноситься по двох конфігах | `TreeRenderConfig` (спільне) або `SpeciesRenderProfile` «Подушки» (породне, як `PadMaxRowsSide`) | `Validate`: колонок у ряду ≤ **16** (крок слота `row·16`, ADR-0004 ред. 8 §2 п. 1); мінімальний крок між центрами спрайтів ≥ `PuffMinSpacingRatio·PuffRadius` (інакше відсів пуфів зливає компонування) — ⚑ BALANCE ред. 3: **не** `Validate`, а нижня межа `q_min(n)` у розкладці (ширина подушки відома лише при перебудові), ADR-0004 ред. 8 §2 п. 3 |
| Перемикач «вибір за пропорцією» | **не вводиться** — вибір вмикається даними атласу (є спрайти в смузі — рішення власника 3) | — | — |
| Нижня межа стиснення, `PadFlatten` спрайта, допуск 0.05 як важіль | **не вводиться** — допуск живе в AC харнесу (PA-102U), висота спрайта похідна (ADR-0004 ред. 8) | — | — |
| Пропорція `a` і ключ `Key` спрайта | похідні в `PadAtlas.Parse` (з `innerW/innerH` або `padPx`; FNV-1a імені) | Editor-імпортер → дані атласу породи (`SpeciesRenderProfile` «Подушки») | **не** поля конфігу й **не** нові поля JSON (контракт TD-073 без змін) |

**Поля BALANCE §1.7.10 е (ред. 3) — розміщення.** Породних відмінностей GD не задав → production — `TreeRenderConfig` (спільне, правило 6); якщо з'являться — переїзд у `SpeciesRenderProfile` «Подушки» цілою групою.

| Поле (BALANCE §1.7.10 е) | Значення (діапазон GD) | Прототип (DevTools) | Production | `Validate` / семантика |
|---|---|---|---|---|
| `PadSideMaxAspectBase` / `PadSideMaxAspectPerTier` (є) | **0.42 / 0.024** — видимий bbox (0.40–0.475 / 0–0.024) | `FoliageConfig` (є) | `TreeRenderConfig` | ціль `t = Base − PerTier·k_t`, `k_t` — як ред. 6 ADR-0004; `Base ≥ 0`, `PerTier ≥ 0`; `t ≤ 0` — цілі немає (як зараз у коді; діагностика — `Base = 0`); `0 < t ≤ PadRowSlopeMaxSide` для будь-якого `k_t ≤ 5` — помилка (знаменник `q* = (a − t)/(t − |s|)`) |
| `PadDomeMaxAspect` (є, TD-083) | **0.52** — видимий bbox (закрито) | `FoliageConfig` | `TreeRenderConfig` | ≥ 0, 0 = цілі немає; `0 < … ≤ PadMaxSlope` купола (0.35, `TexturedStyleConfig`) — помилка (той самий знаменник) |
| `PadAspectMaxColumns` (новий) | **3** (2–4; жорстка межа коду 16) | `FoliageConfig` — межа пошуку **цілі** (читається лише разом із цілями), а не уточнення ширини спрайта `PadSpriteMaxWidthRatio` (`TexturedStyleConfig`): `n_w` рахується від ширини, як і раніше, а поле обмежує лише кроки до цілі | `TreeRenderConfig` | ціле, `1 ≤ … ≤ 16`; `n_w ≥ PadAspectMaxColumns` → лише крок `n_w` і, за потреби, нахил → 0 |
| `PadAspectBandBelowRatio` (новий) | **0.10** (0.05–0.15) → смуга бічних `[0.90·t, t]` | `FoliageConfig` | `TreeRenderConfig` | `[0, 1)`; `aspectMin = (1 − β)·t ≤ aspectMax = t` — за побудовою |
| `PadDomeAspectBandBelowRatio` (новий) | **0.04** (0–0.06) → смуга купола `[0.96·t, t]` = [0.499, 0.52] | `FoliageConfig` | `TreeRenderConfig` | `[0, 1)`; та сама смуга для `PickDome` |
| `PadTipShiftMaxRatio` (новий) | **0.8** (0.6–0.9) — частка радіуса суцільного еліпса | `TexturedStyleConfig` — поруч із `PadTipSpriteRadii`, яку уточнює (правило «один важіль — один конфіг») | `TreeRenderConfig` | `[0, 1]` (> 1 вивело б кінчик із суцільного еліпса — порушення покриття) |
| `PadTipSpriteMinScale` (новий) | **0.75** (0.60–1.0; ширина ≥ 1.8·R) | `TexturedStyleConfig` — поруч із `PadTipSpriteRadii` | `TreeRenderConfig` | `(0, 1]`; 1 = без зменшення; масштаб **рівномірний** (інваріант ADR-0004 ред. 8) |
| `d_min`, `q_min(n)`, `o_min`, `w_nom(n)`, прямокутник цілі, правило класу купола | — | **не поля** — похідні в розкладці від `PuffMinSpacingRatio`·`PuffRadius` (`FoliageConfig`), `PadClusterOverlap` (`TexturedStyleConfig`) і частки щільного ректа спрайта (`VisualInset`) | — | друга копія бази розсинхронізувалася б при тонуванні наявних ручок; клас купола «не нижчий за клас кроку `n_w`» — правило, без перемикача |
| Поріг нерівномірності 0.05, `PadAspectOverflow`, `PadTipSprites`, `SpriteScaleNonUniformMax` | — | **не поля** — AC і колонки харнесу (`PreviewMetricsHarness`, ключі `-cfg` — лише для полів вище) | — | — |

[MODEL]/[STYLE] не зачіпаються; `GrowthRules.Version` не змінюється (компонування — лише рендер); `TreeGraph` і хіт-тест — без змін.

**Детермінізм і версія:** [MODEL]-правила §1.7 (з U-i, новими дефолтами ред. 2 і класифікацією слотів осі крони за стороною) → `GrowthRules.Version = 6` — **один** інкремент (ADR-0003 ред. 5 §8); AC18 PA-102R (`Moyogi()` ≡ `Style = null`) — тест і далі: нове перевизначення `CapNeckLengthUnits` за замовчуванням `NaN`. Поля основи й `PadDomeMaxAspect` на ріст і версію правил не впливають (прив'язка до `y_rim`, B6; стеля купола — лише розкладка листя).

## Розглянуті альтернативи
| Варіант | Плюси | Мінуси |
|---|---|---|
| **Повний профіль породи** `SpeciesConfig.Shape` (усі поля форми переїжджають з `GrowthConfig`/`SpeciesConfig`) + стиль як модифікатор (перша редакція цього ADR) | Одна структура форми на породу; «панель ручок» без розкиду | Великий дифф сигнатур, 3 копії глобальних чисел; AC19 вимагає фікстур до рефактору. Лишається опцією для PA-007, якщо дизайнерам знадобиться різна база форми по породах |
| Пресет стилю = **повний** профіль (пропозиція GD «`SpeciesConfig.DefaultStyle` + окремі ассети») | Просто | 3 × 3 ассети або втрата налаштувань породи |
| Перевизначення через `Nullable<double>` у полях | Без сентинела | `Nullable` у кожному полі SO/DTO, гірший інспектор; `NaN` + bool-пара в SO дають те саме |
| Domain-профіль `[Serializable]` прямо в SO (без дзеркального DTO) | Немає дублювання полів | Перейменування поля в Domain тихо скидає значення ассета (`FormerlySerializedAs` — UnityEngine); Domain залежить від формату ассетів |
| Рендерні ручки (Небарі, подушки) теж у Domain-профілі | Одна структура | Порушує ADR-0002/0004: Domain знав би про рендер |
| Зберігати профіль/стиль у сейві вже в MVP | «Готово на майбутнє» | Немає споживача (рішення власника), зміна схеми без потреби, ризик заморозити баланс |
| (ред. 2) `TrunkSwayProfile` як `double[]` у `SpeciesConfig` / `ShapeParams` | Звично для SO (масив), довільна довжина ланцюга | Посилання на мутабельний масив у `readonly struct` (аліасинг, незмінність «за домовленістю»): `ShapeStyleProfile.Clone()` / клон `SpeciesConfig` у DevTools — `MemberwiseClone`, тобто **поверхневий**, тож правка елемента в клоні (харнес `-cfg`, пресет) тихо змінює спільний масив каталогу порід для всіх наступних прогонів; перевірка довжини в рантаймі; ланцюг фіксований (`TrunkChainSegments` = 5 → 4 амплітуди) |
| (ред. 2) Kuromatsu `PrimaryLengthFactor` через `SpeciesConfig.Style` (профіль-перевизначення породи) | Без нового поля в `SpeciesConfig` | Порода і стиль змішуються: пресет Chokkan/Shakan у DevTools заміняє `Style` і губить 0.78; AC18 (Moyogi ≡ `null`) втрачає сенс для Kuromatsu |
| (ред. 2) Окремі σ-версії ручок у пресетах (Chokkan: `TrunkBowCurvature` 0, `TrunkSwayIrregularity` 0, …) | Явно видно в пресеті | Кожна нова σ-ручка вимагає правки всіх пресетів; v1.1 (σ з дроту) потребувала б динамічного профілю |
| (ред. 3) Кора за породою в `SpeciesConfig` (Domain) | Одна панель породи | Domain з текстурами й кольорами — порушення ADR-0002; або дзеркальні DTO з `Texture2D` у Bootstrap без користі |
| (ред. 3) `SpeciesRenderProfile` як поле `SpeciesConfig`-SO (вкладений об'єкт) | Один ассет на породу | Ассет моделі тягне текстури (усі споживачі конфігу моделі — тести, харнес — вантажать арт); змішування [MODEL] і [RENDER] в одному інспекторі без межі |
| (ред. 3) Мапінг через `Dictionary<SpeciesId, …>` або `Resources.Load($"species_{id}_render")` | Без каталогу | Алокації/рядки, неявні шляхи; `Resources` тягне все з теки в збірку |
| (ред. 3) Кора як перевизначення в `ShapeStyleProfile` | Одна система «профілів» | Стиль — Domain і правила росту; кора — порода й рендер; перемикання пресета стирало б кору |
| (ред. 3) `PadNodeTarget` як `int[6]` | Звично для SO | Аліасинг масиву при `MemberwiseClone` (див. ред. 2, `TrunkSwayProfile`) |
| (ред. 2) `TreeBaseBelowRimUnits` у рендер-профілі породи | Одна панель ручок породи | Це геометрія горщика/сцени: міняє лінію відліку всіх AC і кадр, однаково для всіх порід |
| (ред. 4) `CapNeckLengthUnits` у `GrowthConfig` + перевизначення породи | Одне глобальне число | Відрізняється між породами (Kuromatsu 12) — правило 1: породне число живе в `SpeciesConfig`; «глобальне + породне перевизначення» заборонене |
| (ред. 4) `CapRibSlot*` з перевизначенням у стилі | Стиль міг би рухати ребра | Структура слотів (правило 2): зміна слотів стилем змінює класифікацію слотів і зайнятість у рості; YAGNI до ігрового споживача |
| (ред. 4) `PlantDepthV` у `TreeRenderConfig` | Одне число без валідатора на горщик | Точка посадки — властивість отвору конкретного горщика (без отвору — частка гребеня `soilCrestPx`); у MVP одне значення забезпечує валідатор `PotConfig` |
| (ред. 4, BALANCE ред. 2) `k_cap` як поле конфігу (`CrownBudgetScale` за породою) або нижня межа 0.25 як поле `GrowthConfig` | Видно в інспекторі | Похідне число — друга копія бази: зміна шийки важелем §1.7.7 (11–18) без ручного перерахунку ламає «висота не росте»; межа 0.25 у діапазоні важеля не діє — мертва ручка (правило 2) |
| (ред. 4, BALANCE ред. 2) `PadDomeMaxAspect` у `TexturedStyleConfig` (як записано в BALANCE §1.7.1) | Поруч з іншими ручками textured-прев'ю | Аналог `PadSideMaxAspect*` живе у `FoliageConfig`: дві стелі пропорції одного читача в різних конфігах; на production обидва однаково йдуть у `TreeRenderConfig` |
| (ред. 4, BALANCE ред. 2) Дозволити `CrownLateralSlotT0 > LateralSlotT1`, лишивши класифікацію слота осі крони серединою `AttachT` | Без зміни `ShootBuilder` | Середина 0.885 інвертує слоти (slot0 0.88–0.96 → «slot1»), діапазони з джитером перетинаються; сторона — уже правило ланцюга й ADR-0003 ред. 5 §4b п. 8 |
| (ред. 4, доповнення ADR-0004 ред. 8) Поле `PadAspectSquashMin` — стиснення в межах допуску 0.05 як останній важіль | Ще один важіль до цілі | Допуск власника — поріг метрики, не бюджет; поле знову відкрило б шлях до нерівномірного масштабу, а висота спрайта навмисно похідна |
| (ред. 4, доповнення) Перемикач `PadAspectSelectionEnabled` | Явне ввімкнення вибору за пропорцією | Мертва ручка: чи є спрайти в смузі, вирішують дані атласу (рішення власника 3); ризик «увімкнено без арту» / «вимкнено з артом» |
| (ред. 4, доповнення 2) `PadAspectMaxColumns` у `TexturedStyleConfig` поруч із `PadSpriteMaxWidthRatio` | Усі «колонкові» ручки разом | Поле обмежує лише пошук цілі пропорції й читається тільки з цілями (`FoliageConfig`); `n_w` від ширини спрайта не змінюється — розміщення за читачем, як у BALANCE §1.7.10 е |
| (ред. 4, доповнення 2) Поля `PadAspectMinStepUnits` / `PadAspectMinOverlap` (крок і перекриття компонування) | Видно в інспекторі | Друга копія `PuffMinSpacingRatio`·`PuffRadius` і `PadClusterOverlap`: тонування наявних ручок (відсів Wild, перекриття рядів) тихо розійшлося б із компонуванням; `o_min` залежить від спрайта (частка щільного ректа) |
| (ред. 4) Текстури поверхні ґрунту в `PotConfig` (на скін) | Ідеальна підгонка під отвір | ×4 текстури на скіни при тому самому вигляді; площина натягується білінійно на будь-який отвір; вологість (PA-203) — властивість ґрунту, не горщика |

## Наслідки
- Позитивні: малий дифф, сигнатури конфігів не змінюються; DevTools-пресети без нових ассетів; Domain чистий; сейв не змінюється; λ обмежено 30° у Domain.
- Негативні / борг: база форми розкидана між `GrowthConfig` і `SpeciesConfig` (дизайнер шукає ручку у двох місцях — знімає інспектор PA-007); `NaN`-сентинел (у SO — bool-пари + тест мапінгу); абсолютні перевизначення стилю однакові для всіх порід.
- Що перевірити: AC19 (Moyogi ≡ null, побітово); тест незмінності конфігів/профілю після `DailyStep`; `DailyStep`@200 0 B GC і ≤ 30 µs з `Resolve` на кожен виклик (AC17); пресет Shakan λ ≤ 30° (кламп `TrunkLeanOffsetMaxDeg`).
- (ред. 2) Позитивні: правила розміщення закривають питання «куди нове поле» без нового ADR; σ-ручки не множать пресети; Kuromatsu тюниться без `Style`; рендер отримує σ з того самого `Resolve`, що й модель.
- (ред. 2) Негативні: `PrimaryLengthFactor` переїжджає з `GrowthConfig` у `SpeciesConfig` — правка сигнатур і тестів (GP, PA-102R); `ShapeParams` ≈ 28 полів; `TrunkSwayProfile4` потребує ручного мапінгу у SO (PA-007).
- (ред. 3) Позитивні: рендер-профіль породи має місце, склад і мапінг; кора за породою без зміни мешу й draw calls; правило 6 закриває «куди рендерне поле»; стиль і порода ортогональні.
- (ред. 3) Негативні: ще один каталог-ассет і валідатор у Bootstrap; текстури трьох порід завжди в пам'яті (≈ 1.1 МБ); +≈ 0.75 МБ до збірки після асетів; `PadNodeTargets` — ще одна структура для ручного мапінгу в SO (PA-007).
- (ред. 3) Що перевірити: T17/T18 PA-102T (CIELAB, побітово однаковий меш з трьома профілями кори, ≤ 1.2 МБ у `BuildReport`, без помилок при `null`-текстурах); AC18 PA-102R (Moyogi ≡ `null` з новими сентинелами); Chokkan `DomeNodeTarget` 12 доходить через `Resolve`; тест незмінності конфігів після `DailyStep` охоплює `PadNodeTargets`.
- (ред. 2) Що перевірити: AC14 (формули, у т.ч. `h_k` з `StableHash`), AC16 (8/18 `u()`, нових збережуваних полів немає, `GrowthRules.Version = 4`), AC17 (`DailyStep`@200 з ≈ 28-польовим `Resolve`), AC18 (Moyogi ≡ `null`; Chokkan — σ-ручки дають 0 без перевизначень); тест незмінності конфігів/профілю після `DailyStep` охоплює нові поля.
- (ред. 4) Позитивні: поля §1.7 розміщено за наявними правилами без нових типів; основа — у `PotConfig`/`TreeRenderConfig`/`SoilVisualConfig`, верх — у `SpeciesConfig`/`GrowthConfig`/`StyleConfig`; плече k4 видаляється повністю (жодної мертвої ручки).
- (ред. 4) Негативні: `PotConfig` росте на групу «Основа» (≈ 12 полів TA на скін + валідатор); новий SO `SoilVisualConfig`; `StylePadGapUnits` змінює семантику разом із `StylePadGapByEnvelope` (старі прогони харнесу з 18 не порівнюються напряму).
- (ред. 4) Що перевірити: AC18 PA-102R з новим перевизначенням `CapNeckLengthUnits` (`NaN` → база); Chokkan отримує шийку 12 через `Resolve`; `Validate` відкидає `CapRibSlotT0 ± CapRibSlotJitter` поза [`AttachTMin`, `AttachTMax`] і `StyleCrownAxisMax < 1`; пошук не знаходить читачів видалених полів (`CrownShoulderSlotT`, `StyleKeepCrownShoulder`, `CapRibSlotT1` — не має з'явитися, `NebariFrontRootScale`, `SoilFront*`, полиця ред. 4); тест незмінності конфігів після `DailyStep` охоплює `CapNeckLengthUnits`, `CapRibSlot*`.
- (ред. 4, доповнення ADR-0004 ред. 8) Що перевірити: `Validate` полів компонування (колонок ≤ 16, мінімальний крок ≥ `PuffMinSpacingRatio·PuffRadius`, смуги `min ≤ max`); пошук не знаходить `CapSideAspect` і стиснення купола; ключі `-cfg` харнесу для нових полів; семантика `PadSideMaxAspect*`/`PadDomeMaxAspect` у коментарях коду — «ціль компонування, візуальний bbox».
- (ред. 4, доповнення 2) Що перевірити: дефолти 0.42/0.024, 0.52, `PadAspectMaxColumns` 3, смуги 0.10/0.04, кінчики 0.8/0.75 — у `FoliageConfig`/`TexturedStyleConfig` (прототип), ключі `-cfg` харнесу для п'яти нових полів; `Validate`: `PadAspectMaxColumns` ∉ [1, 16], смуги ∉ [0, 1), `PadTipShiftMaxRatio` ∉ [0, 1], `PadTipSpriteMinScale` ∉ (0, 1], ціль `0 < t ≤` нахилу — помилка; мінімальний крок — не `Validate`, а `q_min(n)` у розкладці (U10 б: крок ≥ 0.60·R у 100% рядів); полів кроку/перекриття компонування немає.
- (ред. 4, BALANCE ред. 2) Що перевірити: U1 — `TargetLength(c2)` = формула × `k_cap` (Momiji 0.745, Kuromatsu/Yamazakura 0.834, Chokkan 1.0; c3+ без `k_cap`) з точністю 1e-12, 8/18 `u()`; `GrowthConfig.Validate` приймає `CrownLateralSlotT0` 0.92 і відкидає `CrownLateralSlotT0 ± AttachTJitter` поза [`AttachTMin`, `AttachTMax`]; бічні c2+ класифіковано за стороною (slot0 на `+s_4` з `AttachT` 0.92 ± 0.04, slot1 на `−s_4` з 0.85 ± 0.04) у 100% пагонів; `NebariRootDescendShareMax` ∉ (0, 1) і `PadDomeMaxAspect` < 0 — помилка валідатора; `PadDomeMaxAspect` 0 = вимкнено (прогін для діагностики U5/U8).
