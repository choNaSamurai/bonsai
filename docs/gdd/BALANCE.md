# Balance Sheet — Pocket Arbor

> Власник: **Game Designer** · Джерело істини для всіх чисел. У Unity — ScriptableObject-и з `Assets/_Project/Config/`.
> Зміна значення = рядок у таблиці «Журнал змін» внизу + синхронізація ассета Gameplay Developer-ом.
> Колонка **Config** — у який ScriptableObject і поле потрапляє значення.

## 1. Ріст дерева — `GrowthConfig`

| Параметр | Значення | Config | Джерело |
|---|---|---|---|
| Макс. вузлів | 200 | `MaxNodes` | ТЗ 3.1.1 |
| Макс. глибина $D_{max}$ | 8 | `MaxDepth` | PA-102 (ТЗ не задає) |
| Базова ймовірність росту $P_{base}$ | 0.20 (на бруньку-кандидата за добу) | `BaseGrowthProbability` | PA-102 |
| Нових пагонів на добу (при $G = 1$) | 1–3 | `ShootsPerDayMin/Max` | ТЗ 3.4 |
| $K_{thick}$ (потовщення) | 0.25 units/добу | `ThickeningRate` | ТЗ 3.1.1 |
| Макс. товщина стовбура | 24.0 units × depthScale | `MaxThickness` | ТЗ 3.1.1 |
| depthScale exponent | 1.8, мін. 0.1 | `DepthScaleExponent`, `DepthScaleMin` | ТЗ 3.1.1 |
| Термо-фактор: нижній поріг | 5.0 °C, ширина 18.0 | `TempLow`, `TempLowRange` | ТЗ 3.1.1 |
| Термо-фактор: верхній поріг | 38.0 °C, ширина 12.0, макс. пригнічення 0.8 | `TempHigh`, `TempHighRange`, `TempHighMaxSuppression` | ТЗ 3.1.1 |
| **Зимовий мінімум T_factor** | **0.25** — $T_{factor} = \max(0.25,\ T_{factor}^{ТЗ})$; ріст ніколи не зупиняється повністю через холод/спеку | `TempFactorMin` | **Рішення власника 2026-10-04** (відхилення від ТЗ: стовп «погода надихає, а не карає») |
| Фенікс-брунька | depth 1, length 6.0, thickness 2.5 | `PhoenixBud*` | ТЗ 3.1.1 |
| Вологість без обмеження росту | W ≥ 0.40 ($M_{soil} = 1$) | `MoistureOptimal` | PA-102 |
| Годин симуляції на добу росту | 24 | `HoursPerGrowthDay` | PA-102 |
| Макс. дітей у вузла | 3 (1 апікальний + 2 бічні) | `MaxChildrenPerNode` | PA-102 |
| Стадія Bud → Growing | AgeDays ≥ 1 | `BudDays` | PA-102 |
| Стадія Growing → Mature | AgeDays ≥ 5 | `MatureDays` | PA-102 |
| Швидкість видовження | 0.5 / добу росту (експ. насичення) | `ElongationRate` | PA-102 |
| Розкриття листя | 3 доби росту (0 → 1) | `LeafUnfoldDays` | PA-102 |
| Нова брунька: довжина / товщина | 3.0 / 1.0 units (товщина ≤ $th_{max}(d)$) | `ShootInitialLength`, `ShootInitialThickness` | PA-102 |
| Цільова довжина пагона, depth 1 | 30.0 units | `ShootBaseLength` | PA-102 |
| Спад цільової довжини з глибиною | ×0.85 на рівень | `ShootLengthDecay` | PA-102 |
| Розкид цільової довжини | ±20% (рівномірний) | `ShootLengthJitter` | PA-102 |
| Множник довжини апікального пагона | 1.2 | `ApicalLengthMultiplier` | PA-102 |
| Розкид кута апікального пагона | ±6° (трикутний) | `ApicalAngleJitterDeg` | PA-102 |
| Вплив `bendBias` на апікальний кут | `bendBias` × 12° | `ApicalBendScaleDeg` | PA-102 |
| Вплив `bendBias` на нахил стовбура | `bendBias` × 8° | `TrunkBendScaleDeg` | PA-102 |
| Макс. світовий кут нового пагона від вертикалі | ±90° (не нижче горизонталі) | `MaxShootWorldAngleDeg` | PA-102, Pot Lip |
| Множник потовщення стовбура при N = MaxNodes (Небарі) | ×3.0 (лише depth 0) | `NebariThickeningMultiplier` | PA-102 |
| Саджанець: стовбур length / target / thickness | 20.0 / 48.0 / 4.0 units | `TrunkInitialLength`, `TrunkTargetLength`, `TrunkInitialThickness` | PA-102 |
| Саджанець: лідер length / thickness / AgeDays | 10.0 / 1.6 / 4 | `SaplingLeaderLength`, `SaplingLeaderThickness`, `SaplingLeaderAgeDays` | PA-102 |
| Саджанець: дика гілочка length / thickness / AgeDays | 8.0 / 1.2 / 2 | `SaplingTwigLength`, `SaplingTwigThickness`, `SaplingTwigAgeDays` | PA-102, GDD §6 |
| Кривина вигину (Безьє) → зсув контрольної точки | `Curvature` ∈ [−1, 1]; зсув = Curvature × 0.25 × Length | `TreeRenderConfig.CurvatureOffsetRatio` (пропозиція для PA-103) | PA-102 |

Формули:

$$\text{depthScale}(d) = \max\left(0.1,\ \text{clamp01}\left(1 - \tfrac{d}{D_{max}}\right)^{1.8}\right)$$

$$P_{growth} = P_{base}\left(1 - \tfrac{d}{D_{max}}\right) M_{soil}\, T_{factor}\, \mathbb{I}(N < 200)$$

$$T_{factor} = \text{clamp01}\left(\tfrac{T-5}{18}\right)\left(1 - \text{clamp}\left(\tfrac{T-38}{12}, 0, 0.8\right)\right)$$

$$M_{soil}(W) = \text{clamp01}\left(\tfrac{W - W_{dry}}{W_{opt} - W_{dry}}\right),\quad W_{dry} = 0.10\ (\texttt{SoilConfig.DryThreshold}),\ W_{opt} = 0.40$$

$$\Delta th = (th_{max} - th)\left(1 - e^{-r\Delta t}\right),\quad r = \frac{K_{thick} M_{soil}\,\text{depthScale}}{th_{max}},\quad th_{max} = 24\cdot\text{depthScale}$$

Значення depthScale / $th_{max}$ при $D_{max} = 8$: d0 1.000 / 24.0 · d1 0.786 / 18.9 · d2 0.596 / 14.3 · d3 0.429 / 10.3 · d4 0.287 / 6.9 · d5 0.171 / 4.1 · d6–d8 0.100 / 2.4.

### 1.1. Алгоритм добового росту (`GrowthGrammar`)

Реалізує Gameplay Developer у Domain (PA-102). Усі числа — з `GrowthConfig` / `SpeciesConfig` / `SoilConfig`; кути в Domain — у радіанах (градуси лише в Config).

**Стан дерева поза вузлами** (зберігається в сейві разом із `TreeGraph`): `GrowthHourCounter` (int, 0..23), `GrowthDayIndex` (int, 0 при посадці), `ShootCredit` (double ≥ 0), `SumGrowthFactor`, `SumMoistureFactor` (double). **Нове поле вузла**: `TargetLength` (double) — пропозиція до ADR-0003 (див. «Залежності» нижче). `HasLeaves` не зберігається: `HasLeaves ≡ Depth ≥ 1 ∧ Status ∈ {Growing, Mature}`.

**Випадковість.** Усі випадкові вибори — через `IRandomSource` (PCG32). Генератор для доби *k*: `Pcg32(seed: SeedGenome.randomSeed, stream: k)`; посадка використовує stream 0, доба росту *k* = `GrowthDayIndex` після інкременту (1, 2, …). Стан RNG не зберігається. `u()` = `NextUInt() / 2^32` ∈ [0, 1). Трикутний розподіл `tri(m, s) = m + s·(u() + u() − 1)` — значення завжди в [m − s, m + s], пік у m.

**Кожна симульована година** (онлайн-тік і офлайн catch-up однаково; не виконується, поки `!OnboardingCompleted` або `IsDormant`):
1. Спочатку `SoilSimulation.SimulateOneHour` (§4) → нове W.
2. `g = M_soil(W) · T_factor(T_години)`; `SumGrowthFactor += g`; `SumMoistureFactor += M_soil(W)`.
3. `GrowthHourCounter++`; якщо `== HoursPerGrowthDay` → `DailyStep()`, лічильник і обидві суми = 0.

**`DailyStep()`** — суворо в такому порядку:
1. `G = SumGrowthFactor / 24`, `M̄ = SumMoistureFactor / 24` (обидва ∈ [0, 1]). `GrowthDayIndex++`. `nebari = (Count ≥ MaxNodes)` — фіксується до спавну.
2. **Розвиток** — для кожного вузла в порядку індексу: `AgeDays++` (календарні доби росту, незалежно від G); `Bud ∧ AgeDays ≥ BudDays → Growing`; `Growing ∧ AgeDays ≥ MatureDays → Mature`. Стовбур (depth 0) завжди `Mature`.
3. **Видовження** — для вузлів зі статусом ≠ Bud: `Length += (TargetLength − Length)·(1 − e^{−ElongationRate·G})`. Bud лишається з `ShootInitialLength`.
4. **Листя** — для `HasLeaves`: `LeafScale = min(1, LeafScale + G / LeafUnfoldDays)`.
5. **Потовщення** — для всіх вузлів формула з §1 з `M_soil = M̄`, `Δt = 1`; для depth 0 при `nebari` — `K_thick × NebariThickeningMultiplier`. Результат клампиться в [th, th_max].
6. **Спавн пагонів** (лише якщо `Count < MaxNodes` і `G > 0`):
   1. Кандидати: вузли з `Status == Mature`, `Depth < MaxDepth`, кількість дітей `< MaxChildrenPerNode`. Вага `w_i = 1 − Depth_i / MaxDepth`. Якщо кандидатів немає — крок 6 завершується, `ShootCredit` не змінюється.
   2. `rate = clamp(P_base · vigor · Σw_i, ShootsPerDayMin, ShootsPerDayMax) · G`, де `vigor` — з `SeedGenome` (0.5–1.5). `ShootCredit += rate`.
   3. `k = min(floor(ShootCredit), ShootsPerDayMax)`; `ShootCredit −= k`.
   4. Повторити *k* разів (зупинка, якщо `Count == MaxNodes` або кандидатів не лишилось; непороджені пагони згорають):
      - **7 викликів `u()` завжди**, навіть якщо частина не використовується (стабільність потоку): `u1` батько, `u2,u3` кут, `u4` знак, `u5` довжина, `u6,u7` кривина.
      - **Батько**: перелік кандидатів (перераховується перед кожним пагоном) у порядку індексу; обирається перший, де накопичена сума `w` > `u1·Σw`.
      - **Тип**: апікальний, якщо в батька 0 дітей; інакше бічний.
      - **Кут** (`BaseAngle = CurrentAngle`, відносно осі батька): апікальний — `bendBias·ApicalBendScaleDeg + ApicalAngleJitterDeg·(u2 + u3 − 1)`; бічний — `sign · tri(BranchAngleMean, BranchAngleSpread)` з `u2,u3`, де `sign` = протилежний до знака останньої бічної дитини батька, а якщо її немає — `u4 < 0.5 ? −1 : +1`.
      - **Обмеження Pot Lip**: світовий кут = світовий кут батька (сума `CurrentAngle` від кореня) + новий кут; якщо |світовий| > `MaxShootWorldAngleDeg` — клампиться до ±90° і `BaseAngle` перераховується. Отже, кінчик нового пагона ніколи не нижчий за кінчик батька → інваріант `Y ≥ potLipY` зберігається ростом за побудовою.
      - **TargetLength** = `ShootBaseLength · ShootLengthDecay^(d−1) · (1 + ShootLengthJitter·(2·u5 − 1))`, × `ApicalLengthMultiplier` для апікального.
      - **Curvature** = `tri(0, CurvatureSpread)` з `u6,u7`.
      - Новий вузол: `Depth = d_батька + 1`, `Length = ShootInitialLength`, `Thickness = min(ShootInitialThickness, th_max(d))`, `Status = Bud`, `AgeDays = 0`, `LeafScale = 0`, без дроту. Вставка зберігає топологічний порядок (ADR-0003).
   5. Якщо після спавну `Count == MaxNodes` → `ShootCredit = 0`.
7. `Version++`, якщо щось змінилось (на практиці — щодоби).

**Посадка (`PlantSeed`, stream 0)** — стан, з яким починається онбординг (час заморожено, `DailyStep` не викликається до `OnboardingCompleted`):
- #0 стовбур: depth 0, `Length = TrunkInitialLength`, `TargetLength = TrunkTargetLength`, `Thickness = TrunkInitialThickness`, `BaseAngle = bendBias·TrunkBendScaleDeg`, `Curvature = tri(0, CurvatureSpread)`, `Mature`, без листя.
- #1 лідер: апікальна дитина #0 за правилами кроку 6.4 (7 викликів `u()`), але `Length = SaplingLeaderLength`, `Thickness = SaplingLeaderThickness`, `Status = Growing`, `AgeDays = SaplingLeaderAgeDays` (стає `Mature` у першому `DailyStep`), `LeafScale = 1`.
- #2 дика гілочка (ціль першого зрізу, GDD §6): бічна дитина #0 (7 викликів `u()`), кут — крайній `sign · (BranchAngleMean + BranchAngleSpread)` (найбільш «дикий», добре читається для підсвічування), `Length = SaplingTwigLength`, `Thickness = SaplingTwigThickness`, `Status = Growing`, `AgeDays = SaplingTwigAgeDays`, `LeafScale = 1`.
- Після онбордингового зрізу: N = 2 (стовбур + лідер). Лічильники росту = 0; перший `DailyStep` — через 24 год після `OnboardingCompleted`.

**Фенікс**: якщо після `Prune` `Count == 1` — одразу (не чекаючи `DailyStep`) додається апікальна дитина стовбура: `PhoenixBud*`, `Status = Bud`, `AgeDays = 0`, кут `bendBias·ApicalBendScaleDeg`, `Curvature = 0`, `TargetLength = ShootBaseLength·ApicalLengthMultiplier`; без RNG. Стовбур сам завжди лишається кандидатом (Mature, depth 0), тож дерево не застрягає й без Фенікса.

**Офлайн / Dormancy**: перші ≤ 168 год офлайну — ті самі погодинні кроки (≤ 7 `DailyStep`); у Dormancy лічильники заморожені, росту немає (пагони пробудження — §9.5, окрема задача).

**Перевірка темпу** (Монте-Карло, 30 сидів, медіана; від стану після онбордингу N = 2, без обрізки; G — середній денний фактор):

| Умови | 16 вузлів | 30 | 50 | 100 | 200 |
|---|---|---|---|---|---|
| G = 1.0 (W ≥ 0.40, T ≥ 23 °C), vigor 1.0 | Д14 | Д23 | Д31 | Д47 | Д81 (мін. Д80) |
| G = 1.0, vigor 0.5 / 1.5 | Д14 / Д13 | Д28 / Д20 | Д41 / Д27 | Д60 / Д43 | Д94 / Д77 |
| G = 0.75 (фолбек Кіото 18.5 °C), vigor 1.0 | Д19 | Д29 | Д39 | Д61 | Д106 |
| G = 0.5 (≈ 14 °C), vigor 1.0 | Д27 | Д41 | Д56 | Д89 | Д156 |

Жорстка нижня межа: (200 − 2) / `ShootsPerDayMax` = 66 діб. Дні 1–~16 — рівно 1 пагін/добу (кламп знизу), насичення 3/добу — коли Σw ≥ 15 (≈ 20+ зрілих кандидатів, ~Д25–30). Розподіл глибин при N = 200: переважно d4–d5, max d7.

**Перевірка 72 год** (Momiji, +36 °C, без дощу, хмарність 0, день 16 год): з мохом W(72 год) ≈ 0.27 (M_soil ≈ 0.58), без моху ≈ 0.17 (M_soil ≈ 0.24) — ≥ 0.10, ріст не зупиняється; $T_{factor}(36) = 1$. Добові G з мохом: ≈ 1.0 / 1.0 / 0.9, четверта доба ≈ 0.2 (полив на Дні 3–4 повертає G = 1).

**Вимірювані AC для QA (PA-102)**:
- Один і той самий геном + однакова погодинна історія → побітово однаковий `TreeSnapshot` (онлайн-тіки ≡ офлайн catch-up).
- При G = 1 за добу народжується 1–3 пагони; при будь-якому G — ≤ `ShootsPerDayMax`; при W < 0.10 увесь день — 0 пагонів і 0 видовження.
- `Count ≤ MaxNodes` завжди; при `Count == MaxNodes` нових вузлів немає, `ShootCredit == 0`, потовщення стовбура ×3.
- Жоден вузол не має `Depth > MaxDepth`; жоден новий пагін не має |світового кута| > 90°.
- Бічний кут нового пагона ∈ [Mean − Spread, Mean + Spread] породи (± до клампу 90°); товщина ≤ $th_{max}(d)$; без NaN.
- Сценарій G = 1, vigor 1, seed з тест-фікстури: N(Д31) ≥ 50, N(Д47) ≥ 100, N ≤ 200 і досягає 200 не раніше Д66.
- Після посадки N = 3, гілочка #2 — бічна дитина стовбура depth 1; після її зрізу N = 2, після зрізу лідера — Фенікс (N = 2, Bud).

**Залежності**: `TargetLength` у `NodeData` і нові поля стану дерева — зміна ADR-0003 / сейву (Unity Architect); `CurvatureOffsetRatio` — рендер PA-103 (Gameplay Dev / Technical Artist); `vigor` і `bendBias` стартових насінин до PA-205 — 1.0 і 0.0.

## 2. Дріт — `GrowthConfig.Wire`

| Параметр | Значення | Config |
|---|---|---|
| Сектор кута | ±π/4 від осі батька | `WireMaxAngle` |
| Днів до фіксації | 14 | `WireSetDays` |
| Релаксація при достроковому знятті | $\theta = \theta_{base} + \frac{k}{14}(\theta_{target}-\theta_{base})$ | — |
| Мін. дистанція драгу | 14 dp | `InputConfig.WireMinDrag` |

## 3. Породи — `SpeciesConfig` (×3)

| Параметр | Kuromatsu | Momiji | Yamazakura | Config |
|---|---|---|---|---|
| $K_{base}$ (випаровування/добу) | 0.15 | 0.28 | 0.22 | `KBase` |
| Кут бічних гілок, середнє ± розкид (трикутний розподіл, §1.1) | 45° ± 15° | 30° ± 10° | 35° ± 20° | `BranchAngleMeanDeg`, `BranchAngleSpreadDeg` |
| Розкид кривини `Curvature` (трикутний, ∈ [−s, s]) | 0.45 (звивистий) | 0.20 (плавні дуги) | 0.30 (м'які вигини) | `CurvatureSpread` |
| Колір листя (основний) | `#2E4F2D` | `#5C8A42` | `#6A994E` | (Art) |
| Сезонні кольори | свічки `#9CCC65` (весна) | `#D98A3C` (вер–жов), `#A33B29` (лис), скидає взимку | цвіт `#FFB7C5` / серцевина `#F48FB1`, 14–21 день навесні | (Art) |

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
| 2026-10-04 | §1 `MaxDepth`, `BaseGrowthProbability`; §1 нові параметри; §1.1 (новий); §3 `CurvatureSpread` | TBD → 8; TBD → 0.20; — → `M_soil` (W_opt 0.40), стадії Bud 1 / Mature 5 діб, видовження 0.5, листя 3 доби, довжини 30 × 0.85^(d−1) ±20%, апікал ×1.2 / ±6° / bendBias×12°, кламп ±90°, MaxChildren 3, Небарі ×3, саджанець (стовбур 20→48 / 4.0, лідер 10, гілочка 8); кривина 0.45/0.20/0.30 | PA-102: розблокувати доменну модель росту; темп: 50/100/200 вузлів на Д31/Д47/Д81 при G = 1 | Game Designer |
| 2026-10-04 | `TempFactorMin` | 0 (ТЗ) → 0.25 | Рішення власника: зимовий мінімум, щоб у холодному кліматі дерево росло повільно, а не стояло місяцями | Оркестратор |
