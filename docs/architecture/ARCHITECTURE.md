# Архітектура Pocket Arbor на Unity

> Власник: **Unity Architect** · Статус: **Draft v0.2** (очікує погодження) · 2026-10-04 (PA-101: фактичні версії пакетів, структура Assets)
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
| Модель дерева | `@immutable TreeNode` + `copyWith` | **Плоска таблиця вузлів (SoA) + версіонування**, незмінні снапшоти для збереження/хроніки | [0003](../adr/0003-tree-data-model.md) |
| Рендер дерева | `TreeRenderBox`, `drawPath` ×4, `drawRawAtlas` | **Процедурний Mesh гілок (1 draw call) + інстансинг листя (1 draw call)**, вітер — у vertex shader | [0004](../adr/0004-procedural-tree-rendering.md) |
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

- **Domain** — 100% чистий C# (аналог «Pure Dart»): `TreeGraph`, `GrowthGrammar`, `SoilSimulation`, `OfflineCatchUp`, `WireGuardrails`, `SeedGenome`, `PhoenixProtocol`, `IClock`, `IRandomSource` (PCG32 — детермінований на всіх платформах, на відміну від `System.Random`/`UnityEngine.Random`). Математика у `double`.
- **Application** — use-cases та стан гри: `GardenSession` (власник `TreeGraph` + `SoilState`), команди `Prune`, `Wire`, `Water`, `Mist`, `PlantSeed`, `OnboardingFlow`, порти (`ISaveStore`, `IWeatherProvider`, `IHaptics`, `IAudioCues`, `IWidgetPublisher`, `IClock`). Публікує доменні події (`NodePruned`, `SoilChanged`, …) через R3 `Subject`/`ReactiveProperty`.
- **Infrastructure** — реалізації портів: `AtomicFileStore`, `OpenMeteoClient`, `LocationProvider`, `NativeHaptics`, `NativeAudioSession`, `WidgetSnapshotPublisher`, `UnityIapStore`, `LocalNotifications`, `UnityClock`.
- **Presentation** — `TreeRenderer`, `LeafInstancer`, `PotView`, `SkyBackdrop`, `CameraRig`, `GestureRouter`, `ToolController`s, `AudioCuePlayer`, `HapticsFeedbackRouter`, UI-в'юшки (`TreeHudView`, `BottomToolDockView`, …). **Побічні ефекти (звук/хаптика/віджет) ініціюються тут**, не в Domain/Application (Резолюція 3 оригіналу).
- **Bootstrap** — `RootLifetimeScope` реєструє все; `AppLifecycleObserver` (`OnApplicationPause`/`OnApplicationFocus`) запускає save, catch-up та паузу аудіо.

## 4. Модель даних дерева (→ ADR-0003)

Оригінал використовує рекурсивне незмінне дерево `TreeNode` з `copyWith`. Для Unity пропонується **плоска таблиця вузлів**:

```csharp
// PocketArbor.Domain — no UnityEngine
public struct NodeData
{
    public int Id;              // стабільний id (монотонний лічильник), не індекс
    public int ParentIndex;     // -1 для кореня (стовбура)
    public byte Depth;
    public double Length;       // біологічні units
    public double Thickness;
    public double BaseAngle;    // рад, відносно батька
    public double CurrentAngle; // з урахуванням дроту
    public double Curvature;
    public int AgeDays;
    public NodeStatus Status;   // Bud, Growing, Mature, Pruned, Dormant
    public float LeafScale;     // 0..1
    public float FlowerStage;   // 0..1
    public long WiredAtUtcMs;   // 0 = без дроту
    public byte WireProgressDays; // 0..14
}

public sealed class TreeGraph
{
    public const int MaxNodes = 200;
    readonly NodeData[] _nodes = new NodeData[MaxNodes]; // преалокація
    public int Count { get; private set; }
    public int Version { get; private set; } // ++ на кожну мутацію → рендер перебудовує меш лише при зміні
    public ReadOnlySpan<NodeData> Nodes => _nodes.AsSpan(0, Count);
    // Мутації тільки через доменні операції: Grow(), Prune(id), Wire(id, angle), Tick(hours) …
    public TreeSnapshot ToSnapshot();          // незмінна копія для save/хроніки
    public static TreeGraph FromSnapshot(TreeSnapshot s);
}
```

Чому: рендер і хіт-тест читають `ReadOnlySpan` без алокацій; Prune — це компакція масиву з переіндексацією батьків (O(n), n ≤ 200); снапшот незмінний, тож «чистота стану» з оригіналу зберігається на межі збереження/хроніки. Каскадне очищення дроту (PA-105A) стає тривіальним — дріт зберігається у самому вузлі.

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
- Шари (Sorting Layers): `Backdrop` (небо-градієнт + текстура васі) → `Pot` → `Tree` → `Leaves` → `FX` → UI (Screen Space – Overlay Canvas).
- Статичні шари (горщик, фон) — звичайні спрайти; кешування в RenderTexture не потрібне (у Flutter воно було через RepaintBoundary — тут draw-calls дешеві, а VRAM-thrashing на foldables знімається автоматично).

### 6.2. Гілки — один процедурний Mesh
- `TreeRenderer` тримає **один** `Mesh` (MarkDynamic) і преалоковані `NativeArray<Vertex>` / `NativeArray<ushort>` під 200 сегментів × N вершин.
- Кожен сегмент — стрічка (ribbon) вздовж кубічної кривої Безьє з товщиною від `Thickness` батька до свого; на з'єднаннях — круглі «капи» (аналог `StrokeCap.round`, прибирає T-joints).
- Меш **перебудовується лише коли `TreeGraph.Version` змінився** (ріст, зріз, дріт), а не щокадру. Перебудова — через `Mesh.SetVertexBufferData` без алокацій; за потреби — Burst job.
- **4 яруси товщини** з оригіналу перетворюються на атрибут вершини (tier → колір кори/текстура), а не на 4 draw calls: усе дерево = **1 draw call**.
- **Вітер** (Perlin, ±0.8°, період 3.5 с) і **дихання Дзен** (Y×1.015) — у vertex shader за атрибутом «гнучкості» (глибина вузла). CPU-вартість анімації ≈ 0.

### 6.3. Листя, квіти, бруньки — інстансинг
- `LeafInstancer`: `Graphics.RenderMeshInstanced` з преалокованим `Matrix4x4[200 * k]` або один комбінований quad-меш з UV-атласом. **1 draw call**.
- Guard з оригіналу зберігається: `LeafScale < 0.02` → інстанс не додається (уникаємо вироджених матриць).
- Атлас листя з mip-maps, `Bilinear/Trilinear` фільтр (аналог `FilterQuality.medium`).

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
6. Схема має `schemaVersion` і мігратори.

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
