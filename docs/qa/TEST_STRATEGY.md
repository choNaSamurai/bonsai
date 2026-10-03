# Test Strategy — Pocket Arbor (Unity)

> Власник: **QA Engineer** · Статус: Draft v0.1 · 2026-10-03

## 1. Піраміда тестів

| Рівень | Інструмент | Що покриваємо | Де |
|---|---|---|---|
| **EditMode / Domain** | Unity Test Framework (NUnit) | Уся математика: ріст, Небарі, Фенікс, дріт, ґрунт, офлайн, геном, пороги жестів (чисті класи) | `Tests/EditMode/Domain` |
| **EditMode / Infrastructure** | UTF + тимчасові теки | AtomicFileStore (корупція, урізання, `.bak`, Poison Pill), парсинг Open-Meteo, міграції схеми | `Tests/EditMode/Infrastructure` |
| **PlayMode** | UTF + `InputTestFixture` | GestureRouter (FSM, abort двома пальцями, deadzones, snapping), онбординг, lifecycle pause/resume | `Tests/PlayMode` |
| **Performance** | `com.unity.test-framework.performance` | Час перебудови мешу при 200 вузлах, `GC.Alloc` = 0 у кадрі, frame time | `Tests/Performance` |
| **Visual regression** | Скриншот-порівняння (Graphics Test Framework або власний `ImageAssert`) | 3 породи × стадії росту, сухість, Dormancy, ніч | `Tests/PlayMode/Visual` |
| **Device** | Ручний чекліст + Profiler/Instruments | 120 FPS, аудіо-латентність, хаптика, віджети, IAP sandbox | `docs/qa/reports/` |

## 2. Обов'язкові сценарії (з ТЗ / Red Team)

### Domain
- [ ] При 200 вузлах нові пагони не з'являються; ріст іде в товщину ≤ 24 units
- [ ] `depth > MaxDepth` не дає NaN у depthScale
- [ ] Зріз усіх гілок → з'являється Фенікс-брунька; стовбур не зрізається
- [ ] Дріт: кут клампиться в ±π/4; жоден вузол піддерева не нижче Pot Lip (Y-up); 14 днів → фіксація; дострокове зняття → пропорційна релаксація; годинник назад → `WireProgressDays` не від'ємний
- [ ] Ґрунт: +36 °C, без дощу, клен з мохом → W > 0 мінімум 72 год; вночі сонячний внесок = 0; −15 °C — сніг не «плавиться» в полив
- [ ] Неперервність дощу: немає стрибка на 0.5 мм
- [ ] Офлайн: Δt < 0 → якір скидається; Δt > 168 год → рівно 7 діб симуляції + Dormancy; онбординг → Δt = 0; кеш погоди > 3 год → опади 0
- [ ] Геном: довжина ≠ 43 → default; NaN/Inf → default; неправильний magic/CRC → default

### Infrastructure
- [ ] Файл 0–64 байти → corrupted без винятку
- [ ] Битий хеш → відновлення з `.bak` і перезапис primary
- [ ] Обидва файли биті → новий сад + запис у quarantine, без крашу

### Input (PlayMode)
- [ ] 2 пальці (один над доком) під час свайпу секатора → зріз скасовано
- [ ] Старт дотику в deadzone → жест ігнорується; вихід у deadzone посеред свайпу → анульовано
- [ ] Свайп < 24 dp або < 120 dp/с → немає зрізу
- [ ] Snapping: радіус по формулі для s ∈ {0.1, 1, 4, 10}; гістерезис 8 dp без мерехтіння

### Performance
- [ ] `GC.Alloc` у steady-state кадрі = 0 B (інструменти Inspect/Pruner, вітер, ніч зі світлячками)
- [ ] Перебудова мешу 200 вузлів ≤ 1.8 мс на референсному пристрої

## 3. Регресія

`docs/qa/REGRESSION_CHECKLIST.md` (створюється в PA-101) — швидкий прогін перед кожним merge у `develop` і повний — перед `release/*`.

## 4. Звітність

- Кожна задача в статусі `QA` → звіт `docs/qa/reports/PA-xxx-YYYY-MM-DD.md` за [шаблоном](reports/REPORT_TEMPLATE.md).
- Баги → секція «Bugs» у звіті з ID `BUG-xxx` + рядок у BACKLOG (bugfix-задача), якщо не виправлено в межах задачі.

## 5. Референсні пристрої (TBD власником)

- iOS: iPhone з ProMotion (120 Hz) + iPhone без ProMotion
- Android: mid-tier (Mali-G52 / PowerVR клас) + flagship
