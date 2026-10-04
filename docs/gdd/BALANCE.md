# Balance Sheet — Pocket Arbor

> Власник: **Game Designer** · Джерело істини для всіх чисел. У Unity — ScriptableObject-и з `Assets/_Project/Config/`.
> Зміна значення = рядок у таблиці «Журнал змін» внизу + синхронізація ассета Gameplay Developer-ом.
> Колонка **Config** — у який ScriptableObject і поле потрапляє значення.

## 1. Ріст дерева — `GrowthConfig`

| Параметр | Значення | Config | Джерело |
|---|---|---|---|
| Макс. вузлів | 200 | `MaxNodes` | ТЗ 3.1.1 |
| Макс. глибина | *TBD (пропозиція 8)* | `MaxDepth` | ТЗ 3.1.1 (не задано) |
| Базова ймовірність росту $P_{base}$ | *TBD* | `BaseGrowthProbability` | ТЗ 3.1.1 |
| Нових пагонів на добу | 1–3 | `ShootsPerDayMin/Max` | ТЗ 3.4 |
| $K_{thick}$ (потовщення) | 0.25 units/добу | `ThickeningRate` | ТЗ 3.1.1 |
| Макс. товщина стовбура | 24.0 units × depthScale | `MaxThickness` | ТЗ 3.1.1 |
| depthScale exponent | 1.8, мін. 0.1 | `DepthScaleExponent`, `DepthScaleMin` | ТЗ 3.1.1 |
| Термо-фактор: нижній поріг | 5.0 °C, ширина 18.0 | `TempLow`, `TempLowRange` | ТЗ 3.1.1 |
| Термо-фактор: верхній поріг | 38.0 °C, ширина 12.0, макс. пригнічення 0.8 | `TempHigh`, `TempHighRange`, `TempHighMaxSuppression` | ТЗ 3.1.1 |
| Фенікс-брунька | depth 1, length 6.0, thickness 2.5 | `PhoenixBud*` | ТЗ 3.1.1 |

Формули:

$$\text{depthScale}(d) = \max\left(0.1,\ \text{clamp01}\left(1 - \tfrac{d}{D_{max}}\right)^{1.8}\right)$$

$$P_{growth} = P_{base}\left(1 - \tfrac{d}{D_{max}}\right) M_{soil}\, T_{factor}\, \mathbb{I}(N < 200)$$

$$T_{factor} = \text{clamp01}\left(\tfrac{T-5}{18}\right)\left(1 - \text{clamp}\left(\tfrac{T-38}{12}, 0, 0.8\right)\right)$$

$$\Delta th = (th_{max} - th)\left(1 - e^{-r\Delta t}\right),\quad r = \frac{K_{thick} M_{soil}\,\text{depthScale}}{th_{max}},\quad th_{max} = 24\cdot\text{depthScale}$$

## 2. Дріт — `GrowthConfig.Wire`

| Параметр | Значення | Config |
|---|---|---|
| Сектор кута | ±π/4 від осі батька | `WireMaxAngle` |
| Днів до фіксації | 14 | `WireSetDays` |
| Релаксація при достроковому знятті | $\theta = \theta_{base} + \frac{k}{14}(\theta_{target}-\theta_{base})$ | — |
| Мін. дистанція драгу | 14 dp | `InputConfig.WireMinDrag` |

## 3. Породи — `SpeciesConfig` (×3)

| Параметр | Kuromatsu | Momiji | Yamazakura |
|---|---|---|---|
| $K_{base}$ (випаровування/добу) | 0.15 | 0.28 | 0.22 |
| Кут гілок, середнє ± розкид | 45° ± 15° | 30° ± 10° | 35° ± 20° |
| Колір листя (основний) | `#2E4F2D` | `#5C8A42` | `#6A994E` |
| Сезонні кольори | свічки `#9CCC65` (весна) | `#D98A3C` (вер–жов), `#A33B29` (лис), скидає взимку | цвіт `#FFB7C5` / серцевина `#F48FB1`, 14–21 день навесні |

## 4. Ґрунт — `SoilConfig`

| Параметр | Значення | Config |
|---|---|---|
| Крок симуляції | 1 год | — |
| Температурний поріг надлишку | 22.0 °C | `TempExcessThreshold` |
| Коеф. температури | 0.012 /°C/добу | `TempCoefficient` |
| Коеф. сонця | 0.05 /добу | `SunCoefficient` |
| Множник моху | 0.7 | `MossMultiplier` |
| Стеля випаровування $E_{max}$ | 0.30 /добу (м'яко через tanh) | `MaxEvaporationPerDay` |
| Кріо-фактор | $\max(0.05,\ 1 + T/10)$ при $T \le 0$ | `FreezeFloor` |
| Дощ | $1 - e^{-0.12p}$, $p \in [0,100]$ мм, лише при $T > 0$ | `RainCoefficient` |
| Цивільна ніч | 22:00–06:00 (сонце = 0) | `NightStart/End` |
| «День» при невідомому isDay | 08:00–20:00 | — |
| Поріг «сухо» (ріст стоп, тріщини) | W < 0.10 | `DryThreshold` |
| Поріг доступності поливу | W < 0.35 | `WaterAvailableThreshold` |
| Нахил листя при посусі | 12° | `DroopAngle` |
| Час розправлення листя | 1.5–2.0 с | `RecoverDuration` |
| Колір: мокро / оптимально / сухо | `#4A3525` / `#634832` / `#8C8275` | (Art) |

Еталонний алгоритм (C#-порт `simulateOneHour`):

```csharp
double hourlyBase = species.KBase / 24.0;
double hourlyTemp = 0.012 * Math.Max(0, T - 22.0) / 24.0;
double hourlySun  = isDay ? 0.05 * (1.0 - Clamp01(cloudCover / 100.0)) / 24.0 : 0.0;
double freeze     = T <= 0 ? Math.Max(0.05, 1.0 + T / 10.0) : 1.0;
double raw        = (hourlyBase + hourlyTemp + hourlySun) * (hasMoss ? 0.7 : 1.0) * freeze;
const double eMax = 0.30 / 24.0;
double evaporation = eMax * Math.Tanh(raw / eMax);
double rain = (precip > 0 && T > 0) ? 1.0 - Math.Exp(-Math.Clamp(precip, 0, 100) * 0.12) : 0.0;
moisture = Clamp01(moisture + rain - evaporation);
```

## 5. Офлайн — `GrowthConfig.Offline`

| Параметр | Значення |
|---|---|
| Поріг Dormancy | 168 год (7 діб) |
| Вологість при вході в Dormancy | ≤ 0.05 |
| TTL кешу погоди | 3 год |
| Синусоїдальна погода без кешу | $T_{avg} \pm 5$ °C, опади = 0 |
| Фолбек-місто | Кіото, 18.5 °C, сонячно |

## 6. Ввід — `InputConfig`

| Параметр | Значення |
|---|---|
| Snap radius | clamp(32·√s, 16, 56) dp |
| Гістерезис захоплення | 8 dp ($R_{release} = R_{snap} + 8$) |
| Slash-to-Commit | довжина > 24 dp, швидкість > 120 dp/с |
| Pinch window | 35–45 мс |
| Deadzones | top max(safe, 59 pt), bottom max(safe, 34 pt), sides max(safe, 24 dp) |
| Масштаб камери | 0.1–10.0 |
| Hitbox UI | ≥ 48×48 dp |
| Zen long-press | 2 пальці, > 1.2 с |
| HUD fade | після 5 с бездіяльності, 600 мс |

## 7. Аудіо та хаптика — `AudioConfig`

| Параметр | Значення |
|---|---|
| Pitch jitter зрізу | ±4% |
| Макс. одночасних зрізів | 3, гучність 0.75 |
| Мін. інтервал хаптики | 70 мс |
| Real voices | 32 |
| Дихання | 4 с вдих / 4 с видих; кільце +18%; крона Y×1.015 |
| Вітер | Perlin, період 3.5 с, ±0.8° |

## 8. Геном насіння

| Поле | Байти | Діапазон |
|---|---|---|
| magic `0xBA` + version `0x01` | 2 | — |
| speciesId | 1 | 0 Pine, 1 Maple, 2 Sakura |
| randomSeed | 4 | uint32 |
| vigor | 2 | 0.5–1.5 (fixed-point) |
| bendBias | 2 | −0.6…+0.6 (fixed-point) |
| CRC | *TBD* | — |
| reserved | решта до 32 | — |
| Кодування | Base64URL, рівно 43 символи | |

> ⚠ ТЗ §5.4 і Резолюція 4 розходяться (magic/version/CRC лише в Резолюції 4). Пропозиція — формат з Резолюції 4; фінальний layout — у PA-205.

## 9. Прогресія та монетизація (Proposed)

> Усі значення цього розділу — **Proposed** (GDD §10, 2026-10-04), чекають погодження власника. До погодження Gameplay Developer їх у ScriptableObject-и не переносить. Нові ассети: `ProgressionConfig`, `MonetizationConfig`, `DormancyConfig` (або підсекція `GrowthConfig.Offline`), `LocalizationSettings` (Unity Localization, не SO балансу).

### 9.1. Алея Майстра — `ProgressionConfig.Alley` (GDD §10.1)

| Параметр | Значення | Config | Нотатка |
|---|---|---|---|
| Подія відкриття 2-го слота | перший дріт спав (`WireSetDays` минули) | `AlleyUnlockEvent = FirstWireSet` | Дріт з Дня 2 → слот не раніше Дня 16 |
| Гарантоване відкриття | День 30 | `AlleyGuaranteeDay` | Якщо дріт не використано |
| Прев'ю порожнього горщика на полиці | з Дня 7 | `AlleyPreviewFromDay` | Тьмяний силует, без таймера/цифр |
| Слотів у MVP | 2 | `AlleyMaxSlots` | v1.1 → 3 |
| Породи для 2-го слота | стартова, що не обрана; насінина з QR; Сакура (IAP) | — | Жодних ексклюзивів за час |

Перевірка: `WireSetDays = 14` (§2) + накладання на Дні 2 → подія на Дні 16; вікно D16–D30 ⊂ D14–D30 з Резолюції 7 архіву.

### 9.2. Розпізнавання стилю крони — `ProgressionConfig.Style` (GDD §10.2, v1.1)

| Стиль | Умова (перевіряється зверху вниз) | Config |
|---|---|---|
| Chokkan | нахил стовбура від вертикалі < 5° і ≤ 1 зміна кривини | `StyleChokkanMaxLean = 5°` |
| Shakan | нахил стовбура 15–45° | `StyleShakanLeanMin/Max = 15°/45°` |
| Fukinagashi | ≥ 70% маси крони з одного боку від осі стовбура | `StyleWindsweptMassRatio = 0.70` |
| Hokidachi | стовбур розгалужується на висоті ≤ 25% від загальної, ≥ 4 гілки 1-го рівня в межах ±20% довжини | `StyleBroomSplitHeight = 0.25`, `StyleBroomMinBranches = 4`, `StyleBroomLengthTolerance = 0.20` |
| Moyogi | решта (≥ 2 зміни кривини або нахил 5–15°) | за замовчуванням |

Каскадні стилі (Kengai, Han-Kengai) не входять: заборонені правилом Pot Lip (`Y >= potLipY`). Нахил стовбура і розподіл маси записуються у знімок Хроніки вже в MVP.

### 9.3. Каталог IAP — `MonetizationConfig` (GDD §10.3)

| SKU | Тип | US tier | Config |
|---|---|---|---|
| `pot_tokoname` | стартовий, безкоштовний | $0 | `Catalog[0].Price = 0`, `IsDefault = true` |
| `pot_kuro` | неконсьюмабл | $2.99 | `Catalog[].ProductId`, `PriceTierUsd` |
| `pot_celadon` | неконсьюмабл | $2.99 | — |
| `species_sakura` | неконсьюмабл (насінина у скриньку) | $3.99 | `RequiresAlleyUnlocked = true` |
| `bundle_master_shelf` | набір Kuro + Celadon + Sakura | $6.99 (−30% від $9.97) | `BundleDiscount = 0.30` |

| Параметр | Значення | Config |
|---|---|---|
| Полиця з'являється в HUD | з Дня 3 (після Aha і першої доби росту) | `ShopVisibleFromDay = 3` |
| Автовідкриття Полиці / попапи | 0 | `ShopAutoPromptCount = 0` |
| Обмежені за часом пропозиції | немає | `TimedOffersEnabled = false` |
| Family Sharing | увімкнено для всіх SKU | store config (UA) |
| Restore Purchases | 4-стадійний FSM, таймаут мережі 12 с | `RestoreNetworkTimeoutSec = 12` (ТЗ §5.5) |
| Регіональні ціни | через store price tiers (автоеквівалент) | store config |

Гіпотеза (не KPI): payer rate 2–4%, ARPPU ≈ $4–5.

### 9.4. Ім'я дерева — `ProgressionConfig.Naming` (GDD §10.4)

| Параметр | Значення | Config |
|---|---|---|
| Макс. довжина імені | 16 символів | `NameMaxLength` |
| Пул запропонованих імен | 24 (Kaze, Yuki, Hana, Sora, Mori, Tsuki, Ame, Hikari, …) | `SuggestedNames[]` (локалізований список) |
| Вибір із пулу | `randomSeed % 24` з геному (детерміновано) | — |
| Заборонені символи | емоджі, системні піктограми, керуючі символи | `NameForbiddenRanges` |
| Бюджет кроку в онбордингу | ≤ 5 с (крок 40–45 с) | — |
| Перейменування | будь-коли з Хроніки | — |

### 9.5. Повернення з Dormancy — `DormancyConfig` (GDD §10.5)

| Параметр | Значення | Config |
|---|---|---|
| Сірих «сплячих пагонів» при пробудженні | 1–3 | `DormantShootsMin/Max` |
| Гарантованих живих бруньок після обрізки | 1 | `WakeGuaranteedBuds` |
| Відступ моху з ґрунту після поливу | 2.0 с | `MossRecedeDuration` |
| Мох на стовбурі | лишається (патина) | `TrunkMossPersists = true` |
| Цільова тривалість ритуалу | 30–45 с | — (AC) |
| Доступні дії до поливу | лише Water | `WakeLockToolsUntilWatered = true` |
| Нотифікацій за час сну | 1 (на вході в Dormancy), далі тиша | `DormancyNotificationCount = 1` |
| Число пропущених днів у UI | не показується | — (Calm UI) |
| Віха Хроніки | «Пробудження» | `ChronicleEvent.Awakening` |

Інваріант: жоден вузол, що існував до Dormancy, не змінюється і не видаляється ритуалом.

### 9.6. Локалізації — Unity Localization `Locales` (GDD §10.6)

| Локаль | Реліз | Умова | Шрифт |
|---|---|---|---|
| `en` | MVP | базова, ASO | ZenMaru Gothic (Latin) |
| `uk` | v1.1 | **вимкнено для MVP (власник, 2026-10-04)**; потребує Cyrillic-шрифту | Cyrillic fallback у бюджеті < 450 КБ |
| `ja` | MVP* | *лише з вичиткою носієм; інакше v1.1 | субсет використаних кандзі |
| `de`, `fr`, `es`, `pt-BR`, `ko` | v1.1 | ринки cozy-ігор за спроможністю | — |
| `zh-Hans`, `zh-Hant` | v1.2 | — | — |

Apple keyword field: 100 байт; CJK-символи — 2–3 байти → окремий короткий набір ключових слів для `ja`.

## Журнал змін

| Дата | Параметр | Було → Стало | Причина | Хто |
|---|---|---|---|---|
| 2026-10-03 | — | Перенесено з ТЗ v3.2 | Старт Unity-версії | Game Designer |
| 2026-10-04 | §9 (новий) | — → Proposed: Алея (FirstWireSet, гарантія D30, прев'ю D7, 2 слоти); пороги стилів; каталог IAP ($2.99/$2.99/$3.99, набір $6.99, −30%, Полиця з Дня 3); ім'я ≤ 16, пул 24; Dormancy-пробудження (1–3 пагони, 1 брунька, 2.0 с, 1 нотифікація); локалі en/uk/ja → v1.1 de/fr/es/pt-BR/ko | PA-005: відповіді на GDD §10 | Game Designer |
| 2026-10-04 | §9.6 `uk` | MVP → v1.1 | Рішення власника: не вирішувати кирилицю/шрифт у MVP | Оркестратор |
