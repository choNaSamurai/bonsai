# Backlog — Pocket Arbor (Unity)

> Власник: головна сесія (оркестратор) · Оновлюють: усі агенти (тільки поле **Статус** і **Нотатки** своїх задач)
> Нумерація та скоуп збережені з [оригінального плану](../reference/flutter-v3.2/ARCHITECTURE_AND_PLAN.md) (PA-101…PA-404), додано **Sprint 0** під Unity-ризики.
> Деталізована задача = окремий файл `docs/tasks/PA-xxx.md` за [шаблоном](TASK_TEMPLATE.md) — створюється, коли задача переходить у `Ready`.

**Статуси**: `Backlog` → `Ready` (DoR) → `In Progress` → `Review` → `QA` → `Done` (DoD) · `Blocked`

**Агенти**: GD — Game Designer · UA — Unity Architect · GP — Gameplay Developer · UI — UI/UX Developer · TA — Technical Artist · QA — QA Engineer

## Sprint 0 — Foundation & Risk Spikes

| ID | Задача | Owner | Support | SP | Статус |
|---|---|---|---|---|---|
| PA-001 | Встановити Unity Hub + Unity 6.6 (6000.6.4f1) (iOS/Android modules), git-lfs; налаштувати UnityYAMLMerge | Власник | UA | 1 | In Progress (бракує модулів iOS/Android) |
| PA-002 | **Spike**: «hello-widget» в Unity-збірці — iOS WidgetKit extension через PostProcessBuild + Android AppWidgetProvider у `.androidlib` | UA | — | 5 | Backlog |
| PA-003 | **Spike**: baseline розміру (.ipa/.aab порожнього URP-2D проєкту зі стрипінгом) і cold start на реальних пристроях | UA | QA | 2 | Backlog |
| PA-004 | **Spike**: вимір аудіо-латентності Unity (Best Latency) на iPhone і mid-tier Android; рішення про native-audio | TA | UA | 2 | Backlog |
| PA-005 | Погодити ADR-0001…0008 і відкриті питання GDD §10 | Власник | UA, GD | 1 | Backlog |
| PA-006 | Скелет `.claude`-воркфлоу: перевірити агентів на пробній задачі | Оркестратор | всі | 1 | Backlog |
| PA-007 | Editor-тулінг: імпорт BALANCE.md → ScriptableObject-и (або ручні ассети + тест відповідності) | GP | GD | 2 | Backlog |

## Sprint 1 — Core Engine

| ID | Задача | Owner | Support | SP | Статус |
|---|---|---|---|---|---|
| PA-101 | Скафолдинг Unity-проєкту: URP 2D, asmdef-шари, VContainer/R3/UniTask/PrimeTween, Input System, Force Text, Player Settings (IL2CPP, ARM64, 120 FPS, ProMotion PostProcess, Privacy Manifest), Quality профілі, стрипінг | UA | — | 3 | Backlog |
| PA-102 | Domain: `TreeGraph` (ADR-0003), `GrowthGrammar` з лімітом 200, Небарі (NaN-guard, експ. насичення), Фенікс, PCG32, `IClock` + EditMode-тести | GP | GD, QA | 5 | Backlog |
| PA-103 | `TreeRenderer`: процедурний меш гілок (Безьє-стрічки, круглі стики, 1 draw call), `LeafInstancer`, вітер у шейдері; 0 GC у кадрі | UA | TA | 5 | Backlog |
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
| PA-404 | Нотифікації, ASO (marketingskills: `aso`, `launch`), перевірка бюджетів розміру, Google Play closed test | GD | QA, UA | 4 | Backlog |

**Gate 4**: віджети стабільні; покупки підтверджуються; білд в бюджеті; готово до сабміту.

## Паралельний трек — Store Compliance

- Google Play closed testing (актуальну вимогу до кількості тестерів/днів перевірити на момент старту) — старт наприкінці Sprint 1.
- App Store: Privacy Manifest, Restore Purchases, опис використання локації (якщо буде).
