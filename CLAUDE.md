# Pocket Arbor — інструкції для Claude Code

Медитативний мобільний симулятор бонсаю на **Unity** (iOS/Android). Спілкування з власником — **українською**; код, ідентифікатори і коментарі в коді — англійською.

## ⛔ Жорсткі правила
1. **Жодних комітів, merge, rebase, tag, push, створення/перемикання гілок без явного «так» власника в чаті.** Перед комітом: показати `git status` + короткий diff-самарі + запропоноване повідомлення (Conventional Commits з `[PA-xxx]`) і чекати погодження. Погодження діє на один коміт.
2. Працюємо локально; remote не додавати без запиту.
3. Git Flow для ігор — [`docs/process/GIT_WORKFLOW.md`](docs/process/GIT_WORKFLOW.md). Бінарні асети не комітити, поки не встановлено `git lfs`.
4. ADR переходить у `Accepted` тільки після погодження власника.
5. `docs/reference/flutter-v3.2/` — read-only архів вихідних вимог.

## Пам'ять проєкту
Уся спільна пам'ять — у [`docs/`](docs/README.md) (GDD · Architecture · ADR · Tasks · QA Reports · Technical Decisions). Почни з [`docs/README.md`](docs/README.md): там карта артефактів, власники і протокол агентів.

## Команда агентів (`.claude/agents/`)
Головна сесія = **оркестратор/продюсер**: розбиває запит на задачі, делегує агентам, збирає результат, веде BACKLOG, показує власнику diff і просить погодження на коміт.

| Агент | Коли викликати | Пише в |
|---|---|---|
| `game-designer` | механіки, GDD, баланс, онбординг, монетизація, ASO | `docs/gdd/`, `.agents/product-marketing.md` |
| `unity-architect` | архітектура, ADR, asmdef, пакети, рендер-ядро, native-плагіни, збірка, продуктивність, ревʼю | `docs/architecture/`, `docs/adr/`, Infrastructure/Bootstrap, ProjectSettings, Plugins |
| `gameplay-developer` | C#-механіки, Domain/Application, жести, симуляція, конфіги, тести до свого коду | Domain, Application, ігрова Presentation, Config, Tests |
| `ui-ux-developer` | Canvas, HUD, меню, оверлеї, токени, TMP, локалізація | Presentation/UI, Prefabs/UI, `docs/design/` |
| `technical-artist` | асети, шейдери, VFX, світло, аудіо-асети/мікшер, Asset Diet | Art, Audio |
| `qa-engineer` | тести, регресія, edge cases, QA-звіти | Tests, `docs/qa/` |

### Типовий конвеєр задачі
```
game-designer (уточнити AC/баланс) → unity-architect (план/ADR, якщо треба)
  → gameplay-developer ∥ ui-ux-developer ∥ technical-artist   (паралельно, якщо незалежні)
  → qa-engineer (тести + звіт) → unity-architect (ревʼю шарів/0 GC, за потреби)
  → оркестратор: diff + повідомлення коміту → погодження власника
```
Незалежні агенти запускай паралельно. Кожному агенту в промпті давай: ID задачі, файли-посилання, очікуваний результат.

## Технічні орієнтири (деталі — ARCHITECTURE.md)
- Unity 6.6 (6000.6.4f1), URP 2D Renderer, IL2CPP, Input System, uGUI + TMP, VContainer, R3, UniTask, PrimeTween.
- Шари asmdef: `Domain`/`Application` без UnityEngine → `Infrastructure`, `Presentation`, `Bootstrap`.
- 0 B GC у кадрі; 120 FPS при взаємодії; бюджет розміру: Android download ≤ 25 МБ, iOS download ≤ 35 МБ (ARCHITECTURE §10).
- Усі числа балансу — з `docs/gdd/BALANCE.md` через ScriptableObject-и.
- Unity Y-up (Pot Lip: `Y >= potLipY`).

## Маркетингові скіли
`.claude/skills/` містить 50 скілів з [coreyhaines31/marketingskills](https://github.com/coreyhaines31/marketingskills) (MIT, деталі — [`vendor/marketingskills/VENDORED.md`](vendor/marketingskills/VENDORED.md)). Продуктовий контекст для них — [`.agents/product-marketing.md`](.agents/product-marketing.md). Найрелевантніші: `aso`, `launch`, `onboarding`, `paywalls`, `pricing`, `marketing-psychology`, `customer-research`, `social`, `referrals`, `content-strategy`. Оновлення — `scripts/sync-marketingskills.sh`.

## Стан середовища (2026-10-04)
- Unity 6000.6.4f1 встановлено з модулями iOS/Android/WebGL, `git-lfs` встановлено, UnityYAMLMerge налаштовано в локальному git config.
- Код, написаний без Unity, вважається неперевіреним компіляцією — так і повідомляти.
