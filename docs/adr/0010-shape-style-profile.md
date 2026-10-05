# ADR-0010: `ShapeStyleProfile` — стиль як накладка на параметри форми породи; стиль не зберігається в сейві (MVP)

- **Статус**: Proposed
- **Дата**: 2026-10-05 · **Ревізія 2**: 2026-10-05 (PA-102R: розміщення нових полів BALANCE §1.5 — §6; статус ревізії — Proposed) · **Ревізія 3**: 2026-10-05 (PA-102T: `SpeciesRenderProfile` — де живе, склад, мапінг, співіснування зі `ShapeStyleProfile`, Asset Diet кори — §7; розміщення полів BALANCE §1.6 — §8; уточнення правила 5 §6 для рендерних ручок; статус ревізії — Proposed)
- **Автор**: Unity Architect
- **Погоджує**: власник проєкту
- **Пов'язані задачі**: PA-102Q, PA-102R, PA-102T, PA-007, PA-103a, PA-103b, PA-105B, PA-205; майбутня задача «дріт на стовбурі» (v1.1)

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
| `TrunkBaseSlotT0` 0.70, `CrownLateralSlotT0` 0.65 | `GrowthConfig` (є) | **ні** (структура слотів, правило 2) | — | — |
| `DomeLateralWeight` | `GrowthConfig` | так | — | Купол |
| `DomeWeightMultiplier` 2.0, `CrownAxisLengthFactor` 0.60 | є | є | — | Купол / Висота |

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
| **Подушки** | атлас подушок породи і сезонний атлас (+ JSON-метадані TA), `PuffRadius`, `PadFlatten`, `PadMaxRowsSide`, `PadMaxRowsDome` | Числа, що **відрізняються** між породами (Kuromatsu 1 ряд) |
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
| `CrownAxisLengthFactor` 0.70 → 0.60 | є (`GrowthConfig`) | є | — | Купол / Висота |
| `CrownApicalNeedsLaterals` (2), `CrownShoulderSlotT` (0.95) | `GrowthConfig` | **ні** — структура росту й слотів (правило 2) | — | — |
| `TierHungryShare`, `TierDeficitGain`, `TierFedFactor`, `TierCapRatio`, `TierCapFactor`, `PinchLateralOnly` | `GrowthConfig` | **ні** — розподіл росту, а не форма стилю (правило 2) | — | — |

- `ShapeParams` отримує `DomeNodeTarget` і `CrownAxisLeanDeg` (≈ 30 полів); `PadNodeTargets` — значення без посилань, `readonly struct` лишається без алокацій. Чи передавати цілі в `ShapeParams`, чи читати з `SpeciesConfig` напряму в `GrowthSimulator` — на розсуд GP; умова одна: Chokkan-пресет змінює `DomeNodeTarget` лише через `Resolve`.
- `T_g` для групи `Dome` = `ShapeParams.DomeNodeTarget`; для `T0…T5` — `PadNodeTarget`; для `Other` баланс не діє (ADR-0003 rev.4 §6).

**[STYLE] — DevTools:** `StyleTierCapRatio` (1.10, S10), `StyleKeepCrownShoulder` (true) — `StyleConfig`, як S1–S8. S10 читає `TierGroup`/`N_g` з аналізу і `T_g` — з того самого джерела, що й ріст (`PadNodeTarget`, `ShapeParams.DomeNodeTarget`), а не власну копію цілей.

**[RENDER] — Presentation (до PA-103a/b — DevTools):** кора — `SpeciesRenderProfile` «Кора» (§7.2); `BarkYoungMaxThickness`, `BarkMatureThickness`, `PadSideMaxAspectBase/PerTier`, `PadFullNodesTopTiers`, `PadMergeOverlapRatio`, `PuffMinSpacingRatio`, `NebariThicknessPerRoot`, `NebariRootCountMax`, `NebariRootThicknessMain/Minor`, `NebariRootMainCount`, `NebariRootMinorStartRatio`, `NebariRootFork*`, `NebariRootLength*` — `TreeRenderConfig` (спільні, правило 6); прототип — `TreeRenderConfig`/`FoliageConfig`.

**Детермінізм і версія:** [MODEL]-правила §1.6 → `GrowthRules.Version = 5` (ADR-0003 rev.4 §8). AC18 PA-102R (`Moyogi()` ≡ `Style = null`) лишається тестом: нові перевизначення за замовчуванням — сентинел, Moyogi бере базу. Рендер-профіль кори на ріст і версію правил не впливає.

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
