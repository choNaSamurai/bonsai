# Архітектура Pocket Arbor на Unity

> Власник: **Unity Architect** · Статус: **Draft v0.4** (очікує погодження) · 2026-10-05 v0.4 (PA-102P: §4 — `LastCutDay`, `AxisRemain`/`InPad`/`BackBudDist`, `rulesVersion`; §6.1 — горщик як масштаб сцени і кадрування камери; §6.2–6.3 — корені Небарі, подушки-кластери) · v0.3 2026-10-05 (PA-102/PA-102S: модель §4 — нові поля, стан дерева, `TreeShapeAnalysis`, детермінізм; рендер §6.2–6.3 під L2 Painterly) · v0.2 2026-10-04 (PA-101: фактичні версії пакетів, структура Assets)
> Джерело вимог: [`reference/flutter-v3.2/`](../reference/flutter-v3.2/) — ТЗ, архітектура і дизайн-система, написані під Flutter. Цей документ **замінює** Flutter-специфічні частини; рушій-незалежні вимоги (математика росту, ґрунт, онбординг, комплаєнс) переходять у [GDD](../gdd/GDD.md) і [BALANCE](../gdd/BALANCE.md) без змін.
> Ключові рішення оформлені як ADR у [`docs/adr/`](../adr/).

---

## 1. Цілі та обмеження

| Ціль | Значення | Коментар для Unity |
|---|---|---|
| Платформи | iOS 16+, Android 10+ (API 29); Web — пріоритет 2 | Unity Web (WebGL2/WebGPU) — після MVP |
| Частота кадрів | 120 FPS на ProMotion під час взаємодії, 60 FPS базово | В режимі споглядання — адаптивне зниження (див. §6.5) |
| GC у кадрі | 0 B алокацій у steady-state | Перевіряється Performance-тестами (`GC.Alloc` recorder) |
| Розмір | Android: Play download arm64 ≤ 25 МБ (жорстка межа 30); iOS: App Store download ≤ 35 МБ, install ≤ 55 МБ | Перевизначено власником 2026-10-04 після Spike PA-003: бюджети ТЗ (.ipa < 25 / .aab < 20) недосяжні для Unity + URP — порожній проєкт уже 18.2 МБ (Android) / ~30 МБ (iOS download). Звіт: [PA-003](../qa/reports/PA-003-2026-10-04.md) |
| Холодний старт | < 1.2 с (Flutter-ціль) | **Ризик**: для Unity реалістично 1.5–2.5 с на mid-tier. Ціль перевизначається після Spike PA-003 |
| Аудіо-латентність | < 15 мс (Flutter-ціль) | **Ризик**: Unity на Android дає ~20–40 мс. Spike PA-004 вимірює і, за потреби, переходимо на native-audio плагін |

## 2. Технологічний стек (пропозиція → ADR-0001…0008)

| Підсистема | Flutter (оригінал) | Unity (пропозиція) | ADR |
|---|---|---|---|
| Рушій | Flutter 3.47 / Impeller | **Unity 6.6 (6000.6.4f1)**, IL2CPP, ARM64 | [0001](../adr/0001-unity-version-and-pipeline.md) |
| Рендер-пайплайн | Impeller (Metal/Vulkan) | **URP + 2D Renderer**, ортографічна камера | [0001](../adr/0001-unity-version-and-pipeline.md) |
| Архітектура коду | Feature-First Clean Arch | Clean Arch через **Assembly Definitions**; Domain без UnityEngine | [0002](../adr/0002-layered-assemblies.md) |
| DI | Riverpod providers | **VContainer** | [0002](../adr/0002-layered-assemblies.md) |
| Реактивний стан | Riverpod Notifier | **R3** (`ReactiveProperty`, `Observable`) | [0002](../adr/0002-layered-assemblies.md) |
| Async | Dart Future | **UniTask** | [0002](../adr/0002-layered-assemblies.md) |
| Модель дерева | `@immutable TreeNode` + `copyWith` | **Плоска таблиця вузлів (SoA) + версіонування**, `AttachT`, похідний аналіз форми, незмінні снапшоти для збереження/хроніки | [0003](../adr/0003-tree-data-model.md) |
| Рендер дерева | `TreeRenderBox`, `drawPath` ×4, `drawRawAtlas` | **Процедурний Mesh гілок уздовж C1-осей (1 draw call) + подушки листя з пуфів (1 draw call)**, вітер — у vertex shader | [0004](../adr/0004-procedural-tree-rendering.md) |
| Шейдери | GLSL `rain_sheen.frag` | **Shader Graph / HLSL** (URP 2D Sprite-Lit) | [0004](../adr/0004-procedural-tree-rendering.md) |
| Ввід | Pointer dispatcher + InteractiveViewer | **Input System + EnhancedTouch**, власний `GestureRouter` | [0005](../adr/0005-input-gesture-router.md) |
| UI | Flutter widgets | **uGUI Canvas + TextMeshPro** | [0006](../adr/0006-ui-ugui-canvas.md) |
| Твіни | Flutter Animations | **PrimeTween** (zero-alloc) | [0006](../adr/0006-ui-ugui-canvas.md) |
| Аудіо | `flutter_soloud` + `audio_session` | **Unity Audio + AudioMixer**, native-плагін для interruptions / becoming-noisy | [0007](../adr/0007-native-platform-layer.md) |
| Хаптика | HapticFeedback | Native-плагін (iOS `UIImpactFeedbackGenerator`, Android `VibrationEffect`) | [0007](../adr/0007-native-platform-layer.md) |
| Віджети ОС | `home_widget` | iOS **WidgetKit extension** (PostProcessBuild), Android **AppWidgetProvider (RemoteViews)** в `.androidlib` | [0007](../adr/0007-native-platform-layer.md) |
| Сховище | атомарний JSON + shared_preferences | **AtomicFileStore** (C#: tmp → bak → rename, SHA-256 envelope) + `PlayerPrefs` для пласких налаштувань | [0008](../adr/0008-persistence.md) |
| Серіалізація | dart:convert | `com.unity.nuget.newtonsoft-json` | [0008](../adr/0008-persistence.md) |
| Погода | dio + Open-Meteo | `UnityWebRequest` + Open-Meteo | — |
| Геолокація | geolocator (coarse) | `Input.location` (accuracy 5 км) + маніфест-патч COARSE-only, або IP/ручне місто | [0007](../adr/0007-native-platform-layer.md) |
| IAP | in_app_purchase | **Unity IAP 5** (`com.unity.purchasing`, StoreKit 2) | — |
| Нотифікації | flutter_local_notifications | `com.unity.mobile.notifications` | — |
| Шеринг | share_plus | NativeShare (yasirkula, MIT) | — |
| QR | qr_flutter | ZXing.Net (Apache-2.0) | — |
| Deep links | app_links | `Application.deepLinkActivated` + Universal/App Links | — |
| Wakelock | wakelock_plus | `Screen.sleepTimeout = SleepTimeout.NeverSleep` | — |
| Локалізація | intl | `com.unity.localization` (en, ja; uk — v1.1) | — |
| Тести | flutter_test, golden_toolkit | **Unity Test Framework** (EditMode/PlayMode), `com.unity.test-framework.performance`, скриншот-порівняння | [QA strategy](../qa/TEST_STRATEGY.md) |

### 2.1. Фактичні версії пакетів (PA-101, Unity 6000.6.4f1)

Джерело правди — `Packages/manifest.json` + `Packages/packages-lock.json`; обґрунтування — TD-005…TD-007.

| Пакет | Версія | Джерело |
|---|---|---|
| `com.unity.render-pipelines.universal` (+ core, shadergraph) | 17.6.0 | вбудований |
| `com.unity.inputsystem` | 1.20.0 | Unity |
| `com.unity.ugui` (uGUI + TextMeshPro) | 2.6.0 | вбудований |
| `com.unity.2d.sprite` | 1.0.0 | вбудований |
| `com.unity.test-framework` / `.performance` | 1.8.0 / 6.6.0 | вбудований |
| `com.unity.nuget.newtonsoft-json` | 3.2.2 | Unity |
| `com.unity.localization` (→ addressables 2.11.2) | 1.5.13 | Unity |
| `com.unity.mobile.notifications` | 2.5.0 | Unity |
| `com.unity.purchasing` (IAP 5, StoreKit 2) | 5.4.4 | Unity registry |
| `jp.hadashikick.vcontainer` | 1.19.0 | OpenUPM |
| `com.cysharp.r3` (R3.Unity) + `org.nuget.r3` (ядро) | 1.3.1 | OpenUPM + UnityNuGet (TD-006) |
| `com.cysharp.unitask` | 2.5.11 | OpenUPM |
| `com.kyrylokuzyk.primetween` | 1.3.3 | OpenUPM |

Модулі в manifest: `androidjni, audio, imageconversion, imgui, jsonserialize, particlesystem, physics2d, ui, unitywebrequest`. Залежностями додатково підтягуються `physics` (від uGUI), `physicscore2d`, `animation`, `uielements`, `hierarchycore`, `assetbundle`, `unitywebrequestassetbundle`. Відсутні: terrain, cloth, vehicles, ai, video, wind, vr/xr, umbra, director/timeline, unityanalytics.

> **Пастка імен**: namespace `PocketArbor.Application` затіняє `UnityEngine.Application` — у коді `PocketArbor.*` пишемо `UnityEngine.Application.xxx` (TD-008).

## 3. Шари та Assembly Definitions

```
Assets/_Project/
├── Scripts/
│   ├── Domain/            PocketArbor.Domain          (noEngineReferences: true)
│   ├── Application/       PocketArbor.Application     (noEngineReferences: true)
│   ├── Infrastructure/    PocketArbor.Infrastructure  (UnityEngine, native bridges)
│   ├── Presentation/      PocketArbor.Presentation    (MonoBehaviours, рендер, ввід, UI)
│   └── Bootstrap/         PocketArbor.Bootstrap       (VContainer LifetimeScope — composition root)
├── Tests/
│   ├── EditMode/Domain/           PocketArbor.Domain.Tests
│   ├── EditMode/Infrastructure/   PocketArbor.Infrastructure.Tests
│   ├── PlayMode/                  PocketArbor.PlayMode.Tests
│   └── Performance/               PocketArbor.Performance.Tests
├── Art/        (Sprites, Materials, Shaders, VFX, Fonts)        — Technical Artist
├── Audio/      (Clips, Mixer)                                    — Technical Artist
├── Config/     (ScriptableObjects: Species, Soil, Growth, Input, Audio) — Game Designer задає значення
├── Prefabs/    (TreeView, Pot, UI/TreeHUD, UI/BottomToolDock, ...)
├── Scenes/     Main.unity (одна сцена)
├── Settings/Rendering/  URP_Default, URP_LowEnd, Renderer2D, URP Global Settings, DefaultVolumeProfile
├── Editor/     PocketArbor.Editor (ProjectSetup — ідемпотентне налаштування проєкту; Build/ — PostProcessBuild)
└── link.xml    (стрипінг, TD-011)

Assets/Plugins/                       (стандартне розташування Unity, TD-010)
├── iOS/         PrivacyInfo.xcprivacy; далі Haptics.mm, AudioSessionBridge.mm, AppGroupBridge.mm, Widget~/ (Swift)
└── Android/     pocketarbor-native.androidlib/ (Kotlin/Java: haptics, noisy receiver, widget)
```

**Правила залежностей** (перевіряються asmdef-посиланнями — порушення не скомпілюється):

```
Bootstrap ──► Presentation ──► Application ──► Domain
     │              │                ▲
     └────► Infrastructure ──────────┘   (реалізує інтерфейси Application/Domain)
```

- **Domain** — 100% чистий C# (аналог «Pure Dart»): `TreeGraph`, `TreeShapeAnalysis`, `TreeGeometry`, `GrowthSimulator` (граматика росту), `SoilSimulation`, `OfflineCatchUp`, `WireGuardrails`, `SeedGenome`, `PhoenixProtocol`, `IClock`, `IRandomSource` (PCG32 — детермінований на всіх платформах, на відміну від `System.Random`/`UnityEngine.Random`). Математика у `double`.
- **Application** — use-cases та стан гри: `GardenSession` (власник `TreeGraph` + `SoilState`), команди `Prune`, `Wire`, `Water`, `Mist`, `PlantSeed`, `OnboardingFlow`, порти (`ISaveStore`, `IWeatherProvider`, `IHaptics`, `IAudioCues`, `IWidgetPublisher`, `IClock`). Публікує доменні події (`NodePruned`, `SoilChanged`, …) через R3 `Subject`/`ReactiveProperty`.
- **Infrastructure** — реалізації портів: `AtomicFileStore`, `OpenMeteoClient`, `LocationProvider`, `NativeHaptics`, `NativeAudioSession`, `WidgetSnapshotPublisher`, `UnityIapStore`, `LocalNotifications`, `UnityClock`.
- **Presentation** — `TreeRenderer` (`BranchMeshBuilder`, `FoliageMeshBuilder`), `PotView`, `SkyBackdrop`, `CameraRig`, `GestureRouter`, `ToolController`s, `AudioCuePlayer`, `HapticsFeedbackRouter`, UI-в'юшки (`TreeHudView`, `BottomToolDockView`, …). **Побічні ефекти (звук/хаптика/віджет) ініціюються тут**, не в Domain/Application (Резолюція 3 оригіналу).
- **Bootstrap** — `RootLifetimeScope` реєструє все; `AppLifecycleObserver` (`OnApplicationPause`/`OnApplicationFocus`) запускає save, catch-up та паузу аудіо.

## 4. Модель даних дерева (→ ADR-0003)

Оригінал використовує рекурсивне незмінне дерево `TreeNode` з `copyWith`. Для Unity — **плоска таблиця вузлів** (реалізовано в PA-102, розширюється в PA-102S):

```csharp
// PocketArbor.Domain.Tree — no UnityEngine
public struct NodeData
{
    public int Id;              // стабільний id (монотонний, < NextId), не індекс
    public int ParentIndex;     // -1 лише для #0 (стовбур)
    public byte Depth;
    public double Length;       // біологічні units
    public double TargetLength; // ціль видовження (BALANCE §1.1)
    public double Thickness;    // діаметр в ОСНОВІ вузла, ніколи не зменшується
    public double BaseAngle;    // рад, відносно осі батька
    public double CurrentAngle; // з урахуванням дроту
    public double Curvature;    // [-1, 1], вигин сегмента
    public double AttachT;      // [0, 1] — де на батькові починається вузол; апікальні = 1 (PA-102S)
    public sbyte BranchSide;    // 0 — стовбур/апікальний, ±1 — бічний на цьому боці (TD-013)
    public int AgeDays;
    public NodeStatus Status;   // Bud, Growing, Mature
    public float LeafScale;     // 0..1
    public float FlowerStage;   // 0..1
    public long WiredAtUtcMs;   // 0 = без дроту
    public byte WireProgressDays; // 0..14
    public int LastCutDay;      // GrowthDayIndex останнього зрізу ДИТИНИ цього вузла; -1 = не було (PA-102P, ADR-0003 rev.3)
}

public sealed class TreeGraph
{
    public const int Capacity = 200;                       // преалокація NodeData[200]
    public int Count { get; }
    public int Version { get; }                            // ++ на кожну мутацію вузлів → перебудова мешу/кешів
    public TreeGenome Genome { get; }                      // seed, species, vigor, bendBias — незмінні
    public GrowthState Growth { get; }                     // лічильники години/доби, ShootCredit, Σ-факторів, Frozen
    public int NextId { get; }
    public int PlantedRulesVersion { get; }                // GrowthRules.Version на посадці (ADR-0003 §8; додається з PA-205)
    public ReadOnlySpan<NodeData> Nodes { get; }
    // Мутації — лише доменні операції: GrowthSimulator.SimulateHour/DailyStep, TreePruning.Prune, дріт (PA-105B)
    public TreeSnapshot ToSnapshot();                      // незмінна копія для save/хроніки/тестів
    public static bool TryFromSnapshot(TreeSnapshot s, out TreeGraph g, out string error);
}
```

**Похідні — не зберігаються, рахуються чистими функціями Domain** (O(n), 0 B GC, преалоковані буфери результату, викликач перераховує на зміну `Version`):
- `TreeShapeAnalysis` — порядок гілки (`Order`/`OrderEff`), стовбурний ланцюг (`ChainIndex`) і вісь крони, `ApicalChild`, `TipDistance`, **`AxisRemain`, `InPad`** (зона подушки: останні `PadDepth` вузлів кожної осі гілки і все, що від них відходить), `IsLeafy` (через `InPad`), `PadId`/`PadSize`, **`BackBudDist`** (відстань до свіжого зрізу, бек-бадінг) — BALANCE §1.2.0, §1.3.4–§1.3.5. Вхід — таблиця вузлів + `ShapeAnalysisParams` (у т.ч. **`GrowthDayIndex`**); результат валідний для пари (`Version`, `GrowthDayIndex`). Споживачі: ріст (ролі вибору батька, бек-бадінг), рендер, хіт-тест, `PruneToStyle`, Хроніка/стилі.
- `TreeGeometry` (PA-103) — C1-криві осей, основа бічної дитини на кривій батька за `AttachT`, точки й дотичні; одна геометрія для рендеру (§6.2), хіт-тесту (§7.1) і дроту/Pot Lip.
- `HasLeaves`, `TierPhase` (біт seed), світові кути.

**Стан зрізу** (ADR-0003 rev.3 §2): `LastCutDay` ставить лише `TreePruning.Prune` батькові зрізаного вузла (`= GrowthDayIndex`; 0 — валідний день, «немає» = −1, усі нові вузли — через одну фабрику з −1). Одне поле дає і бек-бадінг (×6 вага вибору батька в радіусі 2 ребер протягом 14 діб росту, лише `ord ≥ 1`), і вік калюсу для рендеру. Апікальна затримка стовбура (`AgeDays ≥ 6 + 3k`) нового стану не потребує.

**Детермінізм** (ADR-0003 §6): PCG32, stream = номер доби (посадка — 0); рівно 8 викликів `u()` на пагін і 18 на посадку (PA-102P бюджет не змінює). Гарантія **A** — побітово однаковий снапшот на тому ж білді/платформі (онлайн ≡ catch-up ≡ save/load шматками). Між платформами (Mono ↔ IL2CPP ARM64) — лише очікування **B**: та сама структура, неперервні значення до ULP (`Math.Exp/Pow`, FMA). Тому `#dna=` передає **геном**, а не дерево; «те саме дерево» на іншому пристрої — лише через снапшот/зображення.

**Версія правил росту** (ADR-0003 §8): `GrowthRules.Version` (const у Domain; 1 — PA-102S, **2 — PA-102P**) інкрементується в тому ж коміті, що змінює результат росту для того самого геному й історії. У коді — один набір правил; сейв зберігає стан і `rulesVersion`, дерево ніколи не переграється від seed → нова версія діє лише на ріст далі, вже виросла форма не змінюється. Фікстури детермінізму несуть `rulesVersion` і падають з вимогою перегенерації при розбіжності.

Чому таблиця: рендер і хіт-тест читають `ReadOnlySpan` без алокацій; Prune — компакція масиву з переіндексацією батьків (O(n), n ≤ 200); снапшот незмінний, тож «чистота стану» з оригіналу зберігається на межі збереження/хроніки. Каскадне очищення дроту (PA-105A) тривіальне — дріт зберігається у самому вузлі. Схема сейву — ADR-0008 (`schemaVersion: 1`).

> **Увага, система координат**: у Unity вісь Y дивиться **вгору**. Правило Pot Lip з ТЗ ($Y_C \le Y_{pot\_lip}$, екранні координати Flutter) у Unity: **$Y_C \ge Y_{pot\_lip}$** для всіх вузлів піддерева.

## 5. Ігровий цикл та час

```
AppStart / OnApplicationPause(false)
  └─► GardenSession.Resume(nowUtc)
        ├─ if !OnboardingCompleted → Δt = 0 (Freeze Guard)
        ├─ Δt < 0 → якір = now (Anti-Lockout)
        ├─ OfflineCatchUp.Run(min(Δt, 168h)) погодинно → Soil + Growth
        ├─ Δt > 168h → Dormancy (moisture ≤ 0.05, дріт фіксується)
        └─ Publish(TreeChanged, SoilChanged)

Кожен кадр (Presentation):  тільки анімації/вітер/частинки — без симуляції
Кожну ігрову хвилину (Application): легкий Tick симуляції в онлайні (накопичення до 1 год)
OnApplicationPause(true):
  ├─ AtomicFileStore.Save(snapshot)   (синхронно, до виходу з методу)
  ├─ Widget: публікується ГОТОВИЙ PNG, знятий раніше (див. §7.3)
  └─ AudioListener.pause = true; tweens paused
```

`IClock` інжектиться всюди — тести керують часом (365 днів офлайну, перевод годинника назад, DST).

## 6. Рендеринг (→ ADR-0004)

### 6.1. Сцена
- URP **2D Renderer**, одна ортографічна камера, `Light2D` (Global) для циркадного освітлення (колір/інтенсивність з токенів [DESIGN_SYSTEM §3.1](../reference/flutter-v3.2/DESIGN_SYSTEM_AND_UI_SPEC.md)).
- Шари (Sorting Layers): `Backdrop` (небо-градієнт + текстура васі) → `Pot` → `Tree` (гілки, коріння) → `SoilFront` (мох/ґрунт поверх коріння Небарі) → `Leaves` → `FX` → UI (Screen Space – Overlay Canvas).
- Статичні шари (горщик, фон) — звичайні спрайти; кешування в RenderTexture не потрібне (у Flutter воно було через RepaintBoundary — тут draw-calls дешеві, а VRAM-thrashing на foldables знімається автоматично).

**Горщик — масштаб сцени** (PA-102P, BALANCE §1.3.0, ADR-0004 §4a, TD-030):
- Дерево рендериться в units моделі 1:1; горщик за замовчуванням має **фіксовану** ширину зовнішнього переднього краю **92 units** і не підлаштовується під дерево (відношення H/P ≈ 1.42, W/P ≈ 1.15 на Д80 Styled = пропорції референсу L2).
- **`PotConfig`** — ScriptableObject у Presentation-конфігах (`Assets/_Project/Config/Pots/pot_<id>.asset`), один на горщик каталогу (BALANCE §9.3: `pot_tokoname` — дефолт, `pot_kuro`, `pot_celadon`; `MonetizationConfig.Catalog[]` посилається на `PotConfig` за `PotId` = `ProductId`). Поля: `PotId`, спрайти корпусу і `SoilFront`, **`FrontWidthUnits`** (92), нормалізовані координати спрайта — зовнішній передній край (ліво/право), верх бортика (= y 0 моделі), лінія ґрунту, точка основи стовбура (45% між переднім і заднім внутрішнім краєм, TD-025). Читають лише `PotView`, `SoilFront` і `CameraFramer`.
- **Domain про горщик не знає**: Pot Lip (`Y ≥ potLipY`) і основа #0 = (0, 0) — у координатах моделі; косметичний горщик не може змінити ріст, хіт-тест чи дріт.
- **MVP-правило**: усі горщики каталогу мають `FrontWidthUnits = 92` (Editor-валідатор `PotConfig`); інша ширина змінює сприйняття того самого дерева і AC пропорцій — лише рішенням GD. До появи `PotConfig` прототип тримає `TreeRenderConfig.PotWidthUnits = 92` (замість `clamp(0.62·W, 60, 170)`, TD-024).
- Схема `PotConfig` — Unity Architect (PA-103a), асети й координати — Technical Artist (PA-108). *(BALANCE §1.3.0 і PA-102P посилаються на «каталог горщиків PA-203» — у BACKLOG PA-203 = візуальні стани ґрунту; каталог/Полиця ще не мають задачі.)*

**Кадрування камери** (`CameraFramer`, Presentation, чиста функція + `CameraRig`; TD-030; числа — **Proposed**, тонують UI/GD):
- Камера кадрує **сцену від горщика**, а не bounds дерева; гравець зумить/панорамує *відносно* базового кадру (pinch, ADR-0005), подвійний тап повертає базовий кадр.
- **Сцена-рамка** = safe area мінус HUD і док інструментів (rect дає UI). **Базовий кадр z = 1 = кадр референсу L2**: ширина горщика = `PotScreenWidthFraction` 0.737 ширини рамки (566/768 px) → видима ширина `V₁ = 92 / 0.737 ≈ 125` units; верх горщика — на `RimFromBottom` 0.224 висоти рамки; центр по X — центр горщика (не дерева: асиметричне дерево не «їздить» кадром).
- **Зум** `z ∈ [ZoomMin, ZoomMax]`, видима ширина `V = z·V₁`, висота `V / aspect`. Береться **найменше** `z`, за якого дерево вміщується: `H_tree ≤ (1 − r(z) − TopMargin)·V/aspect` і `max(|x_min|, |x_max|) ≤ (0.5 − SideMargin)·V` (bounds кори + подушок без коренів, із тих самих буферів, що й меш, кеш на `Version`); `r(z) = lerp(RimFromBottomAtMinZoom, RimFromBottom, (z − ZoomMin)/(1 − ZoomMin))`, для `z ≥ 1` — `RimFromBottom`. Монотонна умова → бісекція 12 кроків, 0 B GC.
- Дефолти: `ZoomMin` **0.78** (рішення власника 2026-10-05: горщик завжди цілий у кадрі), `ZoomMax` 1.30 (Wild H/P до 1.9), `TopMargin` 0.19 (на референсному aspect 768×1376 Styled H = 130 дає рівно z = 1), `SideMargin` 0.03, `RimFromBottomAtMinZoom` 0.30.
- **Саджанець Д0–Д10 — рішення власника 2026-10-05: горщик завжди цілий** (`ZoomMin` 0.78; `RimFromBottomAtMinZoom` підібрати так, щоб низ горщика лишався в кадрі). Саджанець у перші дні менший у кадрі; виразність онбордингового зрізу забезпечують підсвічування гілочки й анімація, а не зум. Відхилений варіант «макро» (`z ≈ 0.60–0.65`): верх горщика і саджанець, бічні краї й низ горщика за кадром, саджанець Д0 займає ≈ 1/3 видимої висоти над бортиком, Д10 (H ≈ 72) — майже всю робочу висоту (без зуму, z = 1, — ≈ 17% і ≈ 42%). Далі кадр плавно відʼїжджає до `z ≈ 0.9–1.0` на Д40–Д80. Альтернатива для власника (§11): `ZoomMin` 0.78 — горщик завжди цілий, саджанець малий (як GDD §10.8 п.1 «тренувальний горщик»).
- **Коли оновлюється**: лише на нову добу росту (`TreeChanged` з іншим `GrowthDayIndex`), на `Resume` після catch-up і на перемикання дерева (Алея); **ніколи** під час/одразу після зрізу чи дроту (дерево після зрізу менше — кадр не «стрибає» під секатором). Гістерезис `|Δz| ≥ 0.02`; перехід 1.2 с ease-in-out (PrimeTween), відкладається, поки палець на екрані; ручний зум гравця зберігається як множник поверх базового.
- **Скріншот-харнеси** (IoU, `compare_momiji`, AC PA-102P) використовують **фіксований** кадр z = 1 на 768×1376 (TD-028), а не `CameraFramer`.

### 6.2. Гілки — один процедурний Mesh уздовж C1-осей
Арт-напрям L2 Painterly (`Art/Source/A_Concept/concept_day.jpg`): S-стовбур з різкою конусністю, Небарі, ярусні гілки з голими внутрішніми частинами, мальована кора з м'яким контуром.
- `TreeRenderer` тримає **один** `Mesh` гілок (MarkDynamic) і преалоковані `NativeArray` (8 192 вершини / 12 288 трикутників, індекси `ushort`). Перебудова **лише коли змінився `TreeGraph.Version`** (ріст, зріз, дріт): `TreeShapeAnalysis` → `TreeGeometry` → вершини, `Mesh.SetVertexBufferData` без алокацій; за потреби — Burst job.
- **Осі**: ланцюг апікальних продовжень = одна безперервна стрічка (стовбурний ланцюг + вісь крони — одна вісь; кожна бічна гілка з продовженнями — своя). Крива — центрипетальний Catmull-Rom → Безьє з узгодженими дотичними (C1, плавна S без зламів); `Curvature` додається функцією з нульовою похідною на кінцях. Кінець осі — напівкругла капа.
- **Кріплення**: основа бічної осі — точка на кривій батька при `AttachT`, зсунута всередину батька (комір без щілини).
- **Профіль**: `w(t) = lerp(Thickness, tipWidth, t)`, `tipWidth = max(Thickness(апікальна дитина), Thickness × 0.45)`; плавний перехід на стику.
- **Небарі — частина цього ж мешу** (ADR-0004 rev.3 §1, BALANCE §1.3.3): розширення основи (`F` до 0.65 на Д80) + 2–7 **товстих звивистих коренів** (`NebariBuilder`, Presentation; у `TreeGeometry` не входять): кожен — 2 кубічні Безьє зі спільною дотичною (C1), вниз-назовні 35–55° → майже горизонтально → кінець під лінією `SoilFront`; товщина 0.30–0.42·th0 → 0.45 основи на лінії моху (у видимій частині не тоншає до 0), хвиля 0.15·th0 з нульовою обвідною на кінцях; кора — `_BarkTrunk` того ж матеріалу; параметри кореня *i* — від хешу `(Seed, i)` і таблиці слотів віяла за індексом (поява нового кореня не зсуває наявні).
- **Кора в подушках, бруньки, калюс**: стрічка `InPad`-вузлів `ord ≥ 2` закінчується на 0.35·`PuffRadius` до кінчика (кінчик під листям); `Bud` — кори ≤ 1.5 units; калюс — вершинний атрибут «день зрізу» (`LastCutDay`) проти глобального `_GrowthDay`, без перебудови мешу (PA-105A).
- **Кора**: дві мальовані безшовні текстури (`bark_trunk`, `bark_young`, 512², ASTC) в одному матеріалі; UV — `v` за довжиною дуги осі (неперервно), `u` поперек × ширину (ізотропна щільність); бленд trunk ↔ young — вершинний атрибут за шириною/порядком. Циліндричне затінення, ink-контур і AA краю — у шейдері (без MSAA). Колишні «4 яруси товщини» оригіналу — це той самий атрибут, а не 4 draw calls: усі гілки = **1 draw call**.
- **Вітер** (Perlin, ±0.8°, період 3.5 с) і **дихання Дзен** (Y×1.015) — у vertex shader за атрибутами `flex`/`windPhase`; HLSL-include спільний із шейдером листя, тож подушки рухаються разом із гілками. CPU-вартість анімації ≈ 0.
- Бюджет @200 вузлів: ≈ 3 000 вершин / 4 500 трикутників, ≈ 100 КБ завантаження на перебудову.

### 6.3. Листя — подушки з кластерів спрайтів
- Листя несуть лише вузли `IsLeafy` (BALANCE §1.3.4): `InPad` — останні `PadDepth` вузлів кожної осі гілки і все, що від них відходить, + верхівка молодого стовбура; внутрішні частини гілок голі.
- **Подушка = компонента `PadId` = кластер цілих спрайтів-подушок** з атласу породи (D5 / дев-арт TA, 9 форм S/M/L із запеченим темним низом): ряди вздовж лінії регресії центрів вузлів, перекриття ×1.3, **бічна — ≤ 2 ряди (Kuromatsu — 1), купол — ≤ 3**; мала подушка (< `PadFullNodes`) — по малому спрайту на вузол; кожен кінчик `InPad` з `LeafScale ≥ 0.5` гарантовано всередині силуету (додатковий S-спрайт, якщо ряди не покрили); тінь-підкладка під подушкою в тому ж меші (ADR-0004 rev.3 §2, TD-027). До появи арту — плейсхолдер-атлас тієї ж розкладки, згенерований Editor-скриптом (TD-020).
- Один меш листя (`FoliageMeshBuilder`, tight-меш спрайтів, преалокація 6 144 вершини) разом із бруньками, квітами, «свічками» Kuromatsu → **1 draw call**. Сортування при перебудові: подушки знизу вгору (апекс останнім), у подушці пуфи знизу вгору; Sorting Layer `Leaves` поверх гілок.
- Нижня тінь подушки — атрибут `padLocalY` у шейдері (без окремого проходу; дефолт 0, бо низ запечено в спрайтах). Варіант спрайта, фаза вітру, підмножина Dormancy — хеш від стабільного `Id`, не від індексу.
- Сезони (осінній/квітковий атлас + `_SeasonBlend`), Dormancy (20% пуфів, −40% насиченості), свіжий приріст, `_Wetness`, `_Droop` — **параметри матеріалу**, без перебудови мешу; приховані пуфи схлопуються у вершинному шейдері (без `discard`). Згасання — масштабом окремих пуфів, не загальною альфою шару (інакше видно перекриття).
- Guard з оригіналу: `LeafScale < 0.02` → пуф не додається.
- **Бюджет і ризик**: Styled Д80 ≈ 60–90 спрайтів з підкладками, Wild Д80 до ≈ 300, ≤ 3 400 вершин (преалокація 6 144); очікуваний overdraw у кроні ≈ 2.5–3.5× (було 4–5× з пуфами; **виміряти** в PA-103b). На Android mid-tier при 120 FPS — головний ризик; мітігація: tight-меш, ≤ 2 вибірки текстури, `half`, профіль `LowEnd` (без підкладки, на 1 ряд менше, render scale 0.8), `OnDemandRendering` у простої. Деталі — ADR-0004 §4.
- Draw calls дерева: **2** (гілки + листя); сцена з горщиком, фоном і FX — 6–8 без UI.

### 6.4. Ефекти
- **Rain sheen** (дощ/обприскування): Shader Graph для листя з параметром `_Wetness` (0..1), що згасає з часом. Fallback для слабких GPU — простий additive-highlight без шуму.
- **Світлячки**: `ParticleSystem` 12–18 частинок, тільки у цивільну ніч (22:00–06:00).
- **Сухий ґрунт**: шейдер ґрунту з `_Dryness` (лерп `soilWet → soilDry`) + маска тріщин.
- **Падіння відрізаної гілки**: відрізане піддерево запікається в окремий тимчасовий меш з пулу, падає з g, обертається, розчиняється (0.35 с).
- **Frosted glass** UI: blur Camera Sorting Layer Texture (2D Renderer) у downsampled RT; fallback — суцільна плашка `#232724EB` на слабких GPU/Low Power Mode.

### 6.5. Частота кадрів і батарея
- `Application.targetFrameRate = 120` при взаємодії; `OnDemandRendering.renderFrameInterval` = 2–4 у споглядальному простої (вітер при 30–60 FPS виглядає так само спокійно), повернення до 1 при першому дотику.
- iOS ProMotion: `CADisableMinimumFrameDurationOnPhone = YES` в Info.plist через `PostProcessBuild`.
- У фоні Unity не рендерить; аудіо — пауза; таймери — на `IClock`, не на кадрах.
- `QualitySettings`: 2 профілі (`Default`, `LowEnd`), вибір за `SystemInfo` при першому запуску + перемикач у налаштуваннях.

## 7. Платформний шар (→ ADR-0007)

### 7.1. Ввід і жести (→ ADR-0005)
`GestureRouter` (Presentation) — єдиний глобальний обробник `EnhancedTouch`:
- Бачить **усі** дотики, включно з тими, що над UI → правило «2 пальці будь-де = abort інструмента» виконується природно.
- `pointerCount ≥ 2` → завжди `CameraRig` (pan/pinch-zoom, масштаб 0.1–10).
- `pointerCount == 1`: `Inspect` → pan; `Pruner`/`Wire` → активний інструмент; `Water`/`Mist` → тап.
- Deadzones з `Screen.safeArea` + мінімуми (59 pt / 34 pt / 24 dp), перераховані в пікселі через `ScreenMetrics` (dp → px = dp × dpi/160; iOS pt → px через native scale).
- Slash-to-Commit (> 24 dp, > 120 dp/с), pinch-window 35–45 мс, `GuardedWireMath` (≥ 14 dp).
- Snapping у screen space: `Camera.WorldToScreenPoint` для вузлів (масив кешується на `TreeGraph.Version` + зміну камери), $R_{snap}(s) = clamp(32\sqrt{s}, 16, 56)$ dp, гістерезис 8 dp.
- FSM режимів: `Inspect, Pruner, Wire, Water, Mist, Zen` — `ToolModeStateMachine` в Application, візуальна частина — в Presentation.

### 7.2. Аудіо
- Project Settings → Audio: DSP Buffer = **Best Latency**, Real Voices = 32.
- `AudioMixer`: групи `Master / SFX / Ambient / Zen`; ембієнт і дихання — `AudioSource.priority = 0` (захист від voice stealing).
- `SfxPool` (преалокований пул `AudioSource`) з лімітом 3 одночасних зрізи, гучність 0.75, pitch-jitter ±4%.
- Native-плагін `AudioSessionBridge`: iOS `AVAudioSession.interruptionNotification` + `routeChangeNotification (oldDeviceUnavailable)`; Android `ACTION_AUDIO_BECOMING_NOISY` + audio focus → пауза/відновлення (усуває «зомбі-тишу»).

### 7.3. Віджети
- **Знімок**: окрема вимкнена камера рендерить дерево у `RenderTexture 512×512` **через 800 мс після останньої дії** (debounce), `AsyncGPUReadback` → `ImageConversion.EncodeNativeArrayToPNG` у фоновому потоці → атомарний запис. У `OnApplicationPause(true)` нічого не рендеримо.
- **iOS**: WidgetKit extension (Swift, `TimelineProvider`) лежить у `Assets/Plugins/iOS/Widget~` і додається до Xcode-проєкту в `PostProcessBuild` (`PBXProject.AddAppExtension`, entitlements App Group). Шлях до контейнера отримуємо через native `containerURL(forSecurityApplicationGroupIdentifier:)`. Lock Screen — 8-бітна альфа-маска 128/256 px (≤ 12 КБ).
  > Причина крашу `0xdead10cc` — утримання файлового lock'у у спільному контейнері під час suspend. Тому запис у App Group завершується **до** повернення з `OnApplicationPause(true)`, без відкритих дескрипторів.
- **Android**: класичний `AppWidgetProvider` + `RemoteViews` у `pocketarbor-native.androidlib` (замість Jetpack Glance, що потребує Compose-тулчейну в Unity Gradle — див. ADR-0007). PNG у `filesDir` → `FileProvider` `content://` URI з `FLAG_GRANT_READ_URI_PERMISSION`; таймер — `Chronometer`/`TextClock`.

### 7.4. Інше
- **Геолокація**: Unity автоматично додає `ACCESS_FINE_LOCATION` при використанні `Input.location` → маніфест-патч (`tools:node="remove"`) і лише `ACCESS_COARSE_LOCATION`. Альтернатива на розгляд (ADR-0007): **без дозволу на геолокацію взагалі** — ручний вибір міста + IP-геолокація, що спрощує комплаєнс.
- **IAP**: Unity IAP 5 — `ConfirmPendingPurchase` = acknowledge у Google Play; Restore — FSM з 4 станів (`Idle → Restoring → Restored(n) | NothingToRestore | NetworkError(retry)`), таймаут 12 с.
- **Privacy Manifest**: власний `PrivacyInfo.xcprivacy` у `Assets/Plugins/iOS/` (UserDefaults `CA92.1` — `PlayerPrefs` на iOS це NSUserDefaults; FileTimestamp `C617.1`).
- **Deep links**: `pocketarbor.app/#dna=<43 chars>` → `SeedGenome.TryParse` (довжина 43, magic `0xBA`, версія, CRC; некоректне → `default`), карантин у `GiftInbox`.

## 8. Збереження (→ ADR-0008)

`AtomicFileStore` (Infrastructure) переносить алгоритм оригіналу 1:1:
1. JSON (Newtonsoft) → UTF-8 → SHA-256 → envelope `[64 hex][json]`.
2. `FileStream` → `tree_state.json.tmp`, `Flush(flushToDisk: true)`.
3. `File.Replace(tmp, primary, bak)` (rename-семантика, без копіювання блоків; fallback `Copy+Delete` на `IOException`).
4. Читання: `length > 64` → перевірка хешу → при невідповідності відновлення з `.bak` **і негайний перезапис primary** (Poison Pill guard), битий файл → `quarantine/`.
5. Шлях: `Application.persistentDataPath`. Налаштування (звук, вібрація, місто) — `PlayerPrefs`.
6. Схема має `schemaVersion` (стартова — 1: `genome`, `growth`, `nextId`, `plantedRulesVersion`, вузли з `targetLength`, `branchSide`, `attachT`, `lastCutDay`) і мігратори; `rulesVersion` — `GrowthRules.Version` збірки, що записала файл (ADR-0003 §8: сейв = стан, без реплею). `double` пишуться з побітовим round-trip. Деталі — ADR-0008 (доповнення 2a; на `develop` — rev.3 з `JsonUtility` і `double` як `long`-біти: при злитті формат чисел — за rev.3, нові цілі поля — без змін).

## 9. Конфігурація та баланс

Усі числа з [BALANCE.md](../gdd/BALANCE.md) живуть у **ScriptableObject**-ах (`SpeciesConfig`, `SoilConfig`, `GrowthConfig`, `InputConfig`, `AudioConfig`). Domain отримує їх як прості C#-структури (мапінг у Bootstrap), щоб лишатися без `UnityEngine`. Game Designer змінює значення в BALANCE.md → Gameplay Developer синхронізує ассети (або Editor-імпортер з таблиці — PA-007).

## 10. Збірка і розмір (Asset Diet)

**Бюджети (власник, 2026-10-04):**

| Платформа | Метрика | Ціль | Жорстка межа | Як міряти |
|---|---|---|---|---|
| Android | Play download size, arm64 | ≤ 25 МБ | 30 МБ | `bundletool build-apks` + `get-size total --device-spec arm64` |
| iOS | App Store download size | ≤ 35 МБ | — | App Store Connect / Xcode App Thinning Size Report |
| iOS | Install size | ≤ 55 МБ | — | розмір `.app` |
| Шрифт | ZenMaru субсет | < 450 КБ | — | без змін з ТЗ |
| Аудіо | усі кліпи | < 1.4 МБ | — | без змін з ТЗ |

Baseline порожнього URP-2D проєкту (PA-003): Android 18.2 МБ download, iOS ~30 МБ download / 40 МБ install. 88% — код рушія (`libunity` + `libil2cpp`).

**Обов'язкові налаштування збірки** (виміряний ефект на Android download):
- IL2CPP, ARM64 only; Managed Stripping = **High** (+ `link.xml`) — без нього +2.9 МБ; **Strip Engine Code** — без нього +4.9 МБ.
- IL2CPP Code Generation = **OptimizeSize** (OptimizeSpeed: +3.7 МБ); C++ Compiler Configuration = **Release** (Master/LTO: +1.3 МБ).
- URP Global Settings: strip unused post-processing / 2D / runtime-debug variants (−1.3 МБ).
- Graphics API Android: Vulkan + GLES3 (GLES3-only заощадив би 0.9 МБ — відкрите рішення, §11). Application Entry: GameActivity (Activity: −0.5 МБ, не беремо).
- Не форкати URP заради вирізання UIElements (найбільший шматок коду).
- Видалити з `manifest.json` невикористані модулі (`com.unity.modules.*`: physics 3D, terrain, vehicles, video, AI, cloth, …).
- Шрифт: ZenMaru Gothic subset → **static** TMP SDF-атлас (Alpha8) з фіксованим charset; dynamic fallback лише для UGC-поля.
- Аудіо: Vorbis/ADPCM mono, `Load Type` = Decompress On Load для коротких SFX, Streaming для ембієнту; бюджет < 1.4 МБ.
- Текстури: ASTC (iOS/Android), спрайт-атласи.
- Перевірка бюджету — Editor-скрипт після білда (`BuildReport`) + `bundletool get-size` з порогом; у CI пізніше. Скрипти вимірювання — у звіті PA-003 §9.
- Внесок VContainer / R3 / UniTask / PrimeTween / Localization+Addressables / Newtonsoft ще не виміряно — повторити PA-003 на проєкті PA-101.

## 11. Відкриті питання (потребують рішення власника)

1. Unity 6.6 (6000.6.4f1) замість 6.3 LTS — погодити компроміс підтримки (ADR-0001)
2. uGUI Canvas vs UI Toolkit для UI — пропоную uGUI (ADR-0006).
3. Геолокація: coarse-permission vs без дозволу (IP + ручне місто) (ADR-0007).
4. Android-віджет: RemoteViews vs Glance (ADR-0007).
5. Перегляд нереалістичних для Unity цілей: cold start < 1.2 с та аудіо < 15 мс — після спайків PA-003/PA-004.
6. ~~Кадр саджанця~~ — **вирішено власником 2026-10-05:** `ZoomMin` 0.78, горщик завжди цілий (§6.1).
