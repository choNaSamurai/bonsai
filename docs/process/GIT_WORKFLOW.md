# Git Flow для Pocket Arbor (Unity)

> Власник: Unity Architect · Статус: **Діє** · Остання ревізія: 2026-10-03

Працюємо **локально**. Remote поки не підключено. **Жоден коміт, merge, tag чи rebase не робиться без явного погодження власника проєкту в чаті.** Агенти готують зміни та пропонують повідомлення коміту — комітить головна сесія лише після «так».

---

## 1. Гілки

| Гілка | Звідки | Куди мержиться | Призначення |
|---|---|---|---|
| `main` | — | — | Лише релізні стани. Кожен коміт на `main` = тег `vX.Y.Z`. |
| `develop` | `main` | `release/*` | Інтеграційна гілка. Завжди відкривається в Unity і проходить EditMode-тести. |
| `feature/PA-<id>-<slug>` | `develop` | `develop` | Одна задача з беклогу. Напр. `feature/PA-102-tree-domain`. |
| `spike/PA-<id>-<slug>` | `develop` | **не мержиться** (лише висновок в ADR) | Прототипи/дослідження ризиків (віджети, аудіо-латентність, розмір білда). |
| `art/<slug>` | `develop` | `develop` | Пакети асетів від Technical Artist (текстури, звуки, шейдери) без коду. |
| `bugfix/PA-<id>-<slug>` | `develop` | `develop` | Баги, знайдені до релізу. |
| `release/v<X.Y.Z>` | `develop` | `main` + `develop` | Стабілізація, сертифікація сторів, тільки фікси. Тут бампається версія. |
| `hotfix/v<X.Y.Z>` | `main` | `main` + `develop` | Критичні фікси живої версії. |

Мерж у `develop`/`main` — `--no-ff`, щоб зберігати межі фіч.

## 2. Особливості саме для ігор (Unity)

1. **Git LFS обов'язковий** для бінарних асетів (текстури, аудіо, шрифти, PSD). Правила — у `.gitattributes`. Ставиться один раз на машину:
   ```bash
   brew install git-lfs && git lfs install
   ```
   До встановлення LFS **не комітимо бінарні файли** — вони потраплять у звичайну історію.
2. **Налаштування Unity-проєкту** (перевіряє Unity Architect при скафолдингу):
   - `Project Settings → Editor → Asset Serialization = Force Text`
   - `Version Control → Mode = Visible Meta Files`
   - `.meta` файли **завжди** комітяться разом з асетом (і видаляються разом з ним).
3. **UnityYAMLMerge** для сцен/префабів (після встановлення Unity):
   ```bash
   git config merge.unityyamlmerge.name "Unity SmartMerge"
   git config merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/<VERSION>/Unity.app/Contents/Tools/UnityYAMLMerge' merge -p %O %B %A %A"
   git config merge.unityyamlmerge.recursive binary
   ```
4. **Мінімізація конфліктів у сценах**: одна сцена `Main.unity` + все інше — у префабах; одна фіча = свій префаб. Сцени помічені `lockable` — при командній роботі `git lfs lock`.
5. **Не комітимо**: `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Builds/`, ключі підпису. Див. `.gitignore`.
6. **Packages/packages-lock.json комітимо** — це наш lock-файл залежностей.
7. **Версія Unity фіксується** у `ProjectSettings/ProjectVersion.txt`; зміна версії рушія — тільки через ADR і окрему гілку `feature/PA-xxx-unity-upgrade`.

## 3. Повідомлення комітів (Conventional Commits + ID задачі)

```
<type>(<scope>): <опис у наказовому способі> [PA-<id>]

<тіло: що і чому, за потреби>
```

- `type`: `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `chore`, `build`, `ci`, `art`, `audio`, `balance`
- `scope`: `domain`, `tree`, `soil`, `input`, `ui`, `audio`, `vfx`, `save`, `platform`, `iap`, `widget`, `gdd`, `adr`, `qa`
- Приклади:
  - `feat(domain): add stochastic growth grammar with node cap [PA-102]`
  - `balance(soil): raise Momiji kBase to 0.28/day [PA-202]`
  - `docs(adr): accept ADR-0003 rendering approach`

## 4. Версіонування

- `X.Y.Z` (SemVer) у `PlayerSettings.bundleVersion`; `Build number` (iOS) / `versionCode` (Android) — монотонно зростаюче ціле.
- Тег на `main`: `vX.Y.Z` (анотований). Тег ставиться тільки після погодження.

## 5. Цикл роботи над задачею

```
1. Задача в docs/tasks/BACKLOG.md має статус Ready (DoR виконано)
2. Головна сесія: git switch develop && git switch -c feature/PA-xxx-slug   (після погодження)
3. Агент(и) виконують роботу → оновлюють docs (ADR / TECH_DECISIONS / QA report)
4. QA Engineer: тести + звіт у docs/qa/reports/
5. Головна сесія показує diff + пропонує повідомлення коміту → чекає «так»
6. Merge у develop (--no-ff) — теж після погодження
```

## 6. Первинна ініціалізація (виконано / очікує)

- [x] `git init -b main`
- [x] `.gitignore`, `.gitattributes` (LFS + UnityYAMLMerge), `.editorconfig`
- [ ] Встановити `git-lfs` (`brew install git-lfs && git lfs install`)
- [x] Перший коміт на `main` (`chore: bootstrap repository`, 94969dc)
- [x] Створити `develop` від першого коміту
- [ ] Опційно: `brew install git-flow` (CLI-обгортка; не обов'язкова — всі кроки виконуються звичайним git)
