# Backlog — Pocket Arbor (Unity)

> Власник: головна сесія (оркестратор) · Оновлюють: усі агенти (тільки поле **Статус** і **Нотатки** своїх задач)
> Нумерація та скоуп збережені з [оригінального плану](../reference/flutter-v3.2/ARCHITECTURE_AND_PLAN.md) (PA-101…PA-404), додано **Sprint 0** під Unity-ризики.
> Деталізована задача = окремий файл `docs/tasks/PA-xxx.md` за [шаблоном](TASK_TEMPLATE.md) — створюється, коли задача переходить у `Ready`.

**Статуси**: `Backlog` → `Ready` (DoR) → `In Progress` → `Review` → `QA` → `Done` (DoD) · `Blocked`

**Агенти**: GD — Game Designer · UA — Unity Architect · GP — Gameplay Developer · UI — UI/UX Developer · TA — Technical Artist · QA — QA Engineer

## Sprint 0 — Foundation & Risk Spikes

| ID | Задача | Owner | Support | SP | Статус |
|---|---|---|---|---|---|
| PA-001 | Встановити Unity Hub + Unity 6.6 (6000.6.4f1) (iOS/Android modules), git-lfs; налаштувати UnityYAMLMerge | Власник | UA | 1 | Done |
| PA-002 | **Spike**: «hello-widget» в Unity-збірці — iOS WidgetKit extension через PostProcessBuild + Android AppWidgetProvider у `.androidlib` | UA | — | 5 | Backlog |
| PA-003 | **Spike**: baseline розміру (.ipa/.aab порожнього URP-2D проєкту зі стрипінгом) і cold start на реальних пристроях | UA | QA | 2 | Done — [звіт](../qa/reports/PA-003-2026-10-04.md); cold start не виміряно (немає пристроїв) |
| PA-004 | **Spike**: вимір аудіо-латентності Unity (Best Latency) на iPhone і mid-tier Android; рішення про native-audio | TA | UA | 2 | Backlog |
| PA-005 | Погодити ADR-0001…0008 і відкриті питання GDD §10 | Власник | UA, GD | 1 | Backlog |
| PA-006 | Скелет `.claude`-воркфлоу: перевірити агентів на пробній задачі | Оркестратор | всі | 1 | Backlog |
| PA-007 | Editor-тулінг: імпорт BALANCE.md → ScriptableObject-и (або ручні ассети + тест відповідності) | GP | GD | 2 | Backlog |
| PA-101b | Розмір білда, варіант C: OptimizeSize, C++ Release, R8, URP-стрипінг, звужений `link.xml`, без сплешу; прибрати Unity Localization і прямий Newtonsoft; ADR-0008 rev.3 (JsonUtility), ADR-0009 (білінг); бюджети Android ≤ 28 (30) / iOS ≤ 45 / 60 | UA | — | 2 | Review ([PA-101b](PA-101b.md)) — Android 25.06, iOS 55.95 / ~43.1 |

## Sprint 1 — Core Engine

| ID | Задача | Owner | Support | SP | Статус |
|---|---|---|---|---|---|
| PA-101 | Скафолдинг Unity-проєкту: URP 2D, asmdef-шари, VContainer/R3/UniTask/PrimeTween, Input System, Force Text, Player Settings (IL2CPP, ARM64, 120 FPS, ProMotion PostProcess, Privacy Manifest), Quality профілі, стрипінг | UA | — | 3 | Done ([PA-101](PA-101.md)) |
| PA-102 | Domain: `TreeGraph` (ADR-0003), `GrowthGrammar` з лімітом 200, Небарі (NaN-guard, експ. насичення), Фенікс, PCG32, `IClock` + EditMode-тести | GP | GD, QA | 5 | Review ([PA-102](PA-102.md)) |
| PA-102V | Візуальний прототип росту: Play Mode сцена, гілки лініями (товщина з моделі), бруньки/листя кружечками, повзунок «день 0→120» (таймлапс), вибір породи / seed / температури. Тимчасовий debug-рендер, не замінює PA-103 | GP | GD | 2 | Review ([PA-102V](PA-102V.md)) |
| PA-102S | Форма бонсаю за концептом L2: AttachT і яруси вздовж стовбура, конусність th_cap, S-стовбур, горизонтальні гілки, подушки листя (TreeShapeAnalysis), Небарі; оновлення прототипу GrowthPreview ([PA-102S](PA-102S.md)) | GP | UA, GD | 5 | Review |
| PA-103S | Стиль-зріз L2 у прототипі: текстури з референсу (подушки листя, кора trunk/young, горщик, ґрунт, васі) + текстурований рендер у GrowthPreview, порівняння поруч із референсом | TA, GP | UA | 5 | Done |
| PA-102P | Форма під референс L2 (Momiji Styled ≈ realism_l2_painterly): S-стовбур, висота й товщина відносно горщика, 6–7 ярусів з проміжками, окремі подушки, товсте звивисте Небарі, голі «ноги» гілок, back-budding; метрика збігу силуету з накладанням | GD, GP | TA, UA | 5 | Done |
| PA-102Q | Наближення до ідеалу, ітерація 2 (шлях A): структурні метрики замість IoU-порогу, рівний стовбур і видимі корені, нижні гілки вище й тонші, широкий купол, вужча крона, густіша сосна; параметри форми як ручки кастомізації (стилі) | GD, GP | TA, UA | 5 | Done |
| PA-102R | Наближення до ідеалу, ітерація 3 — нові правила моделі: довжина первинних за ярусом (рівні подушки), мінімальний проміжок ярусів одного боку, нерегулярний S-вигин зі скручуванням, товсті корені вище; лінія відліку yc/H; баги: кадр Shakan, «латки» коміра | GD, GP | TA, UA | 5 | Review (GP 2026-10-05: AC5–AC10, AC13–AC18, AC20–AC21 ✅; AC1 (80%-умова), AC2, AC3, AC4, AC12 частково, AC19 — ні в межах діапазонів, рішення GD) |
| PA-102T | Ідеал, ітерація 4 (A+): пріоритет росту молодих верхніх ярусів до розміру нижніх (рівні подушки), купол-шапка замість «пагоди», нижні подушки не злипаються, густіші тонші корені. Якщо не дасть результату — перехід на шаблон для Styled (B) | GD, GP | TA, UA | 5 | Review (GP 2026-10-05: модель T1–T7 ✅, T14/T15/T18 ✅; T8–T12, T16, T19 ❌; **критерій B спрацював** — рішення власника) |
| PA-103a | Рендер L2 — гілки й геометрія (ADR-0004 rev.2): `TreeGeometry` (Domain, спільна для рендеру/хіт-тесту/дроту), C1-сплайни осей, кріплення за AttachT з «коміром», профіль конусності, Небарі в мешу стовбура, мальована кора trunk/young, вітер у шейдері; 0 GC у кадрі | UA | TA | 5 | Backlog |
| PA-103b | Рендер L2 — листя подушками: пуфи зі спрайтів атласу D5 (до появи — Editor-плейсхолдер, TD-020), сортування, нижня тінь, сезони/Dormancy через матеріал, спільний вітер; LowEnd-профіль проти overdraw | UA | TA | 5 | Backlog |
| PA-104 | `GestureRouter` (ADR-0005): 2-пальцевий pan/zoom завжди, abort інструмента, deadzones Ingress/Egress, Slash-to-Commit, pinch window, snapping + гістерезис | GP | UA, QA | 5 | Backlog |
| PA-105A | Секатор: зріз піддерева, калюс, падіння гілки (пул), заборона зрізу стовбура | GP | TA | 3 | Backlog |
| PA-105B | Дріт: сектор ±π/4, Pot Lip через FK (Unity Y-up!), 14 днів, springback, time-jump guard | GP | — | 3 | Backlog |
| PA-106 | Онбординг v2.1 (40–45 с), Freeze Guard, Seed Gift Inbox | GP | UI, GD | 3 | Backlog |
| PA-107 | Базовий UI-кістяк: TreeHUD, BottomToolDock, SafeAreaFitter, токени, TMP-шрифт (subset) | UI | TA | 3 | Backlog |
| PA-108 | Базові арт-асети: горщик, фон васі, атлас листя/бруньок, шейдери гілок і листя (Shader Graph) | TA | UA | 5 | Backlog |

**Gate 1**: 120 FPS на iPhone ProMotion при 200 вузлах, 0 B GC у кадрі; жести розділені; онбординг зі зрізом на 25–40 с.

## Sprint 2 — World & Soil

| ID | Задача | Owner | Support | SP | Статус |
|---|---|---|---|---|---|
| PA-201 | `OpenMeteoClient` (UnityWebRequest, TTL 3 год), локація (ADR-0007 рішення), фолбек Кіото | GP | UA | 4 | Backlog |
| PA-202 | `SoilSimulation` погодинна (tanh-кламп, дощ експонентою, freeze, циркадний фільтр) + тести 72 год @ +36 °C | GP | GD, QA | 5 | Backlog |
| PA-203 | Візуальні стани сухості (шейдер ґрунту, тріщини, нахил листя), Misting + rain sheen + fallback | TA | GP, UI | 5 | Backlog |
| PA-204 | `OfflineCatchUp`: lifecycle, Anti-Lockout, 7-денний ліміт → Dormancy, stuck-weather guard | GP | QA | 5 | Backlog |
| PA-205 | `AtomicFileStore` (ADR-0008) + `SeedGenome` (43 chars, runtime-валідація) + тести корупції | UA | GP, QA | 4 | Backlog |
| PA-206 | Циркадне небо і світло (`Light2D`, градієнти DS §3.1), світлячки вночі | TA | — | 3 | Backlog |

**Gate 2**: ґрунт тримає 3–4 дні; > 7 діб → Dormancy; сейв переживає kill процесу і битий файл.

## Sprint 3 — Game Feel & Audio

| ID | Задача | Owner | Support | SP | Статус |
|---|---|---|---|---|---|
| PA-301 | Аудіо: Mixer, SfxPool (3 голоси, jitter ±4%), ембієнт з пріоритетом, `AudioSessionBridge` (interruptions, noisy) | TA | UA | 5 | Backlog |
| PA-302 | Хаптика: native-плагін + `HapticsThrottler` 70 мс | UA | GP | 2 | Backlog |
| PA-303 | Хроніка Гілок: щоденні снапшоти (< 5 КБ), `TimelapseSheet`, DST-safe дати | UI | GP | 4 | Backlog |
| PA-304 | Polaroid: офскрін-рендер, Hanko, QR (ZXing), NativeShare, deep link `#dna=` | UI | GP, TA | 3 | Backlog |
| PA-305 | Режим Дзен: приховання UI, дрейф камери, дихання 4-4 у шейдері, `Screen.sleepTimeout` | UI | TA, GP | 3 | Backlog |
| PA-306 | Полірування онбордингу: ASMR, хореографія Aha!-моменту (DS §7.2) | TA | GP, UI | 2 | Backlog |

**Gate 3**: латентність аудіо в межах цілі (після PA-004); Дзен без лагів; таймлапс ≥ 60 FPS.

## Sprint 4 — Platform, IAP, Release

| ID | Задача | Owner | Support | SP | Статус |
|---|---|---|---|---|---|
| PA-401A | Proactive snapshot pipeline (debounce 800 мс, AsyncGPUReadback, атомарний запис у App Group / filesDir) | UA | — | 3 | Backlog |
| PA-401B | Нативні віджети: iOS WidgetKit (+ Lock Screen маска), Android AppWidgetProvider + FileProvider | UA | UI | 5 | Backlog |
| PA-402 | Unity IAP 5: каталог, acknowledge, Restore FSM, Family Sharing, Privacy Manifest | GP | UI, QA | 5 | Backlog |
| PA-403 | Профайлінг: 0 GC, adaptive frame pacing, батарея у фоні, memory warnings | UA | QA | 4 | Backlog |
| PA-404 | Нотифікації (⚠ вимкнути `RequestAuthorizationOnAppLaunch` у NotificationsSettings — запит дозволу лише після онбордингу), ASO (marketingskills: `aso`, `launch`), перевірка бюджетів розміру, Google Play closed test | GD | QA, UA | 4 | Backlog |

**Gate 4**: віджети стабільні; покупки підтверджуються; білд в бюджеті; готово до сабміту.

## Паралельний трек — Store Compliance

- Google Play closed testing (актуальну вимогу до кількості тестерів/днів перевірити на момент старту) — старт наприкінці Sprint 1.
- App Store: Privacy Manifest, Restore Purchases, опис використання локації (якщо буде).
